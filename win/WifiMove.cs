using System.Text;
using System.Text.RegularExpressions;

namespace EasyDKiosk;

/// <summary>
/// Builds Android 11+ <c>cmd wifi</c> commands to save shop Wi-Fi on a TV.
/// Prefers save-only <c>add-network</c> so the setup network stays saved and
/// the shop network can be stored even when it is out of range. Never persists a password.
/// </summary>
internal static class WifiMove
{
    public const int MinSdk = 30;
    public const int MaxSsidChars = 32;
    public const int MinPskChars = 8;
    public const int MaxPskChars = 63;

    public enum Security { Wpa2, Wpa3, Open }

    public static bool TryParseSecurity(string text, out Security security)
    {
        security = Security.Wpa2;
        if (string.IsNullOrWhiteSpace(text)) return false;
        switch (text.Trim().ToLowerInvariant())
        {
            case "wpa2": security = Security.Wpa2; return true;
            case "wpa3": security = Security.Wpa3; return true;
            case "open": security = Security.Open; return true;
            default: return false;
        }
    }

    public static string SecurityArg(Security security) => security switch
    {
        Security.Wpa3 => "wpa3",
        Security.Open => "open",
        _ => "wpa2"
    };

    public static string SecurityLabel(Security security) => security switch
    {
        Security.Wpa3 => "WPA3",
        Security.Open => "Open",
        _ => "WPA2"
    };

    /// <summary>Single-quote for Android's toybox/mksh. A quote in the value becomes: 'foo'\''bar'</summary>
    public static string ShQuote(string value)
    {
        value ??= "";
        return "'" + value.Replace("'", "'\\''") + "'";
    }

    public static string Validate(string ssid, Security security, string password)
    {
        ssid ??= "";
        password ??= "";
        if (ssid.Length == 0) return "Enter the shop Wi-Fi name (SSID).";
        if (ssid.IndexOfAny(new[] { '\0', '\r', '\n' }) >= 0) return "The network name cannot contain line breaks.";
        if (Encoding.UTF8.GetByteCount(ssid) > MaxSsidChars)
            return "The network name is too long (maximum 32 bytes).";
        if (security == Security.Open)
        {
            if (!string.IsNullOrEmpty(password))
                return "Open networks do not use a password. Clear the password, or choose WPA2 or WPA3.";
            return null;
        }
        if (password.IndexOfAny(new[] { '\0', '\r', '\n' }) >= 0) return "The password cannot contain line breaks.";
        if (password.Length < MinPskChars || password.Length > MaxPskChars)
            return "Enter the Wi-Fi password (8 to 63 characters) for WPA2 or WPA3.";
        return null;
    }

    static string BuildWifiCommand(string verb, string ssid, Security security, string password)
    {
        var cmd = new StringBuilder();
        cmd.Append("cmd wifi ").Append(verb).Append(' ').Append(ShQuote(ssid)).Append(' ').Append(SecurityArg(security));
        if (security != Security.Open)
            cmd.Append(' ').Append(ShQuote(password ?? ""));
        return cmd.ToString();
    }

    /// <summary>Save-only. Works even if the shop Wi-Fi is out of range. Does not remove other saved networks. Never log this string.</summary>
    public static string BuildAddCommand(string ssid, Security security, string password)
        => BuildWifiCommand("add-network", ssid, security, password);

    /// <summary>Connects and also saves. Can drop the current network. Kept as a fallback; prefer <see cref="BuildAddCommand"/>.</summary>
    public static string BuildConnectCommand(string ssid, Security security, string password)
        => BuildWifiCommand("connect-network", ssid, security, password);

    public static string LogCommand(string ssid, Security security, string verb = "add-network")
        => "cmd wifi " + verb + " " + ShQuote(ssid ?? "") + " " + SecurityArg(security)
           + (security == Security.Open ? "" : " ****");

    public static string Redact(string text, params string[] secrets)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        foreach (var s in secrets)
        {
            if (string.IsNullOrEmpty(s)) continue;
            text = text.Replace(s, "••••");
        }
        return text;
    }

    public static string ParseConnectedSsid(string wifiStatus)
    {
        if (string.IsNullOrEmpty(wifiStatus)) return "";
        string v = null;
        var m = Regex.Match(wifiStatus, @"connected to\s+""([^""]*)""", RegexOptions.IgnoreCase);
        if (m.Success) v = m.Groups[1].Value;
        if (v == null)
        {
            m = Regex.Match(wifiStatus, @"SSID:\s*""([^""]*)""");
            if (m.Success) v = m.Groups[1].Value;
        }
        if (v == null)
        {
            m = Regex.Match(wifiStatus, @"SSID:\s*(<unknown ssid>|[^\s,]+)");
            if (m.Success) v = m.Groups[1].Value;
        }
        if (string.IsNullOrEmpty(v) || string.Equals(v, "<unknown ssid>", StringComparison.OrdinalIgnoreCase))
            return "";
        return v;
    }

    /// <summary>SSIDs from <c>cmd wifi list-networks</c>, in list order.</summary>
    public static List<string> ParseSavedSsids(string listNetworks)
    {
        var list = new List<string>();
        if (string.IsNullOrEmpty(listNetworks)) return list;
        foreach (var raw in listNetworks.Split('\n'))
        {
            var line = raw.TrimEnd('\r').Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("Network Id", StringComparison.OrdinalIgnoreCase)) continue;
            var m = Regex.Match(line, @"^\s*\d+\s+(.+?)\s+(open|owe|wpa2|wpa3)\s*$", RegexOptions.IgnoreCase);
            if (!m.Success) continue;
            var ssid = m.Groups[1].Value.Trim();
            if (ssid.Length > 0) list.Add(ssid);
        }
        return list;
    }

    public static bool ListContainsSsid(IEnumerable<string> saved, string ssid)
        => saved != null && !string.IsNullOrEmpty(ssid)
           && saved.Any(s => string.Equals(s, ssid, StringComparison.Ordinal));

    public static bool LooksLikeSaveAccepted(string output)
    {
        var all = (output ?? "").ToLowerInvariant();
        if (all.Contains("unknown command") || all.Contains("does not exist") || all.Contains("security exception")
            || all.Contains("failure") || all.Contains("failed") || all.Contains("error"))
            return false;
        return all.Contains("save successful") || all.Contains("success")
               || all.Contains("added") || all.Contains("saved");
    }

    /// <summary>Opens every Wi-Fi instruction block. Written for shopkeepers setting TVs up in the shop, including several at once.</summary>
    public static string ShopWifiIntro()
        => "If your shop Wi-Fi password is too long or awkward to type with the TV remote, use one of these options. This is for setting TVs up in the shop — including several TVs in one visit.\r\n\r\n"
           + "Recommended: Add a child network with a second small router (a travel router) connected to your shop router, and give that child network a short, easy password. Join this PC and each TV to that simple setup Wi-Fi, then set up the kiosk on each TV. When a TV is ready, use Move TV to shop Wi-Fi to save your shop network on it. The setup network stays saved too. The TV will join the shop Wi-Fi when that network is in range — often after you have finished the others. You do not need to type the shop password on the remote.\r\n\r\n"
           + "Fallback: Temporarily set a simple password on the shop Wi-Fi itself while you set up the TVs. When you change the password back, every device on that Wi-Fi is disconnected, and each one — including every TV — must rejoin with the original password.";

    public static string ShopWifiStepHelp()
        => "Never automatic. Enter the shop network name, security, and password; you will be asked to check them before they are sent. This saves the shop Wi-Fi on the TV without removing the setup network, even if the shop network is out of range. The password is not saved on this PC. If you have more TVs to set up, stay on the setup Wi-Fi until you have finished them all.";

    /// <summary>Confirmation dialog body. Includes the password so the shopkeeper can check it. Do not write this to the log or to disk.</summary>
    public static string ConfirmDetails(string ssid, Security security, string password)
    {
        var passLine = security == Security.Open ? "(no password — Open network)" : (password ?? "");
        return "Check these details before they are sent to the TV:\r\n\r\n"
               + "Network name (SSID):  " + ssid + "\r\n"
               + "Security:  " + SecurityLabel(security) + "\r\n"
               + "Password:  " + passLine + "\r\n\r\n"
               + "The shop network will be saved on this TV. The setup Wi-Fi already on the TV will stay saved alongside it — it is not removed. If the shop Wi-Fi is out of range now, the TV can join it later when it is in reach.\r\n\r\n"
               + "If the TV switches to the shop Wi-Fi immediately, this PC will lose its connection to the TV until you join the same shop Wi-Fi and use Find my TV. Kiosk Manager does not change this PC’s Wi-Fi for you. On Google TV / Chromecast, turn Wireless debugging back on first (and pair again if asked).\r\n\r\n"
               + "If you also want to turn debugging off, press Turn off debugging only after this step, and only if you still want later access from this PC.";
    }

    public static string ManualInstructions()
        => "Save or join the shop Wi-Fi on the TV yourself. Leave the setup network saved if it is already there — do not forget or remove it.\r\n"
           + "• Xiaomi / Android TV: Settings › Network & Internet › Wi‑Fi (or Network & Internet), choose the shop network, and enter the password with the remote.\r\n"
           + "• Google TV / Chromecast: Settings › Network & Internet, choose the shop network, and enter the password.\r\n"
           + "You can also open TV Settings from the hidden Kiosk menu (hold OK for about 3 seconds).\r\n"
           + "If you are setting up several TVs, you can stay on the simple setup Wi-Fi until each one has the shop network saved.\r\n"
           + "After a TV joins the shop network, connect this PC to the same shop Wi-Fi and use Find my TV. On Google TV / Chromecast, turn Wireless debugging back on first (and pair again if asked).";
}

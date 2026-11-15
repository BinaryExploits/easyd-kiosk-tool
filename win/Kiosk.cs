using System.Text;
using System.Text.RegularExpressions;

namespace EasyDKiosk;

public enum StepState { Pending, Running, Done, Failed, Skipped }

public sealed class AppEntry
{
    public string Package = "";
    public string Label = "";
    public bool System;
}

public sealed class HomeCandidate
{
    public string Package = "";
    public string Why = "";
    public bool Recommended;
    public override string ToString() => $"{Package}   — {Why}";
}

/// <summary>All device-side operations for building and removing the kiosk.</summary>
public sealed class KioskOps
{
    public const string DefaultTarget = "com.taknik.easydplayer";
    public const string DefaultTargetName = "EasyD Player";
    public const string KioskPkg = "com.easyd.kiosk";
    public const string HomeComponent = "com.easyd.kiosk/.HomeAlias";
    public const string ReceiverComponent = "com.easyd.kiosk/.ConfigReceiver";
    public const string DeviceStateFile = "/data/local/tmp/easyd_kiosk_disabled.txt";

    public static readonly string[] KnownStock =
    {
        "com.google.android.tvlauncher",          // Android TV home (Xiaomi Mi TV Stick, etc.)
        "com.google.android.apps.tv.launcherx",   // Google TV home (Chromecast with Google TV)
        "com.google.android.tungsten.setupwraith" // Google TV helper that re-enables its home
    };

    /// <summary>Never disable these, even if they answer the HOME intent (boot / settings / setup).</summary>
    public static readonly HashSet<string> Protected = new(StringComparer.Ordinal)
    {
        "android", "com.android.settings", "com.android.tv.settings", "com.google.android.tv.settings",
        "com.android.provision", "com.google.android.setupwizard", "com.google.android.tv.setupwizard",
        "com.android.systemui", "com.android.shell", KioskPkg
    };

    private static readonly Regex PkgRx = new(@"^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z0-9_]+)+$");
    private static readonly Regex ComponentRx = new(@"(?<![\w.])([A-Za-z][\w]*(?:\.[\w]+)+)/(\.?[\w.$]+)");

    private readonly Adb _adb;
    private readonly Action<string> _log;
    public KioskOps(Adb adb, Action<string> log) { _adb = adb; _log = log; }

    public static bool ValidPackage(string p) => p != null && PkgRx.IsMatch(p);

    public async Task<bool> IsInstalledAsync(string s, string pkg, bool quiet = true)
    {
        var r = await _adb.ShellAsync(s, $"pm list packages {pkg}", quiet);
        return r.Out.Split('\n').Any(l => l.Trim() == "package:" + pkg);
    }

    public async Task<HashSet<string>> PackagesAsync(string s, string flags)
    {
        var r = await _adb.ShellAsync(s, $"pm list packages {flags}", true);
        return new HashSet<string>(r.Out.Split('\n').Select(l => l.Trim())
            .Where(l => l.StartsWith("package:")).Select(l => l.Substring(8)));
    }

    public async Task<string> PropAsync(string s, string name)
        => (await _adb.ShellAsync(s, $"getprop {name}", true)).Out.Trim();

    public async Task<string> VersionNameAsync(string s, string pkg)
    {
        var r = await _adb.ShellAsync(s, $"dumpsys package {pkg}", true);
        var m = Regex.Match(r.Out, @"versionName=(\S+)");
        return m.Success && m.Groups[1].Value != "null" ? m.Groups[1].Value : "";
    }

    /// <summary>Packages whose activities currently answer MAIN/HOME (enabled ones only).</summary>
    public async Task<List<(string Pkg, string Component)>> HomeActivitiesAsync(string s, bool quiet = true)
    {
        var r = await _adb.ShellAsync(s,
            "cmd package query-activities --brief --user 0 -a android.intent.action.MAIN -c android.intent.category.HOME", quiet);
        var list = new List<(string, string)>();
        foreach (Match m in ComponentRx.Matches(r.Out))
        {
            var pkg = m.Groups[1].Value;
            if (!list.Any(x => x.Item2 == m.Value)) list.Add((pkg, m.Value));
        }
        return list;
    }

    public async Task<string> ResolveHomeAsync(string s)
    {
        var r = await _adb.ShellAsync(s,
            "cmd package resolve-activity --brief --user 0 -a android.intent.action.MAIN -c android.intent.category.HOME");
        var last = r.Out.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0) ?? "";
        return last;
    }

    /// <summary>Home screens that should be turned off so only the kiosk is left.</summary>
    public async Task<List<HomeCandidate>> CandidatesToDisableAsync(string s, string target)
    {
        var enabled = await PackagesAsync(s, "-e");
        var res = new List<HomeCandidate>();
        foreach (var k in KnownStock)
            if (enabled.Contains(k))
                res.Add(new HomeCandidate { Package = k, Recommended = true,
                    Why = k.EndsWith("setupwraith") ? "Google TV helper (would switch Google TV home back on)" : "stock home screen with suggestions" });
        foreach (var (pkg, comp) in await HomeActivitiesAsync(s))
        {
            if (pkg == target || Protected.Contains(pkg) || res.Any(c => c.Package == pkg)) continue;
            if (comp.Contains("FallbackHome", StringComparison.OrdinalIgnoreCase)) continue;
            var lower = pkg.ToLowerInvariant();
            bool risky = lower.Contains("setting") || lower.Contains("setup") || lower.Contains("provision");
            if (risky) continue;
            res.Add(new HomeCandidate { Package = pkg, Recommended = true, Why = "other home screen / launcher" });
        }
        return res;
    }

    public async Task<(int Code, string Data)> BroadcastAsync(string s, string action, string extras = "")
    {
        var r = await _adb.ShellAsync(s,
            $"am broadcast --include-stopped-packages -n {ReceiverComponent} -a {action} {extras}".Trim(), true, 120000);
        var m = Regex.Match(r.Out, @"Broadcast completed: result=(-?\d+)(?:, data=""(?<d>[^""]*)"")?");
        if (!m.Success) { _log("  broadcast reply: " + r.All); return (int.MinValue, ""); }
        return (int.Parse(m.Groups[1].Value), m.Groups["d"].Value);
    }

    public async Task<bool> InstallKioskAsync(string s)
    {
        var apk = Adb.ExtractApk();
        var r = await _adb.RunAsync(180000, false, "-s", s, "install", "-r", apk);
        return r.Out.Contains("Success");
    }

    /// <summary>Launchable apps with labels, via the kiosk helper (installed if needed); falls back to package names.</summary>
    public async Task<List<AppEntry>> ListAppsAsync(string s)
    {
        if (!await IsInstalledAsync(s, KioskPkg))
        {
            _log("Installing the kiosk helper to read app names (this does not change the home screen yet)...");
            await InstallKioskAsync(s);
        }
        var (code, data) = await BroadcastAsync(s, "com.easyd.kiosk.LIST_APPS");
        var apps = new List<AppEntry>();
        if (code == 1 && data.StartsWith("APPS:"))
        {
            try
            {
                var txt = Encoding.UTF8.GetString(Convert.FromBase64String(data.Substring(5)));
                foreach (var line in txt.Split('\n'))
                {
                    var p = line.Split('\t');
                    if (p.Length >= 3 && ValidPackage(p[0]))
                        apps.Add(new AppEntry { Package = p[0], Label = p[1], System = p[2] == "1" });
                }
            }
            catch (Exception ex) { _log("Could not read app names: " + ex.Message); }
        }
        if (apps.Count == 0)
        {
            _log("Falling back to package names (pm list packages -3).");
            foreach (var p in (await PackagesAsync(s, "-3")).OrderBy(x => x))
                if (p != KioskPkg) apps.Add(new AppEntry { Package = p, Label = p });
        }
        return apps.OrderBy(a => a.Label, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string LocalStatePath(string deviceId)
        => Path.Combine(Adb.DataDir, "disabled-" + Regex.Replace(deviceId, @"[^\w.-]", "_") + ".txt");

    public async Task SaveStateAsync(string s, string deviceId, IEnumerable<string> pkgs)
    {
        var list = pkgs.Where(ValidPackage).Distinct().ToList();
        try { await File.WriteAllLinesAsync(LocalStatePath(deviceId), list); } catch (Exception ex) { _log("Could not save local state: " + ex.Message); }
        await _adb.ShellAsync(s, $"echo '{string.Join(" ", list)}' > {DeviceStateFile}", true);
    }

    public async Task<HashSet<string>> LoadStateAsync(string s, string deviceId)
    {
        var set = new HashSet<string>();
        var f = LocalStatePath(deviceId);
        if (File.Exists(f)) foreach (var l in await File.ReadAllLinesAsync(f)) if (ValidPackage(l.Trim())) set.Add(l.Trim());
        var r = await _adb.ShellAsync(s, $"cat {DeviceStateFile}", true);
        if (r.Ok) foreach (var p in r.Out.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)) if (ValidPackage(p)) set.Add(p);
        return set;
    }

    public async Task ClearStateAsync(string s, string deviceId)
    {
        try { File.Delete(LocalStatePath(deviceId)); } catch { }
        await _adb.ShellAsync(s, $"rm -f {DeviceStateFile}", true);
    }
}

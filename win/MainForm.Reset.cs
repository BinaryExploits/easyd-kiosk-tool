namespace EasyDKiosk;

public sealed partial class MainForm
{
    private const int ResetPage = 5;
    private int _resetReturnPage;

    private void ShowResetOptions()
    {
        _resetReturnPage = (_page == ResetPage) ? 0 : _page;
        ShowPage(ResetPage);
        Status("Reset options.");
    }

    private LinkLabel ResetOptionsLink()
    {
        var l = new LinkLabel
        {
            Text = "Reset options — undo the kiosk or factory-reset the TV",
            AutoSize = true,
            Margin = new Padding(0, 14, 0, 4),
            LinkColor = Ui.Brand,
            ActiveLinkColor = Ui.Brand
        };
        l.LinkClicked += (_, _) => ShowResetOptions();
        return l;
    }

    private Control PageReset()
    {
        var p = Ui.Page();
        var back = Ui.Secondary("◀  Back");
        back.Click += (_, _) =>
        {
            int to = _resetReturnPage;
            if (to == ResetPage || to < 0 || to >= DonePage + 1) to = 0;
            ShowPage(to);
        };
        _actionControls.Add(back);
        p.Controls.Add(Ui.Row(back));
        p.Controls.Add(Ui.Heading("Reset options"));
        p.Controls.Add(Ui.Text("Ways to undo the kiosk, clear Easy Display Player, or start the TV from scratch. Factory reset is listed first because it is the most complete — and the most destructive.", 640, Ui.Muted));

        // 1) Factory reset FIRST
        p.Controls.Add(Section("1. Factory reset (erases everything)"));
        p.Controls.Add(Ui.Text("A factory reset deletes all apps, accounts, Wi‑Fi passwords and settings on the TV stick or Chromecast. You will have to set the device up again. Use this only if you cannot restore the kiosk any other way, or you want a completely clean device.", 640, Ui.Bad));

        p.Controls.Add(Sub("Xiaomi Mi TV Stick 4K / Android TV — from Settings"));
        p.Controls.Add(Ui.Text("Open Settings › Device Preferences › Reset (some regions label this Restart, Reset, or Recover; it is usually the last item under Device Preferences). Confirm the reset. This path is from Xiaomi support: “Factory reset for TV, TV stick, Mi box” and the Mi TV FAQ (“Settings › Device Preference › Reset”).", 640));

        p.Controls.Add(Sub("Xiaomi / Android TV — if the menus will not open"));
        p.Controls.Add(Ui.Text("Xiaomi’s official recovery steps: unplug the stick from power. Press and hold Back and Confirm (OK) on the Bluetooth remote at the same time, and plug the power back in while still holding both buttons. When the recovery screen appears, choose Wipe data / Factory reset and confirm. (Xiaomi support: “Factory reset for TV, TV stick, Mi box”, and “What to do if the Mi TV Stick cannot enter the main screen?”.)", 640));

        p.Controls.Add(Sub("Google TV / Chromecast — from Settings"));
        p.Controls.Add(Ui.Text("Open Settings (profile or All settings) › System › About › Factory Reset, then confirm. This path is from Google’s “How to factory reset your Chromecast or Google TV Streamer”.", 640));

        p.Controls.Add(Sub("Google TV / Chromecast — if the menus will not open"));
        p.Controls.Add(Ui.Text("With the Chromecast with Google TV (or Google TV Streamer) plugged into the TV and powered on, press and hold the button on the back of the device. The LED blinks yellow. When the light turns solid white, release the button — the device resets. This is from Google’s factory-reset and unresponsive-device help pages. It cannot be undone and wipes the device.", 640));

        // 2) Restore with the tool
        p.Controls.Add(Section("2. Restore the normal launcher with Kiosk Manager"));
        p.Controls.Add(Ui.Text("If this PC can still reach the TV, this is the safest option: it turns the original home screen back on, removes the kiosk menu shortcut, uninstalls the kiosk app, and leaves your other apps in place.", 640));
        p.Controls.Add(Ui.Text("Connect to the TV (USB debugging or Wireless debugging must be on — if you used Turn off debugging, turn USB debugging back on in Developer options first). Then open the last wizard step and click Restore original.", 640));
        var restore = Ui.Secondary("↺  Restore original");
        restore.ForeColor = Ui.Bad;
        restore.FlatAppearance.BorderColor = Ui.Bad;
        restore.Click += async (_, _) =>
        {
            if (_serial == null)
            {
                MessageBox.Show(this, "Connect to the TV first (debugging must be on), then come back here or use Restore original on the last step.", Text);
                return;
            }
            ShowPage(3);
            await RestoreAsync();
        };
        _actionControls.Add(restore);
        p.Controls.Add(Ui.Row(restore));

        // 3) Kiosk menu on the TV
        p.Controls.Add(Section("3. From the TV Kiosk menu"));
        p.Controls.Add(Ui.Text("On the TV, hold OK (or the Centre key) for about 3 seconds, or press Up Up Down Down Left Right Left Right, or press Home twice quickly. This works even while Easy Display Player is open. In the menu you can open TV Settings, or App settings / Clear data (the TV’s app-info screen for Easy Display Player).", 640));

        // 4) Settings > Apps
        p.Controls.Add(Section("4. Clear Easy Display Player data or uninstall it in Settings"));
        p.Controls.Add(Ui.Text("If you can reach Settings (from the Kiosk menu, or after you restore the normal launcher): open Settings › Apps (or Apps & notifications), choose Easy Display Player, then Clear data / Clear cache, or Uninstall. On Android TV this is often under Settings › Device Preferences › Apps; on Google TV it is under Settings › Apps.", 640));

        // 5) Manual ADB
        p.Controls.Add(Section("5. Manual ADB commands (without this tool)"));
        p.Controls.Add(Ui.Text("Debugging must be on. If you turned it off with the Turn off debugging button, first turn USB debugging back on in Developer options (and Wireless debugging on Google TV), then connect with adb.", 640));
        p.Controls.Add(Sub("Xiaomi Mi TV Stick 4K / Android TV"));
        p.Controls.Add(CopyBox(
            "adb shell pm enable com.google.android.tvlauncher\r\n" +
            "adb shell pm enable com.google.android.tungsten.setupwraith\r\n" +
            "adb uninstall com.easyd.kiosk"));
        p.Controls.Add(Sub("Google TV / Chromecast (also re-enable Google TV’s home screen)"));
        p.Controls.Add(CopyBox(
            "adb shell pm enable com.google.android.tvlauncher\r\n" +
            "adb shell pm enable com.google.android.tungsten.setupwraith\r\n" +
            "adb shell pm enable com.google.android.apps.tv.launcherx\r\n" +
            "adb uninstall com.easyd.kiosk"));

        p.Controls.Add(Ui.Text("Sources: Xiaomi support “Factory reset for TV, TV stick, Mi box” (https://www.mi.com/global/support/article/KA-12918/) and “What to do if the Mi TV Stick cannot enter the main screen?” (https://www.mi.com/global/support/faq/details/KA-557214/); Google “How to factory reset your Chromecast or Google TV Streamer” (https://support.google.com/chromecast/answer/6254654) and “Fix an unresponsive Google TV device” (https://support.google.com/googletv/answer/12360626).", 640, Ui.Muted));
        return p;
    }

    private static Label Section(string t) => new()
    {
        Text = t, AutoSize = true, MaximumSize = new Size(640, 0),
        Font = new Font("Segoe UI Semibold", 13f), ForeColor = Ui.Brand,
        Margin = new Padding(0, 18, 0, 6)
    };

    private static Label Sub(string t) => new()
    {
        Text = t, AutoSize = true, MaximumSize = new Size(640, 0),
        Font = new Font("Segoe UI Semibold", 11f), ForeColor = Color.FromArgb(33, 33, 33),
        Margin = new Padding(0, 8, 0, 2)
    };

    private Control CopyBox(string commands)
    {
        var box = new TextBox
        {
            Text = commands, ReadOnly = true, Multiline = true,
            Font = new Font("Consolas", 9.5f), Width = 620,
            Height = 22 * (commands.Split('\n').Length + 1),
            ScrollBars = ScrollBars.None, BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(248, 250, 253), Margin = new Padding(0, 4, 0, 4),
            WordWrap = false
        };
        var copy = Ui.Secondary("Copy");
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(commands.Replace("\r\n", "\n").Trim()); Status("Copied to the clipboard."); }
            catch (Exception ex) { Status("Could not copy: " + ex.Message); }
        };
        _actionControls.Add(copy);
        var wrap = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0)
        };
        wrap.Controls.Add(box);
        wrap.Controls.Add(copy);
        return wrap;
    }
}

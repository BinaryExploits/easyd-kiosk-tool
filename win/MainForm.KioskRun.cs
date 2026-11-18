namespace EasyDKiosk;

internal sealed class KioskRunResult
{
    public bool Ok;
    public bool Warn;
    public bool ShowDone;
    public string Message = "";
}

public sealed partial class MainForm
{
    /// <summary>Runs the Make Easy Display Kiosk steps on one already-connected device. Does not show UI pages.</summary>
    private async Task<KioskRunResult> RunMakeKioskCoreAsync(
        string serial, string deviceId, string target, string targetLabel,
        IReadOnlyList<string> toDisable, Action<int, StepState, string> onStep, CancellationToken ct)
    {
        void Step(int i, StepState s, string note = null)
        {
            try { onStep?.Invoke(i, s, note); } catch { }
        }

        if (string.IsNullOrEmpty(serial))
            return FailRes("This PC is not connected to the TV.");
        if (ct.IsCancellationRequested) return FailRes("Stopped.");

        if (string.IsNullOrEmpty(deviceId))
        {
            try { deviceId = await _ops.PropAsync(serial, "ro.serialno"); } catch { }
            if (string.IsNullOrEmpty(deviceId)) deviceId = serial;
        }

        Step(0, StepState.Running);
        if (!await _ops.IsInstalledAsync(serial, target, false))
        {
            Step(0, StepState.Failed);
            return FailRes(targetLabel + " is not installed on the TV. Install it from the Play Store on that TV, then try again.");
        }
        Step(0, StepState.Done);

        Step(1, StepState.Running);
        if (!await _ops.InstallKioskAsync(serial))
        {
            Step(1, StepState.Failed);
            return FailRes("The kiosk launcher could not be installed (see log).");
        }
        Step(1, StepState.Done);

        Step(2, StepState.Running);
        var (code, data) = await _ops.BroadcastAsync(serial, "com.easyd.kiosk.SET_TARGET", "--es target " + target);
        Log("  kiosk replied: " + code + " " + data);
        if (code != 1 || data != "OK:" + target)
        {
            Step(2, StepState.Failed, data);
            return FailRes(data != null && data.Contains("not_launchable")
                ? targetLabel + " has no TV launcher activity, so it cannot be opened as the home app."
                : "The kiosk app did not accept the setting (see log).");
        }
        Step(2, StepState.Done);

        Step(3, StepState.Running);
        bool a11yOk = await _ops.EnableMenuShortcutAsync(serial);
        Step(3, a11yOk ? StepState.Done : StepState.Failed, a11yOk ? null : "double-Home still works");
        if (!a11yOk) Log("The kiosk menu shortcut could not be turned on. Holding OK while another app is open may not work; press Home twice quickly instead.");

        Step(4, StepState.Running);
        var homes = await _ops.HomeActivitiesAsync(serial, quiet: false);
        if (!homes.Any(h => h.Pkg == KioskOps.KioskPkg))
        {
            Step(4, StepState.Failed);
            return FailRes("Android does not list the kiosk as a home screen, so nothing was turned off. Your TV is unchanged apart from the extra app.");
        }
        Step(4, StepState.Done);

        Step(5, StepState.Running);
        var previous = await _ops.LoadStateAsync(serial, deviceId);
        await _ops.SaveStateAsync(serial, deviceId, previous.Concat(toDisable ?? Array.Empty<string>()));
        int failed = 0;
        foreach (var pkg in toDisable ?? Array.Empty<string>())
        {
            if (ct.IsCancellationRequested) return FailRes("Stopped.");
            var r = await _adb.ShellAsync(serial, "pm disable-user --user 0 " + pkg);
            if (!r.Out.Contains("disabled")) failed++;
        }
        Step(5, failed == 0 ? StepState.Done : StepState.Failed, (toDisable?.Count ?? 0) - failed + "/" + (toDisable?.Count ?? 0) + " turned off");

        Step(6, StepState.Running);
        await _adb.ShellAsync(serial, "cmd package set-home-activity --user 0 " + KioskOps.HomeComponent);
        var resolved = await _ops.ResolveHomeAsync(serial);
        bool isDefault = resolved.StartsWith(KioskOps.KioskPkg);
        Step(6, isDefault ? StepState.Done : StepState.Failed, isDefault ? null : "TV may ask which home to use");
        if (!isDefault) Log("Home currently resolves to: " + resolved + ". If the TV asks, choose “Easy Display Kiosk Manager” and “Always”.");

        Step(7, StepState.Running);
        await _adb.ShellAsync(serial, "input keyevent KEYCODE_HOME");
        Step(7, StepState.Done);

        if (failed == 0 && isDefault)
        {
            return new KioskRunResult
            {
                Ok = true,
                Warn = !a11yOk,
                ShowDone = true,
                Message = "✔ Done! The TV now opens " + targetLabel + " on start-up and when Home is pressed."
            };
        }
        return new KioskRunResult
        {
            Ok = true,
            Warn = true,
            ShowDone = false,
            Message = "⚠ Mostly done — see the steps above and the log. The TV may ask which home app to use: choose “Easy Display Kiosk Manager” → Always."
        };
    }

    private static KioskRunResult FailRes(string message) => new() { Ok = false, Message = message };
}

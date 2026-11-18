using System.Reflection;

namespace EasyDKiosk;

public sealed partial class MainForm
{
    private readonly TextBox _wifiSsid = new()
    {
        Width = 280, PlaceholderText = "Shop Wi-Fi name"
    };
    private readonly ComboBox _wifiSecurity = new()
    {
        Width = 120, DropDownStyle = ComboBoxStyle.DropDownList
    };
    private readonly TextBox _wifiPassword = new()
    {
        Width = 280, UseSystemPasswordChar = true, PlaceholderText = "Password"
    };
    private readonly CheckBox _wifiShowPass = new()
    {
        Text = "Show password", AutoSize = true, Margin = new Padding(8, 10, 0, 0)
    };
    private readonly Button _moveWifi = Ui.Secondary("Move TV to shop Wi-Fi");
    private readonly Label _wifiResult = Ui.Text("", 640, Ui.Muted);
    private Image _wifiDiagramImage;

    private Control BuildShopWifiIntro(bool includeDiagram)
    {
        var wrap = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Margin = new Padding(0, 4, 0, 4)
        };
        wrap.Controls.Add(new Label
        {
            Text = "Shop Wi-Fi",
            AutoSize = true, Font = new Font("Segoe UI Semibold", 12f), ForeColor = Ui.Brand,
            Margin = new Padding(0, 4, 0, 4)
        });
        wrap.Controls.Add(Ui.Text(WifiMove.ShopWifiIntro(), 640));
        if (includeDiagram)
        {
            var pic = WifiSetupDiagramBox();
            if (pic != null) wrap.Controls.Add(pic);
        }
        return wrap;
    }

    private PictureBox WifiSetupDiagramBox()
    {
        try
        {
            _wifiDiagramImage ??= LoadWifiSetupDiagram();
        }
        catch { }
        if (_wifiDiagramImage == null) return null;
        return new PictureBox
        {
            Image = _wifiDiagramImage,
            SizeMode = PictureBoxSizeMode.Zoom,
            Width = 640, Height = 320,
            Margin = new Padding(0, 8, 0, 8),
            BackColor = Color.FromArgb(247, 249, 252),
            BorderStyle = BorderStyle.FixedSingle,
            AccessibleName = "Recommended: travel router child network"
        };
    }

    private static Image LoadWifiSetupDiagram()
    {
        var asm = Assembly.GetExecutingAssembly();
        using var s = asm.GetManifestResourceStream("wifi-setup-diagram.png");
        if (s == null) return null;
        using var tmp = Image.FromStream(s);
        return new Bitmap(tmp);
    }

    private Control BuildShopWifiSection()
    {
        var p = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Margin = new Padding(0, 12, 0, 8)
        };
        p.Controls.Add(new Label
        {
            Text = "Move TV to shop Wi-Fi (optional)",
            AutoSize = true, Font = new Font("Segoe UI Semibold", 13f), ForeColor = Ui.Brand,
            Margin = new Padding(0, 8, 0, 4)
        });
        p.Controls.Add(BuildShopWifiIntro(includeDiagram: true));
        p.Controls.Add(Ui.Text(WifiMove.ShopWifiStepHelp(), 640, Ui.Muted));

        p.Controls.Add(Ui.Row(new Label { Text = "Network name (SSID)", AutoSize = true }, _wifiSsid));

        _wifiSecurity.Items.AddRange(new object[] { "WPA2", "WPA3", "Open" });
        _wifiSecurity.SelectedIndex = 0;
        _wifiSecurity.SelectedIndexChanged += (_, _) => UpdateWifiPasswordEnabled();
        p.Controls.Add(Ui.Row(new Label { Text = "Security", AutoSize = true }, _wifiSecurity));

        _wifiShowPass.CheckedChanged += (_, _) =>
            _wifiPassword.UseSystemPasswordChar = !_wifiShowPass.Checked;
        p.Controls.Add(Ui.Row(new Label { Text = "Password", AutoSize = true }, _wifiPassword, _wifiShowPass));

        _moveWifi.Click += async (_, _) => await MoveTvToShopWifiAsync();
        _actionControls.Add(_moveWifi);
        p.Controls.Add(Ui.Row(_moveWifi));
        p.Controls.Add(_wifiResult);
        UpdateWifiPasswordEnabled();
        return p;
    }

    private void UpdateWifiPasswordEnabled()
    {
        bool open = string.Equals(_wifiSecurity.SelectedItem as string, "Open", StringComparison.OrdinalIgnoreCase);
        _wifiPassword.Enabled = !open;
        _wifiShowPass.Enabled = !open;
    }

    private void ClearShopWifiSecrets()
    {
        _wifiPassword.Text = "";
        _wifiShowPass.Checked = false;
        _wifiPassword.UseSystemPasswordChar = true;
        _wifiResult.Text = "";
        _wifiResult.ForeColor = Ui.Muted;
    }

    private void PrepareShopWifiOnDone()
    {
        _moveWifi.Enabled = _serial != null;
        _wifiResult.ForeColor = Ui.Muted;
        _wifiResult.Text = "";
        _wifiPassword.Text = "";
        _wifiShowPass.Checked = false;
        _wifiPassword.UseSystemPasswordChar = true;
        UpdateWifiPasswordEnabled();
    }

    private async Task MoveTvToShopWifiAsync()
    {
        if (string.IsNullOrEmpty(_serial))
        {
            _wifiResult.ForeColor = Ui.Warn;
            _wifiResult.Text = "This PC is no longer connected to the TV. Connect again, then try this step.";
            return;
        }

        var ssid = (_wifiSsid.Text ?? "").Trim();
        if (!WifiMove.TryParseSecurity(_wifiSecurity.SelectedItem as string ?? "WPA2", out var security))
            security = WifiMove.Security.Wpa2;
        var password = security == WifiMove.Security.Open ? "" : (_wifiPassword.Text ?? "");

        var problem = WifiMove.Validate(ssid, security, password);
        if (problem != null)
        {
            _wifiResult.ForeColor = Ui.Warn;
            _wifiResult.Text = problem;
            Status(problem);
            return;
        }

        var confirm = MessageBox.Show(this, WifiMove.ConfirmDetails(ssid, security, password),
            "Check shop Wi-Fi details", MessageBoxButtons.OKCancel, MessageBoxIcon.Information, MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.OK) return;

        SetBusy(true, "Saving shop Wi-Fi on the TV...");
        var serial = _serial;
        try
        {
            Log("=== Move TV to shop Wi-Fi: " + WifiMove.LogCommand(ssid, security, "add-network") + " ===");
            int.TryParse(await _ops.PropAsync(serial, "ro.build.version.sdk"), out var sdk);
            if (sdk > 0 && sdk < WifiMove.MinSdk)
            {
                ShowWifiManual("This TV runs Android " + (string.IsNullOrEmpty(_deviceAndroid) ? sdk.ToString() : _deviceAndroid)
                    + ", which is older than 11, so this PC cannot save the shop network automatically.\r\n\r\n" + WifiMove.ManualInstructions());
                return;
            }

            string currentSsid = "";
            List<string> before = new();
            try
            {
                var st = await _adb.ShellAsync(serial, "cmd wifi status", true, 8000);
                currentSsid = WifiMove.ParseConnectedSsid(st.All);
                var listed = await _adb.ShellAsync(serial, "cmd wifi list-networks", true, 8000);
                before = WifiMove.ParseSavedSsids(listed.All);
                Log("Saved networks before: " + string.Join(", ", before) + (currentSsid.Length > 0 ? " (connected: " + currentSsid + ")" : ""));
            }
            catch (Exception ex) { Log("Could not read current Wi-Fi list: " + ex.Message); }

            var cmd = WifiMove.BuildAddCommand(ssid, security, password);
            AdbResult r;
            try
            {
                r = await _adb.ShellStdinAsync(serial, cmd, WifiMove.LogCommand(ssid, security, "add-network"), 25000);
            }
            finally
            {
                // Drop the password from the form as soon as it has been sent. Never write it to disk.
                _wifiPassword.Text = "";
                _wifiShowPass.Checked = false;
                _wifiPassword.UseSystemPasswordChar = true;
            }

            var resultText = WifiMove.Redact((r.All ?? "").Trim(), password);
            // Never forget or remove the setup network.

            List<string> after = new();
            try
            {
                var listed = await _adb.ShellAsync(serial, "cmd wifi list-networks", true, 8000);
                after = WifiMove.ParseSavedSsids(listed.All);
                Log("Saved networks after: " + string.Join(", ", after));
            }
            catch (Exception ex) { Log("Could not re-read Wi-Fi list: " + ex.Message); }

            bool shopSaved = WifiMove.ListContainsSsid(after, ssid);
            bool setupKept = currentSsid.Length == 0
                || WifiMove.ListContainsSsid(after, currentSsid)
                || before.Count == 0;
            bool stillConnected = StillLooksConnected(await ProbeDeviceStateAsync(serial));
            bool accepted = r.Ok || WifiMove.LooksLikeSaveAccepted(resultText) || shopSaved;

            if (!accepted && stillConnected && !shopSaved)
            {
                var fail = "The TV did not save the shop Wi-Fi.";
                if (resultText.Length > 0) fail += " " + TrimResult(resultText);
                ShowWifiManual(fail + "\r\n\r\n" + WifiMove.ManualInstructions());
                return;
            }

            if (shopSaved && stillConnected)
            {
                var msg = "Shop Wi-Fi \"" + ssid + "\" is saved on this TV.";
                if (currentSsid.Length > 0 && !string.Equals(currentSsid, ssid, StringComparison.Ordinal))
                    msg += setupKept
                        ? " The setup network \"" + currentSsid + "\" is still saved alongside it, and this PC can still reach the TV."
                        : " This PC can still reach the TV.";
                else
                    msg += " This PC can still reach the TV.";
                msg += " The TV will join the shop network when it is in range. If you have more TVs to set up, stay on this setup Wi-Fi and use Set up another TV.";
                if (!setupKept && currentSsid.Length > 0)
                    msg += " Warning: the setup network \"" + currentSsid + "\" was not found in the saved list. It should have been left in place.";
                _wifiResult.ForeColor = setupKept ? Ui.Good : Ui.Warn;
                _wifiResult.Text = msg;
                Status("Shop Wi-Fi saved on the TV.");
                Log("Move TV result: " + msg);
                return;
            }

            Status("Checking whether the TV is still reachable...");
            var check = await CheckWifiMoveAsync(serial, ssid, currentSsid, shopSaved);
            _wifiResult.ForeColor = check.Good ? Ui.Good : (check.Lost ? Ui.Good : Ui.Warn);
            _wifiResult.Text = check.Message;
            if (!string.IsNullOrEmpty(resultText) && !check.Lost)
                _wifiResult.Text = check.Message + "\r\n" + TrimResult(resultText);
            Status(check.Lost || check.Good ? "Shop Wi-Fi command sent." : "Shop Wi-Fi: check the result below.");
            Log("Move TV result: " + check.Message);

            if (check.Lost)
            {
                _serial = null;
                _turnOffDebug.Enabled = false;
                _moveWifi.Enabled = false;
                _lblDevice.Text = "No TV connected";
                try { await _adb.RunAsync(8000, true, "disconnect", serial); } catch { }
            }
        }
        catch (Exception ex)
        {
            Log("Move TV to shop Wi-Fi: " + WifiMove.Redact(ex.Message, password));
            ShowWifiManual("Could not save the shop Wi-Fi on the TV: " + WifiMove.Redact(ex.Message, password)
                + "\r\n\r\n" + WifiMove.ManualInstructions());
        }
        finally
        {
            if (_busy) SetBusy(false);
            // SetBusy(false) re-enables action buttons; keep them off if the TV dropped.
            if (_serial == null)
            {
                _moveWifi.Enabled = false;
                _turnOffDebug.Enabled = false;
            }
        }
    }

    private void ShowWifiManual(string text)
    {
        _wifiResult.ForeColor = Ui.Warn;
        _wifiResult.Text = text;
        Status("Could not save the shop Wi-Fi automatically. See the instructions on this page.");
    }

    private async Task<string> ProbeDeviceStateAsync(string serial)
    {
        try
        {
            var r = await _adb.RunAsync(5000, true, "-s", serial, "get-state");
            return (r.Ok ? r.Out : r.All) ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static bool StillLooksConnected(string state)
        => (state ?? "").IndexOf("device", StringComparison.OrdinalIgnoreCase) >= 0
           && (state ?? "").IndexOf("not found", StringComparison.OrdinalIgnoreCase) < 0
           && (state ?? "").IndexOf("offline", StringComparison.OrdinalIgnoreCase) < 0
           && (state ?? "").IndexOf("unauthorized", StringComparison.OrdinalIgnoreCase) < 0;

    private async Task<(bool Good, bool Lost, string Message)> CheckWifiMoveAsync(string serial, string ssid, string previousSsid, bool shopSaved)
    {
        for (int i = 0; i < 4; i++)
        {
            await Task.Delay(2000);
            var state = await ProbeDeviceStateAsync(serial);
            if (!StillLooksConnected(state))
            {
                return (true, true,
                    "This PC can no longer reach the TV — it has probably joined \"" + ssid
                    + "\". The setup network should still be saved on the TV. Connect this PC to the shop Wi-Fi and use Find my TV. On Google TV / Chromecast, turn Wireless debugging back on first (and pair again if asked). If you have more TVs to set up, join this PC back to the setup Wi-Fi first.");
            }
        }

        string status = "";
        try
        {
            var r = await _adb.ShellAsync(serial, "cmd wifi status", true, 8000);
            status = (r.All ?? "").Trim();
        }
        catch { }

        var now = WifiMove.ParseConnectedSsid(status);
        if (!string.IsNullOrEmpty(now) && string.Equals(now, ssid, StringComparison.Ordinal))
        {
            return (true, false,
                "The TV reports it is already on \"" + ssid + "\", and this PC can still reach it. The setup network should still be saved on the TV.");
        }
        if (shopSaved)
        {
            return (true, false,
                "Shop Wi-Fi \"" + ssid + "\" is saved on this TV"
                + (previousSsid.Length > 0 ? ", and the setup network should still be saved alongside it" : "")
                + ". This PC can still reach the TV. The TV will join the shop network when it is in range.");
        }
        if (!string.IsNullOrEmpty(now))
        {
            return (false, false,
                "This PC can still reach the TV, which reports it is on \"" + now
                + "\", and the shop network \"" + ssid + "\" was not confirmed in the saved list. You can try again, or save it in Settings.\r\n\r\n"
                + WifiMove.ManualInstructions());
        }
        return (false, false,
            "The command was sent, but this PC cannot confirm that the shop network was saved. You can try again, or save it in Settings.\r\n\r\n"
            + WifiMove.ManualInstructions());
    }

    private static string TrimResult(string text)
    {
        var lines = text.Split('\n').Select(l => l.TrimEnd()).Where(l => l.Trim().Length > 0).Take(8);
        return string.Join("\r\n", lines);
    }
}

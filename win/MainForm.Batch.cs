namespace EasyDKiosk;

public sealed partial class MainForm
{
    private FlowLayoutPanel _batchPanel;
    private readonly Button _batchSetup = Ui.Primary("Set up selected TVs");
    private readonly Button _batchRetry = Ui.Secondary("Retry failed");
    private readonly CheckBox _batchWifi = new()
    {
        Text = "Save shop Wi-Fi on all selected TVs (same network for all)",
        AutoSize = true, Margin = new Padding(0, 8, 0, 2)
    };
    private readonly CheckBox _batchDebugOff = new()
    {
        Text = "Turn off debugging on all selected TVs when they are done",
        AutoSize = true, Margin = new Padding(0, 2, 0, 6)
    };
    private readonly TextBox _batchSsid = new() { Width = 240, PlaceholderText = "Shop Wi-Fi name" };
    private readonly ComboBox _batchSecurity = new() { Width = 100, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _batchPassword = new() { Width = 200, UseSystemPasswordChar = true, PlaceholderText = "Password" };
    private readonly CheckBox _batchShowPass = new() { Text = "Show password", AutoSize = true, Margin = new Padding(8, 10, 0, 0) };
    private readonly FlowLayoutPanel _batchWifiFields = new()
    {
        AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        FlowDirection = FlowDirection.TopDown, WrapContents = false, Visible = false, Margin = new Padding(0)
    };
    private readonly Label _batchSummary = Ui.Text("", 640, Ui.Muted);

    private Control BuildBatchSetupPanel()
    {
        _batchPanel = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Margin = new Padding(0, 8, 0, 12), Visible = false
        };
        _batchPanel.Controls.Add(new Label
        {
            Text = "Set up several TVs at once",
            AutoSize = true, Font = new Font("Segoe UI Semibold", 13f), ForeColor = Ui.Brand,
            Margin = new Padding(0, 4, 0, 4)
        });
        _batchPanel.Controls.Add(Ui.Text(
            "Tick the TVs you want, then click Set up selected TVs. Each one is made a kiosk for Easy Display Player, one after another. If a TV asks you to allow USB debugging, tick Always allow and press OK — that row will say Waiting for Allow. A failure does not stop the others. The ordinary one-TV path is unchanged: click a row to connect just that TV.",
            640, Ui.Muted));

        _batchWifi.CheckedChanged += (_, _) => _batchWifiFields.Visible = _batchWifi.Checked;
        _batchSecurity.Items.AddRange(new object[] { "WPA2", "WPA3", "Open" });
        _batchSecurity.SelectedIndex = 0;
        _batchSecurity.SelectedIndexChanged += (_, _) =>
        {
            bool open = string.Equals(_batchSecurity.SelectedItem as string, "Open", StringComparison.OrdinalIgnoreCase);
            _batchPassword.Enabled = !open;
            _batchShowPass.Enabled = !open;
        };
        _batchShowPass.CheckedChanged += (_, _) =>
            _batchPassword.UseSystemPasswordChar = !_batchShowPass.Checked;
        _batchWifiFields.Controls.Add(Ui.Row(new Label { Text = "Network name (SSID)", AutoSize = true }, _batchSsid));
        _batchWifiFields.Controls.Add(Ui.Row(new Label { Text = "Security", AutoSize = true }, _batchSecurity));
        _batchWifiFields.Controls.Add(Ui.Row(new Label { Text = "Password", AutoSize = true }, _batchPassword, _batchShowPass));

        _batchPanel.Controls.Add(_batchWifi);
        _batchPanel.Controls.Add(_batchWifiFields);
        _batchPanel.Controls.Add(_batchDebugOff);

        _batchSetup.Click += async (_, _) => await RunBatchSetupAsync(retryFailedOnly: false);
        _batchRetry.Click += async (_, _) => await RunBatchSetupAsync(retryFailedOnly: true);
        _batchRetry.Enabled = false;
        _actionControls.Add(_batchSetup);
        _actionControls.Add(_batchRetry);
        _batchPanel.Controls.Add(Ui.Row(_batchSetup, _batchRetry));
        _batchPanel.Controls.Add(_batchSummary);
        return _batchPanel;
    }

    private void ShowBatchPanelIfNeeded()
    {
        if (_batchPanel != null) _batchPanel.Visible = _tvHits.Count > 0;
    }

    private void SetHitBatchStatus(TvHit hit, string status, Color? color = null, string reason = null)
    {
        if (hit.BatchStatus == null) return;
        hit.LastBatchError = reason;
        hit.BatchStatus.ForeColor = color ?? Color.FromArgb(33, 33, 33);
        hit.BatchStatus.Text = string.IsNullOrEmpty(reason) ? status : status + " — " + reason;
    }

    private async Task RunBatchSetupAsync(bool retryFailedOnly)
    {
        var selected = _tvHits.Where(h => h.Pick != null && h.Pick.Checked).ToList();
        if (retryFailedOnly)
            selected = selected.Where(h => !string.IsNullOrEmpty(h.LastBatchError) || (h.BatchStatus != null && (h.BatchStatus.Text ?? "").StartsWith("Failed", StringComparison.Ordinal))).ToList();

        if (selected.Count == 0)
        {
            _batchSummary.ForeColor = Ui.Warn;
            _batchSummary.Text = retryFailedOnly ? "No failed TVs are selected." : "Tick at least one TV first.";
            return;
        }

        bool doWifi = _batchWifi.Checked;
        bool doDebug = _batchDebugOff.Checked;
        string ssid = "";
        var security = WifiMove.Security.Wpa2;
        string password = "";
        if (doWifi)
        {
            ssid = (_batchSsid.Text ?? "").Trim();
            if (!WifiMove.TryParseSecurity(_batchSecurity.SelectedItem as string ?? "WPA2", out security))
                security = WifiMove.Security.Wpa2;
            password = security == WifiMove.Security.Open ? "" : (_batchPassword.Text ?? "");
            var problem = WifiMove.Validate(ssid, security, password);
            if (problem != null)
            {
                _batchSummary.ForeColor = Ui.Warn;
                _batchSummary.Text = problem;
                return;
            }
        }

        var confirm = "Set up " + selected.Count + " selected TV" + (selected.Count == 1 ? "" : "s") + " as Easy Display Player kiosks?\r\n\r\n"
            + "Each TV will open Easy Display Player at start-up and when Home is pressed. A failure does not stop the others. You can retry failed TVs.\r\n\r\n"
            + "If a TV shows Allow USB debugging?, tick Always allow from this computer and press OK.";
        if (doWifi)
        {
            confirm += "\r\n\r\nCheck these shop Wi-Fi details (they will be saved on each TV; the setup network stays saved):\r\n"
                + "Network name (SSID):  " + ssid + "\r\n"
                + "Security:  " + WifiMove.SecurityLabel(security) + "\r\n"
                + "Password:  " + (security == WifiMove.Security.Open ? "(no password — Open network)" : password);
        }
        if (doDebug)
            confirm += "\r\n\r\nDebugging will be turned off on each TV after it is set up (and after shop Wi-Fi is saved, if you chose that). This PC will then lose that TV.";
        if (MessageBox.Show(this, confirm, "Set up selected TVs", MessageBoxButtons.OKCancel, MessageBoxIcon.Information, MessageBoxDefaultButton.Button2) != DialogResult.OK)
            return;

        SetBusy(true, "Setting up " + selected.Count + " TV(s)...");
        _batchRetry.Enabled = false;
        int done = 0, failed = 0;
        var ct = _pageCts.Token;
        try
        {
            foreach (var hit in selected)
            {
                SetHitBatchStatus(hit, "Waiting", Ui.Muted);
                hit.LastBatchError = null;
            }

            // Connect / wait for Allow on all selected first (so several remotes can be answered together).
            await Task.WhenAll(selected.Select(h => EnsureHitReadyAsync(h, TimeSpan.FromSeconds(120), ct)));

            foreach (var hit in selected)
            {
                if (ct.IsCancellationRequested) break;
                if (hit.State != "device" || string.IsNullOrEmpty(hit.Serial))
                {
                    if ((hit.BatchStatus?.Text ?? "").StartsWith("Failed", StringComparison.Ordinal)) { failed++; continue; }
                    SetHitBatchStatus(hit, "Failed", Ui.Bad, "Could not connect. If the TV asked to allow USB debugging, tick Always allow and press OK, then retry.");
                    failed++;
                    continue;
                }

                SetHitBatchStatus(hit, "Installing", Ui.Brand);
                Status("Installing on " + (string.IsNullOrWhiteSpace(hit.Model) ? hit.Endpoint.Address : hit.Model) + "...");
                Log("=== Batch: Make Easy Display Kiosk on " + hit.Endpoint.Address + " ===");
                try
                {
                    string deviceId = await _ops.PropAsync(hit.Serial, "ro.serialno");
                    if (string.IsNullOrEmpty(deviceId)) deviceId = hit.Serial;
                    var cands = await _ops.CandidatesToDisableAsync(hit.Serial, KioskOps.DefaultTarget);
                    var toDisable = cands.Where(c => c.Recommended).Select(c => c.Package).ToList();
                    var result = await RunMakeKioskCoreAsync(hit.Serial, deviceId, KioskOps.DefaultTarget, KioskOps.DefaultTargetName,
                        toDisable, null, ct);
                    if (!result.Ok && !result.ShowDone)
                    {
                        SetHitBatchStatus(hit, "Failed", Ui.Bad, result.Message);
                        failed++;
                        continue;
                    }

                    if (doWifi)
                    {
                        SetHitBatchStatus(hit, "Saving shop Wi-Fi", Ui.Brand);
                        var wifi = await SaveShopWifiOnAsync(hit.Serial, ssid, security, password);
                        if (!wifi.ok)
                        {
                            SetHitBatchStatus(hit, "Failed", Ui.Bad, "Kiosk is set up, but shop Wi-Fi was not saved: " + wifi.reason);
                            failed++;
                            continue;
                        }
                    }

                    if (doDebug)
                    {
                        SetHitBatchStatus(hit, "Turning off debugging", Ui.Brand);
                        await TurnOffDebuggingOnAsync(hit.Serial);
                        hit.State = "none";
                    }

                    SetHitBatchStatus(hit, "Done", Ui.Good);
                    hit.LastBatchError = null;
                    hit.KioskInstalled = true;
                    done++;
                }
                catch (Exception ex)
                {
                    Log("Batch " + hit.Endpoint.Address + ": " + WifiMove.Redact(ex.Message, password));
                    SetHitBatchStatus(hit, "Failed", Ui.Bad, ex.Message);
                    failed++;
                }
            }
        }
        finally
        {
            if (doWifi)
            {
                _batchPassword.Text = "";
                _batchShowPass.Checked = false;
                _batchPassword.UseSystemPasswordChar = true;
            }
            if (_busy) SetBusy(false);
        }

        _batchSummary.ForeColor = failed == 0 ? Ui.Good : (done > 0 ? Ui.Warn : Ui.Bad);
        _batchSummary.Text = "Finished: " + done + " done, " + failed + " failed, of " + selected.Count + " selected."
            + (failed > 0 ? " Click Retry failed to try the failed TVs again (leave them ticked)." : "")
            + (doWifi && done > 0 ? " Shop Wi-Fi was saved without removing the setup network. The TVs will join it when it is in range." : "")
            + (doDebug && done > 0 ? " Debugging was turned off on the TVs that finished. To reach one again, turn USB debugging back on (and Wireless debugging on Google TV), then use Find my TV." : "");
        _batchRetry.Enabled = failed > 0;
        Status(_batchSummary.Text);
    }

    private async Task EnsureHitReadyAsync(TvHit hit, TimeSpan wait, CancellationToken ct)
    {
        try
        {
            await _adb.RunAsync(15000, true, "connect", hit.Endpoint.Address);
            var deadline = DateTime.UtcNow + wait;
            bool sawUnauthorized = false;
            while (!ct.IsCancellationRequested && DateTime.UtcNow < deadline)
            {
                var devs = await _adb.DevicesAllAsync();
                var found = devs.FirstOrDefault(d => d.Serial == hit.Endpoint.Address)
                    ?? devs.FirstOrDefault(d => d.Serial == hit.Serial)
                    ?? devs.FirstOrDefault(d => d.Serial.StartsWith(hit.Endpoint.Ip + ":"));
                if (found != null && found.State == "device")
                {
                    hit.Serial = found.Serial;
                    hit.State = "device";
                    if (string.IsNullOrWhiteSpace(hit.Model))
                    {
                        try { hit.Model = await _ops.PropAsync(found.Serial, "ro.product.model"); } catch { }
                    }
                    SetHitBatchStatus(hit, "Waiting", Ui.Muted);
                    RefreshTvRow(hit);
                    return;
                }
                if (found != null && found.State == "unauthorized")
                {
                    hit.Serial = found.Serial;
                    hit.State = "unauthorized";
                    sawUnauthorized = true;
                    SetHitBatchStatus(hit, "Waiting for Allow", Ui.Warn, "On the TV, tick Always allow and press OK");
                }
                try { await Task.Delay(1500, ct); } catch (TaskCanceledException) { return; }
            }
            if (hit.State != "device")
            {
                SetHitBatchStatus(hit, "Failed", Ui.Bad,
                    sawUnauthorized
                        ? "Timed out waiting for Allow on the TV. Tick Always allow, press OK, then retry."
                        : "Could not connect to this TV.");
            }
        }
        catch (Exception ex)
        {
            SetHitBatchStatus(hit, "Failed", Ui.Bad, ex.Message);
        }
    }

    private async Task<(bool ok, string reason)> SaveShopWifiOnAsync(string serial, string ssid, WifiMove.Security security, string password)
    {
        int.TryParse(await _ops.PropAsync(serial, "ro.build.version.sdk"), out var sdk);
        if (sdk > 0 && sdk < WifiMove.MinSdk)
            return (false, "This TV is older than Android 11.");

        string current = "";
        List<string> before = new();
        try
        {
            var st = await _adb.ShellAsync(serial, "cmd wifi status", true, 8000);
            current = WifiMove.ParseConnectedSsid(st.All);
            var listed = await _adb.ShellAsync(serial, "cmd wifi list-networks", true, 8000);
            before = WifiMove.ParseSavedSsids(listed.All);
        }
        catch { }

        var cmd = WifiMove.BuildAddCommand(ssid, security, password);
        var r = await _adb.ShellStdinAsync(serial, cmd, WifiMove.LogCommand(ssid, security, "add-network"), 25000);
        var text = WifiMove.Redact((r.All ?? "").Trim(), password);

        List<string> after = new();
        try
        {
            var listed = await _adb.ShellAsync(serial, "cmd wifi list-networks", true, 8000);
            after = WifiMove.ParseSavedSsids(listed.All);
        }
        catch { }

        bool shopSaved = WifiMove.ListContainsSsid(after, ssid);
        if (shopSaved) return (true, "");
        if (r.Ok || WifiMove.LooksLikeSaveAccepted(text)) return (true, "");
        var reason = text.Length > 0 ? text : "The TV did not save the shop network.";
        if (reason.Length > 180) reason = reason.Substring(0, 180);
        return (false, reason);
    }

    private async Task TurnOffDebuggingOnAsync(string serial)
    {
        try
        {
            await _adb.ShellAsync(serial, "settings put global adb_enabled 0; settings put global adb_wifi_enabled 0", false, 8000);
        }
        catch (Exception ex) { Log("adb_enabled: " + ex.Message); }
        try { await _adb.RunAsync(10000, true, "disconnect", serial); } catch { }
    }

    private void ClearBatchSecrets()
    {
        _batchPassword.Text = "";
        _batchShowPass.Checked = false;
        _batchPassword.UseSystemPasswordChar = true;
        _batchSummary.Text = "";
        _batchRetry.Enabled = false;
    }
}

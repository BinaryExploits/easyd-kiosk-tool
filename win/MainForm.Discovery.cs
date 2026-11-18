using System.Net;
using System.Text.RegularExpressions;

namespace EasyDKiosk;

public sealed partial class MainForm
{
    private sealed class TvHit
    {
        public DiscoveredEndpoint Endpoint;
        public string Serial;
        public string State = "unknown"; // device, unauthorized, none
        public string Model;
        public string DeviceName;
        public bool? KioskInstalled;
        public Panel Row;
        public Label Info;
        public Button IdentifyBtn;
    }

    private readonly List<TvHit> _tvHits = new();
    private static readonly Regex AddrParts = new(
        @"^(?<ip>\d{1,3}(?:\.\d{1,3}){3})(?::(?<port>\d{2,5}))?$");

    private async Task FindTvsAsync()
    {
        if (string.IsNullOrEmpty(_adb.AdbPath)) return;
        _pageCts.Cancel();
        var ct = _pageCts = new CancellationTokenSource();
        _foundTvs.Controls.Clear();
        _tvHits.Clear();
        _identifyBanner.Visible = false;
        _scanStatus.ForeColor = Ui.Muted;
        _scanStatus.Text = "Scanning this Wi‑Fi for TVs…";
        SetBusy(true, "Finding TVs...");
        try
        {
            var extras = new List<(string Ip, int Port, string Source)>();
            try
            {
                foreach (var s in await _adb.MdnsServicesAsync())
                {
                    var m = AddrParts.Match(s.Address ?? "");
                    if (!m.Success || !m.Groups["port"].Success) continue;
                    extras.Add((m.Groups["ip"].Value, int.Parse(m.Groups["port"].Value), "mdns-" + s.Type));
                }
            }
            catch (Exception ex) { Log("mDNS scan: " + ex.Message); }

            var hosts = TvDiscovery.HostsInLocalSlash24s();
            Log($"Scanning {hosts.Count} address(es) on port 5555, plus {extras.Count} mDNS result(s).");
            List<string> open;
            try { open = await TvDiscovery.ScanTcpAsync(hosts, 5555, 300, 96, ct.Token); }
            catch (OperationCanceledException) { return; }
            if (ct.IsCancellationRequested) return;

            var endpoints = TvDiscovery.Merge(open, extras);
            if (endpoints.Count == 0)
            {
                _scanStatus.ForeColor = Ui.Warn;
                _scanStatus.Text = "No TVs were found. Guest or shop Wi‑Fi often blocks device discovery. Type the TV’s IP address below instead.";
                Status("No TVs found.");
                return;
            }

            _scanStatus.Text = $"Found {endpoints.Count} device(s). Reading details — if a TV shows “Allow USB debugging?”, tick Always allow and press OK.";
            Status("Reading TV details...");
            foreach (var ep in endpoints)
            {
                if (ct.IsCancellationRequested) return;
                var hit = new TvHit { Endpoint = ep };
                BuildTvRow(hit);
                _tvHits.Add(hit);
                _foundTvs.Controls.Add(hit.Row);
                await ProbeHitAsync(hit, ct.Token);
            }

            _ = PollUnauthorizedAsync(ct.Token);
            int ready = _tvHits.Count(h => h.State == "device");
            int waiting = _tvHits.Count(h => h.State == "unauthorized");
            _scanStatus.ForeColor = Color.FromArgb(33, 33, 33);
            _scanStatus.Text = waiting > 0
                ? $"Found {endpoints.Count}. {waiting} waiting for you to allow USB debugging on the TV."
                : $"Found {endpoints.Count} TV(s)" + (ready > 0 ? $", {ready} ready to connect." : ".");
            Status("Select a TV, or type an IP below.");
        }
        catch (Exception ex)
        {
            Log("Find my TV: " + ex.Message);
            _scanStatus.ForeColor = Ui.Bad;
            _scanStatus.Text = "The scan failed: " + ex.Message;
        }
        finally { if (_busy) SetBusy(false); }
    }

    private void BuildTvRow(TvHit hit)
    {
        var row = new Panel
        {
            Width = 640, Height = 56, Margin = new Padding(0, 2, 0, 2),
            BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(248, 250, 253),
            Cursor = Cursors.Hand
        };
        var info = new Label
        {
            AutoSize = false, Width = 500, Height = 52, Left = 8, Top = 2,
            Font = new Font("Segoe UI", 9.5f), ForeColor = Color.FromArgb(33, 33, 33),
            Text = hit.Endpoint.Address + "  ·  reading…",
            Cursor = Cursors.Hand
        };
        var idBtn = Ui.Secondary("Identify");
        idBtn.Margin = new Padding(0);
        idBtn.Left = 515;
        idBtn.Top = 8;
        idBtn.Enabled = false;
        idBtn.Click += async (_, _) => await IdentifyHitAsync(hit);
        void use(object s, EventArgs e)
        {
            if (s == idBtn) return;
            _ = UseHitAsync(hit);
        }
        row.Click += use;
        info.Click += use;
        row.Controls.Add(info);
        row.Controls.Add(idBtn);
        hit.Row = row;
        hit.Info = info;
        hit.IdentifyBtn = idBtn;
        _actionControls.Add(idBtn);
    }

    private void RefreshTvRow(TvHit hit)
    {
        string kiosk = hit.KioskInstalled == true ? "Kiosk already installed"
            : hit.KioskInstalled == false ? "No kiosk yet" : "";
        if (hit.State == "unauthorized")
        {
            hit.Info.Text = hit.Endpoint.Address + "  ·  Waiting for Allow on TV";
            hit.Info.ForeColor = Ui.Warn;
            hit.IdentifyBtn.Enabled = false;
            return;
        }
        if (hit.State != "device")
        {
            hit.Info.Text = hit.Endpoint.Address + "  ·  Could not read details";
            hit.Info.ForeColor = Ui.Muted;
            hit.IdentifyBtn.Enabled = false;
            return;
        }
        var bits = new List<string> { hit.Endpoint.Address };
        if (!string.IsNullOrWhiteSpace(hit.Model)) bits.Add(hit.Model);
        if (!string.IsNullOrWhiteSpace(hit.DeviceName) && hit.DeviceName != "null") bits.Add(hit.DeviceName);
        if (kiosk.Length > 0) bits.Add(kiosk);
        hit.Info.Text = string.Join("   ·   ", bits);
        hit.Info.ForeColor = Color.FromArgb(33, 33, 33);
        hit.IdentifyBtn.Enabled = true;
    }

    private async Task ProbeHitAsync(TvHit hit, CancellationToken ct)
    {
        var addr = hit.Endpoint.Address;
        try
        {
            await _adb.RunAsync(15000, true, "connect", addr);
            if (ct.IsCancellationRequested) return;
            AdbDevice found = null;
            for (int i = 0; i < 8 && !ct.IsCancellationRequested; i++)
            {
                var devs = await _adb.DevicesAllAsync();
                found = devs.FirstOrDefault(d => d.Serial == addr)
                    ?? devs.FirstOrDefault(d => d.Serial.StartsWith(hit.Endpoint.Ip + ":"));
                if (found != null && (found.State == "device" || found.State == "unauthorized")) break;
                try { await Task.Delay(400, ct); } catch (TaskCanceledException) { return; }
            }
            if (found == null)
            {
                hit.State = "none";
                RefreshTvRow(hit);
                return;
            }
            hit.Serial = found.Serial;
            hit.State = found.State;
            if (found.State != "device")
            {
                RefreshTvRow(hit);
                return;
            }
            hit.Model = await _ops.PropAsync(found.Serial, "ro.product.model");
            var name = (await _adb.ShellAsync(found.Serial, "settings get global device_name", true, 8000)).Out.Trim();
            hit.DeviceName = (name == "null") ? "" : name;
            hit.KioskInstalled = await _ops.IsInstalledAsync(found.Serial, KioskOps.KioskPkg);
            RefreshTvRow(hit);
        }
        catch (Exception ex)
        {
            Log($"Probe {addr}: {ex.Message}");
            hit.State = "none";
            RefreshTvRow(hit);
        }
    }

    private async Task PollUnauthorizedAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var waiting = _tvHits.Where(h => h.State == "unauthorized" && h.Serial != null).ToList();
                if (waiting.Count == 0) return;
                try { await Task.Delay(1500, ct); } catch (TaskCanceledException) { return; }
                var devs = await _adb.DevicesAllAsync();
                foreach (var hit in waiting)
                {
                    if (ct.IsCancellationRequested) return;
                    var d = devs.FirstOrDefault(x => x.Serial == hit.Serial)
                        ?? devs.FirstOrDefault(x => x.Serial.StartsWith(hit.Endpoint.Ip + ":"));
                    if (d != null && d.State == "device")
                    {
                        hit.Serial = d.Serial;
                        hit.State = "device";
                        hit.Model = await _ops.PropAsync(d.Serial, "ro.product.model");
                        var name = (await _adb.ShellAsync(d.Serial, "settings get global device_name", true, 8000)).Out.Trim();
                        hit.DeviceName = (name == "null") ? "" : name;
                        hit.KioskInstalled = await _ops.IsInstalledAsync(d.Serial, KioskOps.KioskPkg);
                        RefreshTvRow(hit);
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log("Scan poll: " + ex.Message); }
    }

    private async Task UseHitAsync(TvHit hit)
    {
        if (hit?.Endpoint == null) return;
        _ipAddr.Text = hit.Endpoint.Port == 5555 ? hit.Endpoint.Ip : hit.Endpoint.Address;
        _connAddr.Text = hit.Endpoint.Address;
        if (hit.Endpoint.Port == 5555)
        {
            if (!_modeIp.Checked) { _connectModePicked = true; _modeIp.Checked = true; ShowConnectMode(); }
            await ConnectByIpAsync();
        }
        else
        {
            if (!_modePair.Checked) { _connectModePicked = true; _modePair.Checked = true; ShowConnectMode(); }
            await ConnectWirelessAsync();
        }
    }

    private async Task IdentifyHitAsync(TvHit hit)
    {
        if (hit == null) return;
        if (hit.State != "device" || string.IsNullOrEmpty(hit.Serial))
        {
            _identifyBanner.Visible = true;
            _identifyBanner.Text = "Waiting for Allow on TV — tick Always allow and press OK, then try Identify again.";
            return;
        }
        string code = Random.Shared.Next(0, 10000).ToString("D4");
        _identifyBanner.Visible = true;
        _identifyBanner.Text = "Look at the TV for this code:  " + code;
        Log($"Identify {hit.Endpoint.Address}: {code}");
        Status("Code " + code + " — compare it with the TV.");
        bool shown = false;
        try
        {
            if (hit.KioskInstalled != false)
            {
                // IdentifyActivity is exported for explicit adb starts (required on Android 11 TV).
                var start = await _adb.ShellAsync(hit.Serial,
                    "am start --user 0 -n com.easyd.kiosk/.IdentifyActivity --es code " + code + " --activity-clear-top",
                    true, 15000);
                shown = start.Ok && !start.All.Contains("Error") && !start.All.Contains("Exception") && !start.All.Contains("Permission Denial");
                if (!shown)
                {
                    var bc = await _ops.BroadcastAsync(hit.Serial, "com.easyd.kiosk.IDENTIFY", "--es code " + code);
                    shown = bc.Code == 1;
                }
            }
            if (!shown)
            {
                // Reliable fallback on Android 11 Xiaomi / Google TV when the kiosk app is not installed yet:
                // a heads-up / notification with the same code.
                var post = await _adb.ShellAsync(hit.Serial,
                    "cmd notification post -S bigtext -t \"Kiosk Manager\" km-identify \"This TV's code is " + code + "\"",
                    true, 15000);
                shown = post.Ok || post.All.Contains("posting");
                if (shown)
                    _identifyBanner.Text = "Look at the TV for this code:  " + code + "  (notification)";
            }
            if (!shown)
            {
                _identifyBanner.Text = "Could not show a code on that TV. The number was " + code + " — try again after installing the kiosk, or use the IP to tell TVs apart.";
            }
        }
        catch (Exception ex)
        {
            Log("Identify: " + ex.Message);
            _identifyBanner.Text = "Could not show a code on that TV (" + ex.Message + ").";
        }
    }

    private async Task TurnOffDebuggingAsync()
    {
        if (string.IsNullOrEmpty(_serial))
        {
            _doneDebugNote.ForeColor = Ui.Warn;
            _doneDebugNote.Text = "This PC is no longer connected to the TV.";
            return;
        }
        SetBusy(true, "Turning off debugging...");
        var serial = _serial;
        try
        {
            Log("=== Turn off debugging ===");
            // Both settings: USB debugging, and Wireless debugging on Google TV. Harmless if the second is unused.
            try
            {
                await _adb.ShellAsync(serial,
                    "settings put global adb_enabled 0; settings put global adb_wifi_enabled 0",
                    false, 8000);
            }
            catch (Exception ex) { Log("adb_enabled command: " + ex.Message); }

            try { await _adb.RunAsync(10000, false, "disconnect", serial); }
            catch (Exception ex) { Log("disconnect: " + ex.Message); }

            await Task.Delay(800);
            bool still = false;
            try
            {
                var devs = await _adb.DevicesAllAsync();
                still = devs.Any(d => d.Serial == serial && d.State == "device");
            }
            catch { }

            if (still)
            {
                _doneDebugNote.ForeColor = Ui.Warn;
                _doneDebugNote.Text = "The TV is still connected. Debugging may not have turned off. You can try the button again, or turn USB debugging off in Developer options on the TV.";
                Status("Debugging may still be on.");
                return;
            }

            _serial = null;
            _turnOffDebug.Enabled = false;
            _lblDevice.Text = "No TV connected";
            _doneDebugNote.ForeColor = Ui.Good;
            _doneDebugNote.Text = "Debugging is off on this TV, and this PC has disconnected. To change or undo the kiosk later, turn USB debugging back on in Developer options (and Wireless debugging on Google TV), then connect again with Kiosk Manager.";
            Status("Debugging turned off.");
            Log("=== Debugging off ===");
        }
        catch (Exception ex)
        {
            _doneDebugNote.ForeColor = Ui.Warn;
            _doneDebugNote.Text = "Could not turn off debugging: " + ex.Message;
            Log("Turn off debugging: " + ex.Message);
        }
        finally { if (_busy) SetBusy(false); }
    }
}

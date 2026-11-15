using System.Text.RegularExpressions;

namespace EasyDKiosk;

public sealed class MainForm : Form
{
    private readonly Adb _adb;
    private readonly KioskOps _ops;

    // shell
    private readonly Label _lblDevice = new() { AutoSize = true, ForeColor = Color.White, Font = new Font("Segoe UI", 10f), Text = "No TV connected" };
    private readonly Panel _content = new() { Dock = DockStyle.Fill, BackColor = Color.White };
    private readonly List<Label> _navItems = new();
    private readonly List<Control> _pages = new();
    private readonly TextBox _log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font("Consolas", 9f), BackColor = Color.FromArgb(250, 250, 250), BorderStyle = BorderStyle.None, WordWrap = true };
    private readonly ToolStripStatusLabel _status = new() { Text = "Ready", Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripProgressBar _progress = new() { Style = ProgressBarStyle.Marquee, Visible = false, Width = 140 };
    private readonly Button _back = Ui.Secondary("◀  Back");
    private readonly Button _next = Ui.Primary("Next  ▶");
    private readonly List<Control> _actionControls = new();
    private int _page;
    private bool _busy;
    private CancellationTokenSource _pageCts = new();

    // state
    private string _serial;      // adb serial of the connected TV
    private string _deviceId;    // ro.serialno (stable across reconnects)
    private string _target = KioskOps.DefaultTarget;
    private string _targetLabel = KioskOps.DefaultTargetName;
    private bool _targetReady;

    // page 2: pair
    private readonly ListBox _found = new() { Width = 560, Height = 90, IntegralHeight = false };
    private readonly TextBox _pairAddr = new() { Width = 220, PlaceholderText = "192.168.1.43:37123" };
    private readonly TextBox _pairCode = new() { Width = 120, MaxLength = 6, PlaceholderText = "123456", Font = new Font("Segoe UI", 12f) };
    private readonly Label _pairResult = Ui.Text("");
    // page 3: connect
    private readonly TextBox _connAddr = new() { Width = 220, PlaceholderText = "192.168.1.43:41234" };
    private readonly ComboBox _devices = new() { Width = 300, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label _connResult = Ui.Text("");
    // page 4: app
    private readonly Label _appStatus = new() { AutoSize = true, MaximumSize = new Size(640, 0), Font = new Font("Segoe UI Semibold", 13f), Margin = new Padding(0, 8, 0, 8) };
    private readonly Label _appHint = Ui.Text("", 640, Ui.Muted);
    private readonly Panel _advanced = new() { Width = 660, Height = 300, Visible = false };
    private readonly TextBox _filter = new() { Width = 260, PlaceholderText = "Filter by name or package" };
    private readonly CheckBox _showSystem = new() { Text = "Show system apps", AutoSize = true };
    private readonly ListView _apps = new() { View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, Width = 640, Height = 220 };
    private List<AppEntry> _appList = new();
    // page 5: kiosk
    private readonly Label _targetInfo = new() { AutoSize = true, Font = new Font("Segoe UI Semibold", 11f), Margin = new Padding(0, 4, 0, 8) };
    private readonly CheckedListBox _homes = new() { Width = 640, Height = 90, CheckOnClick = true, IntegralHeight = false };
    private readonly ListView _steps = new() { View = View.Details, Width = 640, Height = 190, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable };
    private readonly Label _kioskResult = new() { AutoSize = true, MaximumSize = new Size(640, 0), Font = new Font("Segoe UI Semibold", 11f), Margin = new Padding(0, 6, 0, 0) };

    private static readonly string[] NavTitles = { "1  Prepare the TV", "2  Pair", "3  Connect", "4  EasyD Player", "5  Make kiosk" };

    public MainForm()
    {
        Text = "EasyD Kiosk Setup";
        Font = new Font("Segoe UI", 10f);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1040, 780);
        MinimumSize = new Size(900, 660);
        BackColor = Color.White;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        _adb = new Adb(Log);
        _ops = new KioskOps(_adb, Log);
        BuildShell();
        BuildPages();
        ShowPage(0);
        Shown += async (_, _) => await InitAdbAsync();
        FormClosing += (_, _) => _pageCts.Cancel();
    }

    // ───────────────────────────── layout ─────────────────────────────
    private void BuildShell()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 74, BackColor = Ui.Brand, Padding = new Padding(20, 10, 20, 10) };
        var titles = new FlowLayoutPanel { Dock = DockStyle.Left, FlowDirection = FlowDirection.TopDown, AutoSize = true, BackColor = Ui.Brand, WrapContents = false };
        titles.Controls.Add(new Label { Text = "EasyD Kiosk Setup", AutoSize = true, ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 17f), Margin = new Padding(0) });
        titles.Controls.Add(new Label { Text = "Make an Android TV / Google TV device show only EasyD Player", AutoSize = true, ForeColor = Color.FromArgb(200, 215, 240), Margin = new Padding(2, 0, 0, 0) });
        header.Controls.Add(titles);
        var devHost = new Panel { Dock = DockStyle.Right, Width = 380, BackColor = Ui.Brand };
        _lblDevice.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        devHost.Controls.Add(_lblDevice);
        devHost.Resize += (_, _) => _lblDevice.Location = new Point(devHost.Width - _lblDevice.Width, 16);
        _lblDevice.SizeChanged += (_, _) => _lblDevice.Location = new Point(devHost.Width - _lblDevice.Width, 16);
        header.Controls.Add(devHost);

        var nav = new FlowLayoutPanel { Dock = DockStyle.Left, Width = 210, BackColor = Ui.Nav, FlowDirection = FlowDirection.TopDown, Padding = new Padding(0, 16, 0, 0), WrapContents = false };
        for (int i = 0; i < NavTitles.Length; i++)
        {
            int idx = i;
            var l = new Label { Text = NavTitles[i], Width = 210, Height = 42, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(18, 0, 0, 0), Margin = new Padding(0), Cursor = Cursors.Hand, Font = new Font("Segoe UI", 10.5f) };
            l.Click += (_, _) => { if (!_busy && CanEnter(idx)) ShowPage(idx); };
            _navItems.Add(l);
            nav.Controls.Add(l);
        }

        var logBox = new GroupBox { Text = "Log", Dock = DockStyle.Bottom, Height = 170, Padding = new Padding(8, 4, 8, 8), Font = new Font("Segoe UI", 9f) };
        logBox.Controls.Add(_log);
        var split = new Splitter { Dock = DockStyle.Bottom, Height = 4, BackColor = Ui.Nav };

        var status = new StatusStrip { SizingGrip = true, BackColor = Ui.Nav };
        status.Items.Add(_status);
        status.Items.Add(_progress);

        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 54, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(16, 4, 16, 4), BackColor = Color.White };
        footer.Controls.Add(_next);
        footer.Controls.Add(_back);
        _back.Click += (_, _) => { if (_page > 0) ShowPage(_page - 1); };
        _next.Click += (_, _) => { if (_page < _pages.Count - 1 && CanEnter(_page + 1)) ShowPage(_page + 1); };

        var main = new Panel { Dock = DockStyle.Fill };
        main.Controls.Add(_content);
        main.Controls.Add(footer);

        Controls.Add(main);
        Controls.Add(split);
        Controls.Add(logBox);
        Controls.Add(nav);
        Controls.Add(header);
        Controls.Add(status);
    }

    private void BuildPages()
    {
        _pages.Add(PagePrepare());
        _pages.Add(PagePair());
        _pages.Add(PageConnect());
        _pages.Add(PageApp());
        _pages.Add(PageKiosk());
        foreach (var p in _pages) _content.Controls.Add(p);
    }

    private Control PagePrepare()
    {
        var p = Ui.Page();
        p.Controls.Add(Ui.Heading("Prepare the TV"));
        p.Controls.Add(Ui.Text("Do this once on the TV with the remote. Keep the TV on and this PC on the same Wi‑Fi network as the TV."));
        var tabs = new TabControl { Width = 660, Height = 300, Margin = new Padding(0, 10, 0, 6) };
        tabs.TabPages.Add(InstrTab("Android TV  (Xiaomi Mi TV Stick 4K, most TV boxes)",
            "1.  Open Settings  ›  Device Preferences  ›  About.\r\n" +
            "2.  Scroll to “Build” and press OK on it 7 times, until it says “You are now a developer”.\r\n" +
            "3.  Go back to Settings  ›  Device Preferences  ›  Developer options.\r\n" +
            "4.  Turn on “USB debugging”, then open “Wireless debugging” and turn it on (choose “Always allow on this network” if asked).\r\n" +
            "5.  Inside Wireless debugging choose “Pair device with pairing code”. Leave that screen open and click Next here.\r\n\r\n" +
            "Needs Android TV 11 or newer. The older Mi TV Stick (Android 9) has no Wireless debugging."));
        tabs.TabPages.Add(InstrTab("Google TV  (Chromecast with Google TV, Google TV Streamer)",
            "1.  Open Settings  ›  System  ›  About.\r\n" +
            "2.  Scroll to “Android TV OS build” and press OK on it 7 times (enter your PIN if asked) until it says “You are now a developer”.\r\n" +
            "3.  Go back to Settings  ›  System  ›  Developer options.\r\n" +
            "4.  Turn on “USB debugging”, then open “Wireless debugging” and turn it on.\r\n" +
            "5.  Inside Wireless debugging choose “Pair device with pairing code”. Leave that screen open and click Next here."));
        p.Controls.Add(tabs);
        p.Controls.Add(Ui.Text("Tip: if Windows Firewall asks about “adb”, click Allow (Private networks) so the TV can be found automatically.", 640, Ui.Muted));
        return p;
    }

    private static TabPage InstrTab(string title, string body)
    {
        var t = new TabPage(title) { BackColor = Color.White, Padding = new Padding(12) };
        t.Controls.Add(new TextBox { Text = body, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Color.White, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10.5f), TabStop = false });
        return t;
    }

    private Control PagePair()
    {
        var p = Ui.Page();
        p.Controls.Add(Ui.Heading("Pair this PC with the TV"));
        p.Controls.Add(Ui.Text("On the TV: Developer options › Wireless debugging › “Pair device with pairing code”. The TV shows a 6‑digit code and an IP address:port. Keep that screen open."));
        var scan = Ui.Secondary("🔍  Search for the TV");
        scan.Click += async (_, _) => await ScanAsync(forPairing: true, _pageCts.Token);
        _actionControls.Add(scan);
        p.Controls.Add(Ui.Row(scan, Ui.Text("Found on the network:", 300, Ui.Muted)));
        _found.SelectedIndexChanged += (_, _) =>
        {
            if (_found.SelectedItem is MdnsService s)
            {
                if (s.Type == "pairing") _pairAddr.Text = s.Address; else _connAddr.Text = s.Address;
            }
        };
        p.Controls.Add(_found);
        p.Controls.Add(Ui.Row(Ui.Text("Pairing IP address:port"), _pairAddr));
        var pair = Ui.Primary("Pair");
        pair.Click += async (_, _) => await PairAsync();
        _actionControls.Add(pair);
        _pairCode.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await PairAsync(); } };
        p.Controls.Add(Ui.Row(Ui.Text("Pairing code"), _pairCode, pair));
        p.Controls.Add(_pairResult);
        var skip = new LinkLabel { Text = "This PC is already paired with the TV — skip to Connect", AutoSize = true, Margin = new Padding(0, 14, 0, 0) };
        skip.LinkClicked += (_, _) => ShowPage(2);
        p.Controls.Add(skip);
        return p;
    }

    private Control PageConnect()
    {
        var p = Ui.Page();
        p.Controls.Add(Ui.Heading("Connect to the TV"));
        p.Controls.Add(Ui.Text("Usually this is filled in automatically. If not, type the IP address:port shown at the top of the TV's Wireless debugging screen (it is different from the pairing port)."));
        var find = Ui.Secondary("🔍  Find automatically");
        find.Click += async (_, _) => await ScanAsync(forPairing: false, _pageCts.Token);
        var conn = Ui.Primary("Connect");
        conn.Click += async (_, _) => await ConnectAsync();
        _actionControls.Add(find); _actionControls.Add(conn);
        p.Controls.Add(Ui.Row(Ui.Text("TV address (IP:port)"), _connAddr, conn, find));
        p.Controls.Add(_connResult);
        p.Controls.Add(Ui.Text("Already connected devices (for example over USB):", 640, Ui.Muted));
        var refresh = Ui.Secondary("Refresh");
        var use = Ui.Secondary("Use this device");
        refresh.Click += async (_, _) => await RefreshDevicesAsync();
        use.Click += async (_, _) => { if (_devices.SelectedItem is string s) await UseDeviceAsync(s); };
        _actionControls.Add(refresh); _actionControls.Add(use);
        p.Controls.Add(Ui.Row(_devices, use, refresh));
        return p;
    }

    private Control PageApp()
    {
        var p = Ui.Page();
        p.Controls.Add(Ui.Heading("EasyD Player"));
        p.Controls.Add(Ui.Text($"The kiosk will open {KioskOps.DefaultTargetName} ({KioskOps.DefaultTarget}) every time the TV starts or Home is pressed.", 640, Ui.Muted));
        p.Controls.Add(_appStatus);
        p.Controls.Add(_appHint);
        var again = Ui.Secondary("Open Play Store on the TV again");
        var check = Ui.Secondary("Check again");
        again.Click += async (_, _) => await OpenStoreAsync();
        check.Click += async (_, _) => await CheckTargetAsync(autoAdvance: true, _pageCts.Token);
        _actionControls.Add(again); _actionControls.Add(check);
        p.Controls.Add(Ui.Row(check, again));

        var adv = new LinkLabel { Text = "Advanced: choose a different app", AutoSize = true, Margin = new Padding(0, 18, 0, 4) };
        adv.LinkClicked += async (_, _) =>
        {
            _advanced.Visible = !_advanced.Visible;
            if (_advanced.Visible && _appList.Count == 0 && _serial != null) await LoadAppsAsync();
        };
        p.Controls.Add(adv);

        _apps.Columns.Add("App", 260);
        _apps.Columns.Add("Package", 360);
        _apps.SelectedIndexChanged += (_, _) => { };
        var useApp = Ui.Primary("Use selected app");
        var useDefault = Ui.Secondary("Back to EasyD Player");
        var reload = Ui.Secondary("Reload list");
        useApp.Click += async (_, _) =>
        {
            if (_apps.SelectedItems.Count == 0) { MessageBox.Show(this, "Select an app in the list first.", Text); return; }
            var a = (AppEntry)_apps.SelectedItems[0].Tag;
            _target = a.Package; _targetLabel = a.Label;
            Log($"Target app set to {a.Label} ({a.Package}).");
            await CheckTargetAsync(autoAdvance: false, _pageCts.Token);
        };
        useDefault.Click += async (_, _) =>
        {
            _target = KioskOps.DefaultTarget; _targetLabel = KioskOps.DefaultTargetName;
            await CheckTargetAsync(autoAdvance: false, _pageCts.Token);
        };
        reload.Click += async (_, _) => await LoadAppsAsync();
        _actionControls.Add(useApp); _actionControls.Add(useDefault); _actionControls.Add(reload);
        _filter.TextChanged += (_, _) => FillApps();
        _showSystem.CheckedChanged += (_, _) => FillApps();
        var advFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        advFlow.Controls.Add(Ui.Row(_filter, _showSystem));
        advFlow.Controls.Add(_apps);
        advFlow.Controls.Add(Ui.Row(useApp, useDefault, reload));
        _advanced.Height = 320;
        _advanced.Controls.Add(advFlow);
        p.Controls.Add(_advanced);
        return p;
    }

    private Control PageKiosk()
    {
        var p = Ui.Page();
        p.Controls.Add(Ui.Heading("Make the kiosk"));
        p.Controls.Add(_targetInfo);
        p.Controls.Add(Ui.Text("These home screens will be turned off (not deleted). All other apps stay installed. Untick anything you want to keep:", 640, Ui.Muted));
        p.Controls.Add(_homes);
        var make = Ui.Primary("✔  Make kiosk");
        make.BackColor = Ui.Good;
        make.Font = new Font("Segoe UI Semibold", 11.5f);
        var restore = Ui.Secondary("↺  Restore original");
        restore.ForeColor = Ui.Bad; restore.FlatAppearance.BorderColor = Ui.Bad;
        make.Click += async (_, _) => await MakeKioskAsync();
        restore.Click += async (_, _) => await RestoreAsync();
        _actionControls.Add(make); _actionControls.Add(restore);
        p.Controls.Add(Ui.Row(make, restore));
        _steps.Columns.Add("Step", 430);
        _steps.Columns.Add("Status", 190);
        p.Controls.Add(_steps);
        p.Controls.Add(_kioskResult);
        p.Controls.Add(Ui.Text("Hidden escape on the TV: right after pressing Home, hold OK for 3 seconds (or press Up Up Down Down Left Right Left Right) to open Android Settings.", 640, Ui.Muted));
        return p;
    }

    // ───────────────────────────── navigation ─────────────────────────────
    private bool CanEnter(int idx)
    {
        if (idx <= 2) return true;
        if (_serial == null) { Status("Connect to the TV first."); return false; }
        if (idx == 4 && !_targetReady) { Status("The kiosk app is not installed on the TV yet."); return false; }
        return true;
    }

    private void ShowPage(int idx)
    {
        _pageCts.Cancel();
        _pageCts = new CancellationTokenSource();
        _page = idx;
        for (int i = 0; i < _pages.Count; i++) _pages[i].Visible = i == idx;
        RefreshNav();
        _back.Visible = idx > 0;
        _next.Visible = idx < _pages.Count - 1;
        StartPageWork(idx);
    }

    private void RefreshNav()
    {
        for (int i = 0; i < _navItems.Count; i++)
        {
            bool cur = i == _page;
            _navItems[i].BackColor = cur ? Ui.BrandLight : Ui.Nav;
            _navItems[i].ForeColor = cur ? Ui.Brand : (CanEnterQuiet(i) ? Color.FromArgb(40, 40, 40) : Color.FromArgb(160, 160, 160));
            _navItems[i].Font = new Font(cur ? "Segoe UI Semibold" : "Segoe UI", 10.5f);
        }
    }

    private void StartPageWork(int idx)
    {
        var token = _pageCts.Token;
        switch (idx)
        {
            case 1: _ = ScanAsync(forPairing: true, token, quietIfNone: true); break;
            case 2:
                _ = RefreshDevicesAsync();
                if (string.IsNullOrWhiteSpace(_connAddr.Text)) _ = ScanAsync(forPairing: false, token, quietIfNone: true);
                break;
            case 3: _ = CheckTargetAsync(autoAdvance: true, token); break;
            case 4: _ = PrepareKioskPageAsync(); break;
        }
    }

    private bool CanEnterQuiet(int idx) => idx <= 2 || (_serial != null && (idx == 3 || _targetReady));

    private void SetBusy(bool busy, string text = null)
    {
        _busy = busy;
        _progress.Visible = busy;
        foreach (var c in _actionControls) c.Enabled = !busy;
        _next.Enabled = _back.Enabled = !busy;
        if (text != null) Status(text);
    }

    private void Status(string s) => _status.Text = s;

    private void Log(string s)
    {
        if (InvokeRequired) { BeginInvoke(() => Log(s)); return; }
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {s}\r\n");
    }

    // ───────────────────────────── actions ─────────────────────────────
    private async Task InitAdbAsync()
    {
        SetBusy(true, "Preparing adb...");
        try
        {
            await _adb.EnsureAsync();
            await _adb.RunAsync("start-server");
            var chk = await _adb.RunAsync(15000, true, "mdns", "check");
            Log("mDNS: " + chk.All.Replace("\n", " "));
            Status("Ready. Follow the steps on the left.");
        }
        catch (Exception ex)
        {
            Log("ERROR: " + ex.Message);
            Status("adb could not be prepared — see the log.");
            MessageBox.Show(this, "Could not prepare the Android tools (adb):\n\n" + ex.Message + "\n\nCheck your internet connection or antivirus and start the app again.", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { SetBusy(false); }
    }

    private async Task ScanAsync(bool forPairing, CancellationToken ct, bool quietIfNone = false)
    {
        if (string.IsNullOrEmpty(_adb.AdbPath)) return;
        string want = forPairing ? "pairing" : "connect";
        Status(forPairing ? "Searching for a TV that is waiting to pair..." : "Searching for the TV...");
        Log($"Searching the network for TVs (mDNS, {want})...");
        for (int i = 0; i < 12 && !ct.IsCancellationRequested; i++)
        {
            List<MdnsService> svcs;
            try { svcs = await _adb.MdnsServicesAsync(); } catch { break; }
            if (ct.IsCancellationRequested) return;
            var prevSel = _found.SelectedItem as MdnsService;
            _found.Items.Clear();
            foreach (var s in svcs) _found.Items.Add(s);
            var hit = svcs.FirstOrDefault(s => s.Type == want);
            if (hit != null)
            {
                if (forPairing) { if (string.IsNullOrWhiteSpace(_pairAddr.Text) || prevSel == null) _pairAddr.Text = hit.Address; _pairCode.Focus(); }
                else _connAddr.Text = hit.Address;
                Log($"Found {hit.Address} ({hit.Name}).");
                Status(forPairing ? "TV found. Type the pairing code shown on the TV and click Pair." : "TV found. Click Connect.");
                return;
            }
            try { await Task.Delay(2500, ct); } catch { return; }
        }
        if (ct.IsCancellationRequested) return;
        Log("No TV found automatically.");
        Status(forPairing
            ? "TV not found automatically — type the IP address:port shown under the pairing code."
            : "TV not found automatically — type the IP address:port shown on the Wireless debugging screen.");
        if (!quietIfNone) MessageBox.Show(this, "The TV was not found automatically.\n\nMake sure the TV is showing the Wireless debugging screen and that this PC is on the same Wi‑Fi, or type the address shown on the TV.", Text);
    }

    private static readonly Regex AddrRx = new(@"^\s*(\d{1,3}(?:\.\d{1,3}){3})(?::(\d{1,5}))?\s*$");

    private async Task PairAsync()
    {
        var m = AddrRx.Match(_pairAddr.Text);
        if (!m.Success || !m.Groups[2].Success) { MessageBox.Show(this, "Type the pairing address exactly as shown on the TV, e.g. 192.168.1.43:37123", Text); return; }
        var code = _pairCode.Text.Trim();
        if (!Regex.IsMatch(code, @"^\d{6}$")) { MessageBox.Show(this, "The pairing code has 6 digits.", Text); return; }
        _pageCts.Cancel();
        SetBusy(true, "Pairing...");
        try
        {
            var r = await _adb.RunAsync(30000, false, "pair", _pairAddr.Text.Trim(), code);
            if (r.All.Contains("Successfully paired"))
            {
                _pairResult.ForeColor = Ui.Good; _pairResult.Text = "✔ Paired. Connecting...";
                if (string.IsNullOrWhiteSpace(_connAddr.Text))
                {
                    // the connect port differs from the pairing port; find it via mDNS
                    for (int i = 0; i < 6 && string.IsNullOrWhiteSpace(_connAddr.Text); i++)
                    {
                        var svc = (await _adb.MdnsServicesAsync()).FirstOrDefault(s => s.Type == "connect" && s.Address.StartsWith(m.Groups[1].Value + ":"));
                        if (svc != null) _connAddr.Text = svc.Address; else await Task.Delay(1500);
                    }
                }
                SetBusy(false);
                ShowPage(2);
                if (!string.IsNullOrWhiteSpace(_connAddr.Text)) await ConnectAsync();
                else
                {
                    // mDNS auto-connect may already have attached the device
                    await RefreshDevicesAsync();
                    var auto = _devices.Items.Cast<string>().FirstOrDefault(d => d.StartsWith(m.Groups[1].Value + ":") || d.Contains("_adb-tls-connect"));
                    if (auto != null) await UseDeviceAsync(auto);
                    else Status("Paired. Now type the IP address:port shown at the top of the Wireless debugging screen and click Connect.");
                }
                return;
            }
            _pairResult.ForeColor = Ui.Bad;
            _pairResult.Text = "✖ Pairing failed. Check the code and address (the code changes each time you open the pairing screen).";
            Status("Pairing failed.");
        }
        finally { if (_busy) SetBusy(false); }
    }

    private async Task ConnectAsync()
    {
        var addr = _connAddr.Text.Trim();
        var m = AddrRx.Match(addr);
        if (!m.Success) { MessageBox.Show(this, "Type the TV address shown on the Wireless debugging screen, e.g. 192.168.1.43:41234", Text); return; }
        if (!m.Groups[2].Success) addr += ":5555";
        _pageCts.Cancel();
        SetBusy(true, "Connecting...");
        try
        {
            await _adb.RunAsync(20000, false, "connect", addr);
            string serial = null;
            for (int i = 0; i < 6 && serial == null; i++)
            {
                var devs = await _adb.DevicesAsync();
                serial = devs.FirstOrDefault(d => d == addr) ?? devs.FirstOrDefault(d => d.StartsWith(m.Groups[1].Value + ":"));
                if (serial == null) await Task.Delay(1000);
            }
            if (serial == null)
            {
                var st = await _adb.RunAsync(10000, true, "-s", addr, "get-state");
                if (st.Out.Trim() == "unauthorized")
                {
                    _connResult.ForeColor = Ui.Warn;
                    _connResult.Text = "The TV is asking for permission. Choose “Allow” on the TV, then click Connect again.";
                }
                else
                {
                    _connResult.ForeColor = Ui.Bad;
                    _connResult.Text = "✖ Could not connect. Check that Wireless debugging is on, the address is right, and this PC is paired (step 2).";
                }
                Status("Not connected.");
                return;
            }
            SetBusy(false);
            await UseDeviceAsync(serial);
        }
        finally { if (_busy) SetBusy(false); }
    }

    private async Task RefreshDevicesAsync()
    {
        if (string.IsNullOrEmpty(_adb.AdbPath)) return;
        var devs = await _adb.DevicesAsync();
        _devices.Items.Clear();
        foreach (var d in devs) _devices.Items.Add(d);
        if (_devices.Items.Count > 0) _devices.SelectedIndex = 0;
    }

    private async Task UseDeviceAsync(string serial)
    {
        SetBusy(true, "Reading device details...");
        try
        {
            var model = await _ops.PropAsync(serial, "ro.product.model");
            var rel = await _ops.PropAsync(serial, "ro.build.version.release");
            int.TryParse(await _ops.PropAsync(serial, "ro.build.version.sdk"), out var sdk);
            _deviceId = await _ops.PropAsync(serial, "ro.serialno");
            if (string.IsNullOrEmpty(_deviceId)) _deviceId = serial;
            if (string.IsNullOrEmpty(model) && sdk == 0)
            {
                _connResult.ForeColor = Ui.Bad; _connResult.Text = "✖ The device did not answer. Accept the “Allow debugging?” prompt on the TV and try again.";
                return;
            }
            _serial = serial;
            _appList.Clear();
            _lblDevice.Text = $"📺  {model}  ·  Android {rel}  ·  {serial}";
            Log($"Connected: {model}, Android {rel} (SDK {sdk}), serial {serial}");
            _connResult.ForeColor = Ui.Good;
            _connResult.Text = $"✔ Connected to {model} (Android {rel}).";
            if (sdk > 0 && sdk < 30)
            {
                _connResult.ForeColor = Ui.Warn;
                _connResult.Text += "\nThis device runs Android older than 11, which the kiosk app does not support.";
                MessageBox.Show(this, $"{model} runs Android {rel}. The kiosk needs Android 11 or newer.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            SetBusy(false);
            ShowPage(3);
        }
        finally { if (_busy) SetBusy(false); }
    }

    private async Task CheckTargetAsync(bool autoAdvance, CancellationToken ct)
    {
        if (_serial == null) return;
        bool openedStore = false;
        bool isDefault = _target == KioskOps.DefaultTarget;
        _appStatus.ForeColor = Color.FromArgb(33, 33, 33);
        _appStatus.Text = $"Checking for {_targetLabel}...";
        _appHint.Text = "";
        Status($"Checking whether {_targetLabel} is installed...");
        _targetReady = false;
        var started = DateTime.Now;
        while (!ct.IsCancellationRequested)
        {
            bool installed;
            try { installed = await _ops.IsInstalledAsync(_serial, _target, quiet: !openedStore ? false : true); }
            catch { return; }
            if (ct.IsCancellationRequested) return;
            if (installed)
            {
                var ver = await _ops.VersionNameAsync(_serial, _target);
                _targetReady = true;
                _appStatus.ForeColor = Ui.Good;
                _appStatus.Text = $"✔ {_targetLabel} is installed{(ver.Length > 0 ? " (version " + ver + ")" : "")}.";
                _appHint.Text = $"Package: {_target}";
                Log($"{_target} is installed {ver}.");
                Status("Ready to make the kiosk.");
                RefreshNav();
                if (autoAdvance && _page == 3)
                {
                    await Task.Delay(1200);
                    if (_page == 3 && !_advanced.Visible) ShowPage(4);
                }
                return;
            }
            if (!isDefault)
            {
                _appStatus.ForeColor = Ui.Bad;
                _appStatus.Text = $"✖ {_target} is not installed on the TV.";
                return;
            }
            if (!openedStore)
            {
                openedStore = true;
                Log($"{_target} is not installed. Opening its Play Store page on the TV.");
                await OpenStoreAsync();
            }
            if (DateTime.Now - started > TimeSpan.FromMinutes(15))
            {
                _appHint.Text = "Still not installed. Install it on the TV, then click “Check again”.";
                Status("Waiting stopped.");
                return;
            }
            try { await Task.Delay(4000, ct); } catch { return; }
        }
    }

    private async Task OpenStoreAsync()
    {
        if (_serial == null) return;
        var r = await _ops_StartStore();
        _appStatus.ForeColor = Ui.Warn;
        if (r.All.Contains("Error") || r.All.Contains("unable to resolve"))
        {
            _appStatus.Text = "Install EasyD Player on the TV";
            _appHint.Text = "The Play Store could not be opened on the TV. Install EasyD Player on the TV another way — this page continues automatically once it is installed.";
        }
        else
        {
            _appStatus.Text = "Press Install on the TV with your remote";
            _appHint.Text = "The EasyD Player page is open in the Play Store on the TV. This page continues automatically once it is installed.";
        }
        Status("Waiting for EasyD Player to be installed on the TV...");
    }

    private Task<AdbResult> _ops_StartStore()
        => _adb.ShellAsync(_serial, $"am start -a android.intent.action.VIEW -d market://details?id={KioskOps.DefaultTarget}");

    private async Task LoadAppsAsync()
    {
        if (_serial == null) return;
        SetBusy(true, "Reading the apps installed on the TV...");
        try
        {
            _appList = await _ops.ListAppsAsync(_serial);
            Log($"{_appList.Count} launchable apps found.");
            FillApps();
            Status("Pick the app the kiosk should open.");
        }
        finally { SetBusy(false); }
    }

    private void FillApps()
    {
        var f = _filter.Text.Trim();
        _apps.BeginUpdate();
        _apps.Items.Clear();
        foreach (var a in _appList)
        {
            if (a.System && !_showSystem.Checked && !a.Package.Contains("easyd", StringComparison.OrdinalIgnoreCase)) continue;
            if (f.Length > 0 && !a.Label.Contains(f, StringComparison.OrdinalIgnoreCase) && !a.Package.Contains(f, StringComparison.OrdinalIgnoreCase)) continue;
            var it = new ListViewItem(new[] { a.Label, a.Package }) { Tag = a };
            _apps.Items.Add(it);
            bool match = a.Package == _target || (_target == KioskOps.DefaultTarget && (a.Package.Contains("easyd", StringComparison.OrdinalIgnoreCase) || a.Label.Contains("easyd", StringComparison.OrdinalIgnoreCase)));
            if (match && _apps.SelectedItems.Count == 0) { it.Selected = true; it.EnsureVisible(); }
        }
        _apps.EndUpdate();
    }

    private async Task PrepareKioskPageAsync()
    {
        _targetInfo.Text = $"Kiosk app: {_targetLabel}  ({_target})";
        _kioskResult.Text = "";
        if (_serial == null) return;
        SetBusy(true, "Looking for home screens on the TV...");
        try
        {
            var cands = await _ops.CandidatesToDisableAsync(_serial, _target);
            _homes.Items.Clear();
            foreach (var c in cands) _homes.Items.Add(c, c.Recommended);
            if (cands.Count == 0) Log("No other home screens are currently enabled.");
            var kioskThere = await _ops.IsInstalledAsync(_serial, KioskOps.KioskPkg);
            var res = kioskThere ? await _ops.ResolveHomeAsync(_serial) : "";
            if (res.StartsWith(KioskOps.KioskPkg))
            {
                _kioskResult.ForeColor = Ui.Good; _kioskResult.Text = "This TV is already in kiosk mode. You can run it again or restore the original.";
            }
            Status("Review the list, then click Make kiosk.");
        }
        finally { SetBusy(false); }
    }

    private void ResetSteps(params string[] names)
    {
        _steps.Items.Clear();
        foreach (var n in names) _steps.Items.Add(new ListViewItem(new[] { n, "○ Waiting" }) { ForeColor = Ui.Muted });
    }

    private void SetStep(int i, StepState s, string note = null)
    {
        var it = _steps.Items[i];
        (string txt, Color col) = s switch
        {
            StepState.Running => ("… Working", Ui.Brand),
            StepState.Done => ("✔ Done", Ui.Good),
            StepState.Failed => ("✖ Failed", Ui.Bad),
            StepState.Skipped => ("– Skipped", Ui.Muted),
            _ => ("○ Waiting", Ui.Muted),
        };
        it.SubItems[1].Text = note == null ? txt : $"{txt} · {note}";
        it.ForeColor = col;
        if (s == StepState.Running) Status(it.Text + "...");
    }

    private void Fail(string msg)
    {
        _kioskResult.ForeColor = Ui.Bad;
        _kioskResult.Text = "✖ " + msg;
        Log("FAILED: " + msg);
        Status("Stopped — nothing further was changed.");
    }

    private async Task MakeKioskAsync()
    {
        if (_serial == null) return;
        var toDisable = _homes.CheckedItems.Cast<HomeCandidate>().Select(c => c.Package).ToList();
        if (MessageBox.Show(this,
                $"Make this TV a kiosk for {_targetLabel}?\n\nThe TV will open {_targetLabel} at start-up and whenever Home is pressed. " +
                (toDisable.Count > 0 ? "These home screens will be turned off:\n  " + string.Join("\n  ", toDisable) : "No home screens will be turned off.") +
                "\n\nYou can undo this any time with “Restore original”.",
                Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

        ResetSteps($"Check {_targetLabel} is installed", "Install the kiosk launcher", $"Set {_targetLabel} as the kiosk app",
            "Check the kiosk works as a home screen", "Turn off the other home screens", "Make the kiosk the default home", "Go to the home screen");
        _kioskResult.Text = "";
        SetBusy(true);
        var s = _serial;
        try
        {
            Log("=== Make kiosk ===");
            SetStep(0, StepState.Running);
            if (!await _ops.IsInstalledAsync(s, _target, false)) { SetStep(0, StepState.Failed); Fail($"{_target} is not installed on the TV."); return; }
            SetStep(0, StepState.Done);

            SetStep(1, StepState.Running);
            if (!await _ops.InstallKioskAsync(s)) { SetStep(1, StepState.Failed); Fail("The kiosk launcher could not be installed (see log)."); return; }
            SetStep(1, StepState.Done);

            SetStep(2, StepState.Running);
            var (code, data) = await _ops.BroadcastAsync(s, "com.easyd.kiosk.SET_TARGET", $"--es target {_target}");
            Log($"  kiosk replied: {code} {data}");
            if (code != 1 || data != "OK:" + _target)
            {
                SetStep(2, StepState.Failed, data);
                Fail(data.Contains("not_launchable") ? $"{_target} has no TV launcher activity, so it cannot be opened as the home app." : "The kiosk app did not accept the setting (see log).");
                return;
            }
            SetStep(2, StepState.Done);

            SetStep(3, StepState.Running);
            var homes = await _ops.HomeActivitiesAsync(s, quiet: false);
            if (!homes.Any(h => h.Pkg == KioskOps.KioskPkg))
            {
                SetStep(3, StepState.Failed);
                Fail("Android does not list the kiosk as a home screen, so nothing was turned off. Your TV is unchanged apart from the extra app.");
                return;
            }
            SetStep(3, StepState.Done);

            SetStep(4, StepState.Running);
            // Remember what we turn off (merged with any earlier run) so Restore can undo it, even from another PC.
            var previous = await _ops.LoadStateAsync(s, _deviceId);
            await _ops.SaveStateAsync(s, _deviceId, previous.Concat(toDisable));
            int failed = 0;
            foreach (var p in toDisable)
            {
                var r = await _adb.ShellAsync(s, $"pm disable-user --user 0 {p}");
                if (!r.Out.Contains("disabled")) failed++;
            }
            SetStep(4, failed == 0 ? StepState.Done : StepState.Failed, $"{toDisable.Count - failed}/{toDisable.Count} turned off");

            SetStep(5, StepState.Running);
            await _adb.ShellAsync(s, $"cmd package set-home-activity --user 0 {KioskOps.HomeComponent}");
            var resolved = await _ops.ResolveHomeAsync(s);
            bool isDefault = resolved.StartsWith(KioskOps.KioskPkg);
            SetStep(5, isDefault ? StepState.Done : StepState.Failed, isDefault ? null : "TV may ask which home to use");
            if (!isDefault) Log($"Home currently resolves to: {resolved}. If the TV asks, choose “{"EasyD Kiosk"}” and “Always”.");

            SetStep(6, StepState.Running);
            await _adb.ShellAsync(s, "input keyevent KEYCODE_HOME");
            SetStep(6, StepState.Done);

            _kioskResult.ForeColor = failed == 0 && isDefault ? Ui.Good : Ui.Warn;
            _kioskResult.Text = failed == 0 && isDefault
                ? $"✔ Done! The TV now opens {_targetLabel} on start-up and when Home is pressed."
                : "⚠ Mostly done — see the steps above and the log. The TV may ask which home app to use: choose “EasyD Kiosk” → Always.";
            Status("Kiosk ready.");
            Log("=== Kiosk ready ===");
        }
        catch (Exception ex) { Fail(ex.Message); }
        finally { SetBusy(false); }
    }

    private async Task RestoreAsync()
    {
        if (_serial == null) return;
        if (MessageBox.Show(this, "Restore the TV's original home screen and remove the kiosk launcher?", Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        ResetSteps("Turn the original home screens back on", "Remove the kiosk launcher", "Go to the home screen");
        _kioskResult.Text = "";
        SetBusy(true);
        var s = _serial;
        try
        {
            Log("=== Restore original ===");
            SetStep(0, StepState.Running);
            var recorded = await _ops.LoadStateAsync(s, _deviceId);
            var disabled = await _ops.PackagesAsync(s, "-d");
            var toEnable = recorded.Concat(KioskOps.KnownStock).Where(disabled.Contains).Distinct().ToList();
            Log(toEnable.Count == 0 ? "Nothing to re-enable." : "Re-enabling: " + string.Join(", ", toEnable));
            foreach (var p in toEnable) await _adb.ShellAsync(s, $"pm enable {p}");
            SetStep(0, StepState.Done, $"{toEnable.Count} turned on");

            SetStep(1, StepState.Running);
            if (await _ops.IsInstalledAsync(s, KioskOps.KioskPkg))
            {
                await _ops.BroadcastAsync(s, "com.easyd.kiosk.DISABLE_HOME");
                var r = await _adb.RunAsync(60000, false, "-s", s, "uninstall", KioskOps.KioskPkg);
                SetStep(1, r.Out.Contains("Success") ? StepState.Done : StepState.Failed);
            }
            else SetStep(1, StepState.Skipped, "not installed");
            await _ops.ClearStateAsync(s, _deviceId);

            // Prefer the stock launcher as default again if several homes exist.
            var homes = await _ops.HomeActivitiesAsync(s);
            var stock = homes.FirstOrDefault(h => h.Pkg == "com.google.android.apps.tv.launcherx");
            if (stock.Pkg == null) stock = homes.FirstOrDefault(h => h.Pkg == "com.google.android.tvlauncher");
            if (stock.Pkg != null) await _adb.ShellAsync(s, $"cmd package set-home-activity --user 0 {stock.Component}");

            SetStep(2, StepState.Running);
            await _adb.ShellAsync(s, "input keyevent KEYCODE_HOME");
            SetStep(2, StepState.Done);
            _kioskResult.ForeColor = Ui.Good;
            _kioskResult.Text = "✔ Restored. The TV is back to its original home screen.";
            Status("Restored.");
            await PrepareKioskPageAsyncSilent();
        }
        catch (Exception ex) { Fail(ex.Message); }
        finally { SetBusy(false); }
    }

    private async Task PrepareKioskPageAsyncSilent()
    {
        var cands = await _ops.CandidatesToDisableAsync(_serial, _target);
        _homes.Items.Clear();
        foreach (var c in cands) _homes.Items.Add(c, c.Recommended);
    }
}

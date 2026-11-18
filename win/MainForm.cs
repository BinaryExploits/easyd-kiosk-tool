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
    private FlowLayoutPanel _footer;
    private readonly Button _back = Ui.Secondary("◀  Back");
    private readonly Button _next = Ui.Primary("Next  ▶");
    private readonly List<Control> _actionControls = new();
    private int _page;
    private bool _busy;
    private CancellationTokenSource _pageCts = new();

    // state
    private string _serial;      // adb serial of the connected TV
    private string _deviceId;    // ro.serialno (stable across reconnects)
    private string _deviceModel;
    private string _deviceAndroid;
    private string _target = KioskOps.DefaultTarget;
    private string _targetLabel = KioskOps.DefaultTargetName;
    private bool _targetReady;
    private const int DonePage = 4;

    // page 2: pair
    private readonly ListBox _found = new() { Width = 560, Height = 90, IntegralHeight = false };
    private readonly TextBox _pairAddr = new() { Width = 220, PlaceholderText = "192.168.1.43:37123" };
    private readonly TextBox _pairCode = new() { Width = 120, MaxLength = 6, PlaceholderText = "123456", Font = new Font("Segoe UI", 12f) };
    private readonly Label _pairResult = Ui.Text("");
    // page 2: connect (IP or pairing)
    private readonly TabControl _prepTabs = new() { Width = 660, Height = 300, Margin = new Padding(0, 10, 0, 6) };
    private readonly RadioButton _modeIp = new()
    {
        Text = "Xiaomi / Android TV: Connect by IP (USB debugging)",
        AutoSize = true, Checked = true, Margin = new Padding(0, 8, 0, 2)
    };
    private readonly RadioButton _modePair = new()
    {
        Text = "Google TV / Chromecast: Pair with code (Wireless debugging)",
        AutoSize = true, Margin = new Padding(0, 2, 0, 10)
    };
    private readonly FlowLayoutPanel _ipPanel = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };
    private readonly FlowLayoutPanel _pairPanel = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };
    private readonly TextBox _ipAddr = new() { Width = 220, PlaceholderText = "192.168.1.43" };
    private readonly TextBox _connAddr = new() { Width = 220, PlaceholderText = "192.168.1.43:41234" };
    private readonly ComboBox _devices = new() { Width = 300, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label _connResult = Ui.Text("");
    private bool _connectModePicked;
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
    // done screen
    private readonly Label _doneTitle = new() { AutoSize = true, Font = new Font("Segoe UI Semibold", 28f), ForeColor = Ui.Good, Margin = new Padding(0, 8, 0, 4), Text = "All done!" };
    private readonly Label _doneSubtitle = new() { AutoSize = true, MaximumSize = new Size(640, 0), Font = new Font("Segoe UI Semibold", 14f), ForeColor = Color.FromArgb(33, 33, 33), Margin = new Padding(0, 4, 0, 12), Text = "This TV now opens EasyD Player only" };
    private readonly Label _doneDevice = new() { AutoSize = true, MaximumSize = new Size(640, 0), Font = new Font("Segoe UI", 11f), ForeColor = Color.FromArgb(33, 33, 33), Margin = new Padding(0, 4, 0, 16) };

    private static readonly string[] NavTitles = { "1  Prepare the TV", "2  Connect", "3  EasyD Player", "4  Make kiosk" };

    public MainForm()
    {
        Text = "EasyDisplay Kiosk Manager";
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
        titles.Controls.Add(new Label { Text = "EasyDisplay Kiosk Manager", AutoSize = true, ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 17f), Margin = new Padding(0) });
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
            l.Click += (_, _) => { if (!_busy && _page != DonePage && CanEnter(idx)) ShowPage(idx); };
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
        _footer = footer;
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
        _pages.Add(PageConnect());
        _pages.Add(PageApp());
        _pages.Add(PageKiosk());
        _pages.Add(PageDone());
        foreach (var p in _pages) _content.Controls.Add(p);
    }

    private Control PagePrepare()
    {
        var p = Ui.Page();
        p.Controls.Add(Ui.Heading("Prepare the TV"));
        p.Controls.Add(Ui.Text("Do this once on the TV with the remote. Keep the TV on and this PC on the same Wi‑Fi network as the TV."));
        _prepTabs.TabPages.Add(InstrTab("Android TV  (Xiaomi Mi TV Stick 4K, most TV boxes)",
            "1.  Open Settings  ›  Device Preferences  ›  About.\r\n" +
            "2.  Scroll to “Android TV OS build” (on some devices it’s just called “Build”) and press OK on it 7 times, until it says “You are now a developer”.\r\n" +
            "3.  Go back to Settings  ›  Device Preferences  ›  Developer options.\r\n" +
            "4.  Turn on “USB debugging”. Many Xiaomi sticks have no “Wireless debugging” entry — USB debugging is enough.\r\n" +
            "5.  Find the TV’s IP address under Settings  ›  Network & Internet  ›  your Wi‑Fi network. Click Next here and type that IP.\r\n\r\n" +
            "When the TV shows “Allow USB debugging?”, tick “Always allow from this computer” and press OK.\r\n" +
            "Needs Android TV 11 or newer. The older Mi TV Stick (Android 9) is not supported."));
        _prepTabs.TabPages.Add(InstrTab("Google TV  (Chromecast with Google TV, Google TV Streamer)",
            "1.  Open Settings  ›  System  ›  About.\r\n" +
            "2.  Scroll to “Android TV OS build” (on some devices it’s just called “Build”) and press OK on it 7 times (enter your PIN if asked) until it says “You are now a developer”.\r\n" +
            "3.  Go back to Settings  ›  System  ›  Developer options.\r\n" +
            "4.  Turn on “USB debugging”, then open “Wireless debugging” and turn it on.\r\n" +
            "5.  Inside Wireless debugging choose “Pair device with pairing code”. Leave that screen open and click Next here."));
        _prepTabs.SelectedIndexChanged += (_, _) =>
        {
            _connectModePicked = false;
            ApplyConnectModeFromPrepare();
        };
        p.Controls.Add(_prepTabs);
        p.Controls.Add(Ui.Text("Tip: if Windows Firewall asks about “adb”, click Allow (Private networks) so the TV can be found automatically.", 640, Ui.Muted));
        return p;
    }

    private static TabPage InstrTab(string title, string body)
    {
        var t = new TabPage(title) { BackColor = Color.White, Padding = new Padding(12) };
        t.Controls.Add(new TextBox { Text = body, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Color.White, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10.5f), TabStop = false });
        return t;
    }

    private Control PageConnect()
    {
        var p = Ui.Page();
        p.Controls.Add(Ui.Heading("Connect to the TV"));
        p.Controls.Add(Ui.Text("Choose how this PC should reach the TV. The default follows the menu type you picked in Prepare.", 640, Ui.Muted));
        _modeIp.Click += (_, _) => { _connectModePicked = true; ShowConnectMode(); };
        _modePair.Click += (_, _) => { _connectModePicked = true; ShowConnectMode(); };
        _modeIp.CheckedChanged += (_, _) => { if (_modeIp.Checked) ShowConnectMode(); };
        _modePair.CheckedChanged += (_, _) => { if (_modePair.Checked) ShowConnectMode(); };
        p.Controls.Add(_modeIp);
        p.Controls.Add(_modePair);

        _ipPanel.Controls.Add(Ui.Text(
            "On the TV, turn on Developer options (Settings › Device Preferences › About, then press “Android TV OS build” — or “Build” — 7 times) and turn on USB debugging. " +
            "Find the IP address under Settings › Network & Internet › your Wi‑Fi network. Type that IP below (port 5555 is used if you don’t add a port). " +
            "When the TV shows “Allow USB debugging?”, tick “Always allow from this computer” and press OK.",
            640, Ui.Muted));
        var ipConn = Ui.Primary("Connect");
        ipConn.Click += async (_, _) => await ConnectByIpAsync();
        _ipAddr.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await ConnectByIpAsync(); } };
        _actionControls.Add(ipConn);
        _ipPanel.Controls.Add(Ui.Row(Ui.Text("TV IP address"), _ipAddr, ipConn));

        _pairPanel.Controls.Add(Ui.Text("On the TV: Developer options › Wireless debugging › “Pair device with pairing code”. The TV shows a 6‑digit code and an IP address:port. Keep that screen open."));
        var scan = Ui.Secondary("🔍  Search for the TV");
        scan.Click += async (_, _) => await ScanAsync(forPairing: true, _pageCts.Token);
        _actionControls.Add(scan);
        _pairPanel.Controls.Add(Ui.Row(scan, Ui.Text("Found on the network:", 300, Ui.Muted)));
        _found.SelectedIndexChanged += (_, _) =>
        {
            if (_found.SelectedItem is MdnsService s)
            {
                if (s.Type == "pairing") _pairAddr.Text = s.Address; else _connAddr.Text = s.Address;
            }
        };
        _pairPanel.Controls.Add(_found);
        _pairPanel.Controls.Add(Ui.Row(Ui.Text("Pairing IP address:port"), _pairAddr));
        var pair = Ui.Primary("Pair");
        pair.Click += async (_, _) => await PairAsync();
        _actionControls.Add(pair);
        _pairCode.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await PairAsync(); } };
        _pairPanel.Controls.Add(Ui.Row(Ui.Text("Pairing code"), _pairCode, pair));
        _pairPanel.Controls.Add(_pairResult);
        _pairPanel.Controls.Add(Ui.Text("After pairing, the connect port is usually filled in automatically. If not, type the IP address:port shown at the top of the TV's Wireless debugging screen (it is different from the pairing port).", 640, Ui.Muted));
        var find = Ui.Secondary("🔍  Find automatically");
        find.Click += async (_, _) => await ScanAsync(forPairing: false, _pageCts.Token);
        var conn = Ui.Primary("Connect");
        conn.Click += async (_, _) => await ConnectWirelessAsync();
        _actionControls.Add(find); _actionControls.Add(conn);
        _pairPanel.Controls.Add(Ui.Row(Ui.Text("TV address (IP:port)"), _connAddr, conn, find));

        p.Controls.Add(_ipPanel);
        p.Controls.Add(_pairPanel);
        p.Controls.Add(_connResult);
        p.Controls.Add(Ui.Text("Already connected devices (for example over USB):", 640, Ui.Muted));
        var refresh = Ui.Secondary("Refresh");
        var use = Ui.Secondary("Use this device");
        refresh.Click += async (_, _) => await RefreshDevicesAsync();
        use.Click += async (_, _) => { if (_devices.SelectedItem is string s) await UseDeviceAsync(s); };
        _actionControls.Add(refresh); _actionControls.Add(use);
        p.Controls.Add(Ui.Row(_devices, use, refresh));
        ApplyConnectModeFromPrepare();
        return p;
    }

    private void ApplyConnectModeFromPrepare()
    {
        if (_connectModePicked) { ShowConnectMode(); return; }
        bool ip = _prepTabs.TabPages.Count == 0 || _prepTabs.SelectedIndex <= 0;
        _modeIp.Checked = ip;
        _modePair.Checked = !ip;
        ShowConnectMode();
    }

    private void ShowConnectMode()
    {
        bool ip = _modeIp.Checked;
        _ipPanel.Visible = ip;
        _pairPanel.Visible = !ip;
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

    private Control PageDone()
    {
        var p = Ui.Page();
        var check = new Label
        {
            Text = "✔", AutoSize = true, Font = new Font("Segoe UI Semibold", 72f),
            ForeColor = Ui.Good, Margin = new Padding(0, 12, 0, 0)
        };
        p.Controls.Add(check);
        p.Controls.Add(_doneTitle);
        p.Controls.Add(_doneSubtitle);
        p.Controls.Add(_doneDevice);
        p.Controls.Add(Ui.Text("To open Android Settings on the TV, hold OK for 3 seconds (or press Up Up Down Down Left Right Left Right).", 640, Ui.Muted));
        p.Controls.Add(Ui.Text("Unplug USB debugging if you like; Restore original needs it on.", 640, Ui.Muted));
        var again = Ui.Primary("Set up another TV");
        var close = Ui.Secondary("Close");
        again.Click += async (_, _) => await ResetForAnotherTvAsync();
        close.Click += (_, _) => Close();
        _actionControls.Add(again); _actionControls.Add(close);
        p.Controls.Add(Ui.Row(again, close));
        return p;
    }

    // ───────────────────────────── navigation ─────────────────────────────
    private bool CanEnter(int idx)
    {
        if (idx <= 1) return true;
        if (_serial == null) { Status("Connect to the TV first."); return false; }
        if (idx == 3 && !_targetReady) { Status("The kiosk app is not installed on the TV yet."); return false; }
        return true;
    }

    private void ShowPage(int idx)
    {
        _pageCts.Cancel();
        _pageCts = new CancellationTokenSource();
        _page = idx;
        for (int i = 0; i < _pages.Count; i++) _pages[i].Visible = i == idx;
        RefreshNav();
        bool onDone = idx == DonePage;
        _back.Visible = idx > 0 && !onDone;
        _next.Visible = idx < 3; // hide on Make kiosk and the done screen
        if (_footer != null) _footer.Visible = !onDone;
        if (!onDone) StartPageWork(idx);
    }

    private void RefreshNav()
    {
        bool done = _page == DonePage;
        for (int i = 0; i < _navItems.Count; i++)
        {
            bool cur = !done && i == _page;
            _navItems[i].Text = done
                ? "✔  " + System.Text.RegularExpressions.Regex.Replace(NavTitles[i], @"^\d+\s+", "")
                : NavTitles[i];
            _navItems[i].BackColor = cur ? Ui.BrandLight : Ui.Nav;
            _navItems[i].ForeColor = done ? Ui.Good
                : cur ? Ui.Brand
                : (CanEnterQuiet(i) ? Color.FromArgb(40, 40, 40) : Color.FromArgb(160, 160, 160));
            _navItems[i].Font = new Font((cur || done) ? "Segoe UI Semibold" : "Segoe UI", 10.5f);
        }
    }

    private void StartPageWork(int idx)
    {
        var token = _pageCts.Token;
        switch (idx)
        {
            case 1:
                ApplyConnectModeFromPrepare();
                _ = RefreshDevicesAsync();
                if (_modePair.Checked)
                    _ = ScanAsync(forPairing: true, token, quietIfNone: true);
                break;
            case 2: _ = CheckTargetAsync(autoAdvance: true, token); break;
            case 3: _ = PrepareKioskPageAsync(); break;
        }
    }

    private bool CanEnterQuiet(int idx) => idx <= 1 || (_serial != null && (idx == 2 || _targetReady));

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
                if (!string.IsNullOrWhiteSpace(_connAddr.Text)) await ConnectWirelessAsync();
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

    private async Task ConnectByIpAsync()
    {
        var raw = _ipAddr.Text.Trim();
        var m = AddrRx.Match(raw);
        if (!m.Success)
        {
            MessageBox.Show(this, "Type the TV’s IP address, e.g. 192.168.1.43 (port 5555 is used if you don’t specify one).", Text);
            return;
        }
        var addr = m.Groups[1].Value + ":" + (m.Groups[2].Success ? m.Groups[2].Value : "5555");
        _ipAddr.Text = addr;
        _pageCts.Cancel();
        var ct = _pageCts = new CancellationTokenSource();
        SetBusy(true, "Connecting...");
        _connResult.ForeColor = Color.FromArgb(33, 33, 33);
        _connResult.Text = "Connecting to " + addr + "…";
        try
        {
            var r = await _adb.RunAsync(20000, false, "connect", addr);
            if (ct.IsCancellationRequested) return;
            var all = (r.All ?? "").ToLowerInvariant();
            bool refused = r.TimedOut
                || all.Contains("refused")
                || all.Contains("no route")
                || all.Contains("timed out")
                || all.Contains("timeout")
                || all.Contains("failed to connect")
                || all.Contains("cannot connect")
                || all.Contains("connection reset");

            string lastState = null, serial = null;
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (!ct.IsCancellationRequested)
            {
                var devs = await _adb.DevicesAllAsync();
                if (ct.IsCancellationRequested) return;
                var hit = devs.FirstOrDefault(d => d.Serial == addr)
                    ?? devs.FirstOrDefault(d => d.Serial.StartsWith(m.Groups[1].Value + ":"));
                if (hit != null)
                {
                    lastState = hit.State;
                    serial = hit.Serial;
                    if (hit.State == "device")
                    {
                        SetBusy(false);
                        await UseDeviceAsync(serial);
                        return;
                    }
                    if (hit.State == "unauthorized")
                    {
                        _connResult.ForeColor = Ui.Warn;
                        _connResult.Text = "Look at the TV: tick Always allow from this computer and press OK.";
                        Status("Waiting for you to allow USB debugging on the TV...");
                    }
                }
                else if (refused)
                {
                    _connResult.ForeColor = Ui.Bad;
                    _connResult.Text = "✖ Could not connect. USB debugging is off, or wrong IP.";
                    Status("Not connected.");
                    return;
                }
                if (DateTime.UtcNow >= deadline) break;
                try { await Task.Delay(1000, ct.Token); } catch (TaskCanceledException) { return; }
            }
            if (ct.IsCancellationRequested) return;
            if (lastState == "unauthorized")
            {
                _connResult.ForeColor = Ui.Warn;
                _connResult.Text = "Look at the TV: tick Always allow from this computer and press OK. Timed out waiting — click Connect again after you allow it.";
                Status("Still waiting for the TV to allow this computer.");
            }
            else
            {
                _connResult.ForeColor = Ui.Bad;
                _connResult.Text = "✖ Could not connect. USB debugging is off, or wrong IP.";
                Status("Not connected.");
            }
        }
        finally { if (_busy) SetBusy(false); }
    }

    private async Task ConnectWirelessAsync()
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
                    _connResult.Text = "Look at the TV: tick Always allow from this computer and press OK, then click Connect again.";
                }
                else
                {
                    _connResult.ForeColor = Ui.Bad;
                    _connResult.Text = "✖ Could not connect. Check that Wireless debugging is on, the address is right, and this PC is paired.";
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
            _deviceModel = model;
            _deviceAndroid = rel;
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
            ShowPage(2);
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
                if (autoAdvance && _page == 2)
                {
                    await Task.Delay(1200);
                    if (_page == 2 && !_advanced.Visible) ShowPage(3);
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
            if (!isDefault) Log($"Home currently resolves to: {resolved}. If the TV asks, choose “{"Kiosk Manager"}” and “Always”.");

            SetStep(6, StepState.Running);
            await _adb.ShellAsync(s, "input keyevent KEYCODE_HOME");
            SetStep(6, StepState.Done);

            _kioskResult.ForeColor = failed == 0 && isDefault ? Ui.Good : Ui.Warn;
            _kioskResult.Text = failed == 0 && isDefault
                ? $"✔ Done! The TV now opens {_targetLabel} on start-up and when Home is pressed."
                : "⚠ Mostly done — see the steps above and the log. The TV may ask which home app to use: choose “Kiosk Manager” → Always.";
            Status("Kiosk ready.");
            Log("=== Kiosk ready ===");
            if (failed == 0 && isDefault)
            {
                SetBusy(false);
                try { await Task.Delay(1200, _pageCts.Token); }
                catch (TaskCanceledException) { return; }
                ShowDone(restored: false);
            }
        }
        catch (Exception ex) { Fail(ex.Message); }
        finally { if (_busy) SetBusy(false); }
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
            Log("=== Restored ===");
            SetBusy(false);
            try { await Task.Delay(1200, _pageCts.Token); }
            catch (TaskCanceledException) { return; }
            ShowDone(restored: true);
        }
        catch (Exception ex) { Fail(ex.Message); }
        finally { if (_busy) SetBusy(false); }
    }

    private void ShowDone(bool restored)
    {
        _doneTitle.Text = "All done!";
        _doneSubtitle.Text = restored
            ? "Restored: the original home screen is back"
            : $"This TV now opens {_targetLabel} only";
        var name = string.IsNullOrWhiteSpace(_deviceModel) ? "TV" : _deviceModel;
        var parts = new List<string> { name };
        if (!string.IsNullOrWhiteSpace(_serial)) parts.Add(_serial);
        if (!string.IsNullOrWhiteSpace(_deviceAndroid)) parts.Add("Android " + _deviceAndroid);
        _doneDevice.Text = string.Join("  ·  ", parts);
        ShowPage(DonePage);
        Status(restored ? "Restored." : "Kiosk ready.");
    }

    private async Task ResetForAnotherTvAsync()
    {
        _pageCts.Cancel();
        _pageCts = new CancellationTokenSource();
        SetBusy(true, "Disconnecting...");
        try
        {
            if (!string.IsNullOrEmpty(_serial) && !string.IsNullOrEmpty(_adb.AdbPath))
            {
                try { await _adb.RunAsync(15000, false, "disconnect", _serial); }
                catch (Exception ex) { Log("disconnect: " + ex.Message); }
            }
        }
        finally { SetBusy(false); }

        _serial = null;
        _deviceId = null;
        _deviceModel = null;
        _deviceAndroid = null;
        _target = KioskOps.DefaultTarget;
        _targetLabel = KioskOps.DefaultTargetName;
        _targetReady = false;
        _connectModePicked = false;
        _lblDevice.Text = "No TV connected";
        _found.Items.Clear();
        _pairAddr.Text = "";
        _pairCode.Text = "";
        _pairResult.Text = "";
        _ipAddr.Text = "";
        _connAddr.Text = "";
        _connResult.Text = "";
        _devices.Items.Clear();
        _appStatus.Text = "";
        _appHint.Text = "";
        _advanced.Visible = false;
        _appList.Clear();
        _apps.Items.Clear();
        _homes.Items.Clear();
        _steps.Items.Clear();
        _kioskResult.Text = "";
        _targetInfo.Text = "";
        ApplyConnectModeFromPrepare();
        Log("--- Set up another TV ---");
        ShowPage(0);
        Status("Ready. Follow the steps on the left.");
    }

    private async Task PrepareKioskPageAsyncSilent()
    {
        var cands = await _ops.CandidatesToDisableAsync(_serial, _target);
        _homes.Items.Clear();
        foreach (var c in cands) _homes.Items.Add(c, c.Recommended);
    }
}

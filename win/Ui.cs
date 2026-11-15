namespace EasyDKiosk;

internal static class Ui
{
    public static readonly Color Brand = Color.FromArgb(13, 71, 161);
    public static readonly Color BrandLight = Color.FromArgb(227, 236, 250);
    public static readonly Color Nav = Color.FromArgb(244, 246, 250);
    public static readonly Color Good = Color.FromArgb(46, 125, 50);
    public static readonly Color Bad = Color.FromArgb(198, 40, 40);
    public static readonly Color Warn = Color.FromArgb(230, 120, 0);
    public static readonly Color Muted = Color.FromArgb(96, 104, 120);

    public static Label Heading(string t) => new()
    {
        Text = t, AutoSize = true, Font = new Font("Segoe UI Semibold", 15f), ForeColor = Brand,
        Margin = new Padding(0, 0, 0, 8)
    };

    public static Label Text(string t, int width = 640, Color? color = null) => new()
    {
        Text = t, AutoSize = true, MaximumSize = new Size(width, 0), ForeColor = color ?? Color.FromArgb(33, 33, 33),
        Margin = new Padding(0, 4, 0, 4)
    };

    public static Button Primary(string t) => Style(new Button { Text = t }, true);
    public static Button Secondary(string t) => Style(new Button { Text = t }, false);

    public static Button Style(Button b, bool primary)
    {
        b.AutoSize = true;
        b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        b.Padding = new Padding(14, 6, 14, 6);
        b.Margin = new Padding(0, 6, 10, 6);
        b.FlatStyle = FlatStyle.Flat;
        b.Cursor = Cursors.Hand;
        b.Font = new Font("Segoe UI Semibold", 10f);
        if (primary)
        {
            b.BackColor = Brand; b.ForeColor = Color.White; b.FlatAppearance.BorderSize = 0;
        }
        else
        {
            b.BackColor = Color.White; b.ForeColor = Brand; b.FlatAppearance.BorderColor = Brand; b.FlatAppearance.BorderSize = 1;
        }
        return b;
    }

    public static FlowLayoutPanel Row(params Control[] cs)
    {
        var f = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0, 2, 0, 2) };
        foreach (var c in cs) { if (c is Label l) { l.Anchor = AnchorStyles.Left; l.Margin = new Padding(0, 10, 8, 0); } f.Controls.Add(c); }
        return f;
    }

    public static FlowLayoutPanel Page()
        => new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(24, 18, 24, 12), Visible = false };
}

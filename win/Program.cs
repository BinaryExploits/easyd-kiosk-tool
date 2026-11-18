namespace EasyDKiosk;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Environment.SetEnvironmentVariable("ADB_MDNS_OPENSCREEN", "1");
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
            MessageBox.Show("Something went wrong:\n\n" + e.Exception.Message + "\n\nYou can try the step again.",
                "Kiosk Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

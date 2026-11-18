using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace EasyDKiosk;

public sealed class AdbResult
{
    public int ExitCode;
    public string Out = "";
    public string Err = "";
    public bool TimedOut;
    public string All => (Out + "\n" + Err).Trim();
    public bool Ok => ExitCode == 0 && !TimedOut;
}

public sealed class MdnsService
{
    public string Name = "";
    public string Type = "";   // pairing | connect
    public string Address = "";
    public override string ToString() => $"{Address}   ({Type}, {Name})";
}

public sealed class AdbDevice
{
    public string Serial = "";
    public string State = ""; // device, unauthorized, offline, ...
}

/// <summary>Thin async wrapper around adb.exe with logging.</summary>
public sealed class Adb
{
    public const string PlatformToolsUrl = "https://dl.google.com/android/repository/platform-tools-latest-windows.zip";
    public string AdbPath { get; private set; } = "";
    private readonly Action<string> _log;

    public Adb(Action<string> log) { _log = log; }

    public static string DataDir
    {
        get
        {
            var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EasyDKiosk");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    /// <summary>Finds or unpacks adb: next to the exe, embedded copy, then download.</summary>
    public async Task EnsureAsync()
    {
        var beside = Path.Combine(AppContext.BaseDirectory, "platform-tools", "adb.exe");
        if (File.Exists(beside)) { AdbPath = beside; _log($"Using adb next to the app: {beside}"); return; }

        var target = Path.Combine(DataDir, "platform-tools", "adb.exe");
        if (File.Exists(target)) { AdbPath = target; _log($"Using adb: {target}"); return; }

        var zipPath = Path.Combine(DataDir, "platform-tools.zip");
        using (var res = Assembly.GetExecutingAssembly().GetManifestResourceStream("platform-tools.zip"))
        {
            if (res != null)
            {
                _log("Unpacking the bundled Android platform-tools (adb)...");
                using var f = File.Create(zipPath);
                await res.CopyToAsync(f);
            }
            else
            {
                _log("Downloading Android platform-tools (adb) from Google...");
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                var bytes = await http.GetByteArrayAsync(PlatformToolsUrl);
                await File.WriteAllBytesAsync(zipPath, bytes);
            }
        }
        await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, DataDir, overwriteFiles: true));
        File.Delete(zipPath);
        if (!File.Exists(target)) throw new FileNotFoundException("adb.exe not found after unpacking platform-tools", target);
        AdbPath = target;
        _log($"adb ready: {target}");
    }

    public static string ExtractApk()
    {
        var path = Path.Combine(DataDir, "EasyDKiosk.apk");
        using var res = Assembly.GetExecutingAssembly().GetManifestResourceStream("EasyDKiosk.apk");
        if (res == null)
        {
            var beside = Path.Combine(AppContext.BaseDirectory, "EasyDKiosk.apk");
            if (File.Exists(beside)) return beside;
            throw new FileNotFoundException("Kiosk APK is missing from the package");
        }
        using (var f = File.Create(path)) res.CopyTo(f);
        return path;
    }

    public async Task<AdbResult> RunAsync(int timeoutMs, bool quiet, params string[] args)
    {
        if (!quiet) _log("> adb " + string.Join(" ", args.Select(a => a.Contains(' ') ? "\"" + a + "\"" : a)));
        var psi = new ProcessStartInfo(AdbPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["ADB_MDNS_OPENSCREEN"] = "1";

        var r = new AdbResult();
        var so = new StringBuilder();
        var se = new StringBuilder();
        var outDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var errDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) => { if (e.Data == null) outDone.TrySetResult(true); else lock (so) so.AppendLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data == null) errDone.TrySetResult(true); else lock (se) se.AppendLine(e.Data); };
        p.Exited += (_, _) => exited.TrySetResult(true);
        try { p.Start(); }
        catch (Exception ex) { r.ExitCode = -1; r.Err = ex.Message; _log("  ! " + ex.Message); return r; }
        try { p.StandardInput.Close(); } catch { }
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        if (await Task.WhenAny(exited.Task, Task.Delay(timeoutMs)) != exited.Task)
        {
            r.TimedOut = true;
            try { p.Kill(true); } catch { }
        }
        // Don't block forever on pipes inherited by a spawned adb server.
        await Task.WhenAny(Task.WhenAll(outDone.Task, errDone.Task), Task.Delay(1500));
        try { r.ExitCode = r.TimedOut ? -1 : p.ExitCode; } catch { r.ExitCode = -1; }
        lock (so) r.Out = so.ToString();
        lock (se) r.Err = se.ToString();
        if (!quiet)
        {
            foreach (var line in r.All.Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0).Take(40))
                _log("  " + line);
            if (r.TimedOut) _log("  ! timed out");
        }
        return r;
    }

    public Task<AdbResult> RunAsync(params string[] args) => RunAsync(60000, false, args);

    public Task<AdbResult> ShellAsync(string serial, string cmd, bool quiet = false, int timeoutMs = 60000)
        => RunAsync(timeoutMs, quiet, "-s", serial, "shell", cmd);

    private static readonly Regex MdnsLine = new(
        @"^(?<name>\S+)\s+_adb-tls-(?<type>pairing|connect)\._tcp\.?\s+(?<addr>\d{1,3}(?:\.\d{1,3}){3}:\d+)",
        RegexOptions.Multiline);

    public async Task<List<MdnsService>> MdnsServicesAsync()
    {
        var r = await RunAsync(15000, true, "mdns", "services");
        return MdnsLine.Matches(r.Out)
            .Select(m => new MdnsService { Name = m.Groups["name"].Value, Type = m.Groups["type"].Value, Address = m.Groups["addr"].Value })
            .GroupBy(s => s.Type + s.Address).Select(g => g.First()).ToList();
    }

    /// <summary>All entries from `adb devices`, including unauthorized.</summary>
    public async Task<List<AdbDevice>> DevicesAllAsync()
    {
        var r = await RunAsync(15000, true, "devices");
        return r.Out.Split('\n').Skip(1)
            .Select(l => l.Trim().Split('\t'))
            .Where(p => p.Length >= 2 && p[0].Trim().Length > 0)
            .Select(p => new AdbDevice { Serial = p[0].Trim(), State = p[1].Trim() })
            .ToList();
    }

    /// <summary>Serials in state "device".</summary>
    public async Task<List<string>> DevicesAsync()
        => (await DevicesAllAsync()).Where(d => d.State == "device").Select(d => d.Serial).ToList();
}

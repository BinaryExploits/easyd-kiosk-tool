using EasyDKiosk;
using System.Text.RegularExpressions;
var adb = new Adb(s => Console.WriteLine("   log| " + s));
typeof(Adb).GetProperty("AdbPath")!.SetValue(adb, Environment.GetEnvironmentVariable("ADB_PATH") ?? "adb");
var ops = new KioskOps(adb, s => Console.WriteLine("   log| " + s));
var s = Environment.GetEnvironmentVariable("ANDROID_SERIAL") ?? "emulator-5554";
// mDNS parser on a realistic sample
var rx = (Regex)typeof(Adb).GetField("MdnsLine", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!.GetValue(null);
var sample = "List of discovered mdns services\nadb-1A2B3C-xYz9\t_adb-tls-pairing._tcp.\t192.168.1.43:37123\nadb-1A2B3C-xYz9\t_adb-tls-connect._tcp.\t192.168.1.43:41234\n";
foreach (Match m in rx.Matches(sample)) Console.WriteLine($"MDNS {m.Groups["type"]} {m.Groups["addr"]}");
Console.WriteLine("devices: " + string.Join(",", await adb.DevicesAsync()));
Console.WriteLine("installed easyd: " + await ops.IsInstalledAsync(s, KioskOps.DefaultTarget));
Console.WriteLine("installed bogus: " + await ops.IsInstalledAsync(s, "com.taknik.easyd"));
Console.WriteLine("version: " + await ops.VersionNameAsync(s, KioskOps.DefaultTarget));
Console.WriteLine("HOME: " + string.Join(" | ", (await ops.HomeActivitiesAsync(s)).Select(h => h.Pkg + " " + h.Component)));
Console.WriteLine("Candidates: " + string.Join(" | ", (await ops.CandidatesToDisableAsync(s, KioskOps.DefaultTarget)).Select(c => c.ToString())));
var apps = await ops.ListAppsAsync(s);
Console.WriteLine("apps: " + string.Join(" | ", apps.Select(a => $"{a.Label}={a.Package}{(a.System ? "*" : "")}")));
Console.WriteLine("set: " + await ops.BroadcastAsync(s, "com.easyd.kiosk.SET_TARGET", "--es target " + KioskOps.DefaultTarget));
Console.WriteLine("get: " + await ops.BroadcastAsync(s, "com.easyd.kiosk.GET_TARGET"));
Console.WriteLine("HOME2: " + string.Join(" | ", (await ops.HomeActivitiesAsync(s)).Select(h => h.Component)));
await ops.SaveStateAsync(s, "EMU1", new[] { "com.google.android.tvlauncher" });
Console.WriteLine("state: " + string.Join(",", await ops.LoadStateAsync(s, "EMU1")));
Console.WriteLine("resolve: " + await ops.ResolveHomeAsync(s));
// cleanup back to stock
await ops.BroadcastAsync(s, "com.easyd.kiosk.DISABLE_HOME");
await adb.RunAsync("-s", s, "uninstall", KioskOps.KioskPkg);
await ops.ClearStateAsync(s, "EMU1");
Console.WriteLine("state after clear: [" + string.Join(",", await ops.LoadStateAsync(s, "EMU1")) + "]");

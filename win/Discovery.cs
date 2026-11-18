using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace EasyDKiosk;

public sealed class DiscoveredEndpoint
{
    public string Ip = "";
    public int Port = 5555;
    public string Source = ""; // tcp5555 | mdns
    public string Address => Ip + ":" + Port;
}

/// <summary>Finds TVs on the local network by /24 TCP scans and optional extra addresses (mDNS).</summary>
public static class TvDiscovery
{
    public static List<IPAddress> LocalIpv4Addresses()
    {
        var list = new List<IPAddress>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var ip = ua.Address;
                    if (IPAddress.IsLoopback(ip) || IsLinkLocal(ip)) continue;
                    if (!list.Any(x => x.Equals(ip))) list.Add(ip);
                }
            }
        }
        catch { }
        return list;
    }

    /// <summary>Each local IPv4's /24 network (first three octets), excluding the PC's own address from the host list later.</summary>
    public static List<string> LocalSlash24Bases()
    {
        var bases = new List<string>();
        foreach (var ip in LocalIpv4Addresses())
        {
            var p = ip.ToString().Split('.');
            if (p.Length != 4) continue;
            var b = p[0] + "." + p[1] + "." + p[2] + ".";
            if (!bases.Contains(b)) bases.Add(b);
        }
        return bases;
    }

    public static List<string> HostsInLocalSlash24s()
    {
        var self = new HashSet<string>(LocalIpv4Addresses().Select(a => a.ToString()));
        var hosts = new List<string>();
        foreach (var b in LocalSlash24Bases())
        {
            for (int i = 1; i <= 254; i++)
            {
                var ip = b + i;
                if (!self.Contains(ip)) hosts.Add(ip);
            }
        }
        return hosts;
    }

    public static bool IsLinkLocal(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        return b.Length == 4 && b[0] == 169 && b[1] == 254;
    }

    /// <summary>Fast parallel TCP connect. Returns IPs that accepted the connection.</summary>
    public static async Task<List<string>> ScanTcpAsync(IEnumerable<string> hosts, int port, int timeoutMs, int parallelism, CancellationToken ct)
    {
        var found = new ConcurrentBag<string>();
        var bag = new ConcurrentBag<string>(hosts);
        var workers = Math.Max(1, Math.Min(parallelism, bag.Count == 0 ? 1 : 256));
        var tasks = new List<Task>();
        for (int w = 0; w < workers; w++)
        {
            tasks.Add(Task.Run(async () =>
            {
                while (!ct.IsCancellationRequested && bag.TryTake(out var ip))
                {
                    if (await IsOpenAsync(ip, port, timeoutMs, ct)) found.Add(ip);
                }
            }, ct));
        }
        try { await Task.WhenAll(tasks); } catch (OperationCanceledException) { }
        return found.Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList();
    }

    public static async Task<bool> IsOpenAsync(string host, int port, int timeoutMs, CancellationToken ct)
    {
        try
        {
            using var tcp = new TcpClient();
            var connect = tcp.ConnectAsync(host, port);
            var delay = Task.Delay(timeoutMs, ct);
            var done = await Task.WhenAny(connect, delay);
            if (done != connect) return false;
            await connect; // observe exception
            return tcp.Connected;
        }
        catch { return false; }
    }

    public static List<DiscoveredEndpoint> Merge(IEnumerable<string> tcp5555, IEnumerable<(string Ip, int Port, string Source)> extras)
    {
        var map = new Dictionary<string, DiscoveredEndpoint>(StringComparer.Ordinal);
        void add(string ip, int port, string source)
        {
            if (string.IsNullOrWhiteSpace(ip) || port <= 0 || port > 65535) return;
            var key = ip + ":" + port;
            if (!map.ContainsKey(key))
                map[key] = new DiscoveredEndpoint { Ip = ip, Port = port, Source = source };
        }
        foreach (var ip in tcp5555) add(ip, 5555, "tcp5555");
        foreach (var e in extras) add(e.Ip, e.Port, e.Source);
        return map.Values.OrderBy(e => e.Ip, StringComparer.Ordinal).ThenBy(e => e.Port).ToList();
    }
}

using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace GraveOps.App.Services;

public sealed record DiscoveredHost(string Address, string OpenPorts, string Guess);

public sealed class DiscoveryService
{
    private static readonly int[] Ports = [22, 80, 32400, 8989, 7878, 9696, 8686, 6767, 5055, 8181, 8080, 8081, 8096, 8265];

    public async Task<List<DiscoveredHost>> ScanLocal24Async(IProgress<(int Done, int Total)>? progress = null, CancellationToken token = default)
    {
        var local = GetPrivateIPv4() ?? throw new InvalidOperationException("Could not determine a private IPv4 address for LAN discovery.");
        var bytes = local.GetAddressBytes();
        var addresses = Enumerable.Range(1, 254).Select(i => $"{bytes[0]}.{bytes[1]}.{bytes[2]}.{i}").ToList();
        var results = new List<DiscoveredHost>();
        var gate = new SemaphoreSlim(48);
        int done = 0;
        var tasks = addresses.Select(async address =>
        {
            await gate.WaitAsync(token);
            try
            {
                var open = new List<int>();
                foreach (var port in Ports)
                {
                    if (await IsOpenAsync(address, port, 220, token)) open.Add(port);
                }
                if (open.Count > 0)
                {
                    var guess = open.Contains(32400) ? "Plex/media server" : open.Contains(8989) || open.Contains(7878) || open.Contains(9696) ? "Media automation host" : open.Contains(8080) || open.Contains(8081) ? "Download-client host" : open.Contains(80) && open.Contains(22) ? "Linux web host / possible Pi-hole" : open.Contains(22) ? "SSH host" : "Web service";
                    lock (results) results.Add(new DiscoveredHost(address, string.Join(", ", open), guess));
                }
            }
            finally
            {
                gate.Release();
                var value = Interlocked.Increment(ref done);
                progress?.Report((value, addresses.Count));
            }
        }).ToArray();
        await Task.WhenAll(tasks);
        return results.OrderBy(x => IPAddress.Parse(x.Address).GetAddressBytes()[3]).ToList();
    }

    private static async Task<bool> IsOpenAsync(string host, int port, int timeoutMs, CancellationToken token)
    {
        using var client = new TcpClient();
        try
        {
            var connect = client.ConnectAsync(host, port, token).AsTask();
            var timeout = Task.Delay(timeoutMs, token);
            return await Task.WhenAny(connect, timeout) == connect && client.Connected;
        }
        catch { return false; }
    }

    private static IPAddress? GetPrivateIPv4()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
        {
            foreach (var addr in nic.GetIPProperties().UnicastAddresses)
            {
                if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                var b = addr.Address.GetAddressBytes();
                if (b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168)) return addr.Address;
            }
        }
        return null;
    }
}

using System.Net;
using System.Net.Sockets;

namespace GraveOps.App.Services;

public sealed class WakeOnLanService
{
    public async Task SendAsync(string macAddress, CancellationToken token = default)
    {
        var clean = new string((macAddress ?? "").Where(Uri.IsHexDigit).ToArray());
        if (clean.Length != 12) throw new InvalidOperationException("Wake-on-LAN MAC must contain 12 hexadecimal digits.");
        var mac = Enumerable.Range(0, 6).Select(i => Convert.ToByte(clean.Substring(i * 2, 2), 16)).ToArray();
        var packet = new byte[6 + 16 * mac.Length];
        Array.Fill(packet, (byte)0xFF, 0, 6);
        for (var i = 6; i < packet.Length; i += mac.Length) Buffer.BlockCopy(mac, 0, packet, i, mac.Length);
        using var udp = new UdpClient { EnableBroadcast = true };
        await udp.SendAsync(packet, new IPEndPoint(IPAddress.Broadcast, 9), token);
    }
}
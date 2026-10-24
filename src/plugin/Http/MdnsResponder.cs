using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;

namespace NOXMFD
{
    // The fixed LAN name ([Network] LanName, "noxmfd" by default, see NETWORKING.md): answers mDNS
    // questions for <name>.local with this PC's LAN address, so a tablet opens
    // http://<name>.local:<port> instead of an IP that DHCP can change. Shares UDP 5353 with Windows' own mDNS service through
    // ReuseAddress and runs on its own background thread; nothing here touches Unity.
    // ponytail: no probing or conflict detection (RFC 6762 §8). Two PCs given the same name both
    // answer and a client keeps whichever reply lands first, so each PC needs its own name. Group
    // memberships are taken at Start, so an adapter that comes up later is joined on the next launch.
    internal static class MdnsResponder
    {
        private const int MdnsPort = 5353;
        private static readonly IPAddress Group = IPAddress.Parse("224.0.0.251");
        private static Socket? _socket;
        private static bool _warnedHandleFailure;

        // Called under TelemetryServer's lifecycle lock. False (with a logged reason) if the port
        // or the multicast group can't be joined; the caller then shows the PC-name URL instead.
        internal static bool Start(string name)
        {
            Stop();
            var sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            try
            {
                sock.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                sock.Bind(new IPEndPoint(IPAddress.Any, MdnsPort));
                int joined = 0;
                foreach (var (address, _) in LocalIpv4())
                {
                    try
                    {
                        sock.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(Group, address));
                        joined++;
                    }
                    catch (SocketException ex) { Plugin.Log?.LogDebug($"[NOXMFD] mDNS: couldn't join on {address}: {ex.Message}"); }
                }
                if (joined == 0) throw new InvalidOperationException("no IPv4 adapter could join the mDNS group");

                _socket = sock;
                new Thread(() => ReceiveLoop(sock, name)) { IsBackground = true, Name = "NOXMFD-mDNS" }.Start();
                return true;
            }
            catch (Exception ex)
            {
                _socket = null;
                try { sock.Close(); } catch { }
                Plugin.Log?.LogWarning($"[NOXMFD] Fixed LAN name '{name}.local' is unavailable: {ex.Message}");
                return false;
            }
        }

        internal static void Stop()
        {
            Socket? sock = Interlocked.Exchange(ref _socket, null);
            try { sock?.Close(); } catch { }
        }

        private static void ReceiveLoop(Socket sock, string name)
        {
            var buf = new byte[9000];   // mDNS caps a packet at 9000 bytes (RFC 6762 §17)
            while (true)
            {
                EndPoint from = new IPEndPoint(IPAddress.Any, 0);
                int len;
                try { len = sock.ReceiveFrom(buf, ref from); }
                catch (Exception ex) when (ex is SocketException || ex is ObjectDisposedException)
                {
                    if (!ReferenceEquals(_socket, sock)) return;   // Stop closed it
                    // Windows reports an earlier send's ICMP "port unreachable" on the next receive.
                    if (ex is SocketException se && (se.SocketErrorCode == SocketError.ConnectionReset || se.SocketErrorCode == SocketError.MessageSize)) continue;
                    Plugin.Log?.LogWarning($"[NOXMFD] mDNS responder stopped: {ex.Message}");
                    return;
                }

                try { HandlePacket(sock, buf, len, (IPEndPoint)from, name); }
                catch (Exception ex)
                {
                    // An unexpected failure (e.g. the adapter list throwing under Wine) must not
                    // escape this thread; warn once, since every later lookup would repeat it.
                    if (_warnedHandleFailure) continue;
                    _warnedHandleFailure = true;
                    Plugin.Log?.LogWarning($"[NOXMFD] mDNS: couldn't answer a lookup: {ex.Message}");
                }
            }
        }

        private static void HandlePacket(Socket sock, byte[] buf, int len, IPEndPoint sender, string name)
        {
            if (!MdnsPacket.TryMatchQuery(buf, len, name, out MdnsPacket.Match match)) return;
            IPAddress? address = MdnsPacket.PickAddress(sender.Address, LocalIpv4());
            if (address == null) return;
            bool legacy = sender.Port != MdnsPort;
            byte[] reply = MdnsPacket.BuildAnswer(name, address, match, legacy);
            try
            {
                if (legacy) { sock.SendTo(reply, sender); return; }
                // Send the multicast answer out of the adapter the question came in on.
                sock.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, address.GetAddressBytes());
                sock.SendTo(reply, new IPEndPoint(Group, MdnsPort));
            }
            catch (Exception ex) when (ex is SocketException || ex is ObjectDisposedException)
            {
                Plugin.Log?.LogDebug($"[NOXMFD] mDNS: reply to {sender} failed: {ex.Message}");
            }
        }

        // Read fresh on each matching question (rare), so a DHCP renewal's new address is answered.
        private static List<(IPAddress Address, IPAddress Mask)> LocalIpv4()
        {
            var list = new List<(IPAddress, IPAddress)>();
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (UnicastIPAddressInformation ua in ni.GetIPProperties().UnicastAddresses)
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork && ua.IPv4Mask != null)
                        list.Add((ua.Address, ua.IPv4Mask));
            }
            return list;
        }
    }
}

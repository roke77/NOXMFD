using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace NOXMFD
{
    // The wire-format half of MdnsResponder (RFC 6762 on top of RFC 1035 messages): validate the
    // player's name, spot a question for <name>.local in a query, build the answer, and pick which
    // local address to answer with. Pure BCL so tools/tests/MdnsPacketTests.cs exercises it
    // without a socket.
    // The name only ever has an IPv4 address: an AAAA question gets an NSEC denial (RFC 6762 §6.1)
    // rather than silence, so a resolver asking A and AAAA together can use the A answer at once.
    // ponytail: Windows' resolver ignores the NSEC and retries AAAA until its ~2.5s timeout, so a
    // Windows device's first lookup of the name is that slow (then cached). Answering AAAA with a
    // real IPv6 address would remove it, at the cost of an IPv6 path the server isn't checked on.
    internal static class MdnsPacket
    {
        internal const ushort TypeA = 1, TypeAaaa = 28, TypeNsec = 47, TypeAny = 255;

        // One DNS label: 1-63 of [a-z0-9-], not starting or ending with '-'. Whitespace, case and a
        // trailing ".local" are forgiven, so "NOXMFD.local" works too. Null when it can't be served.
        internal static string? NormalizeName(string? raw)
        {
            if (raw == null) return null;
            string n = raw.Trim().ToLowerInvariant();
            if (n.EndsWith(".local", StringComparison.Ordinal)) n = n.Substring(0, n.Length - 6);
            if (n.Length == 0 || n.Length > 63 || n[0] == '-' || n[n.Length - 1] == '-') return null;
            foreach (char c in n)
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-')) return null;
            return n;
        }

        // What a query asks about name.local, if anything.
        internal struct Match
        {
            internal ushort Id;
            internal ushort EchoType;   // the first matching question's type, for a legacy reply
            internal bool WantsA;       // A or ANY
            internal bool WantsAaaa;
        }

        // True when a query (not a response) asks for name.local as A, AAAA or ANY, class IN or
        // ANY, with or without the QU bit. Malformed or truncated packets are just "no match".
        internal static bool TryMatchQuery(byte[] buf, int len, string name, out Match match)
        {
            match = default;
            if (len < 12 || (buf[2] & 0xF8) != 0) return false;   // QR set, or an opcode other than QUERY
            match.Id = (ushort)(buf[0] << 8 | buf[1]);
            int questions = buf[4] << 8 | buf[5];
            string want = name + ".local";
            int pos = 12;
            for (int i = 0; i < questions; i++)
            {
                if (!TryReadName(buf, len, ref pos, out string q) || pos + 4 > len) return false;
                ushort type = (ushort)(buf[pos] << 8 | buf[pos + 1]);
                int cls = (buf[pos + 2] << 8 | buf[pos + 3]) & 0x7FFF;   // the top bit is the QU flag
                pos += 4;
                if (!(cls == 1 || cls == 255) || !string.Equals(q, want, StringComparison.OrdinalIgnoreCase)) continue;
                bool a = type == TypeA || type == TypeAny, aaaa = type == TypeAaaa;
                if (!a && !aaaa) continue;
                if (match.EchoType == 0) match.EchoType = type;
                match.WantsA |= a;
                match.WantsAaaa |= aaaa;
            }
            return match.EchoType != 0;
        }

        // A one-shot query from a port other than 5353 (legacy unicast, RFC 6762 §6.7) gets its ID
        // back, the first matching question echoed and a short TTL. A normal answer has ID 0 and
        // the cache-flush bit, since the name belongs to this PC alone. An AAAA-only question gets
        // the NSEC denial as its answer and the A record as an additional one.
        internal static byte[] BuildAnswer(string name, IPAddress addr, Match m, bool legacyUnicast)
        {
            byte[] qname = EncodeName(name + ".local");
            ushort cls = (ushort)(legacyUnicast ? 0x0001 : 0x8001);   // class IN, + cache-flush
            uint ttl = legacyUnicast ? 10u : 120u;
            bool extraA = m.WantsAaaa && !m.WantsA;
            var b = new List<byte>(96);
            Put16(b, legacyUnicast ? m.Id : (ushort)0);
            Put16(b, 0x8400);                                                  // QR | AA
            Put16(b, (ushort)(legacyUnicast ? 1 : 0));                         // QDCOUNT
            Put16(b, (ushort)((m.WantsA ? 1 : 0) + (m.WantsAaaa ? 1 : 0)));   // ANCOUNT
            Put16(b, 0);                                                       // NSCOUNT
            Put16(b, (ushort)(extraA ? 1 : 0));                                // ARCOUNT
            if (legacyUnicast) { b.AddRange(qname); Put16(b, m.EchoType); Put16(b, 1); }
            if (m.WantsA) PutRecord(b, qname, TypeA, cls, ttl, addr.GetAddressBytes());
            if (m.WantsAaaa)
            {
                // NSEC rdata: the next name (ours again), then window 0 with a one-byte bitmap
                // holding only bit 1 (A): "this name has an A record and nothing else".
                var rdata = new List<byte>(qname) { 0x00, 0x01, 0x40 };
                PutRecord(b, qname, TypeNsec, cls, ttl, rdata.ToArray());
            }
            if (extraA) PutRecord(b, qname, TypeA, cls, ttl, addr.GetAddressBytes());
            return b.ToArray();
        }

        private static void PutRecord(List<byte> b, byte[] qname, ushort type, ushort cls, uint ttl, byte[] rdata)
        {
            b.AddRange(qname);
            Put16(b, type);
            Put16(b, cls);
            Put16(b, (ushort)(ttl >> 16)); Put16(b, (ushort)ttl);
            Put16(b, (ushort)rdata.Length);
            b.AddRange(rdata);
        }

        // The local IPv4 address on the sender's subnet, so a PC with extra adapters (VPN, Hyper-V,
        // WSL) answers with the one the asking device can reach. Null if none shares its subnet.
        internal static IPAddress? PickAddress(IPAddress sender, IEnumerable<(IPAddress Address, IPAddress Mask)> locals)
        {
            byte[] s = sender.GetAddressBytes();
            if (s.Length != 4) return null;
            foreach (var (address, mask) in locals)
            {
                byte[] a = address.GetAddressBytes(), m = mask.GetAddressBytes();
                if (a.Length != 4 || m.Length != 4) continue;
                bool same = true;
                for (int i = 0; i < 4 && same; i++) same = (a[i] & m[i]) == (s[i] & m[i]);
                if (same) return address;
            }
            return null;
        }

        private static bool TryReadName(byte[] buf, int len, ref int pos, out string name)
        {
            name = "";
            var sb = new StringBuilder();
            int p = pos, jumps = 0;
            bool jumped = false;
            while (true)
            {
                if (p >= len) return false;
                int l = buf[p];
                if (l == 0) { p++; break; }
                if ((l & 0xC0) == 0xC0)
                {
                    // A compression pointer; the jump cap stops a pointer loop in a hostile packet.
                    if (p + 1 >= len || ++jumps > 16) return false;
                    if (!jumped) { pos = p + 2; jumped = true; }
                    p = (l & 0x3F) << 8 | buf[p + 1];
                    continue;
                }
                if ((l & 0xC0) != 0 || p + 1 + l > len) return false;
                if (sb.Length > 0) sb.Append('.');
                sb.Append(Encoding.ASCII.GetString(buf, p + 1, l));
                p += 1 + l;
            }
            if (!jumped) pos = p;
            name = sb.ToString();
            return true;
        }

        private static byte[] EncodeName(string dotted)
        {
            var b = new List<byte>(dotted.Length + 2);
            foreach (string label in dotted.Split('.'))
            {
                b.Add((byte)label.Length);
                b.AddRange(Encoding.ASCII.GetBytes(label));
            }
            b.Add(0);
            return b.ToArray();
        }

        private static void Put16(List<byte> b, ushort v) { b.Add((byte)(v >> 8)); b.Add((byte)v); }
    }
}

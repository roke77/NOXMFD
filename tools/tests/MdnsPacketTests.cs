using System.Net;

namespace NOXMFD.Tests
{
    public class MdnsPacketTests
    {
        // A query with one question, as a resolver sends it: header, then <labels> type class.
        private static byte[] Query(ushort id, string name, ushort type, ushort cls = 1)
        {
            var b = new List<byte> { (byte)(id >> 8), (byte)id, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0 };
            foreach (string label in name.Split('.'))
            {
                b.Add((byte)label.Length);
                b.AddRange(System.Text.Encoding.ASCII.GetBytes(label));
            }
            b.AddRange(new byte[] { 0, (byte)(type >> 8), (byte)type, (byte)(cls >> 8), (byte)cls });
            return b.ToArray();
        }

        [Theory]
        [InlineData("noxmfd", "noxmfd")]
        [InlineData("  NOXMFD.local ", "noxmfd")]
        [InlineData("cockpit-2", "cockpit-2")]
        [InlineData("", null)]
        [InlineData("-x", null)]
        [InlineData("no xmfd", null)]
        [InlineData("my_pc", null)]
        [InlineData("a.b", null)]
        public void NormalizeName_accepts_single_dns_labels_only(string raw, string? expected)
        {
            Assert.Equal(expected, MdnsPacket.NormalizeName(raw));
        }

        private static bool Match(byte[] q, out MdnsPacket.Match m) => MdnsPacket.TryMatchQuery(q, q.Length, "noxmfd", out m);

        [Fact]
        public void A_query_for_the_name_matches_case_insensitively_with_or_without_QU()
        {
            Assert.True(Match(Query(7, "NOXMFD.local", 1), out var m));
            Assert.Equal(7, m.Id);
            Assert.Equal(1, m.EchoType);
            Assert.True(m.WantsA);
            Assert.False(m.WantsAaaa);
            Assert.True(Match(Query(0, "noxmfd.local", 255, 0x8001), out m));
            Assert.True(m.WantsA);
            Assert.True(Match(Query(0, "noxmfd.local", 28), out m));
            Assert.True(m.WantsAaaa);
            Assert.False(m.WantsA);
        }

        [Fact]
        public void Other_names_types_responses_and_truncated_packets_do_not_match()
        {
            Assert.False(Match(Query(0, "printer.local", 1), out _));
            Assert.False(Match(Query(0, "noxmfd.local", 16), out _));   // TXT
            byte[] response = Query(0, "noxmfd.local", 1);
            response[2] = 0x84;
            Assert.False(Match(response, out _));
            byte[] q = Query(0, "noxmfd.local", 1);
            Assert.False(MdnsPacket.TryMatchQuery(q, q.Length - 3, "noxmfd", out _));
        }

        [Fact]
        public void A_second_question_using_a_compression_pointer_matches()
        {
            // Q1 = printer.local; Q2 = "noxmfd" + pointer to Q1's "local" label (offset 12 + 8).
            var b = new List<byte>(Query(0, "printer.local", 1));
            b[5] = 2;
            b.AddRange(new byte[] { 6, (byte)'n', (byte)'o', (byte)'x', (byte)'m', (byte)'f', (byte)'d', 0xC0, 20, 0, 1, 0, 1 });
            Assert.True(Match(b.ToArray(), out var m));
            Assert.True(m.WantsA);
        }

        [Fact]
        public void A_pointer_loop_is_rejected()
        {
            byte[] loop = { 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0xC0, 12, 0, 1, 0, 1 };
            Assert.False(Match(loop, out _));
        }

        private const int NameLen = 1 + 6 + 1 + 5 + 1;   // noxmfd.local, uncompressed

        [Fact]
        public void Multicast_answer_is_an_authoritative_A_record_with_cache_flush()
        {
            Match(Query(9, "noxmfd.local", 1), out var m);
            byte[] r = MdnsPacket.BuildAnswer("noxmfd", IPAddress.Parse("192.168.1.61"), m, legacyUnicast: false);
            Assert.Equal(new byte[] { 0, 0, 0x84, 0, 0, 0, 0, 1, 0, 0, 0, 0 }, r[..12]);
            Assert.Equal(new byte[] { 0, 1, 0x80, 1, 0, 0, 0, 120, 0, 4, 192, 168, 1, 61 }, r[(12 + NameLen)..]);
        }

        [Fact]
        public void An_AAAA_question_gets_an_NSEC_denial_plus_the_A_record()
        {
            Match(Query(0, "noxmfd.local", 28), out var m);
            byte[] r = MdnsPacket.BuildAnswer("noxmfd", IPAddress.Parse("192.168.1.61"), m, legacyUnicast: false);
            Assert.Equal(new byte[] { 0, 1, 0, 0, 0, 1 }, r[6..12]);   // ANCOUNT 1, NSCOUNT 0, ARCOUNT 1
            int nsec = 12 + NameLen;
            Assert.Equal(new byte[] { 0, 47, 0x80, 1, 0, 0, 0, 120, 0, NameLen + 3 }, r[nsec..(nsec + 10)]);
            int bitmap = nsec + 10 + NameLen;
            Assert.Equal(new byte[] { 0, 1, 0x40 }, r[bitmap..(bitmap + 3)]);
            Assert.Equal(new byte[] { 0, 1, 0x80, 1, 0, 0, 0, 120, 0, 4, 192, 168, 1, 61 }, r[^14..]);
        }

        [Fact]
        public void Legacy_unicast_answer_echoes_the_id_and_question()
        {
            Match(Query(0xBEEF, "noxmfd.local", 255), out var m);
            byte[] r = MdnsPacket.BuildAnswer("noxmfd", IPAddress.Parse("10.0.0.5"), m, legacyUnicast: true);
            Assert.Equal(new byte[] { 0xBE, 0xEF, 0x84, 0, 0, 1, 0, 1 }, r[..8]);
            byte[] asQuery = (byte[])r.Clone();
            asQuery[2] = 0;   // re-read as a query to parse the echoed question
            Assert.True(Match(asQuery, out var echoed));
            Assert.Equal(255, echoed.EchoType);
            Assert.Equal(new byte[] { 0, 1, 0, 0, 0, 10, 0, 4, 10, 0, 0, 5 }, r[^12..]);
        }

        [Fact]
        public void PickAddress_answers_with_the_address_on_the_senders_subnet()
        {
            var locals = new[]
            {
                (IPAddress.Parse("172.20.0.1"), IPAddress.Parse("255.255.240.0")),    // WSL/Hyper-V
                (IPAddress.Parse("192.168.1.61"), IPAddress.Parse("255.255.255.0")),
            };
            Assert.Equal(IPAddress.Parse("192.168.1.61"), MdnsPacket.PickAddress(IPAddress.Parse("192.168.1.30"), locals));
            Assert.Equal(IPAddress.Parse("172.20.0.1"), MdnsPacket.PickAddress(IPAddress.Parse("172.20.3.9"), locals));
            Assert.Null(MdnsPacket.PickAddress(IPAddress.Parse("10.1.1.1"), locals));
        }
    }
}

using NOXMFD;

namespace NOXMFD.Tests
{
    public class ExtensionMessageWireTests
    {
        [Fact]
        public void An_extension_type_round_trips_to_its_id()
        {
            Assert.Equal("ext.tac", ExtensionMessageWire.TypeFor("tac"));
            Assert.True(ExtensionMessageWire.TryIdFrom("ext.tac", out string id));
            Assert.Equal("tac", id);
        }

        [Theory]
        [InlineData("sqd.roster")]
        [InlineData("presence")]
        [InlineData("fuel")]
        [InlineData("ext.")]
        [InlineData("EXT.tac")]
        [InlineData("")]
        [InlineData(null)]
        public void NOXMFDs_own_types_and_a_bare_prefix_address_no_extension(string? type)
        {
            Assert.False(ExtensionMessageWire.TryIdFrom(type, out _));
        }

        [Fact]
        public void Payload_cap_counts_utf8_bytes_and_rejects_null()
        {
            Assert.True(ExtensionMessageWire.PayloadFits(""));
            Assert.True(ExtensionMessageWire.PayloadFits(new string('a', ExtensionMessageWire.MaxPayloadBytes)));
            Assert.False(ExtensionMessageWire.PayloadFits(new string('a', ExtensionMessageWire.MaxPayloadBytes + 1)));
            // 'é' is two bytes in UTF-8: half the character count already fills the cap.
            Assert.False(ExtensionMessageWire.PayloadFits(new string('é', ExtensionMessageWire.MaxPayloadBytes / 2 + 1)));
            Assert.False(ExtensionMessageWire.PayloadFits(null));
        }
    }
}

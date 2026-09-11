using NOXMFD;

namespace NOXMFD.Tests
{
    // IconColorRegistry (docs/vanilla-icons-plus-extension.md) is pure BCL static state, same
    // testable shape as ExtensionJsonValidator. Its dictionaries are shared static state across
    // tests, so each case uses its own unique type key and clears what it sets rather than relying
    // on test-run order.
    public class IconColorRegistryTests
    {
        public IconColorRegistryTests() => IconColorRegistry.ClearFactionOverride();

        [Fact]
        public void Faction_override_defaults_to_all_null()
        {
            var over = IconColorRegistry.FactionOverride;
            Assert.Null(over.Friendly);
            Assert.Null(over.Enemy);
            Assert.Null(over.Neutral);
        }

        [Fact]
        public void Faction_override_round_trips_and_clears()
        {
            IconColorRegistry.SetFactionOverride("#111111", "#222222", "#333333");
            var set = IconColorRegistry.FactionOverride;
            Assert.Equal("#111111", set.Friendly);
            Assert.Equal("#222222", set.Enemy);
            Assert.Equal("#333333", set.Neutral);

            IconColorRegistry.ClearFactionOverride();
            var cleared = IconColorRegistry.FactionOverride;
            Assert.Null(cleared.Friendly);
            Assert.Null(cleared.Enemy);
            Assert.Null(cleared.Neutral);
        }

        [Fact]
        public void Type_override_round_trips_through_the_snapshot_with_its_faction_filter()
        {
            const string type = "__test_aa_unit__";
            IconColorRegistry.SetTypeOverride(type, "#ff5eff", factionFilter: 2);

            var ov = IconColorRegistry.TypeOverridesSnapshot()[type];
            Assert.Equal("#ff5eff", ov.Hex);
            Assert.Equal(2, ov.FactionFilter);

            IconColorRegistry.ClearTypeOverride(type);
            Assert.False(IconColorRegistry.TypeOverridesSnapshot().ContainsKey(type));
        }

        [Fact]
        public void Type_override_without_a_faction_filter_carries_a_null_filter()
        {
            const string type = "__test_any_faction_unit__";
            IconColorRegistry.SetTypeOverride(type, "#abcdef", factionFilter: null);

            Assert.Null(IconColorRegistry.TypeOverridesSnapshot()[type].FactionFilter);

            IconColorRegistry.ClearTypeOverride(type);
        }

        [Fact]
        public void Type_override_ignores_an_empty_unit_type_or_hex()
        {
            Assert.False(IconColorRegistry.SetTypeOverride("", "#ffffff", null));
            Assert.False(IconColorRegistry.SetTypeOverride("__test_empty_hex__", "", null));

            Assert.False(IconColorRegistry.TypeOverridesSnapshot().ContainsKey(""));
            Assert.False(IconColorRegistry.TypeOverridesSnapshot().ContainsKey("__test_empty_hex__"));
        }

        [Fact]
        public void Snapshot_is_a_defensive_copy_not_a_live_view_of_later_changes()
        {
            const string type = "__test_defensive_copy__";
            IconColorRegistry.SetTypeOverride(type, "#123456", null);
            var snapshot = IconColorRegistry.TypeOverridesSnapshot();

            IconColorRegistry.ClearTypeOverride(type);

            Assert.True(snapshot.ContainsKey(type));
            Assert.False(IconColorRegistry.TypeOverridesSnapshot().ContainsKey(type));
        }

        [Theory]
        [InlineData("#123456")]
        [InlineData("#12345678")]
        [InlineData("#ABCdef")]
        public void Valid_hex_colors_are_accepted(string hex)
        {
            const string type = "__test_valid_hex__";
            Assert.True(IconColorRegistry.SetTypeOverride(type, hex, null));
            Assert.Equal(hex, IconColorRegistry.TypeOverridesSnapshot()[type].Hex);
            IconColorRegistry.ClearTypeOverride(type);
        }

        [Theory]
        [InlineData("red")]
        [InlineData("#123")]
        [InlineData("#12345g")]
        [InlineData("#123456789")]
        public void Invalid_hex_colors_are_rejected_without_replacing_the_live_value(string hex)
        {
            const string type = "__test_invalid_hex__";
            Assert.True(IconColorRegistry.SetTypeOverride(type, "#112233", null));
            Assert.False(IconColorRegistry.SetTypeOverride(type, hex, null));
            Assert.Equal("#112233", IconColorRegistry.TypeOverridesSnapshot()[type].Hex);
            IconColorRegistry.ClearTypeOverride(type);
        }

        [Fact]
        public void Invalid_faction_filter_is_rejected_without_replacing_the_live_value()
        {
            const string type = "__test_invalid_faction__";
            Assert.True(IconColorRegistry.SetTypeOverride(type, "#112233", 2));
            Assert.False(IconColorRegistry.SetTypeOverride(type, "#445566", 3));
            Assert.Equal(2, IconColorRegistry.TypeOverridesSnapshot()[type].FactionFilter);
            IconColorRegistry.ClearTypeOverride(type);
        }

        [Fact]
        public void Published_snapshot_is_reused_until_the_registry_changes()
        {
            const string type = "__test_snapshot_reuse__";
            IconColorRegistry.SetTypeOverride(type, "#112233", null);
            var first = IconColorRegistry.TypeOverridesSnapshot();
            Assert.Same(first, IconColorRegistry.TypeOverridesSnapshot());

            IconColorRegistry.SetTypeOverride(type, "#445566", null);
            var changed = IconColorRegistry.TypeOverridesSnapshot();
            Assert.NotSame(first, changed);
            Assert.Equal("#112233", first[type].Hex);
            Assert.Equal("#445566", changed[type].Hex);
            IconColorRegistry.ClearTypeOverride(type);
        }

        [Fact]
        public void Invalid_faction_override_preserves_all_previous_colors()
        {
            Assert.True(IconColorRegistry.SetFactionOverride("#111111", "#222222", "#333333"));
            Assert.False(IconColorRegistry.SetFactionOverride("#444444", "invalid", "#666666"));
            var current = IconColorRegistry.FactionOverride;
            Assert.Equal("#111111", current.Friendly);
            Assert.Equal("#222222", current.Enemy);
            Assert.Equal("#333333", current.Neutral);
        }
    }
}

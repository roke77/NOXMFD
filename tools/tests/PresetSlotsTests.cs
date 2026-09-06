using System;
using System.Collections.Generic;
using System.IO;

namespace NOXMFD.Tests
{
    // A minimal IPresetSlot for exercising the generic PresetSlots helpers directly, independent of
    // HudPreset/TgtPresetStore's own game-specific fields.
    internal sealed class FakeSlot : IPresetSlot
    {
        public string Name { get; set; } = string.Empty;
        public bool HasData { get; set; }
    }

    public class PresetSlotsTests
    {
        [Fact]
        public void Empty_creates_the_requested_count_of_distinct_instances()
        {
            var slots = PresetSlots.Empty<FakeSlot>(5);
            Assert.Equal(5, slots.Length);
            slots[0].Name = "a";
            Assert.Equal(string.Empty, slots[1].Name);   // not the same shared instance
        }

        [Fact]
        public void BoolArray_round_trips_through_JSON_including_a_false_in_the_middle()
        {
            var arr = new[] { true, false, true };
            string json = PresetSlots.BoolArrayJson(arr);
            var parsed = (List<object?>)JsonLite.Parse(json)!;
            var d = new Dictionary<string, object?> { ["k"] = parsed };
            Assert.Equal(arr, PresetSlots.ParseBoolArray(d, "k"));
        }

        [Fact]
        public void ParseBoolArray_returns_empty_not_null_when_the_key_is_missing()
        {
            var d = new Dictionary<string, object?>();
            Assert.Empty(PresetSlots.ParseBoolArray(d, "missing"));
        }

        [Fact]
        public void StringArray_round_trips_through_JSON_including_a_quote_that_needs_escaping()
        {
            var arr = new[] { "FRIENDLY", "say \"hi\"", "" };
            string json = PresetSlots.StringArrayJson(arr);
            var parsed = (List<object?>)JsonLite.Parse(json)!;
            var d = new Dictionary<string, object?> { ["k"] = parsed };
            Assert.Equal(arr, PresetSlots.ParseStringArray(d, "k"));
        }

        [Fact]
        public void ParseStringArray_returns_empty_not_null_when_the_key_is_missing()
        {
            var d = new Dictionary<string, object?>();
            Assert.Empty(PresetSlots.ParseStringArray(d, "missing"));
        }

        [Fact]
        public void ReadNameAndHasData_reads_both_fields_and_defaults_when_missing()
        {
            var slot = new FakeSlot();
            PresetSlots.ReadNameAndHasData(new Dictionary<string, object?> { ["name"] = "BVR", ["hasData"] = true }, slot);
            Assert.Equal("BVR", slot.Name);
            Assert.True(slot.HasData);

            var untouched = new FakeSlot();
            PresetSlots.ReadNameAndHasData(new Dictionary<string, object?>(), untouched);
            Assert.Equal(string.Empty, untouched.Name);
            Assert.False(untouched.HasData);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void CleanName_rejects_null_empty_and_whitespace_only(string? input)
        {
            Assert.Equal(string.Empty, PresetSlots.CleanName(input));
        }

        [Fact]
        public void CleanName_trims_surrounding_whitespace()
        {
            Assert.Equal("BVR", PresetSlots.CleanName("  BVR  "));
        }

        [Fact]
        public void SummaryJson_carries_index_name_hasData_and_nothing_else()
        {
            var slots = PresetSlots.Empty<FakeSlot>(2);
            slots[0].Name = "BVR";
            slots[0].HasData = true;
            string json = PresetSlots.SummaryJson(2, slots);

            Assert.Contains("\"current\":2", json);
            Assert.Contains("\"index\":1", json);
            Assert.Contains("\"name\":\"BVR\"", json);
            Assert.Contains("\"hasData\":true", json);
            Assert.Contains("\"index\":2", json);
            Assert.Contains("\"hasData\":false", json);
        }

        [Fact]
        public void Rename_updates_the_slot_and_calls_persist()
        {
            var slots = PresetSlots.Empty<FakeSlot>(3);
            int persistCalls = 0;
            bool ok = PresetSlots.Rename(slots, 2, "CAS", () => persistCalls++);
            Assert.True(ok);
            Assert.Equal("CAS", slots[1].Name);
            Assert.Equal(1, persistCalls);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(4)]
        public void Rename_rejects_an_out_of_range_index_without_persisting(int index)
        {
            var slots = PresetSlots.Empty<FakeSlot>(3);
            int persistCalls = 0;
            bool ok = PresetSlots.Rename(slots, index, "CAS", () => persistCalls++);
            Assert.False(ok);
            Assert.Equal(0, persistCalls);
        }

        [Fact]
        public void Rename_rejects_a_whitespace_only_name_without_persisting()
        {
            var slots = PresetSlots.Empty<FakeSlot>(3);
            slots[0].Name = "BVR";
            int persistCalls = 0;
            bool ok = PresetSlots.Rename(slots, 1, "   ", () => persistCalls++);
            Assert.False(ok);
            Assert.Equal("BVR", slots[0].Name);   // unchanged
            Assert.Equal(0, persistCalls);
        }

        [Fact]
        public void Delete_resets_the_slot_to_a_fresh_instance_and_calls_persist()
        {
            var slots = PresetSlots.Empty<FakeSlot>(3);
            slots[1].Name = "CAS";
            slots[1].HasData = true;
            int persistCalls = 0;

            bool ok = PresetSlots.Delete(slots, 2, () => persistCalls++);

            Assert.True(ok);
            Assert.Equal(string.Empty, slots[1].Name);
            Assert.False(slots[1].HasData);
            Assert.Equal(1, persistCalls);
        }

        [Fact]
        public void Delete_rejects_an_out_of_range_index_without_persisting()
        {
            var slots = PresetSlots.Empty<FakeSlot>(3);
            int persistCalls = 0;
            Assert.False(PresetSlots.Delete(slots, 99, () => persistCalls++));
            Assert.Equal(0, persistCalls);
        }

        [Fact]
        public void WriteToDisk_backs_up_the_prior_content_before_overwriting()
        {
            string dir = Path.Combine(Path.GetTempPath(), "noxmfd-presetslots-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "presets.json");
            try
            {
                File.WriteAllText(path, "{\"old\":true}");
                PresetSlots.WriteToDisk(path, "{\"new\":true}", "test presets");

                Assert.Equal("{\"new\":true}", File.ReadAllText(path));
                Assert.Equal("{\"old\":true}", File.ReadAllText(path + ".bak"));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void WriteToDisk_reports_a_failure_through_LogWarning_instead_of_throwing()
        {
            // A directory instead of a file makes File.WriteAllText fail — WriteToDisk must swallow
            // that (matching every other store's own best-effort persist) and report it via the
            // injected LogWarning seam instead.
            string dir = Path.Combine(Path.GetTempPath(), "noxmfd-presetslots-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string bogusPath = dir;   // a directory path, not a file
            string? logged = null;
            var previous = PresetSlots.LogWarning;
            PresetSlots.LogWarning = msg => logged = msg;
            try
            {
                PresetSlots.WriteToDisk(bogusPath, "{}", "test presets");
                Assert.NotNull(logged);
                Assert.Contains("test presets", logged);
            }
            finally
            {
                PresetSlots.LogWarning = previous;
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}

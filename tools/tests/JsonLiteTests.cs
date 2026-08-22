using System.Collections.Generic;
using NOXMFD;

namespace NOXMFD.Tests
{
    public class JsonLiteTests
    {
        [Fact]
        public void Parses_top_level_object_with_a_string_field()
        {
            var root = (Dictionary<string, object?>)JsonLite.Parse("{\"activeRouteId\":\"r_1\"}")!;
            Assert.Equal("r_1", root["activeRouteId"]);
        }

        [Fact]
        public void Parses_nested_arrays_and_objects()
        {
            const string json = "{\"routes\":[{\"id\":\"r_1\",\"nextIndex\":1,\"waypoints\":[{\"x\":100.5,\"z\":-200.0}]}]}";
            var root = (Dictionary<string, object?>)JsonLite.Parse(json)!;
            var routes = (List<object?>)root["routes"]!;
            var route = (Dictionary<string, object?>)routes[0]!;
            Assert.Equal(1.0, (double)route["nextIndex"]!);
            var waypoints = (List<object?>)route["waypoints"]!;
            var wp = (Dictionary<string, object?>)waypoints[0]!;
            Assert.Equal(100.5, (double)wp["x"]!);
            Assert.Equal(-200.0, (double)wp["z"]!);
        }

        [Fact]
        public void Unescapes_quote_backslash_and_control_chars_in_strings()
        {
            var obj = (Dictionary<string, object?>)JsonLite.Parse("{\"n\":\"a\\\"b\\\\c\\nd\"}")!;
            Assert.Equal("a\"b\\c\nd", obj["n"]);
        }

        [Fact]
        public void Parses_true_false_and_null_literals()
        {
            var obj = (Dictionary<string, object?>)JsonLite.Parse("{\"t\":true,\"f\":false,\"n\":null}")!;
            Assert.True((bool)obj["t"]!);
            Assert.False((bool)obj["f"]!);
            Assert.Null(obj["n"]);
        }

        [Fact]
        public void Parses_empty_object_and_array()
        {
            Assert.Empty((Dictionary<string, object?>)JsonLite.Parse("{}")!);
            Assert.Empty((List<object?>)JsonLite.Parse("[]")!);
        }

        [Theory]
        [InlineData("not json")]
        [InlineData("")]
        public void Malformed_input_degrades_to_null_instead_of_throwing(string input)
        {
            Assert.Null(JsonLite.Parse(input));
        }

        [Fact]
        public void EscapeJson_escapes_the_named_control_characters()
        {
            string escaped = JsonLite.EscapeJson("a\"b\\c\nd\te\bf\fg");
            string expected = "a" + "\\\"" + "b" + "\\\\" + "c" + "\\n" + "d" + "\\t" + "e" + "\\b" + "f" + "\\f" + "g";
            Assert.Equal(expected, escaped);
        }

        [Fact]
        public void EscapeJson_escapes_other_C0_control_chars_as_unicode()
        {
            string escaped = JsonLite.EscapeJson("a\u0001b");
            Assert.Equal("a\\u0001b", escaped);
        }

        [Fact]
        public void EscapeJson_round_trips_through_Parse()
        {
            const string original = "weird \"name\"\twith\ncontrolchars";
            string json = "{\"n\":\"" + JsonLite.EscapeJson(original) + "\"}";
            var obj = (Dictionary<string, object?>)JsonLite.Parse(json)!;
            Assert.Equal(original, obj["n"]);
        }

        [Fact]
        public void EscapeJson_passes_null_and_empty_through_unchanged()
        {
            Assert.Equal(string.Empty, JsonLite.EscapeJson(null!));
            Assert.Equal(string.Empty, JsonLite.EscapeJson(""));
        }
    }
}

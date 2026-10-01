using System;
using System.Collections.Generic;
using System.IO;

namespace NOXMFD.Tests
{
    // ThemeColors guards what reaches served CSS (issue 105); ThemeStore is the CFG > UI theme list.
    // Each test points the store at its own empty temp folder, so Load() starts it from DEFAULT.
    public class ThemeStoreTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "noxmfd-themes-test-" + Guid.NewGuid());

        public ThemeStoreTests()
        {
            Directory.CreateDirectory(_dir);
            ThemeStore.ConfigDir = _dir;
            ThemeStore.Load();
        }

        public void Dispose()
        {
            ThemeStore.ConfigDir = null;
            try { Directory.Delete(_dir, true); } catch { }
        }

        private static Dictionary<string, object?> State() => (Dictionary<string, object?>)JsonLite.Parse(ThemeStore.StateJson)!;
        private static string ActiveId() => (string)State()["active"]!;

        [Theory]
        [InlineData("--no-green-rgb", "#FF8800", "#ff8800")]
        [InlineData("--no-bg", "#000000", "#000000")]
        [InlineData("--no-green-rgb", "#ff880", null)]                    // 5 digits
        [InlineData("--no-green-rgb", "red", null)]                       // named colour
        [InlineData("--no-green-rgb", "#ff8800\n", null)]                 // trailing newline would split the SSE data line
        [InlineData("--no-green-rgb", "#ff8800;}body{x:y", null)]         // CSS injection attempt
        [InlineData("--no-bezel-hi", "#ff8800", null)]                    // shell chrome isn't editable
        [InlineData("--no-green-dim", "#ff8800", null)]                   // calculated shade isn't editable
        [InlineData(null, "#ff8800", null)]
        public void Normalize_accepts_only_editable_tokens_with_six_digit_hex(string? token, string? value, string? expected)
        {
            Assert.Equal(expected, ThemeColors.Normalize(token, value));
        }

        [Fact]
        public void BuildCss_writes_rgb_tokens_as_triples_in_panel_order_and_drops_invalid_values()
        {
            var colors = new Dictionary<string, string>
            {
                ["--no-ink"] = "#101010",
                ["--no-green-rgb"] = "#ff8800",
                ["--no-teal"] = "#123456",          // not editable: dropped
            };
            Assert.Equal(":root{--no-green-rgb:255, 136, 0;--no-ink:#101010;}", ThemeColors.BuildCss(colors));
            Assert.Equal(string.Empty, ThemeColors.BuildCss(new Dictionary<string, string>()));
        }

        [Fact]
        public void Share_code_round_trips_and_rejects_tampering()
        {
            var colors = new Dictionary<string, string> { ["--no-red-rgb"] = "#aa0000", ["--no-route-cyan"] = "#00ffff" };
            string code = ThemeColors.BuildCode("Night \"ops\"", colors);

            Assert.True(ThemeColors.TryParseCode("  " + code + "\n", out string name, out var parsed));
            Assert.Equal("Night \"ops\"", name);
            Assert.Equal(colors, parsed);

            Assert.False(ThemeColors.TryParseCode("NOXT9:" + code.Substring(ThemeColors.CodePrefix.Length), out _, out _));
            Assert.False(ThemeColors.TryParseCode(ThemeColors.CodePrefix + "%%%not-base64", out _, out _));
            Assert.False(ThemeColors.TryParseCode(ThemeColors.CodePrefix + new string('A', 5000), out _, out _));
            Assert.False(ThemeColors.TryParseCode(null, out _, out _));
        }

        [Fact]
        public void Share_code_import_keeps_only_valid_colours()
        {
            string json = "{\"n\":\"Mixed\",\"c\":{\"--no-red-rgb\":\"#aa0000\",\"--no-bg\":\"url(x)\",\"--no-key-hi\":\"#ffffff\"}}";
            string code = ThemeColors.CodePrefix + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
            Assert.True(ThemeColors.TryParseCode(code, out _, out var parsed));
            Assert.Equal(new Dictionary<string, string> { ["--no-red-rgb"] = "#aa0000" }, parsed);
        }

        [Fact]
        public void Default_is_read_only_until_a_theme_is_created()
        {
            Assert.Equal(ThemeStore.DefaultId, ActiveId());
            Assert.False(ThemeStore.SetColor("--no-green-rgb", "#ff8800"));
            Assert.False(ThemeStore.ResetColor(null));

            Assert.True(ThemeStore.Create("Amber"));
            Assert.NotEqual(ThemeStore.DefaultId, ActiveId());
            Assert.True(ThemeStore.SetColor("--no-green-rgb", "#ff8800"));
            Assert.Equal(":root{--no-green-rgb:255, 136, 0;}", ThemeStore.ActiveCss);

            Assert.True(ThemeStore.Select(ThemeStore.DefaultId));
            Assert.Equal(string.Empty, ThemeStore.ActiveCss);
        }

        [Fact]
        public void Create_copies_the_active_colours_and_names_stay_unique()
        {
            ThemeStore.Create("Amber");
            ThemeStore.SetColor("--no-green-rgb", "#ff8800");
            ThemeStore.Create("Amber");                                  // copies Amber's colours
            Assert.Equal(":root{--no-green-rgb:255, 136, 0;}", ThemeStore.ActiveCss);

            var themes = (List<object?>)State()["themes"]!;
            Assert.Equal("Amber (2)", ((Dictionary<string, object?>)themes[1]!)["name"]);
            Assert.True(ThemeStore.Create("DEFAULT"));
            Assert.Equal("DEFAULT (2)", ((Dictionary<string, object?>)((List<object?>)State()["themes"]!)[2]!)["name"]);
        }

        [Fact]
        public void Deleting_the_active_theme_falls_back_to_default()
        {
            ThemeStore.Create("Amber");
            string id = ActiveId();
            Assert.True(ThemeStore.Delete(id));
            Assert.Equal(ThemeStore.DefaultId, ActiveId());
            Assert.False(ThemeStore.Select(id));
        }

        [Fact]
        public void Themes_survive_a_reload_and_a_tampered_file_is_cleaned()
        {
            ThemeStore.Create("Amber");
            ThemeStore.SetColor("--no-green-rgb", "#ff8800");
            string id = ActiveId();

            ThemeStore.Load();
            Assert.Equal(id, ActiveId());
            Assert.Equal(":root{--no-green-rgb:255, 136, 0;}", ThemeStore.ActiveCss);

            File.WriteAllText(Path.Combine(_dir, "com.roque.NOXMFD.themes.json"),
                "{\"active\":\"t_x\",\"themes\":[{\"id\":\"t_x\",\"name\":\"Bad\",\"colors\":{\"--no-bg\":\"red;}*{\",\"--no-ink\":\"#010101\"}}]}");
            ThemeStore.Load();
            Assert.Equal(":root{--no-ink:#010101;}", ThemeStore.ActiveCss);
        }

        [Fact]
        public void Import_adds_and_activates_a_theme_and_respects_the_cap()
        {
            string code = ThemeColors.BuildCode("Shared", new Dictionary<string, string> { ["--no-amber-rgb"] = "#ffcc00" });
            Assert.True(ThemeStore.Import(code));
            Assert.Equal(":root{--no-amber-rgb:255, 204, 0;}", ThemeStore.ActiveCss);
            Assert.False(ThemeStore.Import("not a code"));

            for (int i = 1; i < ThemeStore.MaxThemes; i++) Assert.True(ThemeStore.Create("T" + i));
            Assert.False(ThemeStore.Create("One too many"));
            Assert.False(ThemeStore.Import(code));
        }
    }
}

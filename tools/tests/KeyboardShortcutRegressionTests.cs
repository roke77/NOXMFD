using System.IO;
using System.Runtime.CompilerServices;

namespace NOXMFD.Tests
{
    // Keybinds.cs can't be compiled into this standalone test project (it needs BepInEx.Configuration/
    // UnityEngine/Rewired, exactly the dependencies this project is built to avoid — see the .csproj
    // header comment). Its static constructor for KeyboardShortcut is what registers that type's
    // BepInEx TOML converter — a parameterless `new KeyboardShortcut()` compiles to a bare initobj,
    // which Mono doesn't reliably run that constructor for, silently leaving every keybind unable to
    // bind (0.36.1's KEY-page-goes-empty bug). So instead of compiling the file, this scans its source
    // text directly for the fixed pattern (KeyboardShortcut.Empty) and the broken one it replaced.
    public class KeyboardShortcutRegressionTests
    {
        private static string KeybindsSource([CallerFilePath] string here = "")
        {
            string dir = Path.GetDirectoryName(here)!;
            string path = Path.Combine(dir, "..", "..", "src", "plugin", "Input", "Keybinds.cs");
            return File.ReadAllText(path);
        }

        [Fact]
        public void NeverConstructsKeyboardShortcutWithABareParameterlessNew()
        {
            string source = KeybindsSource();
            Assert.DoesNotContain("new KeyboardShortcut()", source);
        }

        [Fact]
        public void UsesKeyboardShortcutEmptyAsTheUnboundDefault()
        {
            string source = KeybindsSource();
            Assert.Contains("KeyboardShortcut.Empty", source);
        }
    }
}

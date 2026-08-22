// apicheck: pre-flight guard for Nuclear Option game updates.
//
// The plugin reaches into game internals via reflection (typeof(T).GetField("name"), etc.). Those
// calls compile regardless of the game's actual shape and only fail at runtime, silently, when a
// member is renamed/retyped. This scans the plugin source for every such call, resolves each
// against the current game assembly via MetadataLoadContext (metadata only, nothing executed), and
// reports any member that's missing or changed type. Run after each game update; non-zero exit
// means something needs a fix.
//
//   dotnet run --project tools/apicheck
//   dotnet run --project tools/apicheck -- "D:\path\to\Nuclear Option"   # explicit game dir
//
// With no argument, reads GameDir from GameDir.props.
//
// Dynamic sites (someObj.GetType().GetField(...), not a literal typeof) aren't checked — only
// listed for manual review. Checks member shape, not behavior (enum/index drift needs an in-game check).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

static class Program
{
    static int Main(string[] args)
    {
        string repoRoot = FindRepoRoot();
        if (repoRoot == null) { Console.Error.WriteLine("apicheck: couldn't find repo root (no NOXMFD.csproj above cwd)."); return 2; }

        string gameDir = args.Length > 0 ? args[0].Trim() : ReadGameDir(repoRoot);
        if (string.IsNullOrEmpty(gameDir))
        {
            Console.Error.WriteLine("apicheck: no game dir. Pass it as an argument, or create GameDir.props with <GameDir>.");
            return 2;
        }
        string managed = Path.Combine(gameDir, "NuclearOption_Data", "Managed");
        string asmPath = Path.Combine(managed, "Assembly-CSharp.dll");
        if (!File.Exists(asmPath)) { Console.Error.WriteLine($"apicheck: Assembly-CSharp.dll not found under {managed}"); return 2; }

        // Sites are extracted from the plugin source, so a new reflection call is picked up automatically.
        var pluginDir = Path.Combine(repoRoot, "src", "plugin");
        var sites = ExtractReflectionSites(pluginDir, out var dynamicSites);
        if (sites.Count == 0) { Console.Error.WriteLine($"apicheck: found no typeof(T).GetField/... sites under {pluginDir}"); return 2; }

        // Resolve from Managed alone — mixing in the host runtime's copies collides on duplicate assembly names.
        var resolver = new PathAssemblyResolver(Directory.GetFiles(managed, "*.dll"));
        string core = File.Exists(Path.Combine(managed, "mscorlib.dll")) ? "mscorlib" : "netstandard";
        using var mlc = new MetadataLoadContext(resolver, coreAssemblyName: core);
        var asm = mlc.LoadFromAssemblyPath(asmPath);
        var byName = asm.GetTypes().GroupBy(t => t.Name).ToDictionary(g => g.Key, g => g.ToArray());

        Console.WriteLine($"apicheck: {asmPath}");
        Console.WriteLine($"          {sites.Count} reflected member(s) from {pluginDir}\n");

        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        int broken = 0;
        foreach (var (type, kind, member) in sites.OrderBy(s => s.type).ThenBy(s => s.member))
        {
            if (!byName.TryGetValue(type, out var cands)) { Console.WriteLine($"  TYPE MISSING   {type}.{member}"); broken++; continue; }
            string detail = null;
            foreach (var t in cands)
            {
                if (kind == "GetField"    && t.GetField(member, Any)    is FieldInfo fi)    { detail = fi.FieldType.Name; break; }
                if (kind == "GetProperty" && t.GetProperty(member, Any) is PropertyInfo pi) { detail = pi.PropertyType.Name; break; }
                if (kind == "GetMethod"   && t.GetMethods(Any).Any(m => m.Name == member))  { detail = "method"; break; }
            }
            if (detail == null) { Console.WriteLine($"  {kind.Substring(3).ToUpper()} MISSING  {type}.{member}"); broken++; }
            else                  Console.WriteLine($"  OK   {type}.{member}  :  {detail}");
        }

        if (dynamicSites.Count > 0)
        {
            Console.WriteLine("\n  Dynamic sites (type not a literal typeof — verify by hand):");
            foreach (var d in dynamicSites) Console.WriteLine($"    {d}");
        }

        Console.WriteLine(broken == 0
            ? "\napicheck: OK — every reflected member is present with a resolvable type."
            : $"\napicheck: {broken} member(s) missing or retyped — the plugin needs a fix for this game version.");
        return broken == 0 ? 0 : 1;
    }

    // Statically-resolvable sites: typeof(Type).GetField/GetMethod/GetProperty("name").
    static List<(string type, string kind, string member)> ExtractReflectionSites(string dir, out List<string> dynamicSites)
    {
        var staticRe  = new Regex(@"typeof\(\s*(\w+)\s*\)\s*\.\s*(GetField|GetMethod|GetProperty)\(\s*""([^""]+)""", RegexOptions.Compiled);
        var dynamicRe = new Regex(@"\.GetType\(\)\s*\.\s*(GetField|GetMethod|GetProperty)\(\s*""([^""]+)""", RegexOptions.Compiled);
        var sites = new HashSet<(string, string, string)>();
        dynamicSites = new List<string>();
        foreach (var file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
        {
            string src = File.ReadAllText(file);
            foreach (Match m in staticRe.Matches(src)) sites.Add((m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value));
            foreach (Match m in dynamicRe.Matches(src)) dynamicSites.Add($"{Path.GetFileName(file)}: .GetType().{m.Groups[1].Value}(\"{m.Groups[2].Value}\")");
        }
        return sites.ToList();
    }

    static string FindRepoRoot()
    {
        for (var d = new DirectoryInfo(Directory.GetCurrentDirectory()); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "NOXMFD.csproj"))) return d.FullName;
        return null;
    }

    static string ReadGameDir(string repoRoot)
    {
        string props = Path.Combine(repoRoot, "GameDir.props");
        if (!File.Exists(props)) return null;
        var m = Regex.Match(File.ReadAllText(props), @"<GameDir>\s*(.*?)\s*</GameDir>");
        return m.Success ? m.Groups[1].Value : null;
    }
}

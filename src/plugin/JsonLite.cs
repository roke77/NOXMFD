using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace NOXMFD
{
    // A minimal JSON VALUE reader — not a general JSON library. docs/hud-waypoint-indicator.md.
    // Scoped to exactly the two shapes this feature needs (the routes persistence file, and a
    // pasted route-export blob for wpt.import): object/array/string-with-basic-escapes/number/
    // bool/null, no unicode \uXXXX escapes, no exponent notation, no streaming, no schema
    // validation. A third distinct JSON-parsing need is the signal to pull in a real library
    // instead of growing this one. Unity's JsonUtility is unreliable for nested objects in this
    // Mono runtime (see CommandDispatcher.cs), so this exists in place of it.
    //
    // Parse returns a tree of Dictionary<string,object> / List<object> / string / double / bool /
    // null — callers walk it defensively with `is` checks, same style as the JS side's own
    // parseRouteJSON never trusting the shape it's handed.
    internal static class JsonLite
    {
        public static object? Parse(string text)
        {
            int i = 0;
            SkipWs(text, ref i);
            object? value = ParseValue(text, ref i);
            return value;
        }

        private static object? ParseValue(string s, ref int i)
        {
            if (i >= s.Length) return null;
            char c = s[i];
            if (c == '{') return ParseObject(s, ref i);
            if (c == '[') return ParseArray(s, ref i);
            if (c == '"') return ParseString(s, ref i);
            if (c == 't' && Match(s, i, "true"))  { i += 4; return true; }
            if (c == 'f' && Match(s, i, "false")) { i += 5; return false; }
            if (c == 'n' && Match(s, i, "null"))  { i += 4; return null; }
            if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber(s, ref i);
            return null;   // unrecognized token — caller's defensive walk treats a missing field as absent
        }

        private static Dictionary<string, object?>? ParseObject(string s, ref int i)
        {
            var obj = new Dictionary<string, object?>();
            i++; // '{'
            SkipWs(s, ref i);
            if (Peek(s, i) == '}') { i++; return obj; }
            while (i < s.Length)
            {
                SkipWs(s, ref i);
                if (Peek(s, i) != '"') return obj;   // malformed — return what we have rather than throw
                string key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (Peek(s, i) != ':') return obj;
                i++; // ':'
                SkipWs(s, ref i);
                obj[key] = ParseValue(s, ref i);
                SkipWs(s, ref i);
                char next = Peek(s, i);
                if (next == ',') { i++; continue; }
                if (next == '}') { i++; break; }
                break;
            }
            return obj;
        }

        private static List<object?> ParseArray(string s, ref int i)
        {
            var arr = new List<object?>();
            i++; // '['
            SkipWs(s, ref i);
            if (Peek(s, i) == ']') { i++; return arr; }
            while (i < s.Length)
            {
                SkipWs(s, ref i);
                arr.Add(ParseValue(s, ref i));
                SkipWs(s, ref i);
                char next = Peek(s, i);
                if (next == ',') { i++; continue; }
                if (next == ']') { i++; break; }
                break;
            }
            return arr;
        }

        private static string ParseString(string s, ref int i)
        {
            i++; // opening '"'
            var sb = new StringBuilder();
            while (i < s.Length && s[i] != '"')
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    char esc = s[i + 1];
                    switch (esc)
                    {
                        case '"':  sb.Append('"');  i += 2; break;
                        case '\\': sb.Append('\\'); i += 2; break;
                        case '/':  sb.Append('/');  i += 2; break;
                        case 'n':  sb.Append('\n'); i += 2; break;
                        case 'r':  sb.Append('\r'); i += 2; break;
                        case 't':  sb.Append('\t'); i += 2; break;
                        case 'b':  sb.Append('\b'); i += 2; break;
                        case 'f':  sb.Append('\f'); i += 2; break;
                        default:   sb.Append(esc);  i += 2; break;   // unrecognized escape — keep the literal char
                    }
                }
                else { sb.Append(c); i++; }
            }
            if (i < s.Length) i++; // closing '"'
            return sb.ToString();
        }

        private static double ParseNumber(string s, ref int i)
        {
            int start = i;
            if (Peek(s, i) == '-') i++;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
            if (Peek(s, i) == '.') { i++; while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++; }
            string token = s.Substring(start, i - start);
            return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0.0;
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++;
        }

        private static char Peek(string s, int i) => i < s.Length ? s[i] : '\0';

        private static bool Match(string s, int i, string token) =>
            i + token.Length <= s.Length && string.CompareOrdinal(s, i, token, 0, token.Length) == 0;

        // Lives next to the parser rather than in TelemetryServer.cs (which has real game
        // touchpoints), so RouteStore.cs and any other pure caller can compile standalone
        // (docs/csharp-unit-testing.md). TelemetryServer.EscapeJson forwards here.
        public static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? string.Empty;
            StringBuilder? sb = null;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                string? esc = c switch
                {
                    '\\' => "\\\\",
                    '"'  => "\\\"",
                    '\n' => "\\n",
                    '\r' => "\\r",
                    '\t' => "\\t",
                    '\b' => "\\b",
                    '\f' => "\\f",
                    _ => c < 0x20 ? "\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture) : null
                };
                if (esc == null) { sb?.Append(c); continue; }
                if (sb == null) { sb = new StringBuilder(s.Length + 8); sb.Append(s, 0, i); }
                sb.Append(esc);
            }
            return sb?.ToString() ?? s;
        }

        // Assert-based self-check (no C# test runner exists in this codebase, same reasoning as the
        // Node self-checks src/web's pure JS modules carry). Called once from Plugin.Awake via
        // TryBind, so a broken parser logs a clear cause here instead of surfacing later as a
        // cryptic empty route list. Covers exactly the shape RouteStore.BuildJson's writer produces
        // (nested objects/arrays, escaped strings, numbers, bool/null) plus malformed inputs that
        // must degrade, not throw.
        public static void SelfCheck()
        {
            void Check(bool cond, string what)
            {
                if (!cond) throw new System.Exception($"JsonLite.SelfCheck failed: {what}");
            }

            // Round-trip the exact shape RouteStore's writer produces.
            const string routeJson = "{\"activeRouteId\":\"r_1\",\"routes\":[{\"id\":\"r_1\",\"name\":\"RT-A\"," +
                "\"nextIndex\":1,\"waypoints\":[{\"id\":\"w_1\",\"name\":\"IP\",\"x\":100.5,\"z\":-200.0}]}]}";
            Check(Parse(routeJson) is Dictionary<string, object?> root && (string?)root["activeRouteId"] == "r_1",
                "top-level object with a string field");
            var routes = ((Dictionary<string, object?>)Parse(routeJson)!)["routes"] as List<object?>;
            Check(routes != null && routes.Count == 1, "nested array of objects");
            var route = routes![0] as Dictionary<string, object?>;
            Check(route != null && (double)route["nextIndex"]! == 1.0, "integer field parses as a number");
            var waypoints = route!["waypoints"] as List<object?>;
            var wp = waypoints![0] as Dictionary<string, object?>;
            Check((double)wp!["x"]! == 100.5, "positive decimal");
            Check((double)wp["z"]! == -200.0, "negative number");

            // Escaped strings.
            Check((string?)((Dictionary<string, object?>)Parse("{\"n\":\"a\\\"b\\\\c\\nd\"}")!)["n"] == "a\"b\\c\nd",
                "escaped quote/backslash/newline in a string");

            // Literals.
            var lits = (Dictionary<string, object?>)Parse("{\"t\":true,\"f\":false,\"n\":null}")!;
            Check((bool)lits["t"]! && !(bool)lits["f"]! && lits["n"] == null, "true/false/null literals");

            // Empty object/array.
            Check(Parse("{}") is Dictionary<string, object?> d0 && d0.Count == 0, "empty object");
            Check(Parse("[]") is List<object?> l0 && l0.Count == 0, "empty array");

            // Malformed input must degrade, never throw — RouteStore.Load/ImportRoute both rely on
            // this to reject bad data instead of taking the plugin down.
            Check(Parse("not json") == null, "garbage input returns null rather than throwing");
            Check(Parse("") == null, "empty input returns null rather than throwing");
        }
    }
}

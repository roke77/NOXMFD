using System;

namespace NOXMFD
{
    // Extension payloads are inserted verbatim into shared SSE JSON. This validator deliberately
    // checks syntax only: extension-owned schemas remain opaque to the NOXMFD core contract.
    internal static class ExtensionJsonValidator
    {
        internal static bool IsCompleteValue(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return false;
            int i = 0;
            SkipWs(json, ref i);
            if (!Value(json, ref i)) return false;
            SkipWs(json, ref i);
            return i == json.Length;
        }

        private static bool Value(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) return false;
            return s[i] switch
            {
                '{' => Object(s, ref i),
                '[' => Array(s, ref i),
                '"' => String(s, ref i),
                't' => Literal(s, ref i, "true"),
                'f' => Literal(s, ref i, "false"),
                'n' => Literal(s, ref i, "null"),
                '-' => Number(s, ref i),
                _ when s[i] >= '0' && s[i] <= '9' => Number(s, ref i),
                _ => false,
            };
        }

        private static bool Object(string s, ref int i)
        {
            i++;
            SkipWs(s, ref i);
            if (Take(s, ref i, '}')) return true;
            while (true)
            {
                if (!String(s, ref i)) return false;
                SkipWs(s, ref i);
                if (!Take(s, ref i, ':') || !Value(s, ref i)) return false;
                SkipWs(s, ref i);
                if (Take(s, ref i, '}')) return true;
                if (!Take(s, ref i, ',')) return false;
                SkipWs(s, ref i);
            }
        }

        private static bool Array(string s, ref int i)
        {
            i++;
            SkipWs(s, ref i);
            if (Take(s, ref i, ']')) return true;
            while (true)
            {
                if (!Value(s, ref i)) return false;
                SkipWs(s, ref i);
                if (Take(s, ref i, ']')) return true;
                if (!Take(s, ref i, ',')) return false;
            }
        }

        private static bool String(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (!Take(s, ref i, '"')) return false;
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return true;
                if (c < 0x20) return false;
                if (c != '\\') continue;
                if (i >= s.Length) return false;
                char escape = s[i++];
                if (escape == 'u')
                {
                    for (int n = 0; n < 4; n++)
                        if (i >= s.Length || !Uri.IsHexDigit(s[i++])) return false;
                }
                else if (escape != '"' && escape != '\\' && escape != '/' && escape != 'b' &&
                         escape != 'f' && escape != 'n' && escape != 'r' && escape != 't') return false;
            }
            return false;
        }

        private static bool Number(string s, ref int i)
        {
            if (Take(s, ref i, '-')) { }
            if (i >= s.Length) return false;
            if (s[i] == '0') i++;
            else
            {
                if (s[i] < '1' || s[i] > '9') return false;
                while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
            }
            if (Take(s, ref i, '.'))
            {
                int fractionStart = i;
                while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
                if (i == fractionStart) return false;
            }
            if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
            {
                i++;
                if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
                int exponentStart = i;
                while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
                if (i == exponentStart) return false;
            }
            return true;
        }

        private static bool Literal(string s, ref int i, string literal)
        {
            if (i + literal.Length > s.Length || string.CompareOrdinal(s, i, literal, 0, literal.Length) != 0)
                return false;
            i += literal.Length;
            return true;
        }

        private static bool Take(string s, ref int i, char expected)
        {
            if (i >= s.Length || s[i] != expected) return false;
            i++;
            return true;
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n')) i++;
        }
    }
}

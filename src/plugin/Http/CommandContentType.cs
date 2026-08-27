using System;

namespace NOXMFD
{
    internal static class CommandContentType
    {
        internal static bool IsJson(string? contentType)
        {
            if (string.IsNullOrWhiteSpace(contentType)) return false;

            int semi = contentType.IndexOf(';');
            string mediaType = semi >= 0 ? contentType.Substring(0, semi) : contentType;
            return string.Equals(mediaType.Trim(), "application/json", StringComparison.OrdinalIgnoreCase);
        }
    }
}

using System;
using System.Text;

namespace NOXMFD
{
    // Wire rules for extension messages (docs/extension-messaging.md), pure part: BCL-only so
    // tools/tests links it directly; ExtensionMessages.cs is the live glue. An extension's messages
    // ride the squadron transport under the type "ext.<id>", so they can never be mistaken for
    // NOXMFD's own "sqd.*" or "presence" messages, nor an extension send one of those.
    internal static class ExtensionMessageWire
    {
        internal const string Prefix = "ext.";

        // Small coordination messages only. Well under the transport's 16 KB cap even after the
        // envelope escapes every quote in a JSON payload.
        internal const int MaxPayloadBytes = 4096;

        internal static string TypeFor(string id) => Prefix + id;

        // The extension id a wire type addresses; false for NOXMFD's own types and for a bare "ext.".
        internal static bool TryIdFrom(string? type, out string id)
        {
            id = string.Empty;
            if (type == null || type.Length <= Prefix.Length || !type.StartsWith(Prefix, StringComparison.Ordinal)) return false;
            id = type.Substring(Prefix.Length);
            return true;
        }

        internal static bool PayloadFits(string? payload) =>
            payload != null && Encoding.UTF8.GetByteCount(payload) <= MaxPayloadBytes;
    }
}

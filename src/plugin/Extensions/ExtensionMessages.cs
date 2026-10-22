using System;
using System.Collections.Generic;
using System.Linq;

namespace NOXMFD
{
    // Per-extension messaging over the squadron transport (docs/extension-messaging.md). Backs
    // Api.SendMessage/SetMessageHandler. Every member here runs on the Unity main thread: the Api
    // calls come from an extension's Update() or command handler, Drain from MissionLifecycle.
    //
    // Sends go only to faction-mates known to run NOXMFD (or squadmates): a session toward a player
    // without the mod is never accepted, and repeated connects stall their match traffic
    // (Squadron.cs). Received messages are delivered as they come: `from` is authenticated by Steam,
    // but the payload is untrusted input that the extension validates itself.
    internal static class ExtensionMessages
    {
        private static readonly Dictionary<string, Api.MessageHandler> _handlers =
            new Dictionary<string, Api.MessageHandler>(StringComparer.Ordinal);
        private static readonly HashSet<string> _warned = new HashSet<string>(StringComparer.Ordinal);
        private static long _drainedSeq;   // our own cursor into Squadron's shared inbox

        // A null or empty id is rejected here: the registry's dictionary would throw on null, and this
        // is reached straight from the public Api.
        private static bool Registered(string? id) => !string.IsNullOrEmpty(id) && ExtensionRegistry.TryGet(id!, out _);

        internal static bool SetHandler(string id, Api.MessageHandler? handler)
        {
            if (!Registered(id)) return Reject(id, "handler", "is not a registered extension");
            if (handler == null) _handlers.Remove(id);
            else _handlers[id] = handler;
            return true;
        }

        internal static void RemoveHandler(string id) => _handlers.Remove(id);

        internal static bool Send(string id, ulong to, string payload)
        {
            if (!Registered(id)) return Reject(id, "send", "is not a registered extension");
            if (!ExtensionMessageWire.PayloadFits(payload))
                return Reject(id, "size", $"message rejected: payload missing or over {ExtensionMessageWire.MaxPayloadBytes} bytes");
            if (to == 0 || to == Squadron.SelfId()) return false;
            if (!Presence.HasNoxmfd(to) && !Squad.SquadmateSteamIds().Contains(to))
                return Reject(id, "peer", $"message to {to} rejected: not known to run NOXMFD");
            return Squadron.SendTo(to, ExtensionMessageWire.TypeFor(id), payload);
        }

        // Called once per frame from MissionLifecycle, right after Squadron.Poll() — an independent
        // cursor into the same shared inbox Squad/Presence read, so messages flow at the main menu too.
        internal static void Drain()
        {
            foreach (var m in Squadron.Since(_drainedSeq))
            {
                _drainedSeq = m.Seq;
                if (!ExtensionMessageWire.TryIdFrom(m.Type, out string id)) continue;
                if (!_handlers.TryGetValue(id, out Api.MessageHandler h)) continue;   // extension not installed here
                if (!ExtensionMessageWire.PayloadFits(m.Payload)) continue;
                try { h(m.From, m.Payload); }
                catch (Exception ex) { Plugin.Log?.LogWarning($"[NOXMFD] extension '{id}' message handler threw: {ex.Message}"); }
            }
        }

        // Logged once per extension and kind, so an extension retrying every frame doesn't flood the log.
        private static bool Reject(string id, string kind, string why)
        {
            if (_warned.Add(id + "|" + kind)) Plugin.Log?.LogWarning($"[NOXMFD] extension '{id}' {why}.");
            return false;
        }
    }
}

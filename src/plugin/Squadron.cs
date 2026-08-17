using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Steamworks;

namespace NOXMFD
{
    // ── Squadron transport ───────────────────────────────────────────────────────
    // Small-text data sharing between players over Steam's peer-to-peer relay
    // (docs/squadron-transport.md). A squadron is a set of SteamIDs; a send addresses each of them
    // directly. Valve's relay handles NAT traversal, encryption and identity, so there is no server
    // and no cost.
    //
    // Why SteamNetworkingMessages and not the game's own connection: the game's transport is the
    // DISTINCT SteamNetworkingSockets interface (Mirage.SteamworksSocket uses CreateListenSocketP2P/
    // ConnectP2P), so this messaging interface is entirely unused by the game and cannot interfere
    // with match traffic. Sending a custom message over the game's own Mirage session would instead
    // charge the sender's error budget and can disconnect them against a vanilla server.
    //
    // Why no Steam lobby: Steamworks.NET's Callback<T>.Create is process-global, so a lobby created
    // here would also fire the game's own SteamLobby.OnLobbyCreated and could satisfy a pending
    // lobby-creation completion source, corrupting the game's lobby state. Membership is therefore a
    // plain SteamID set for now; see the doc's deferred section.
    //
    // Steam ownership: the game's SteamManager calls SteamAPI.Init (and throws if initialised twice)
    // and pumps SteamAPI.RunCallbacks every frame from its own Update. This class therefore never
    // initialises, shuts down, or pumps Steam — it only registers a callback and polls its channel.
    internal static class Squadron
    {
        // Our own channel on the messaging interface. The game never touches this interface at all,
        // so the value only has to be stable, not negotiated.
        private const int Channel = 0x4E58;   // 'N','X'

        // Small text only, by design (docs/squadron-transport.md). A 10-waypoint route is ~436 bytes;
        // this leaves room for far larger routes while still rejecting anything that belongs to the
        // deferred heavy-payload features. Steam itself would allow 512 KB.
        internal const int MaxPayloadBytes = 16 * 1024;

        private const int MaxReceivePerPoll = 32;   // bound the work done in one frame

        // How many inbound messages stay queued for the browser to collect. Each SSE connection
        // tracks the last sequence it forwarded, so a display that reconnects mid-mission picks up
        // from wherever it left off rather than replaying everything or missing a burst.
        private const int InboxCapacity = 64;

        private static readonly object _lock = new object();
        private static readonly HashSet<ulong> _peers = new HashSet<ulong>();
        private static readonly List<Inbound> _inbox = new List<Inbound>();
        private static long _seq;

        private static Callback<SteamNetworkingMessagesSessionRequest_t>? _sessionRequest;
        private static bool _inited;

        internal readonly struct Inbound
        {
            internal Inbound(long seq, ulong from, string type, string payload)
            { Seq = seq; From = from; Type = type; Payload = payload; }

            internal long   Seq     { get; }
            internal ulong  From    { get; }
            internal string Type    { get; }
            internal string Payload { get; }
        }

        // ── Lifecycle ────────────────────────────────────────────────────────────

        // Registering the session callback needs Steam already initialised, and the plugin's own
        // Awake can run before the game's SteamManager gets there. So this is lazy and retry-safe
        // rather than a boot-order bet: it stays uninitialised until Steam is actually up, and any
        // later call picks it up. _warned keeps a permanently Steam-less launch (a non-Steam build)
        // from logging once per frame forever.
        private static bool _warned;

        private static bool EnsureInit()
        {
            if (_inited) return true;
            try
            {
                if (!SteamAPI.IsSteamRunning()) return false;
                // An unsolicited session must be accepted before its messages arrive. Only peers the
                // pilot already added are accepted, so a stranger who learns this SteamID cannot open
                // a session by sending to it — see AcceptOrReject.
                _sessionRequest = Callback<SteamNetworkingMessagesSessionRequest_t>.Create(AcceptOrReject);
                _inited = true;
                Plugin.Log?.LogInfo("[NOXMFD] Squadron transport ready");
                return true;
            }
            catch (Exception ex)
            {
                // No Steam (or a non-Steam build): the feature is simply unavailable. Everything else
                // in the mod keeps working, so this must never be fatal.
                if (!_warned)
                {
                    _warned = true;
                    Plugin.Log?.LogWarning($"[NOXMFD] Squadron transport unavailable: {ex.Message}");
                }
                return false;
            }
        }

        // Is Steam actually usable? Every entry point checks this so a non-Steam launch degrades to
        // "squadron does nothing" rather than throwing into the frame loop.
        internal static bool Ready => EnsureInit();

        internal static ulong SelfId()
        {
            if (!Ready) return 0;
            try { return SteamUser.GetSteamID().m_SteamID; }
            catch { return 0; }
        }

        // ── Membership ───────────────────────────────────────────────────────────

        internal static bool AddPeer(ulong steamId)
        {
            if (steamId == 0 || steamId == SelfId()) return false;
            lock (_lock)
            {
                if (!_peers.Add(steamId)) return true;   // already a member — idempotent
            }
            // Accept pre-emptively so the peer's first message is not dropped waiting for us to
            // answer their session request.
            var id = Identity(steamId);
            try { SteamNetworkingMessages.AcceptSessionWithUser(ref id); } catch { }
            Plugin.Log?.LogInfo($"[NOXMFD] Squadron peer added: {steamId}");
            return true;
        }

        internal static bool RemovePeer(ulong steamId)
        {
            bool removed;
            lock (_lock) { removed = _peers.Remove(steamId); }
            if (removed)
            {
                var id = Identity(steamId);
                try { SteamNetworkingMessages.CloseSessionWithUser(ref id); } catch { }
                Plugin.Log?.LogInfo($"[NOXMFD] Squadron peer removed: {steamId}");
            }
            return removed;
        }

        internal static void Clear()
        {
            ulong[] all;
            lock (_lock)
            {
                all = new ulong[_peers.Count];
                _peers.CopyTo(all);
                _peers.Clear();
            }
            foreach (ulong p in all)
            {
                var id = Identity(p);
                try { SteamNetworkingMessages.CloseSessionWithUser(ref id); } catch { }
            }
        }

        internal static ulong[] Peers()
        {
            lock (_lock)
            {
                var all = new ulong[_peers.Count];
                _peers.CopyTo(all);
                return all;
            }
        }

        // Only peers the pilot added are accepted. Anyone else is closed out rather than left
        // pending, so an unsolicited sender gets no session and no traffic.
        private static void AcceptOrReject(SteamNetworkingMessagesSessionRequest_t req)
        {
            ulong from = req.m_identityRemote.GetSteamID64();
            bool known;
            lock (_lock) { known = _peers.Contains(from); }
            var id = Identity(from);
            try
            {
                if (known) SteamNetworkingMessages.AcceptSessionWithUser(ref id);
                else       SteamNetworkingMessages.CloseSessionWithUser(ref id);
            }
            catch { }
            if (!known) Plugin.Log?.LogInfo($"[NOXMFD] Squadron rejected session from unknown {from}");
        }

        // ── Send ─────────────────────────────────────────────────────────────────

        // Broadcasts one typed payload to every peer, reliably and in order. Returns how many peers
        // it went to. Reliable because routes and target designation must arrive — the deferred
        // datalink/video features are the ones that want an unreliable channel instead, which is why
        // the envelope carries a type rather than assuming one kind of message.
        internal static int Send(string type, string payload)
        {
            if (!Ready) return 0;
            payload ??= string.Empty;
            type    ??= string.Empty;

            string wire = Envelope(type, payload);
            byte[] bytes = Encoding.UTF8.GetBytes(wire);
            if (bytes.Length > MaxPayloadBytes)
            {
                Plugin.Log?.LogWarning($"[NOXMFD] Squadron send rejected: {bytes.Length} B exceeds {MaxPayloadBytes} B");
                return 0;
            }

            ulong[] peers = Peers();
            if (peers.Length == 0) return 0;

            int sent = 0;
            IntPtr buf = Marshal.AllocHGlobal(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, buf, bytes.Length);
                foreach (ulong p in peers)
                {
                    var id = Identity(p);
                    try
                    {
                        EResult r = SteamNetworkingMessages.SendMessageToUser(
                            ref id, buf, (uint)bytes.Length,
                            Constants.k_nSteamNetworkingSend_Reliable, Channel);
                        if (r == EResult.k_EResultOK) sent++;
                        else Plugin.Log?.LogWarning($"[NOXMFD] Squadron send to {p} failed: {r}");
                    }
                    catch (Exception ex) { Plugin.Log?.LogWarning($"[NOXMFD] Squadron send to {p} threw: {ex.Message}"); }
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
            return sent;
        }

        // ── Receive ──────────────────────────────────────────────────────────────

        // Drains this channel into the inbox. Called every frame from MissionLifecycle, the same
        // persistent host that polls keybinds and drains commands — so squadron traffic flows at the
        // main menu too, not only during a mission (route planning happens before a flight).
        internal static void Poll()
        {
            if (!Ready) return;
            IntPtr[] msgs = new IntPtr[MaxReceivePerPoll];
            int n;
            try { n = SteamNetworkingMessages.ReceiveMessagesOnChannel(Channel, msgs, msgs.Length); }
            catch { return; }

            for (int i = 0; i < n; i++)
            {
                IntPtr ptr = msgs[i];
                if (ptr == IntPtr.Zero) continue;
                try
                {
                    var m = SteamNetworkingMessage_t.FromIntPtr(ptr);
                    ulong from = m.m_identityPeer.GetSteamID64();

                    // Everything past this point is untrusted input from another player's machine —
                    // bounds first, parse second.
                    bool known;
                    lock (_lock) { known = _peers.Contains(from); }
                    if (!known) continue;
                    if (m.m_cbSize <= 0 || m.m_cbSize > MaxPayloadBytes) continue;

                    byte[] data = new byte[m.m_cbSize];
                    Marshal.Copy(m.m_pData, data, 0, m.m_cbSize);
                    Accept(from, Encoding.UTF8.GetString(data));
                }
                catch (Exception ex) { Plugin.Log?.LogWarning($"[NOXMFD] Squadron receive error: {ex.Message}"); }
                finally
                {
                    // The instance Release() throws by design in this binding; the static one is the
                    // real free. Skipping it leaks the native message.
                    try { SteamNetworkingMessage_t.Release(ptr); } catch { }
                }
            }
        }

        private static void Accept(ulong from, string wire)
        {
            if (!TryParse(wire, out string type, out string payload)) return;
            lock (_lock)
            {
                _seq++;
                _inbox.Add(new Inbound(_seq, from, type, payload));
                if (_inbox.Count > InboxCapacity) _inbox.RemoveRange(0, _inbox.Count - InboxCapacity);
            }
        }

        // Everything newer than `afterSeq`, oldest first. The SSE loop passes the last sequence it
        // forwarded on that connection, so each display gets each message once.
        internal static List<Inbound> Since(long afterSeq)
        {
            var outp = new List<Inbound>();
            lock (_lock)
            {
                foreach (var m in _inbox) if (m.Seq > afterSeq) outp.Add(m);
            }
            return outp;
        }

        internal static long LatestSeq() { lock (_lock) { return _seq; } }

        // ── Wire format ──────────────────────────────────────────────────────────
        // Versioned from the first message: squadron members will run different mod versions, and a
        // transport carrying several independently evolving features has to ignore what it cannot
        // parse rather than misread it. Hand-rolled rather than JsonUtility because this runs off
        // a Steam callback path and the format is two strings.
        private const int WireVersion = 1;

        private static string Envelope(string type, string payload) =>
            "{\"v\":" + WireVersion +
            ",\"type\":\"" + TelemetryServer.EscapeJson(type) + "\"" +
            ",\"payload\":\"" + TelemetryServer.EscapeJson(payload) + "\"}";

        private static bool TryParse(string wire, out string type, out string payload)
        {
            type = string.Empty; payload = string.Empty;
            if (string.IsNullOrEmpty(wire)) return false;
            try
            {
                var env = UnityEngine.JsonUtility.FromJson<WireEnvelope>(wire);
                if (env == null || env.v != WireVersion) return false;
                type    = env.type    ?? string.Empty;
                payload = env.payload ?? string.Empty;
                return type.Length > 0;
            }
            catch { return false; }
        }

        [Serializable]
        private class WireEnvelope
        {
            public int    v;
            public string type;
            public string payload;
        }

        private static SteamNetworkingIdentity Identity(ulong steamId)
        {
            var id = new SteamNetworkingIdentity();
            id.Clear();
            id.SetSteamID64(steamId);
            return id;
        }
    }
}

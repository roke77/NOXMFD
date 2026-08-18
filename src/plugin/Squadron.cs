using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Steamworks;

namespace NOXMFD
{
    // ── Squadron transport ───────────────────────────────────────────────────────
    // Generic small-text messaging between two players over Steam's peer-to-peer relay
    // (docs/squadron-transport.md). This layer only moves bytes between SteamIDs — it knows nothing
    // about squads, leaders, or invites; that protocol lives in Squad.cs, on top of this. Valve's
    // relay handles NAT traversal, encryption and identity, so there is no server and no cost.
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
    //
    // Trust model: this layer accepts a session from ANYONE — an invite has to reach a stranger
    // before they can decide to join a squad, so gating sessions by a pre-approved list (the old
    // design) doesn't fit a leader/member protocol. Consent now lives at the Squad.cs layer: it
    // decides, per message TYPE and sender, whether a message is meaningful (e.g. an sqd.invite is
    // welcome from anyone, but an sqd.roster is only trusted from the current leader). The bounds
    // that matter against an unsolicited sender — a size cap and a versioned, strictly-parsed
    // envelope — are unchanged.
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

        // How many inbound messages stay queued for Squad.cs (and, for debugging, the browser) to
        // collect. Each reader tracks the last sequence it consumed, so a late reader picks up from
        // wherever it left off rather than replaying everything or missing a burst.
        private const int InboxCapacity = 64;

        private static readonly object _lock = new object();
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
                _sessionRequest = Callback<SteamNetworkingMessagesSessionRequest_t>.Create(AcceptSession);
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
        // "squad does nothing" rather than throwing into the frame loop.
        internal static bool Ready => EnsureInit();

        internal static ulong SelfId()
        {
            if (!Ready) return 0;
            try { return SteamUser.GetSteamID().m_SteamID; }
            catch { return 0; }
        }

        // ── Sessions ─────────────────────────────────────────────────────────────
        // Bookkeeping only — SendTo/AcceptSession work regardless of whether a session was opened
        // here first (Steam opens one implicitly on first send/receive). Squad.cs calls these to
        // proactively open a session before it has anything to send (e.g. a leader opening toward an
        // invitee) and to close one when a relationship ends (a member leaving, a kick, disband).

        internal static void OpenSession(ulong steamId)
        {
            if (!Ready || steamId == 0 || steamId == SelfId()) return;
            var id = Identity(steamId);
            try { SteamNetworkingMessages.AcceptSessionWithUser(ref id); } catch { }
        }

        internal static void CloseSession(ulong steamId)
        {
            if (!Ready || steamId == 0) return;
            var id = Identity(steamId);
            try { SteamNetworkingMessages.CloseSessionWithUser(ref id); } catch { }
        }

        internal static void CloseSessions(IEnumerable<ulong> steamIds)
        {
            foreach (ulong p in steamIds) CloseSession(p);
        }

        // Accept every incoming session request — see the trust-model note at the top of this file.
        private static void AcceptSession(SteamNetworkingMessagesSessionRequest_t req)
        {
            ulong from = req.m_identityRemote.GetSteamID64();
            var id = Identity(from);
            try { SteamNetworkingMessages.AcceptSessionWithUser(ref id); } catch { }
        }

        // ── Send ─────────────────────────────────────────────────────────────────

        // Sends one typed payload to exactly one peer, reliably and in order. True on success.
        // Reliable because every squad-protocol message must arrive — an unreliable channel is only
        // interesting for the deferred datalink/video features, which is why the envelope carries a
        // type rather than assuming one kind of message.
        internal static bool SendTo(ulong peer, string type, string payload)
        {
            if (!Ready || peer == 0) return false;
            payload ??= string.Empty;
            type    ??= string.Empty;

            string wire = Envelope(type, payload);
            byte[] bytes = Encoding.UTF8.GetBytes(wire);
            if (bytes.Length > MaxPayloadBytes)
            {
                Plugin.Log?.LogWarning($"[NOXMFD] Squadron send rejected: {bytes.Length} B exceeds {MaxPayloadBytes} B");
                return false;
            }

            var id = Identity(peer);
            IntPtr buf = Marshal.AllocHGlobal(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, buf, bytes.Length);
                EResult r = SteamNetworkingMessages.SendMessageToUser(
                    ref id, buf, (uint)bytes.Length,
                    Constants.k_nSteamNetworkingSend_Reliable, Channel);
                if (r == EResult.k_EResultOK) return true;
                Plugin.Log?.LogWarning($"[NOXMFD] Squadron send to {peer} failed: {r}");
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"[NOXMFD] Squadron send to {peer} threw: {ex.Message}");
                return false;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        // Convenience for the leader's broadcasts (roster, disband, shared data) — same send, looped.
        internal static void SendToAll(IEnumerable<ulong> peers, string type, string payload)
        {
            foreach (ulong p in peers) SendTo(p, type, payload);
        }

        // ── Receive ──────────────────────────────────────────────────────────────

        // Drains this channel into the inbox. Called every frame from MissionLifecycle, the same
        // persistent host that polls keybinds and drains commands — so squad traffic flows at the
        // main menu too, not only during a mission (squad formation happens before a flight too).
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
                    // bounds first, parse second. Who is allowed to say what is Squad.cs's job now.
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

        // Everything newer than `afterSeq`, oldest first. Squad.Drain() (and, for debugging, the SSE
        // loop) passes the last sequence it consumed, so each reader sees each message once.
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
        // Versioned from the first message: squad members will run different mod versions, and a
        // transport carrying several independently evolving message types has to ignore what it
        // cannot parse rather than misread it. Hand-rolled rather than JsonUtility because this runs
        // off a Steam callback path and the format is two strings.
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

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace NOXMFD
{
    // Backing store for Api.cs — kept separate so Api.cs stays a small, purely documented public
    // surface while this holds whatever bookkeeping it needs without that becoming part of the
    // contract. TelemetryServer reads from this for routing/serialization; MissionLifecycle.Update
    // drains the command queue.
    internal static class ExtensionRegistry
    {
        internal sealed class Entry
        {
            public string Id = string.Empty;
            public string Label = string.Empty;
            public Api.AssetResolver Resolve = _ => null;
            public Api.CommandHandler? Command;
        }

        private static readonly Dictionary<string, Entry> _extensions = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private static readonly object _extLock = new object();

        // Latest published slice JSON per extension id — volatile-latest-wins: written from an
        // extension's own Update(), read by TelemetryServer's frame serializer every tick.
        private static readonly ConcurrentDictionary<string, string> _slices = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        // Latest published high-rate event JSON per event name, same shape as _slices.
        private static readonly ConcurrentDictionary<string, string> _events = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> _emptyEvents = new Dictionary<string, string>(StringComparer.Ordinal);

        private const int MaxQueuedCommands = 64;
        private static readonly Queue<(string Id, string Json)> _cmdQueue = new Queue<(string, string)>();
        private static readonly object _cmdLock = new object();

        internal static bool Register(string id, string label, Api.AssetResolver resolve, Api.CommandHandler? command)
        {
            if (string.IsNullOrEmpty(id) || resolve == null) return false;
            lock (_extLock)
            {
                if (_extensions.ContainsKey(id))
                {
                    Plugin.Log?.LogWarning($"[NOXMFD] extension '{id}' already registered — ignoring.");
                    return false;
                }
                _extensions[id] = new Entry { Id = id, Label = string.IsNullOrEmpty(label) ? id : label, Resolve = resolve, Command = command };
            }
            Plugin.Log?.LogInfo($"[NOXMFD] extension '{id}' registered ({label}).");
            return true;
        }

        internal static void Unregister(string id)
        {
            lock (_extLock) _extensions.Remove(id);
            _slices.TryRemove(id, out _);
        }

        internal static bool TryGet(string id, out Entry entry)
        {
            lock (_extLock) return _extensions.TryGetValue(id, out entry!);
        }

        // For GET /ext-manifest — sorted by id for a deterministic order, since a plain Dictionary's
        // enumeration order isn't a contract worth relying on and the EXT hub's nav should render in
        // the same order every load.
        internal static List<Entry> Manifest()
        {
            lock (_extLock)
            {
                var list = new List<Entry>(_extensions.Values);
                list.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
                return list;
            }
        }

        internal static void PublishSlice(string id, string json)
        {
            if (string.IsNullOrEmpty(id) || json == null) return;
            _slices[id] = json;
        }

        // Built fresh per frame — the expected extension count is small, so no caching is worth the
        // complexity.
        internal static string SlicesJson()
        {
            if (_slices.IsEmpty) return "{}";
            var sb = new StringBuilder("{");
            bool first = true;
            foreach (var kv in _slices)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(TelemetryServer.EscapeJson(kv.Key)).Append("\":").Append(kv.Value);
            }
            return sb.Append('}').ToString();
        }

        internal static void PublishEvent(string eventName, string json)
        {
            if (string.IsNullOrEmpty(eventName) || json == null) return;
            _events[eventName] = json;
        }

        // Snapshot for one SSE connection's per-tick diff pass — a copy so that connection's own
        // "last sent per name" comparison isn't racing a concurrent publish mid-iteration. Returns a
        // shared empty instance when nothing's published, so the common zero-extension case costs no
        // allocation.
        internal static Dictionary<string, string> EventsSnapshot()
            => _events.IsEmpty ? _emptyEvents : new Dictionary<string, string>(_events, StringComparer.Ordinal);

        // Continuous MJPEG feed state, one per extension id that ever calls PushMjpegFrame.
        internal sealed class MjpegState
        {
            public byte[]? Jpg;
            public long FrameId;
            public int Subscribers;
            public readonly object Lock = new object();
        }
        private static readonly ConcurrentDictionary<string, MjpegState> _mjpeg = new ConcurrentDictionary<string, MjpegState>(StringComparer.Ordinal);
        private static MjpegState GetOrAddMjpeg(string id) => _mjpeg.GetOrAdd(id, _ => new MjpegState());

        internal static void PushMjpegFrame(string id, byte[] jpg)
        {
            if (string.IsNullOrEmpty(id) || jpg == null || jpg.Length == 0) return;
            MjpegState st = GetOrAddMjpeg(id);
            lock (st.Lock) { st.Jpg = jpg; st.FrameId++; }
        }

        internal static void ClearMjpegFrame(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            MjpegState st = GetOrAddMjpeg(id);
            lock (st.Lock) { st.Jpg = null; st.FrameId++; }
        }

        internal static bool WantsMjpegFrames(string id)
            => !string.IsNullOrEmpty(id) && _mjpeg.TryGetValue(id, out MjpegState st) && Volatile.Read(ref st.Subscribers) > 0;

        // Bumped/dropped by HandleExtMjpegAsync's try/finally.
        internal static void MjpegSubscribe(string id) => Interlocked.Increment(ref GetOrAddMjpeg(id).Subscribers);
        internal static void MjpegUnsubscribe(string id) { if (_mjpeg.TryGetValue(id, out MjpegState st)) Interlocked.Decrement(ref st.Subscribers); }

        internal static bool TryGetMjpegFrame(string id, out byte[]? jpg, out long frameId)
        {
            jpg = null; frameId = -1;
            if (!_mjpeg.TryGetValue(id, out MjpegState st)) return false;
            lock (st.Lock) { jpg = st.Jpg; frameId = st.FrameId; }
            return true;
        }

        internal static bool TryEnqueueCommand(string id, string json)
        {
            lock (_cmdLock)
            {
                if (_cmdQueue.Count >= MaxQueuedCommands) return false;
                _cmdQueue.Enqueue((id, json));
                return true;
            }
        }

        // Drained once per frame on the main thread (MissionLifecycle.Update, alongside
        // CommandDispatcher.Drain), not a mission-scoped reader — an extension's command endpoint
        // must work at the main menu, not just in-mission.
        internal static void Drain()
        {
            while (true)
            {
                (string Id, string Json) item;
                lock (_cmdLock)
                {
                    if (_cmdQueue.Count == 0) break;
                    item = _cmdQueue.Dequeue();
                }
                if (!TryGet(item.Id, out Entry entry) || entry.Command == null) continue;
                try { entry.Command(item.Json); }
                catch (Exception ex) { Plugin.Log?.LogWarning($"[NOXMFD] extension '{item.Id}' command threw: {ex.Message}"); }
            }
        }
    }
}

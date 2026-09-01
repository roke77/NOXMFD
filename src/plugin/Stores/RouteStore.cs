using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace NOXMFD
{
    internal sealed class Waypoint
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public float X;
        public float Z;
    }

    internal sealed class SteerPoint
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public float X;
        public float Z;
        public string SharedBy = string.Empty;
        public bool IsShared => SharedBy.Length > 0;
        public bool SharedWithSquad;
    }

    internal sealed class Route
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public int NextIndex;
        public List<Waypoint> Waypoints = new List<Waypoint>();

        // Empty = a route this pilot created or pasted themselves — fully editable. Non-empty = the
        // squad leader's display name at the moment this was accepted — the route's CONTENT
        // (name/waypoints) is then read-only; only this pilot's own progress through it (NextIndex)
        // and whether it's their active route are still theirs to change. Not persisted across a
        // squad session ending — see AcceptShared.
        public string SharedBy = string.Empty;
        public bool IsShared => SharedBy.Length > 0;

        // True once this pilot's OWN route has been shared with the squad at least once (via the
        // share button, RouteStore.ShareRoute) — from then on, every edit to it re-broadcasts
        // automatically (see BroadcastIfShared) instead of requiring another manual click. Ephemeral
        // like SharedBy/pendingShared: not persisted, since it only means anything within the squad
        // session that first shared it.
        public bool SharedWithSquad;
    }

    // A shared route awaiting this pilot's accept/reject — not yet a real Route (docs/squadron-
    // transport.md). Kept entirely in memory, not persisted to routes.json: like squad membership
    // itself, a pending share only makes sense within the squad session that produced it.
    internal sealed class PendingSharedRoute
    {
        public string Id = string.Empty;   // == the leader's own route id — see ReceiveSharedRoute
        public string Name = string.Empty;
        public string FromName = string.Empty;
        public List<Waypoint> Waypoints = new List<Waypoint>();
    }

    internal sealed class PendingSharedSteerPoint
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public string FromName = string.Empty;
        public float X;
        public float Z;
    }

    // Route and steer-point storage (docs/steer-points.md) — the plugin, not any browser, is the
    // single source of truth for the whole navigation library. One process owns the data, so every
    // browser sees the same selection regardless of which one created it; the route-specific
    // proximity advance also runs independently of which page is visible.
    //
    // Static, plugin-lifetime (NOT mission-scoped) — routes must survive a mission restart AND a
    // full game restart, and must be browsable/editable at the main menu.
    internal static class RouteStore
    {
        // The plugin is the only proximity authority; clients only render the resulting NextIndex.
        private const float AdvanceThresholdM = 1000f;

        private static List<Route> _routes = new List<Route>();
        private static List<SteerPoint> _steerPoints = new List<SteerPoint>();
        private static string? _activeRouteId;
        private static string? _activeSteerPointId;
        private static readonly List<PendingSharedRoute> _pendingShared = new List<PendingSharedRoute>();
        private static readonly List<PendingSharedSteerPoint> _pendingSharedSteerPoints = new List<PendingSharedSteerPoint>();

        // Server-thread-readable cache, mirrors TelemetryServer.HudOptionsJson's threading: every
        // mutator below runs on the Unity main thread only (CommandDispatcher.Drain, called from
        // MissionLifecycle.Update — unconditional, so this works at the main menu too), and rebuilds
        // this string synchronously as its last step. The HTTP server thread only ever reads the
        // reference, never the underlying route/steer-point lists, so no lock is needed.
        internal static volatile string RoutesJson = "{\"activeRouteId\":null,\"activeSteerPointId\":null,\"routes\":[],\"steerPoints\":[]}";

        // Storage/log seam: keeps this file free of any BepInEx/Plugin reference so a standalone
        // test project can compile and exercise the mutators directly. Plugin.Awake sets both
        // before calling Load(); a test project leaves ConfigDir null (FilePath then resolves under
        // the test run's own working directory) and can set PersistToDisk false to exercise the
        // mutators without touching disk at all.
        internal static string? ConfigDir;
        internal static Action<string>? LogWarning;
        internal static bool PersistToDisk = true;

        // Same seam, for Squad.cs (Steam/BepInEx-dependent, also outside the test project):
        // Plugin.Awake wires this to Squad.SendData; left null in tests, where share/broadcast
        // calls simply report that no transport accepted the payload.
        internal static Func<string, string, bool>? SendSquadData;

        private static string FilePath =>
            Path.Combine(ConfigDir ?? ".", "com.roque.NOXMFD.routes.json");

        // ── lifecycle ────────────────────────────────────────────────────────────────────────

        // Called once from Plugin.Awake via TryBind. A missing file is normal (fresh install) — a
        // corrupt one is logged and treated the same as missing, never thrown: one broken data file
        // must not take the rest of the plugin down with it (same defensive shape TryBind already
        // gives every other subsystem).
        public static void Load()
        {
            if (!File.Exists(FilePath)) return;
            try
            {
                string text = File.ReadAllText(FilePath);
                if (JsonLite.Parse(text) is Dictionary<string, object?> root)
                {
                    _activeRouteId = root.TryGetValue("activeRouteId", out object? a) ? a as string : null;
                    _activeSteerPointId = root.TryGetValue("activeSteerPointId", out object? s) ? s as string : null;
                    _routes = ParseRoutes(root.TryGetValue("routes", out object? r) ? r : null);
                    _steerPoints = ParseSteerPoints(root.TryGetValue("steerPoints", out object? sp) ? sp : null);
                }
            }
            catch (Exception ex)
            {
                LogWarning?.Invoke($"[NOXMFD] routes file unreadable, starting empty: {ex.Message}");
                _routes = new List<Route>();
                _steerPoints = new List<SteerPoint>();
                _activeRouteId = null;
                _activeSteerPointId = null;
            }
            RefreshServedJsonOnly();
        }

        private static List<Route> ParseRoutes(object? value)
        {
            var routes = new List<Route>();
            if (value is not List<object?> list) return routes;
            foreach (object? item in list)
            {
                if (item is not Dictionary<string, object?> d) continue;
                var route = new Route
                {
                    Id        = d.TryGetValue("id", out object? id) ? (id as string ?? string.Empty) : string.Empty,
                    Name      = d.TryGetValue("name", out object? nm) ? (nm as string ?? string.Empty) : string.Empty,
                    NextIndex = d.TryGetValue("nextIndex", out object? ni) && ni is double nid ? (int)nid : 0,
                    // SharedBy is NOT restored from disk (see Route.SharedBy's own header comment —
                    // session-only by design) even though older files may still carry the field: a
                    // route that was locked read-only when the game last closed must come back
                    // editable after a restart, since the squad session that justified the lock is
                    // long gone by then and would otherwise never get the chance to clear it
                    // (OnSquadEnded only runs while the plugin is actually live).
                };
                if (d.TryGetValue("waypoints", out object? wps) && wps is List<object?> wlist)
                {
                    foreach (object? wItem in wlist)
                    {
                        if (wItem is not Dictionary<string, object?> wd) continue;
                        if (!(wd.TryGetValue("x", out object? xv) && xv is double x)) continue;
                        if (!(wd.TryGetValue("z", out object? zv) && zv is double z)) continue;
                        route.Waypoints.Add(new Waypoint
                        {
                            Id   = wd.TryGetValue("id", out object? wid) ? (wid as string ?? string.Empty) : string.Empty,
                            Name = wd.TryGetValue("name", out object? wnm) ? (wnm as string ?? string.Empty) : string.Empty,
                            X = (float)x,
                            Z = (float)z,
                        });
                    }
                }
                if (route.Id.Length > 0) routes.Add(route);
            }
            return routes;
        }

        private static List<SteerPoint> ParseSteerPoints(object? value)
        {
            var points = new List<SteerPoint>();
            if (value is not List<object?> list) return points;
            foreach (object? item in list)
            {
                if (item is not Dictionary<string, object?> d) continue;
                if (!(d.TryGetValue("x", out object? xv) && xv is double x)) continue;
                if (!(d.TryGetValue("z", out object? zv) && zv is double z)) continue;
                string id = d.TryGetValue("id", out object? idv) ? (idv as string ?? string.Empty) : string.Empty;
                if (id.Length == 0) continue;
                points.Add(new SteerPoint
                {
                    Id = id,
                    Name = d.TryGetValue("name", out object? nm) ? (nm as string ?? string.Empty) : string.Empty,
                    X = (float)x,
                    Z = (float)z,
                });
            }
            return points;
        }

        // The served view adds live squad-sharing flags and pending entries to the persisted WPT
        // library. Those fields only mean something within the current squad session.
        private static void Save()
        {
            RefreshServedJsonOnly();
            if (!PersistToDisk) return;
            string fileJson = BuildFileJson();
            try { File.WriteAllText(FilePath, fileJson); }
            catch (Exception ex) { LogWarning?.Invoke($"[NOXMFD] failed to persist routes: {ex.Message}"); }
        }

        // For mutations that don't touch persisted route shape (pending-share bookkeeping): only the
        // served view changes, so no file write is needed.
        private static void RefreshServedJsonOnly() => RoutesJson = BuildRoutesJson(served: true);

        private static string BuildPendingJson()
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < _pendingShared.Count; i++)
            {
                if (i > 0) sb.Append(',');
                PendingSharedRoute p = _pendingShared[i];
                sb.Append("{\"id\":\"").Append(JsonLite.EscapeJson(p.Id))
                  .Append("\",\"name\":\"").Append(JsonLite.EscapeJson(p.Name))
                  .Append("\",\"fromName\":\"").Append(JsonLite.EscapeJson(p.FromName))
                  .Append("\",\"waypointCount\":").Append(p.Waypoints.Count)
                  .Append('}');
            }
            return sb.Append(']').ToString();
        }

        private static string BuildPendingSteerPointsJson()
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < _pendingSharedSteerPoints.Count; i++)
            {
                if (i > 0) sb.Append(',');
                PendingSharedSteerPoint p = _pendingSharedSteerPoints[i];
                sb.Append("{\"id\":\"").Append(JsonLite.EscapeJson(p.Id))
                  .Append("\",\"name\":\"").Append(JsonLite.EscapeJson(p.Name))
                  .Append("\",\"fromName\":\"").Append(JsonLite.EscapeJson(p.FromName))
                  .Append("\",\"x\":").Append(p.X.ToString("0.0", CultureInfo.InvariantCulture))
                  .Append(",\"z\":").Append(p.Z.ToString("0.0", CultureInfo.InvariantCulture))
                  .Append('}');
            }
            return sb.Append(']').ToString();
        }

        private static string BuildFileJson() => BuildRoutesJson(served: false);

        // `served` gates session-only squad flags and pending shares from the persisted library.
        private static string BuildRoutesJson(bool served)
        {
            var sb = new StringBuilder();
            sb.Append("{\"activeRouteId\":")
              .Append(_activeRouteId != null ? "\"" + JsonLite.EscapeJson(_activeRouteId) + "\"" : "null")
              .Append(",\"activeSteerPointId\":")
              .Append(_activeSteerPointId != null ? "\"" + JsonLite.EscapeJson(_activeSteerPointId) + "\"" : "null")
              .Append(",\"routes\":[");
            for (int i = 0; i < _routes.Count; i++)
            {
                if (i > 0) sb.Append(',');
                Route r = _routes[i];
                sb.Append("{\"id\":\"").Append(JsonLite.EscapeJson(r.Id))
                  .Append("\",\"name\":\"").Append(JsonLite.EscapeJson(r.Name))
                  .Append("\",\"nextIndex\":").Append(r.NextIndex);
                // sharedBy/sharedWithSquad: served-view only, never persisted — both are session-only
                // by design (Route's own field comments), so a game restart must not restore either.
                if (served)
                {
                    sb.Append(",\"sharedBy\":\"").Append(JsonLite.EscapeJson(r.SharedBy)).Append('"');
                    sb.Append(",\"sharedWithSquad\":").Append(r.SharedWithSquad ? "true" : "false");
                }
                sb.Append(",\"waypoints\":[");
                for (int j = 0; j < r.Waypoints.Count; j++)
                {
                    if (j > 0) sb.Append(',');
                    Waypoint w = r.Waypoints[j];
                    sb.Append("{\"id\":\"").Append(JsonLite.EscapeJson(w.Id))
                      .Append("\",\"name\":\"").Append(JsonLite.EscapeJson(w.Name))
                      .Append("\",\"x\":").Append(w.X.ToString("0.0", CultureInfo.InvariantCulture))
                      .Append(",\"z\":").Append(w.Z.ToString("0.0", CultureInfo.InvariantCulture))
                      .Append('}');
                }
                sb.Append("]}");
            }
            sb.Append("],\"steerPoints\":[");
            for (int i = 0; i < _steerPoints.Count; i++)
            {
                if (i > 0) sb.Append(',');
                SteerPoint p = _steerPoints[i];
                sb.Append("{\"id\":\"").Append(JsonLite.EscapeJson(p.Id))
                  .Append("\",\"name\":\"").Append(JsonLite.EscapeJson(p.Name))
                  .Append("\",\"x\":").Append(p.X.ToString("0.0", CultureInfo.InvariantCulture))
                  .Append(",\"z\":").Append(p.Z.ToString("0.0", CultureInfo.InvariantCulture))
                  .Append(",\"sharedBy\":\"").Append(JsonLite.EscapeJson(p.SharedBy)).Append('"');
                if (served) sb.Append(",\"sharedWithSquad\":").Append(p.SharedWithSquad ? "true" : "false");
                sb.Append('}');
            }
            sb.Append(']');
            if (served)
            {
                sb.Append(",\"pendingShared\":").Append(BuildPendingJson())
                  .Append(",\"pendingSharedSteerPoints\":").Append(BuildPendingSteerPointsJson());
            }
            return sb.Append('}').ToString();
        }

        // ── id and route-name generation ─────────────────────────────────────────────────────

        private static string FreshId(string prefix) => prefix + Guid.NewGuid().ToString("N");

        private static string FreshRouteName() => "RT-" + Guid.NewGuid().ToString("N").Substring(0, 5).ToUpperInvariant();

        private static string UniqueRouteName(string name, string? excludeId)
        {
            var taken = new HashSet<string>(StringComparer.Ordinal);
            foreach (Route r in _routes) if (r.Id != excludeId) taken.Add(r.Name);
            if (!taken.Contains(name)) return name;
            int n = 2;
            while (taken.Contains(name + " (" + n + ")")) n++;
            return name + " (" + n + ")";
        }

        private static Route? FindRoute(string? id) =>
            id == null ? null : _routes.Find(r => r.Id == id);

        private static Route? ActiveRoute => FindRoute(_activeRouteId);

        private static SteerPoint? FindSteerPoint(string? id) =>
            id == null ? null : _steerPoints.Find(p => p.Id == id);

        private static SteerPoint? ActiveSteerPoint => FindSteerPoint(_activeSteerPointId);

        // ── route mutations ───────────────────────────────────────────────────────────────────

        public static Route CreateRoute(string? name)
        {
            var route = new Route
            {
                Id = FreshId("r_"),
                Name = UniqueRouteName(string.IsNullOrEmpty(name) ? FreshRouteName() : name!, null),
                NextIndex = 0,
            };
            _routes.Add(route);
            _activeRouteId = route.Id;
            Save();
            return route;
        }

        public static bool RenameRoute(string id, string name)
        {
            Route? route = FindRoute(id);
            if (route == null || route.IsShared) return false;   // shared content is read-only
            route.Name = UniqueRouteName(name, id);
            Save();
            BroadcastIfShared(route);
            return true;
        }

        public static bool DeleteRoute(string id)
        {
            Route? route = FindRoute(id);
            if (route == null) return false;
            _routes.Remove(route);
            if (_activeRouteId == id) _activeRouteId = _routes.Count > 0 ? _routes[0].Id : null;
            Save();
            BroadcastDeleteIfShared(route);
            return true;
        }

        public static void SetActiveRoute(string? id)
        {
            _activeRouteId = string.IsNullOrEmpty(id) ? null : id;
            Save();
        }

        public static void ClearRoutes()
        {
            // Tombstone every route this pilot had shared before wiping it — same reasoning
            // DeleteRoute's own BroadcastDeleteIfShared already applies to one route at a time; a
            // bulk clear must not skip that just because it drops the whole list in one call, or a
            // member's accepted copy of any of them would sit stale forever with no way to learn it
            // was removed.
            foreach (Route r in _routes) BroadcastDeleteIfShared(r);
            _routes.Clear();
            _activeRouteId = null;
            Save();
        }

        public static bool ResetRoute(string id)
        {
            Route? route = FindRoute(id);
            if (route == null) return false;
            route.NextIndex = 0;
            Save();
            return true;
        }

        // Parses a pasted route export (serializeRoute's shape: {name, waypoints:[{name,x,z}]}) —
        // same defensive validation as the client's own parseRouteJSON, independently re-checked
        // here since this is the actual source of truth. Fresh ids throughout; always starts at
        // nextIndex 0 — imported progress isn't part of a route's portable definition.
        public static bool ImportRoute(string? text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (JsonLite.Parse(text) is not Dictionary<string, object?> data) return false;
            if (!(data.TryGetValue("waypoints", out object? wps) && wps is List<object?> wlist)) return false;

            var waypoints = new List<Waypoint>();
            foreach (object? item in wlist)
            {
                if (item is not Dictionary<string, object?> w) return false;
                if (!(w.TryGetValue("x", out object? xv) && xv is double x)) return false;
                if (!(w.TryGetValue("z", out object? zv) && zv is double z)) return false;
                string wname = w.TryGetValue("name", out object? nm) && nm is string ns ? ns : string.Empty;
                waypoints.Add(new Waypoint { Id = FreshId("w_"), Name = wname, X = (float)x, Z = (float)z });
            }

            string routeName = data.TryGetValue("name", out object? rn) && rn is string rns && rns.Trim().Length > 0
                ? rns.Trim() : FreshRouteName();
            var route = new Route { Id = FreshId("r_"), Name = UniqueRouteName(routeName, null), NextIndex = 0, Waypoints = waypoints };
            _routes.Add(route);
            _activeRouteId = route.Id;
            Save();
            return true;
        }

        // ── steer points ─────────────────────────────────────────────────────────────────────

        public static SteerPoint AddSteerPoint(float x, float z, string? name)
        {
            var point = new SteerPoint { Id = FreshId("s_"), Name = name ?? string.Empty, X = x, Z = z };
            _steerPoints.Add(point);
            _activeSteerPointId = point.Id;
            Save();
            return point;
        }

        public static bool RenameSteerPoint(string id, string name)
        {
            SteerPoint? point = FindSteerPoint(id);
            if (point == null || point.IsShared) return false;
            point.Name = name;
            Save();
            BroadcastIfShared(point);
            return true;
        }

        public static bool DeleteSteerPoint(string id)
        {
            int index = _steerPoints.FindIndex(p => p.Id == id);
            if (index < 0) return false;
            SteerPoint point = _steerPoints[index];
            _steerPoints.RemoveAt(index);
            if (_activeSteerPointId == id)
            {
                _activeSteerPointId = _steerPoints.Count == 0
                    ? null
                    : _steerPoints[Math.Min(index, _steerPoints.Count - 1)].Id;
            }
            Save();
            BroadcastDeleteIfShared(point);
            return true;
        }

        public static void SetActiveSteerPoint(string? id)
        {
            _activeSteerPointId = string.IsNullOrEmpty(id) || FindSteerPoint(id) == null ? null : id;
            Save();
        }

        public static void CycleSteerPoint(int dir)
        {
            if (ActiveRoute != null || _steerPoints.Count == 0) return;
            int index = _activeSteerPointId == null ? -1 : _steerPoints.FindIndex(p => p.Id == _activeSteerPointId);
            int next = index < 0 && dir < 0
                ? _steerPoints.Count - 1
                : ((index + dir) % _steerPoints.Count + _steerPoints.Count) % _steerPoints.Count;
            _activeSteerPointId = _steerPoints[next].Id;
            Save();
        }

        public static void StepNavigation(int dir)
        {
            if (ActiveRoute != null) StepWaypoint(dir);
            else CycleSteerPoint(dir);
        }

        public static void AddNavigationPoint(float x, float z, string? name)
        {
            if (ActiveRoute != null) AddWaypoint(x, z, name);
            else AddSteerPoint(x, z, name);
        }

        public static bool ImportSteerPoints(string? text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (JsonLite.Parse(text) is not Dictionary<string, object?> data) return false;
            if (!(data.TryGetValue("steerPoints", out object? raw) && raw is List<object?> list)) return false;

            var imported = new List<SteerPoint>();
            foreach (object? item in list)
            {
                if (item is not Dictionary<string, object?> p) return false;
                if (!(p.TryGetValue("x", out object? xv) && xv is double x)) return false;
                if (!(p.TryGetValue("z", out object? zv) && zv is double z)) return false;
                string name = p.TryGetValue("name", out object? nm) && nm is string ns ? ns : string.Empty;
                imported.Add(new SteerPoint { Id = FreshId("s_"), Name = name, X = (float)x, Z = (float)z });
            }
            if (imported.Count == 0) return false;

            _steerPoints.AddRange(imported);
            _activeSteerPointId = imported[0].Id;
            Save();
            return true;
        }

        // ── squad-shared navigation data (docs/squadron-transport.md) ─────────────────────────
        // Distinct from ImportRoute above: a pasted route is deliberately stripped of identity (any
        // paste always becomes a fresh, independent copy — see wpt-route.js's serializeRoute), but a
        // squad share needs to carry the LEADER's own route id through, unchanged, every time it's
        // (re)shared — that stable id is what makes "ignore a duplicate/repeat share" and "don't let
        // a member edit a route that isn't theirs" possible at all. BuildSharePayloadJson below
        // builds this shape ({id, name, waypoints}) specifically for this path.

        // Called from Squad.HandleData the instant a squadmate's share arrives over Steam
        // (docs/squadron-transport.md) — not routed through a browser command. A share whose id
        // we've never seen becomes a new pending entry. A share whose id matches an already-PENDING
        // one just refreshes that pending entry's content (the leader edited before this pilot got
        // around to accept/reject). A share whose id matches an already-ACCEPTED route is a re-share
        // after a leader edit — updates that route's content in place via UpdateSharedRoute, so a
        // leader's edit always reaches an already-accepted member instead of being dropped as a
        // duplicate of the original share.
        public static bool ReceiveSharedRoute(string? text, string fromName)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (JsonLite.Parse(text) is not Dictionary<string, object?> data) return false;
            string id = data.TryGetValue("id", out object? idv) && idv is string ids ? ids : string.Empty;
            if (id.Length == 0) return false;   // not a squad-share payload (or malformed) — reject
            if (!(data.TryGetValue("waypoints", out object? wps) && wps is List<object?> wlist)) return false;

            var waypoints = new List<Waypoint>();
            foreach (object? item in wlist)
            {
                if (item is not Dictionary<string, object?> w) return false;
                if (!(w.TryGetValue("x", out object? xv) && xv is double x)) return false;
                if (!(w.TryGetValue("z", out object? zv) && zv is double z)) return false;
                string wname = w.TryGetValue("name", out object? nm) && nm is string ns ? ns : string.Empty;
                waypoints.Add(new Waypoint { Id = FreshId("w_"), Name = wname, X = (float)x, Z = (float)z });
            }
            string routeName = data.TryGetValue("name", out object? rn) && rn is string rns && rns.Trim().Length > 0
                ? rns.Trim() : FreshRouteName();

            Route? accepted = _routes.Find(r => r.Id == id);
            if (accepted != null)
            {
                // A route with this id that is no longer IsShared isn't a re-share of an accepted
                // copy — it's this pilot's OWN content now (OnSquadEnded unlocked it after the squad
                // that first shared it ended), possibly edited since. Matching on id alone would let
                // rejoining that same leader silently overwrite those local edits the moment they
                // re-share their own still-identical-id route. Drop it instead — the pilot already
                // owns this content and didn't ask to replace it.
                if (!accepted.IsShared)
                {
                    LogWarning?.Invoke($"[NOXMFD] squad share for route {id} ignored: already exists locally, unlocked.");
                    return false;
                }
                UpdateSharedRoute(accepted, routeName, waypoints);
                Save();
                return true;
            }

            PendingSharedRoute? pending = _pendingShared.Find(p => p.Id == id);
            if (pending != null)
            {
                pending.Name = routeName;
                pending.Waypoints = waypoints;
                RefreshServedJsonOnly();
                return true;
            }

            _pendingShared.Add(new PendingSharedRoute { Id = id, Name = routeName, FromName = fromName, Waypoints = waypoints });
            RefreshServedJsonOnly();
            return true;
        }

        // Applies a re-share's fresh content to an already-accepted shared route, preserving this
        // pilot's own progress through it as best as possible instead of resetting to zero:
        //   - If the route was already fully completed (no current next waypoint), it stays completed.
        //   - If the current next waypoint still exists somewhere in the new waypoint list (matched by
        //     name/x/z, since re-shared waypoints don't carry stable ids — see shareRoutePayload),
        //     NextIndex follows it to its new position.
        //   - If the current next waypoint was deleted by the leader, NextIndex advances to just past
        //     whichever already-completed waypoints still survive in the new list — i.e. it moves on
        //     to whatever's next now, without resetting the completed ones.
        private static void UpdateSharedRoute(Route route, string name, List<Waypoint> newWaypoints)
        {
            var used = new bool[newWaypoints.Count];
            int newNextIndex;
            if (route.NextIndex >= route.Waypoints.Count)
            {
                newNextIndex = newWaypoints.Count;
            }
            else
            {
                Waypoint target = route.Waypoints[route.NextIndex];
                int foundAt = FindWaypointMatch(newWaypoints, used, target);
                if (foundAt >= 0)
                {
                    newNextIndex = foundAt;
                }
                else
                {
                    int matched = 0;
                    for (int i = 0; i < route.NextIndex; i++)
                    {
                        if (FindWaypointMatch(newWaypoints, used, route.Waypoints[i]) >= 0) matched++;
                    }
                    newNextIndex = matched;
                }
            }
            route.Name = UniqueRouteName(name, route.Id);
            route.Waypoints = newWaypoints;
            route.NextIndex = Math.Max(0, Math.Min(newNextIndex, newWaypoints.Count));
        }

        // First not-yet-`used` waypoint in `list` matching `target` by content (name/x/z — the only
        // identity a re-shared waypoint carries). Marks it used so each new waypoint can satisfy at
        // most one old-waypoint match, even when several waypoints share identical name/x/z.
        private static int FindWaypointMatch(List<Waypoint> list, bool[] used, Waypoint target)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (used[i]) continue;
                if (list[i].Name == target.Name && list[i].X == target.X && list[i].Z == target.Z)
                {
                    used[i] = true;
                    return i;
                }
            }
            return -1;
        }

        // Moves a pending share into the real route list, tagged read-only under the leader's name.
        // Keeps the SAME id the leader's own copy has — accepting the identical share again later
        // (e.g. after this pilot left and rejoined, or the leader re-shared) is then just another
        // ReceiveSharedRoute no-op against an id already in _routes, not a second entry. Does not
        // activate it — accepting only adds it to the library, same as any other new route; picking
        // it as the active one is a separate, ordinary click, same as every other route.
        public static bool AcceptShared(string id)
        {
            int idx = _pendingShared.FindIndex(p => p.Id == id);
            if (idx < 0) return false;
            PendingSharedRoute p = _pendingShared[idx];
            _pendingShared.RemoveAt(idx);
            _routes.Add(new Route { Id = p.Id, Name = UniqueRouteName(p.Name, null), NextIndex = 0, Waypoints = p.Waypoints, SharedBy = p.FromName });
            Save();
            return true;
        }

        public static bool RejectShared(string id)
        {
            int removed = _pendingShared.RemoveAll(p => p.Id == id);
            if (removed == 0) return false;
            RefreshServedJsonOnly();
            return true;
        }

        // Called from Squad.HandleData the instant a squadmate's delete-tombstone arrives (the
        // leader deleted a route this pilot had pending or already accepted — BroadcastDeleteIfShared
        // above), not routed through a browser command. Removes either without an accept/reject step:
        // unlike ReceiveSharedRoute this never creates anything, so there's no ambiguity to resolve,
        // just something to take away.
        public static bool RemoveSharedRoute(string id)
        {
            bool removedPending = _pendingShared.RemoveAll(p => p.Id == id) > 0;
            Route? accepted = _routes.Find(r => r.Id == id && r.IsShared);
            if (accepted == null)
            {
                if (removedPending) RefreshServedJsonOnly();
                return removedPending;
            }

            _routes.Remove(accepted);
            if (_activeRouteId == id) _activeRouteId = _routes.Count > 0 ? _routes[0].Id : null;
            Save();
            return true;
        }

        // Once squad membership or leadership ends, pending navigation shares have no sender to
        // accept and accepted entries no longer need protection from leader updates. Drop the
        // former and unlock the latter in one lifecycle hook shared by routes and steer points.
        public static void OnSquadEnded()
        {
            bool changed = _pendingShared.Count > 0 || _pendingSharedSteerPoints.Count > 0;
            _pendingShared.Clear();
            _pendingSharedSteerPoints.Clear();
            foreach (Route r in _routes)
            {
                if (r.SharedBy.Length > 0) { r.SharedBy = string.Empty; changed = true; }
                // Same "this session's relationship is over" reasoning as SharedBy above, for the
                // OUTGOING half: without clearing this, editing the route after joining a LATER,
                // completely different squad would auto-broadcast it there (BroadcastIfShared) even
                // though Share was never pressed for that squad — SharedWithSquad only ever means
                // "the squad I first shared this with," not "always keep sharing this."
                if (r.SharedWithSquad) { r.SharedWithSquad = false; changed = true; }
            }
            foreach (SteerPoint p in _steerPoints)
            {
                if (p.SharedBy.Length > 0) { p.SharedBy = string.Empty; changed = true; }
                if (p.SharedWithSquad) { p.SharedWithSquad = false; changed = true; }
            }
            if (changed) Save();
            else RefreshServedJsonOnly();
        }

        // WPT's share button (wpt.share). Sends the route now AND flips SharedWithSquad on so every
        // later edit re-broadcasts on its own (BroadcastIfShared) — the pilot never has to remember
        // to click share again after touching a route the squad already has.
        public static bool ShareRoute(string id)
        {
            Route? route = FindRoute(id);
            if (route == null || route.IsShared) return false;   // can't share someone else's content
            route.SharedWithSquad = true;
            RefreshServedJsonOnly();   // WPT's SQD label reflects the flag immediately, not just on the next Save()
            return SendSquadData?.Invoke("wpt.route", BuildSharePayloadJson(route)) ?? false;
        }

        // Fires after any mutation to a route that was previously shared — silently does nothing if
        // this route was never shared, or the squad has since disbanded/lost its leader (SendData
        // itself checks role/membership, same guard the manual share button already relies on).
        private static void BroadcastIfShared(Route route)
        {
            if (route.SharedWithSquad) SendSquadData?.Invoke("wpt.route", BuildSharePayloadJson(route));
        }

        // Fires when a route that was shared with the squad gets deleted — without this, a member's
        // accepted copy would sit stale forever with no way to learn the leader removed it (unlike an
        // edit, there's no new content to re-share; the payload is just the id to drop). Same
        // SharedWithSquad guard as BroadcastIfShared — a route never shared has nothing to tell.
        private static void BroadcastDeleteIfShared(Route route)
        {
            if (route.SharedWithSquad) SendSquadData?.Invoke("wpt.route-deleted", route.Id);
        }

        // {id, name, waypoints:[{name,x,z}]} — the same shape ReceiveSharedRoute expects, now built
        // entirely server-side so an auto-reshare needs no browser involved at all.
        private static string BuildSharePayloadJson(Route route)
        {
            var sb = new StringBuilder();
            sb.Append("{\"id\":\"").Append(JsonLite.EscapeJson(route.Id))
              .Append("\",\"name\":\"").Append(JsonLite.EscapeJson(route.Name))
              .Append("\",\"waypoints\":[");
            for (int i = 0; i < route.Waypoints.Count; i++)
            {
                if (i > 0) sb.Append(',');
                Waypoint w = route.Waypoints[i];
                sb.Append("{\"name\":\"").Append(JsonLite.EscapeJson(w.Name))
                  .Append("\",\"x\":").Append(w.X.ToString("0.0", CultureInfo.InvariantCulture))
                  .Append(",\"z\":").Append(w.Z.ToString("0.0", CultureInfo.InvariantCulture))
                  .Append('}');
            }
            return sb.Append("]}").ToString();
        }

        // Steer points use the same one-item accept/reject and read-only ownership model as routes.
        // Their stable id makes an edit a replacement of the accepted copy instead of a duplicate.
        public static bool ReceiveSharedSteerPoint(string? text, string fromName)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (JsonLite.Parse(text) is not Dictionary<string, object?> data) return false;
            string id = data.TryGetValue("id", out object? idv) && idv is string ids ? ids : string.Empty;
            if (id.Length == 0) return false;
            if (!(data.TryGetValue("x", out object? xv) && xv is double x)) return false;
            if (!(data.TryGetValue("z", out object? zv) && zv is double z)) return false;
            string name = data.TryGetValue("name", out object? nm) && nm is string ns ? ns : string.Empty;

            SteerPoint? accepted = FindSteerPoint(id);
            if (accepted != null)
            {
                if (!accepted.IsShared) return false;
                accepted.Name = name;
                accepted.X = (float)x;
                accepted.Z = (float)z;
                Save();
                return true;
            }

            PendingSharedSteerPoint? pending = _pendingSharedSteerPoints.Find(p => p.Id == id);
            if (pending != null)
            {
                pending.Name = name;
                pending.X = (float)x;
                pending.Z = (float)z;
                RefreshServedJsonOnly();
                return true;
            }

            _pendingSharedSteerPoints.Add(new PendingSharedSteerPoint
            {
                Id = id,
                Name = name,
                FromName = fromName,
                X = (float)x,
                Z = (float)z,
            });
            RefreshServedJsonOnly();
            return true;
        }

        public static bool AcceptSharedSteerPoint(string id)
        {
            int index = _pendingSharedSteerPoints.FindIndex(p => p.Id == id);
            if (index < 0 || FindSteerPoint(id) != null) return false;
            PendingSharedSteerPoint pending = _pendingSharedSteerPoints[index];
            _pendingSharedSteerPoints.RemoveAt(index);
            _steerPoints.Add(new SteerPoint
            {
                Id = pending.Id,
                Name = pending.Name,
                X = pending.X,
                Z = pending.Z,
                SharedBy = pending.FromName,
            });
            Save();
            return true;
        }

        public static bool RejectSharedSteerPoint(string id)
        {
            int removed = _pendingSharedSteerPoints.RemoveAll(p => p.Id == id);
            if (removed == 0) return false;
            RefreshServedJsonOnly();
            return true;
        }

        public static bool RemoveSharedSteerPoint(string id)
        {
            bool removedPending = _pendingSharedSteerPoints.RemoveAll(p => p.Id == id) > 0;
            SteerPoint? accepted = _steerPoints.Find(p => p.Id == id && p.IsShared);
            if (accepted == null)
            {
                if (removedPending) RefreshServedJsonOnly();
                return removedPending;
            }
            return DeleteSteerPoint(id);
        }

        public static bool ShareSteerPoint(string id)
        {
            SteerPoint? point = FindSteerPoint(id);
            if (point == null || point.IsShared) return false;
            point.SharedWithSquad = true;
            RefreshServedJsonOnly();
            return SendSquadData?.Invoke("wpt.steerpoint", BuildSharePayloadJson(point)) ?? false;
        }

        private static void BroadcastIfShared(SteerPoint point)
        {
            if (point.SharedWithSquad) SendSquadData?.Invoke("wpt.steerpoint", BuildSharePayloadJson(point));
        }

        private static void BroadcastDeleteIfShared(SteerPoint point)
        {
            if (point.SharedWithSquad) SendSquadData?.Invoke("wpt.steerpoint-deleted", point.Id);
        }

        private static string BuildSharePayloadJson(SteerPoint point) =>
            "{\"id\":\"" + JsonLite.EscapeJson(point.Id) +
            "\",\"name\":\"" + JsonLite.EscapeJson(point.Name) +
            "\",\"x\":" + point.X.ToString("0.0", CultureInfo.InvariantCulture) +
            ",\"z\":" + point.Z.ToString("0.0", CultureInfo.InvariantCulture) + "}";

        public static bool RenameWaypoint(int index, string name)
        {
            Route? route = ActiveRoute;
            if (route == null || route.IsShared || index < 0 || index >= route.Waypoints.Count) return false;
            route.Waypoints[index].Name = name;
            Save();
            BroadcastIfShared(route);
            return true;
        }

        public static bool ReorderWaypoint(int from, int to)
        {
            Route? route = ActiveRoute;
            if (route == null || route.IsShared || from < 0 || from >= route.Waypoints.Count || to < 0 || to >= route.Waypoints.Count) return false;
            Waypoint moved = route.Waypoints[from];
            route.Waypoints.RemoveAt(from);
            route.Waypoints.Insert(to, moved);
            Save();
            BroadcastIfShared(route);
            return true;
        }

        // Rewinds/jumps the active route's progress to `index` — clamped to a valid range so an
        // out-of-range index can't produce a negative or overshooting count (mirrors
        // wpt-route.js's resetProgress; used directly by the per-waypoint reset button and, via
        // StepWaypoint below, by W+/W-).
        public static bool ResetWaypoint(int index)
        {
            Route? route = ActiveRoute;
            if (route == null) return false;
            route.NextIndex = Math.Max(0, Math.Min(index, route.Waypoints.Count));
            Save();
            return true;
        }

        // nextIndex is a COUNT of completed waypoints, not a waypoint's identity. A delete before
        // it shifts it down by one (one fewer completed ahead of it); a delete AT it leaves the
        // number as-is (now naming whatever slid up into that slot); a delete after it is untouched.
        public static bool RemoveWaypoint(int index)
        {
            Route? route = ActiveRoute;
            if (route == null || route.IsShared || index < 0 || index >= route.Waypoints.Count) return false;
            route.Waypoints.RemoveAt(index);
            if (index < route.NextIndex) route.NextIndex--;
            route.NextIndex = Math.Max(0, Math.Min(route.NextIndex, route.Waypoints.Count));
            Save();
            BroadcastIfShared(route);
            return true;
        }

        // The route one step (dir = +1/-1) from the active one, wrapping — including a "no route
        // active" stop between the last route and the first, so this can step OUT of a route as
        // well as into one. No routes at all: stays on "none".
        public static void CycleActiveRoute(int dir)
        {
            if (_routes.Count == 0) { _activeRouteId = null; Save(); return; }
            int idx = _activeRouteId == null ? -1 : _routes.FindIndex(r => r.Id == _activeRouteId);
            int pos = idx + 1;
            int total = _routes.Count + 1;
            int nextPos = ((pos + dir) % total + total) % total;
            _activeRouteId = nextPos == 0 ? null : _routes[nextPos - 1].Id;
            Save();
        }

        public static void StepWaypoint(int dir)
        {
            Route? route = ActiveRoute;
            if (route == null) return;
            route.NextIndex = Math.Max(0, Math.Min(route.NextIndex + dir, route.Waypoints.Count));
            Save();
        }

        // Creates a default route first if none is active yet, mirroring addWaypointToActive — a
        // long-press on MAP works before any visit to WPT. A long-press while a SHARED route is
        // active is a no-op rather than silently editing someone else's content — the pilot needs
        // to activate/create their own route first, same as every other edit on a shared one.
        public static void AddWaypoint(float x, float z, string? name)
        {
            if (ActiveRoute != null && ActiveRoute.IsShared) return;
            if (ActiveRoute == null)
            {
                var fresh = new Route { Id = FreshId("r_"), Name = UniqueRouteName(FreshRouteName(), null), NextIndex = 0 };
                _routes.Add(fresh);
                _activeRouteId = fresh.Id;
            }
            Route route = ActiveRoute!;
            route.Waypoints.Add(new Waypoint { Id = FreshId("w_"), Name = name ?? string.Empty, X = x, Z = z });
            Save();
            BroadcastIfShared(route);
        }

        // ── proximity advance, ticked at 1 Hz from TelemetryReader's slow block ─────────────────

        public static void AdvanceIfNear(float worldX, float worldZ)
        {
            Route? route = ActiveRoute;
            if (route == null || route.NextIndex >= route.Waypoints.Count) return;
            Waypoint next = route.Waypoints[route.NextIndex];
            float dx = next.X - worldX, dz = next.Z - worldZ;
            if (Math.Sqrt(dx * dx + dz * dz) > AdvanceThresholdM) return;
            route.NextIndex++;
            Save();
        }

        // ── in-process read for HudWaypointCue (no network round trip) ─────────────────────────

        public static bool TryGetActiveWaypoint(out float x, out float z, out string name, out int index)
        {
            x = z = 0f; name = string.Empty; index = 0;
            Route? route = ActiveRoute;
            if (route == null || route.NextIndex >= route.Waypoints.Count) return false;
            Waypoint wp = route.Waypoints[route.NextIndex];
            x = wp.X; z = wp.Z; name = wp.Name; index = route.NextIndex;
            return true;
        }

        // Route activation is the priority switch: an active route owns navigation even when it is
        // complete. Only the explicit no-route state falls back to the selected static steer point.
        public static bool TryGetActiveNavigationPoint(
            out float x, out float z, out string name, out int index, out bool isSteerPoint)
        {
            if (ActiveRoute != null)
            {
                isSteerPoint = false;
                return TryGetActiveWaypoint(out x, out z, out name, out index);
            }

            x = z = 0f;
            name = string.Empty;
            index = 0;
            isSteerPoint = true;
            SteerPoint? point = ActiveSteerPoint;
            if (point == null) return false;
            x = point.X;
            z = point.Z;
            name = point.Name;
            index = _steerPoints.IndexOf(point);
            return true;
        }

        // Test-only: _routes/_activeRouteId are static (plugin-lifetime by design, see the class
        // comment), so NOXMFD.Tests' RouteStoreTests resets them between test methods to avoid one
        // test's routes leaking into the next. Never called from plugin code.
        internal static void ResetForTests()
        {
            _routes = new List<Route>();
            _steerPoints = new List<SteerPoint>();
            _activeRouteId = null;
            _activeSteerPointId = null;
            _pendingShared.Clear();
            _pendingSharedSteerPoints.Clear();
            SendSquadData = null;
            RoutesJson = "{\"activeRouteId\":null,\"activeSteerPointId\":null,\"routes\":[],\"steerPoints\":[]}";
        }
    }
}

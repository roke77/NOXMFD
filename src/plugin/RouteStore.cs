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

    internal sealed class Route
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public int NextIndex;
        public List<Waypoint> Waypoints = new List<Waypoint>();
    }

    // Waypoint/route storage (docs/hud-waypoint-indicator.md, Option 2) — the plugin, not any
    // browser, is the single source of truth for the whole route library. One process owns the
    // data and ticks it every frame, so every browser sees the same route list regardless of
    // which one created it, and proximity-advance runs independent of which page is visible.
    //
    // Static, plugin-lifetime (NOT mission-scoped) — routes must survive a mission restart AND a
    // full game restart, and must be browsable/editable at the main menu.
    internal static class RouteStore
    {
        private const float AdvanceThresholdM = 1000f;   // must track wpt.js's WPT_ADVANCE_RADIUS_M;
                                                            // the client has no proximity check of its
                                                            // own, so this is the only threshold that
                                                            // matters.

        private static List<Route> _routes = new List<Route>();
        private static string? _activeRouteId;

        // Server-thread-readable cache, mirrors TelemetryServer.HudOptionsJson's threading: every
        // mutator below runs on the Unity main thread only (CommandDispatcher.Drain, called from
        // MissionLifecycle.Update — unconditional, so this works at the main menu too), and rebuilds
        // this string synchronously as its last step. The HTTP server thread only ever reads the
        // reference, never the underlying List<Route>, so no lock is needed.
        internal static volatile string RoutesJson = "{\"activeRouteId\":null,\"routes\":[]}";

        // Storage/log seam (docs/csharp-unit-testing.md) — keeps this file free of any BepInEx/
        // Plugin reference so a standalone test project can compile and exercise the mutators
        // directly. Plugin.Awake sets both before calling Load(); a test project leaves ConfigDir
        // null (FilePath then resolves under the test run's own working directory) and can set
        // PersistToDisk false to exercise the mutators without touching disk at all.
        internal static string? ConfigDir;
        internal static Action<string>? LogWarning;
        internal static bool PersistToDisk = true;

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
                    _routes = ParseRoutes(root.TryGetValue("routes", out object? r) ? r : null);
                }
            }
            catch (Exception ex)
            {
                LogWarning?.Invoke($"[NOXMFD] routes file unreadable, starting empty: {ex.Message}");
                _routes = new List<Route>();
                _activeRouteId = null;
            }
            RoutesJson = BuildJson();
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

        private static void Save()
        {
            RoutesJson = BuildJson();
            if (!PersistToDisk) return;
            try { File.WriteAllText(FilePath, RoutesJson); }
            catch (Exception ex) { LogWarning?.Invoke($"[NOXMFD] failed to persist routes: {ex.Message}"); }
        }

        private static string BuildJson()
        {
            var sb = new StringBuilder();
            sb.Append("{\"activeRouteId\":")
              .Append(_activeRouteId != null ? "\"" + JsonLite.EscapeJson(_activeRouteId) + "\"" : "null")
              .Append(",\"routes\":[");
            for (int i = 0; i < _routes.Count; i++)
            {
                if (i > 0) sb.Append(',');
                Route r = _routes[i];
                sb.Append("{\"id\":\"").Append(JsonLite.EscapeJson(r.Id))
                  .Append("\",\"name\":\"").Append(JsonLite.EscapeJson(r.Name))
                  .Append("\",\"nextIndex\":").Append(r.NextIndex)
                  .Append(",\"waypoints\":[");
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
            sb.Append("]}");
            return sb.ToString();
        }

        // ── id / name generation, mirrors waypoints-store.js's freshId/freshRouteName/uniqueRouteName ──

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

        // ── mutations — 1:1 with wpt.js's action list; each ends by calling Save() ─────────────

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
            if (route == null) return false;
            route.Name = UniqueRouteName(name, id);
            Save();
            return true;
        }

        public static bool DeleteRoute(string id)
        {
            int removed = _routes.RemoveAll(r => r.Id == id);
            if (removed == 0) return false;
            if (_activeRouteId == id) _activeRouteId = _routes.Count > 0 ? _routes[0].Id : null;
            Save();
            return true;
        }

        public static void SetActiveRoute(string? id)
        {
            _activeRouteId = string.IsNullOrEmpty(id) ? null : id;
            Save();
        }

        public static void ClearRoutes()
        {
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

        public static bool RenameWaypoint(int index, string name)
        {
            Route? route = ActiveRoute;
            if (route == null || index < 0 || index >= route.Waypoints.Count) return false;
            route.Waypoints[index].Name = name;
            Save();
            return true;
        }

        public static bool ReorderWaypoint(int from, int to)
        {
            Route? route = ActiveRoute;
            if (route == null || from < 0 || from >= route.Waypoints.Count || to < 0 || to >= route.Waypoints.Count) return false;
            Waypoint moved = route.Waypoints[from];
            route.Waypoints.RemoveAt(from);
            route.Waypoints.Insert(to, moved);
            Save();
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
            if (route == null || index < 0 || index >= route.Waypoints.Count) return false;
            route.Waypoints.RemoveAt(index);
            if (index < route.NextIndex) route.NextIndex--;
            route.NextIndex = Math.Max(0, Math.Min(route.NextIndex, route.Waypoints.Count));
            Save();
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
        // long-press on MAP works before any visit to WPT.
        public static void AddWaypoint(float x, float z, string? name)
        {
            if (ActiveRoute == null)
            {
                var route = new Route { Id = FreshId("r_"), Name = UniqueRouteName(FreshRouteName(), null), NextIndex = 0 };
                _routes.Add(route);
                _activeRouteId = route.Id;
            }
            ActiveRoute!.Waypoints.Add(new Waypoint { Id = FreshId("w_"), Name = name ?? string.Empty, X = x, Z = z });
            Save();
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

        // Test-only: _routes/_activeRouteId are static (plugin-lifetime by design, see the class
        // comment), so NOXMFD.Tests' RouteStoreTests resets them between test methods to avoid one
        // test's routes leaking into the next. Never called from plugin code.
        internal static void ResetForTests()
        {
            _routes = new List<Route>();
            _activeRouteId = null;
            RoutesJson = "{\"activeRouteId\":null,\"routes\":[]}";
        }
    }
}

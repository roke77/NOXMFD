using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace NOXMFD
{
    internal static class CapturedAssetEndpoint
    {
        // Captured in-game map image, set from the Unity main thread.
        private static byte[]? _mapImage;
        private static readonly object _mapLock = new object();

        // Per-aircraft-type map icons, keyed by unitName.
        private static readonly Dictionary<string, byte[]> _icons = new Dictionary<string, byte[]>();
        private static readonly object _iconLock = new object();

        // A 1×1 fully-transparent PNG registered for types that have no map icon (buildings, etc.).
        // Serving this with HTTP 200 — instead of 404 — stops the client re-requesting icon-less
        // types and keeps the browser console clean; the client spots the 1×1 size and falls back
        // to its generic square marker.
        internal static readonly byte[] NoIconPng = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M8AAAMBAQDJ/pLvAAAAAElFTkSuQmCC");

        // Per-weapon-type icons, keyed by weapon display name.
        private static readonly Dictionary<string, byte[]> _weaponIcons = new Dictionary<string, byte[]>();
        private static readonly object _weaponLock = new object();

        // Per-countermeasure icons, keyed by short name ("flares", "jammer").
        private static readonly Dictionary<string, byte[]> _cmIcons = new Dictionary<string, byte[]>();
        private static readonly object _cmLock = new object();

        // TGT filter vehicle-type icons, keyed by vehicle typeName ("TRUCK" … "RDR") — the
        // same names the "tgt" telemetry block's vehicle row carries. Served at /tgt-icon?type=.
        private static readonly Dictionary<string, byte[]> _tgtIcons = new Dictionary<string, byte[]>();
        private static readonly object _tgtLock = new object();

        // BDF ship-type icons, keyed by ship typeName ("CV" … "LC") — the same names the
        // "bdf" telemetry block's ship row carries (docs/bdf-page.md). Served at /bdf-icon?type=.
        private static readonly Dictionary<string, byte[]> _bdfIcons = new Dictionary<string, byte[]>();
        private static readonly object _bdfLock = new object();

        // HUD-page building-type icons, keyed by building typeName ("CIV" … "AMMO"). A separate
        // map from _tgtIcons on purpose: a name like "RDR" is BOTH a vehicle and a building type, so
        // sharing one keyspace would collide. Served at /building-icon?type=.
        private static readonly Dictionary<string, byte[]> _buildingIcons = new Dictionary<string, byte[]>();
        private static readonly object _buildingLock = new object();

        // HUD OPTIONS category-row icons (PNG) — AIRCRAFT/MISSILES/VEHICLES/BUILDINGS/SHIPS, keyed by
        // the same fixed label the HUD page's CATEGORY_LABELS carries (the game exposes no per-category
        // name to key by instead). FRIENDLY/ENEMY have no entry — the game draws no glyph on those rows
        // either. Served at /hud-cat-icon?cat=.
        private static readonly Dictionary<string, byte[]> _hudCategoryIcons = new Dictionary<string, byte[]>();
        private static readonly object _hudCategoryLock = new object();

        // Airframe silhouette assets. Images keyed by "unitName|partName" — partName is the
        // GameObject name from Aircraft.partLookup (e.g. "wing1_L") or "__bg" for the background
        // silhouette. Layouts keyed by unitName, value is a JSON descriptor of part placements.
        private static readonly Dictionary<string, byte[]> _airframeImages = new Dictionary<string, byte[]>();
        private static readonly Dictionary<string, string> _airframeLayouts = new Dictionary<string, string>();
        private static readonly object _airframeLock = new object();

        internal static void SetMapImage(byte[] image)
        {
            lock (_mapLock) _mapImage = image;
            Plugin.Log?.LogInfo($"[NOXMFD] In-game map image ready ({image.Length} bytes) — serving at /map.");
        }

        internal static void SetIcon(string unitName, byte[] png)
        {
            if (string.IsNullOrEmpty(unitName)) return;
            lock (_iconLock) _icons[unitName] = png;
        }

        internal static void SetWeaponIcon(string name, byte[] png)
        {
            if (string.IsNullOrEmpty(name)) return;
            lock (_weaponLock) _weaponIcons[name] = png;
        }

        internal static void SetTgtIcon(string name, byte[] png)
        {
            if (string.IsNullOrEmpty(name)) return;
            lock (_tgtLock) _tgtIcons[name] = png;
        }

        internal static void SetBdfIcon(string name, byte[] png)
        {
            if (string.IsNullOrEmpty(name)) return;
            lock (_bdfLock) _bdfIcons[name] = png;
        }

        internal static void SetBuildingIcon(string name, byte[] png)
        {
            if (string.IsNullOrEmpty(name)) return;
            lock (_buildingLock) _buildingIcons[name] = png;
        }

        internal static void SetHudCategoryIcon(string name, byte[] png)
        {
            if (string.IsNullOrEmpty(name)) return;
            lock (_hudCategoryLock) _hudCategoryIcons[name] = png;
        }

        internal static void SetCmIcon(string key, byte[] png)
        {
            if (string.IsNullOrEmpty(key)) return;
            lock (_cmLock) _cmIcons[key] = png;
        }

        internal static void SetAirframeImage(string unitName, string partName, byte[] png)
        {
            if (string.IsNullOrEmpty(unitName) || string.IsNullOrEmpty(partName) || png == null) return;
            lock (_airframeLock) _airframeImages[unitName + "|" + partName] = png;
        }

        internal static void SetAirframeLayout(string unitName, string json)
        {
            if (string.IsNullOrEmpty(unitName) || json == null) return;
            lock (_airframeLock) _airframeLayouts[unitName] = json;
        }

        internal static void ClearMissionState()
        {
            lock (_mapLock) _mapImage = null;
        }

        internal static void ServeMap(HttpListenerContext ctx)
        {
            // Prefer the map image we extracted straight from the game — its bounds match the
            // world coordinates exactly, so the plane lines up with no calibration.
            byte[]? captured;
            lock (_mapLock) captured = _mapImage;
            if (captured != null)
            {
                // The captured map is JPEG (downscaled in TelemetryReader.MapSpriteToJpg).
                TelemetryServer.WriteBinary(ctx, captured, "image/jpeg");
                return;
            }

            // Fallback: a map file dropped into the plugins folder, used until a mission loads.
            string dir = BepInEx.Paths.PluginPath;
            string pngPath = Path.Combine(dir, "map.png");
            string jpgPath = Path.Combine(dir, "map.jpg");
            string jpegPath = Path.Combine(dir, "map.jpeg");
            string noExtPath = Path.Combine(dir, "map");

            string filePath = File.Exists(pngPath) ? pngPath
                            : File.Exists(jpgPath) ? jpgPath
                            : File.Exists(jpegPath) ? jpegPath
                            : File.Exists(noExtPath) ? noExtPath
                            : string.Empty;

            string contentType = filePath.EndsWith(".png") ? "image/png" : "image/jpeg";

            if (filePath == string.Empty)
            {
                ctx.Response.StatusCode = 404;
                try { ctx.Response.Close(); } catch { }
                Plugin.Log?.LogWarning($"[NOXMFD] Map not found in: {dir}");
                return;
            }

            try
            {
                TelemetryServer.WriteBinary(ctx, File.ReadAllBytes(filePath), contentType);
            }
            catch { }
            finally { try { ctx.Response.Close(); } catch { } }
        }

        internal static void ServeIcon(HttpListenerContext ctx) => ServePng(ctx, _icons, _iconLock, "type");
        internal static void ServeWeaponIcon(HttpListenerContext ctx) => ServePng(ctx, _weaponIcons, _weaponLock, "name");
        internal static void ServeCmIcon(HttpListenerContext ctx) => ServePng(ctx, _cmIcons, _cmLock, "type");
        internal static void ServeTgtIcon(HttpListenerContext ctx) => ServePng(ctx, _tgtIcons, _tgtLock, "type");
        internal static void ServeBdfIcon(HttpListenerContext ctx) => ServePng(ctx, _bdfIcons, _bdfLock, "type");
        internal static void ServeBuildingIcon(HttpListenerContext ctx) => ServePng(ctx, _buildingIcons, _buildingLock, "type");
        internal static void ServeHudCategoryIcon(HttpListenerContext ctx) => ServePng(ctx, _hudCategoryIcons, _hudCategoryLock, "cat");

        private static void ServePng(HttpListenerContext ctx, Dictionary<string, byte[]> dict, object dictLock, string queryKey)
        {
            string key = ctx.Request.QueryString[queryKey] ?? string.Empty;
            byte[]? png = null;
            if (key.Length > 0)
                lock (dictLock) dict.TryGetValue(key, out png);

            if (png == null)
            {
                ctx.Response.StatusCode = 404;
                try { ctx.Response.Close(); } catch { }
                return;
            }

            TelemetryServer.WriteBinary(ctx, png, "image/png");
        }

        internal static void ServeAirframeImage(HttpListenerContext ctx)
        {
            string type = ctx.Request.QueryString["type"] ?? string.Empty;
            string part = ctx.Request.QueryString["part"] ?? string.Empty;
            byte[]? png = null;
            if (type.Length > 0 && part.Length > 0)
                lock (_airframeLock) _airframeImages.TryGetValue(type + "|" + part, out png);

            if (png == null)
            {
                ctx.Response.StatusCode = 404;
                try { ctx.Response.Close(); } catch { }
                return;
            }

            TelemetryServer.WriteBinary(ctx, png, "image/png");
        }

        internal static void ServeAirframeLayout(HttpListenerContext ctx)
        {
            string type = ctx.Request.QueryString["type"] ?? string.Empty;
            string? json = null;
            if (type.Length > 0)
                lock (_airframeLock) _airframeLayouts.TryGetValue(type, out json);

            if (json == null)
            {
                ctx.Response.StatusCode = 404;
                try { ctx.Response.Close(); } catch { }
                return;
            }

            TelemetryServer.WriteBinary(ctx, Encoding.UTF8.GetBytes(json), "application/json; charset=utf-8");
        }
    }
}

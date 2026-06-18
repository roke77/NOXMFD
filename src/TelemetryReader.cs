using System;
using System.Collections.Generic;
using UnityEngine;

namespace NOTelemetryReader
{
    internal class TelemetryReader : MonoBehaviour
    {
        private const float FastInterval = 0.1f; // 10 Hz — position / speed
        private const float SlowInterval = 1.0f; // 1 Hz  — world scan + map metadata (FindObjectsByType is expensive)

        private float _fastTimer;
        private float _slowTimer;
        private int   _totalUnits;
        private int   _totalAircraft;

        // Map metadata, resolved once LevelInfo is available.
        private LevelInfo? _level;
        private bool  _mapValid;
        private float _mapW, _mapH;
        private int   _gridOffsetX, _gridOffsetY;
        private bool  _mapCaptured;

        // Aircraft-type map icons we've already extracted (keyed by unitName).
        private readonly HashSet<string> _capturedIcons = new HashSet<string>();

        // Slowly-changing context, refreshed in the 1 Hz scan.
        private string   _missionName = string.Empty;
        private string   _mapName     = string.Empty;
        private string[] _loadout     = Array.Empty<string>();

        private void Update()
        {
            _fastTimer += Time.deltaTime;
            _slowTimer += Time.deltaTime;

            if (_slowTimer >= SlowInterval)
            {
                _slowTimer = 0f;
                ScanWorld();
            }

            if (_fastTimer >= FastInterval)
            {
                _fastTimer = 0f;
                PushSnapshot();
            }
        }

        private void ScanWorld()
        {
            Unit[] units = UnityEngine.Object.FindObjectsByType<Unit>(FindObjectsSortMode.None);
            int aircraft = 0;
            foreach (Unit u in units)
                if (u is Aircraft) aircraft++;
            _totalUnits    = units.Length;
            _totalAircraft = aircraft;

            // Resolve the map bounds + grid offsets and capture the real in-game map image.
            if (_level == null)
                _level = UnityEngine.Object.FindObjectOfType<LevelInfo>();

            MapSettings? ms = _level != null ? _level.LoadedMapSettings : null;
            if (ms != null)
            {
                _mapW        = ms.MapSize.x;
                _mapH        = ms.MapSize.y;
                _gridOffsetX = ms.OffsetX;
                _gridOffsetY = ms.OffsetY;
                _mapValid    = _mapW > 0f && _mapH > 0f;
                _mapName     = CleanName(ms.name);
                TryCaptureMap(ms);
            }

            _missionName = MissionManager.CurrentMission?.Name ?? string.Empty;

            // Loadout changes rarely (only on rearm) — building it here at 1 Hz keeps the
            // 10 Hz push allocation-free.
            GameManager.GetLocalAircraft(out Aircraft ac);
            if (ac != null)
                _loadout = BuildLoadout(ac);
        }

        private static string CleanName(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            int clone = name.IndexOf("(Clone)", StringComparison.Ordinal);
            if (clone >= 0) name = name.Substring(0, clone);
            return name.Trim();
        }

        // Aggregates the aircraft's weapon mounts into display strings like "2x AGM-65".
        private static string[] BuildLoadout(Aircraft aircraft)
        {
            var lo = aircraft.loadout;
            if (lo == null || lo.weapons == null) return Array.Empty<string>();

            var counts = new Dictionary<string, int>();
            var order  = new List<string>();

            foreach (WeaponMount m in lo.weapons)
            {
                if (m == null || m.info == null || m.info.hideInDisplay) continue;

                string name = !string.IsNullOrEmpty(m.info.weaponName) ? m.info.weaponName
                            : !string.IsNullOrEmpty(m.mountName)        ? m.mountName
                            : m.info.shortName;
                if (string.IsNullOrEmpty(name)) continue;

                if (!counts.ContainsKey(name)) { counts[name] = 0; order.Add(name); }
                counts[name]++;
            }

            var result = new string[order.Count];
            for (int i = 0; i < order.Count; i++)
                result[i] = counts[order[i]] > 1 ? $"{counts[order[i]]}x {order[i]}" : order[i];
            return result;
        }

        private void PushSnapshot()
        {
            GameManager.GetLocalAircraft(out Aircraft aircraft);
            if (aircraft == null)
                return;

            // The game uses a floating-origin system: transform.position drifts back toward
            // zero as the world re-centers. The true world coordinate is pos - Datum.originPosition.
            Vector3 world   = aircraft.transform.position - Datum.originPosition;
            float   heading = aircraft.transform.eulerAngles.y;

            TryCaptureIcon(aircraft.definition);

            TelemetryServer.Push(new TelemetrySnapshot
            {
                Valid          = true,
                Time           = Time.time,
                PlaneName      = aircraft.definition.unitName,
                IconOrient     = aircraft.definition.mapOrient,
                IconScale      = aircraft.definition.mapIconSize,
                MissionName    = _missionName,
                MapName        = _mapName,
                Loadout        = _loadout,
                WorldX         = world.x,
                WorldY         = world.y,
                WorldZ         = world.z,
                Heading        = heading,
                TAS            = aircraft.speed,
                AGL            = Mathf.Max(0f, aircraft.radarAlt),
                GearDown       = aircraft.gearDeployed,
                TotalUnits     = _totalUnits,
                TotalAircraft  = _totalAircraft,
                MapValid       = _mapValid,
                MapW           = _mapW,
                MapH           = _mapH,
                GridOffsetX    = _gridOffsetX,
                GridOffsetY    = _gridOffsetY
            });
        }

        // Pulls MapSettings.MapImage (the actual in-game map sprite) into PNG bytes and hands
        // them to the server.
        private void TryCaptureMap(MapSettings ms)
        {
            if (_mapCaptured) return;

            byte[]? png = SpriteToPng(ms.MapImage);
            _mapCaptured = true; // whether it worked or not, don't retry every second

            if (png != null)
            {
                TelemetryServer.SetMapImage(png);
                Plugin.Log?.LogInfo($"[NOTelemetry] Captured in-game map ({png.Length} bytes).");
            }
            else
            {
                Plugin.Log?.LogWarning("[NOTelemetry] Map capture unavailable; falling back to map file.");
            }
        }

        // Extracts an aircraft type's top-down map icon to PNG, once per type, and registers it.
        private void TryCaptureIcon(UnitDefinition def)
        {
            if (def == null || string.IsNullOrEmpty(def.unitName) || _capturedIcons.Contains(def.unitName))
                return;
            if (def.mapIcon == null) { _capturedIcons.Add(def.unitName); return; }

            byte[]? png = SpriteToPng(def.mapIcon);
            _capturedIcons.Add(def.unitName); // don't retry this type regardless of outcome

            if (png != null)
            {
                TelemetryServer.SetIcon(def.unitName, png);
                Plugin.Log?.LogInfo($"[NOTelemetry] Captured icon for '{def.unitName}' ({png.Length} bytes).");
            }
        }

        // Renders a sprite (atlas-safe via textureRect) to PNG bytes. Uses a Blit→RenderTexture
        // round-trip so it works even when the source texture isn't CPU-readable. Must run on the
        // Unity main thread (Graphics/ReadPixels/EncodeToPNG).
        private static byte[]? SpriteToPng(Sprite sprite)
        {
            if (sprite == null || sprite.texture == null) return null;

            try
            {
                Texture2D tex = sprite.texture;
                Rect r = sprite.textureRect;
                int w = Mathf.RoundToInt(r.width);
                int h = Mathf.RoundToInt(r.height);
                if (w <= 0 || h <= 0)
                {
                    w = tex.width;
                    h = tex.height;
                    r = new Rect(0f, 0f, w, h);
                }

                RenderTexture rt = RenderTexture.GetTemporary(
                    tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(tex, rt);

                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = rt;
                Texture2D readable = new Texture2D(w, h, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(r.x, r.y, w, h), 0, 0);
                readable.Apply();
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);

                byte[] png = ImageConversion.EncodeToPNG(readable);
                Destroy(readable);
                return png;
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"[NOTelemetry] Sprite capture failed: {ex.Message}");
                return null;
            }
        }
    }
}

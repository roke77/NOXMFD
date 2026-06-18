using System;
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
                TryCaptureMap(ms);
            }
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

            TelemetryServer.Push(new TelemetrySnapshot
            {
                Valid          = true,
                Time           = Time.time,
                PlaneName      = aircraft.definition.unitName,
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
        // them to the server. Must run on the Unity main thread (Graphics/ReadPixels/EncodeToPNG).
        private void TryCaptureMap(MapSettings ms)
        {
            if (_mapCaptured) return;

            Sprite sprite = ms.MapImage;
            if (sprite == null || sprite.texture == null) return;

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

                // Blit into a RenderTexture so we can read it back even if the source isn't readable.
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

                TelemetryServer.SetMapImage(png);
                _mapCaptured = true;
                Plugin.Log?.LogInfo($"[NOTelemetry] Captured in-game map '{sprite.name}' {w}x{h} -> {png.Length} bytes.");
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"[NOTelemetry] Map capture failed ({ex.Message}); falling back to map file.");
                _mapCaptured = true; // don't retry every second
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Reflection;
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
        private const int IconsPerScan = 16;  // cap new icon extractions per scan to avoid a frame hitch

        // Cached unit list from the 1 Hz scan; positions are read from it at 10 Hz.
        private Unit[] _units = Array.Empty<Unit>();

        // Slowly-changing context, refreshed in the 1 Hz scan.
        private string         _missionName = string.Empty;
        private string         _mapName     = string.Empty;
        private LoadoutEntry[]  _loadout     = Array.Empty<LoadoutEntry>();

        // Weapon-type icons we've already extracted (keyed by weapon display name).
        private readonly HashSet<string> _capturedWeaponIcons = new HashSet<string>();
        private bool _capturedFlareIcon  = false;
        private bool _capturedJammerIcon = false;

        private int _flares    = -1;   // IR flares remaining (refreshed in the 1 Hz scan)
        private int _flaresMax = -1;   // IR flares capacity   (refreshed in the 1 Hz scan)

        // The game's HUD faction colors, read once from GameAssets.
        private string _colFriendly = "#39ff14";
        private string _colHostile  = "#ff4040";
        private string _colNeutral  = "#9aa0a6";
        private bool   _colorsRead;

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
            _units = units;

            int aircraft = 0;
            int iconBudget = IconsPerScan;
            foreach (Unit u in units)
            {
                if (u == null) continue;
                if (u is Aircraft) aircraft++;
                // Pre-extract each unit type's map icon (a few per scan so it doesn't hitch).
                if (iconBudget > 0 && TryCaptureIcon(u.definition)) iconBudget--;
            }
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

            ReadFactionColors();

            // Loadout changes rarely (only on rearm) — building it here at 1 Hz keeps the
            // 10 Hz push allocation-free.
            GameManager.GetLocalAircraft(out Aircraft ac);
            if (ac != null)
            {
                _loadout = BuildLoadout(ac);
                CountFlares(ac, out _flares, out _flaresMax);
                TryCaptureCmIcons(ac);
            }
        }

        // Sums IR flares (remaining + capacity) across all flare ejectors. Returns (-1, -1)
        // if the aircraft has no flare system.
        private static void CountFlares(Aircraft ac, out int ammo, out int max)
        {
            ammo = -1; max = -1;
            FlareEjector[] ejectors = ac.GetComponentsInChildren<FlareEjector>();
            if (ejectors == null || ejectors.Length == 0) return;
            int total = 0, totalMax = 0;
            foreach (FlareEjector fe in ejectors)
                if (fe != null) { total += fe.GetAmmo(); totalMax += fe.GetMaxAmmo(); }
            ammo = total; max = totalMax;
        }

        // Extracts the flares + radar jammer Sprites from any matching component on the
        // aircraft, once each. Saves them as PNGs so /cm?type=flares|jammer can serve them.
        private void TryCaptureCmIcons(Aircraft ac)
        {
            if (!_capturedFlareIcon)
            {
                FlareEjector? fe = ac.GetComponentInChildren<FlareEjector>();
                if (fe != null && fe.displayImage != null)
                {
                    byte[]? png = SpriteToPng(fe.displayImage, isIcon: true);
                    if (png != null) { TelemetryServer.SetCmIcon("flares", png); _capturedFlareIcon = true; }
                }
            }
            if (!_capturedJammerIcon)
            {
                RadarJammer? rj = ac.GetComponentInChildren<RadarJammer>();
                if (rj != null && rj.displayImage != null)
                {
                    byte[]? png = SpriteToPng(rj.displayImage, isIcon: true);
                    if (png != null) { TelemetryServer.SetCmIcon("jammer", png); _capturedJammerIcon = true; }
                }
            }
        }

        // PowerSupply.maxCharge is private; cache the FieldInfo and read it via reflection.
        private static FieldInfo? _powerMaxField;
        private static float GetEwMaxKJ(PowerSupply ps)
        {
            if (ps == null) return -1f;
            if (_powerMaxField == null)
                _powerMaxField = typeof(PowerSupply).GetField("maxCharge", BindingFlags.NonPublic | BindingFlags.Instance);
            if (_powerMaxField == null) return -1f;
            try { return _powerMaxField.GetValue(ps) is float f ? f : -1f; }
            catch { return -1f; }
        }

        // The active countermeasure index points into CountermeasureManager's private station
        // list, so we reflect into it once (cached) and check the active station's type.
        private static FieldInfo?  _cmStationsField;
        private static MethodInfo? _cmGetFirstMethod;

        private static byte GetSelectedCmCategory(Aircraft ac)
        {
            CountermeasureManager mgr = ac.countermeasureManager;
            if (mgr == null) return 0;

            try
            {
                if (_cmStationsField == null)
                    _cmStationsField = typeof(CountermeasureManager)
                        .GetField("countermeasureStations", BindingFlags.NonPublic | BindingFlags.Instance);

                if (_cmStationsField?.GetValue(mgr) is not System.Collections.IList list || list.Count == 0)
                    return 0;

                int idx = mgr.activeIndex;
                if (idx < 0 || idx >= list.Count) return 0;

                object station = list[idx];
                if (station == null) return 0;

                if (_cmGetFirstMethod == null)
                    _cmGetFirstMethod = station.GetType()
                        .GetMethod("GetFirstCountermeasure", BindingFlags.Public | BindingFlags.Instance);

                if (_cmGetFirstMethod?.Invoke(station, null) is not Countermeasure cm) return 0;
                if (cm is FlareEjector) return 1;
                if (cm is RadarJammer)  return 2;
                if (cm is ChaffEjector) return 3;
                return 0;
            }
            catch { return 0; }
        }

        // Reads the game's HUD faction colors once (constant for the session).
        private void ReadFactionColors()
        {
            if (_colorsRead) return;
            try
            {
                GameAssets ga = GameAssets.i;
                if (ga == null) return;
                _colFriendly = ColorHex(ga.HUDFriendly);
                _colHostile  = ColorHex(ga.HUDHostile);
                _colNeutral  = ColorHex(ga.HUDNeutral);
                _colorsRead  = true;
            }
            catch { /* fall back to defaults */ }
        }

        private static string ColorHex(Color c)
        {
            int r = Mathf.Clamp((int)(c.r * 255f + 0.5f), 0, 255);
            int g = Mathf.Clamp((int)(c.g * 255f + 0.5f), 0, 255);
            int b = Mathf.Clamp((int)(c.b * 255f + 0.5f), 0, 255);
            return $"#{r:x2}{g:x2}{b:x2}";
        }

        private static string CleanName(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            int clone = name.IndexOf("(Clone)", StringComparison.Ordinal);
            if (clone >= 0) name = name.Substring(0, clone);
            return name.Trim();
        }

        // Aggregates the aircraft's live weapon stations by type (summing remaining/total ammo),
        // and extracts each weapon's icon. Uses weaponStations rather than the static loadout so
        // ammo counts reflect what's actually left.
        private readonly Dictionary<string, int> _loIndex = new Dictionary<string, int>();
        private readonly List<string> _loNames = new List<string>();
        private readonly List<int>    _loCur   = new List<int>();
        private readonly List<int>    _loMax   = new List<int>();

        private LoadoutEntry[] BuildLoadout(Aircraft aircraft)
        {
            var stations = aircraft.weaponStations;
            if (stations == null) return Array.Empty<LoadoutEntry>();

            _loIndex.Clear(); _loNames.Clear(); _loCur.Clear(); _loMax.Clear();

            foreach (WeaponStation st in stations)
            {
                if (st == null) continue;
                WeaponInfo info = st.WeaponInfo;
                if (info == null || info.hideInDisplay) continue;

                string name = !string.IsNullOrEmpty(info.weaponName) ? info.weaponName : info.shortName;
                if (string.IsNullOrEmpty(name)) continue;

                if (!_loIndex.TryGetValue(name, out int i))
                {
                    i = _loNames.Count;
                    _loIndex[name] = i;
                    _loNames.Add(name); _loCur.Add(0); _loMax.Add(0);
                }
                _loCur[i] += st.Ammo;
                _loMax[i] += st.FullAmmo;
                TryCaptureWeaponIcon(name, info.weaponIcon);
            }

            var result = new LoadoutEntry[_loNames.Count];
            for (int i = 0; i < _loNames.Count; i++)
                result[i] = new LoadoutEntry { Name = _loNames[i], Ammo = _loCur[i], FullAmmo = _loMax[i] };
            return result;
        }

        // Extracts a weapon type's icon to PNG, once per name, and registers it.
        private void TryCaptureWeaponIcon(string name, Sprite icon)
        {
            if (string.IsNullOrEmpty(name) || _capturedWeaponIcons.Contains(name)) return;
            _capturedWeaponIcons.Add(name);
            if (icon == null) return;

            byte[]? png = SpriteToPng(icon, isIcon: true);
            if (png != null) TelemetryServer.SetWeaponIcon(name, png);
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

            PowerSupply ps     = aircraft.GetPowerSupply();
            float       ewKJ   = ps != null ? ps.GetChargeKJ() : -1f;
            float       ewKJMax = ps != null ? GetEwMaxKJ(ps)  : -1f;

            string selWeapon = string.Empty;
            WeaponManager wm = aircraft.weaponManager;
            WeaponInfo selInfo = wm != null && wm.currentWeaponStation != null ? wm.currentWeaponStation.WeaponInfo : null;
            if (selInfo != null)
                selWeapon = !string.IsNullOrEmpty(selInfo.weaponName) ? selInfo.weaponName : selInfo.shortName;

            byte cmCategory = GetSelectedCmCategory(aircraft);

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
                Flares         = _flares,
                FlaresMax      = _flaresMax,
                EwKJ           = ewKJ,
                EwKJMax        = ewKJMax,
                SelWeapon      = selWeapon,
                CmCategory     = cmCategory,
                TotalUnits     = _totalUnits,
                TotalAircraft  = _totalAircraft,
                MapValid       = _mapValid,
                MapW           = _mapW,
                MapH           = _mapH,
                GridOffsetX    = _gridOffsetX,
                GridOffsetY    = _gridOffsetY,
                Units          = BuildUnits(aircraft),
                ColFriendly    = _colFriendly,
                ColHostile     = _colHostile,
                ColNeutral     = _colNeutral
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

        // Extracts a unit type's top-down map icon to PNG, once per type, and registers it.
        // Returns true if it attempted a (costly) extraction this call, so callers can budget.
        private bool TryCaptureIcon(UnitDefinition def)
        {
            if (def == null || string.IsNullOrEmpty(def.unitName) || _capturedIcons.Contains(def.unitName))
                return false;

            _capturedIcons.Add(def.unitName); // mark regardless so we never retry this type
            if (def.mapIcon == null) return false;

            byte[]? png = SpriteToPng(def.mapIcon, isIcon: true);
            if (png != null)
                TelemetryServer.SetIcon(def.unitName, png);
            return true;
        }

        // Builds the list of units the player's faction can see. Friendlies appear at their
        // true position; enemies only when tracked, at their last-known position (fog of war).
        private readonly List<UnitInfo> _unitBuf = new List<UnitInfo>(256);

        private UnitInfo[] BuildUnits(Aircraft player)
        {
            var playerHQ = player.NetworkHQ;
            if (playerHQ == null) return Array.Empty<UnitInfo>();

            // The player's current target(s): the live weapon target list (public API, no
            // reflection). Reference-matched against each scanned unit below.
            List<Unit> targets = player.weaponManager != null ? player.weaponManager.GetTargetList() : null;
            bool hasTargets = targets != null && targets.Count > 0;

            _unitBuf.Clear();
            foreach (Unit u in _units)
            {
                if (u == null || u.disabled || ReferenceEquals(u, player)) continue;

                UnitDefinition def = u.definition;
                if (def == null) continue;

                // One call resolves both visibility and position under fog of war.
                if (!playerHQ.TryGetKnownPosition(u, out GlobalPosition gp)) continue;

                var hq = u.NetworkHQ;
                byte faction = hq == null ? (byte)0 : (hq == playerHQ ? (byte)1 : (byte)2);

                _unitBuf.Add(new UnitInfo
                {
                    Type     = def.unitName,
                    X        = gp.x,
                    Z        = gp.z,
                    Heading  = u.transform.eulerAngles.y,
                    Faction  = faction,
                    Orient   = def.mapOrient,
                    Scale    = def.mapIconSize,
                    Targeted = hasTargets && targets.Contains(u)
                });
            }
            return _unitBuf.ToArray();
        }

        // Renders a sprite (atlas-safe via textureRect) to PNG bytes. Uses a Blit→RenderTexture
        // round-trip so it works even when the source texture isn't CPU-readable. Must run on the
        // Unity main thread (Graphics/ReadPixels/EncodeToPNG).
        // When 'isIcon' is true and the result is fully opaque (e.g. alpha-less DXT1 ship icons),
        // the alpha channel is rebuilt from luminance so the silhouette isn't a solid square.
        private static byte[]? SpriteToPng(Sprite sprite, bool isIcon = false)
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

                if (isIcon) SynthesizeAlphaIfOpaque(readable);

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

        // Icons stored without an alpha channel (DXT1, RGB24, …) read as fully opaque, so a
        // straight tint produces a solid square. The game draws them as light-on-dark
        // silhouettes, so when an icon comes back with no transparency we derive alpha from
        // luminance — dark background → transparent, bright shape → opaque.
        private static void SynthesizeAlphaIfOpaque(Texture2D readable)
        {
            Color32[] px = readable.GetPixels32();
            byte minA = 255;
            for (int i = 0; i < px.Length; i++)
                if (px[i].a < minA) { minA = px[i].a; if (minA == 0) break; }
            if (minA < 250) return; // already has real transparency — leave it alone

            for (int i = 0; i < px.Length; i++)
            {
                Color32 c = px[i];
                c.a = (byte)((c.r * 77 + c.g * 150 + c.b * 29) >> 8); // ~Rec.601 luminance
                px[i] = c;
            }
            readable.SetPixels32(px);
            readable.Apply();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace NOXMFD
{
    // Mission-scoped HUD declutter. Lives on the same GameObject as TelemetryReader (created when
    // a mission starts, destroyed when it ends), so there's nothing to restore on mission end —
    // the HUD is torn down with the aircraft anyway.
    //
    // Re-applied on a slow interval because the game rebuilds the HUD per aircraft spawn and (for
    // the boxed readouts) the HeadMountedDisplay toggles their GameObjects active/inactive every
    // frame. Reading HudDeclutterConfig each tick lets a future keybind/UI flip a flag and have us hide or
    // restore within one interval.
    internal class HudDeclutter : MonoBehaviour
    {
        private const float Interval = 0.5f;
        private float _timer = Interval; // apply immediately on first Update

        // Graphics (Text/Image) we disabled (the boxed readouts + the minimap's box background).
        // We disable the child graphics rather than the GameObject because the HMD re-asserts the
        // GameObject's active state every frame, but never re-enables these graphics.
        private readonly HashSet<Graphic> _hiddenGraphics = new HashSet<Graphic>();

        private static readonly Dictionary<Type, FieldInfo?> _borderFields = new Dictionary<Type, FieldInfo?>();

        private void Update()
        {
            UpdateMinimap();                       // cheap, every frame — responsive, no flicker

            _timer += Time.unscaledDeltaTime;
            if (_timer < Interval) return;
            _timer = 0f;
            ApplyHudApps();
        }

        // ---- HUD widgets -----------------------------------------------------------------------

        private void ApplyHudApps()
        {
            bool master = HudDeclutterConfig.DeclutterHud && Plugin.FeaturesActive;

            // Top-right weapon / ammo / countermeasure / capacitor cluster. This is CombatHUD's
            // private 'topRightPanel' GameObject — NOT the WeaponIndicator/CountermeasureIndicator
            // HUDApps (those are a separate, off-screen copy). Same target NO_Tactitools uses.
            UpdateWeaponPanel(master && HudDeclutterConfig.HideWeaponAmmo);

            // Boxed top readouts (heading / airspeed / altitude). Only the copies with an assigned
            // 'border' are hidden; the borderless boresight-following center readouts are kept.
            // The else branch restores them when the flag is flipped off at runtime (e.g. via the
            // in-game config menu) — the HMD never re-enables graphics we disabled, so we must.
            if (master && HudDeclutterConfig.HideTopBoxes)
            {
                HideBoxedReadout<Bearing>();
                HideBoxedReadout<SpeedGauge>();
                HideBoxedReadout<Altitude>();
            }
            else if (_hiddenGraphics.Count > 0)
            {
                RestoreBoxedReadouts();
            }
        }

        // Re-enable every boxed-readout graphic we disabled and forget them. Symmetric with the
        // weapon-panel / minimap restore paths so the HideTopBoxes toggle is fully reversible.
        private void RestoreBoxedReadouts()
        {
            foreach (Graphic g in _hiddenGraphics)
                if (g != null) g.enabled = true;
            _hiddenGraphics.Clear();
        }

        // Top-right weapon / ammo / countermeasure / capacitor cluster = CombatHUD's private
        // 'topRightPanel' GameObject. SetActive(false) on it (same as NO_Tactitools).
        private static FieldInfo? _topRightPanelField;
        private bool _weaponPanelHidden;

        private void UpdateWeaponPanel(bool shouldHide)
        {
            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            if (hud == null) return;
            if (_topRightPanelField == null)
                _topRightPanelField = typeof(CombatHUD).GetField("topRightPanel", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_topRightPanelField?.GetValue(hud) is not GameObject panel || panel == null) return;

            if (shouldHide)
            {
                if (panel.activeSelf)
                {
                    panel.SetActive(false);
                    _weaponPanelHidden = true;
                }
            }
            else if (_weaponPanelHidden)
            {
                panel.SetActive(true);
                _weaponPanelHidden = false;
            }
        }

        // Disable the child graphics of every BOXED copy of T (the ones with an assigned border).
        // Re-applied each tick so HMD-toggled / freshly-spawned copies get re-hidden.
        private void HideBoxedReadout<T>() where T : HUDApp
        {
            T[] instances = UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (T inst in instances)
            {
                if (inst == null || !HasAssignedBorder(inst)) continue;
                foreach (Graphic g in inst.GetComponentsInChildren<Graphic>(true))
                {
                    if (g != null && g.enabled)
                    {
                        g.enabled = false;
                        _hiddenGraphics.Add(g);
                    }
                }
            }
        }

        // ---- Minimap ---------------------------------------------------------------------------
        // The corner minimap and the full M-key map are the same DynamicMap (minimized vs
        // maximized), so we can't destroy it. While minimized we: suppress the map canvas (the
        // game re-enables it itself on Maximize), disable the LowerLeftPanel box background, and
        // hide the forwardRef / GridAircraft ornaments. All restored when maximized or toggled off.
        private bool _minimapHidden;
        private Image? _minimapBox;
        private readonly List<GameObject> _minimapOrnaments = new List<GameObject>();

        private void UpdateMinimap()
        {
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map == null) return;

            bool hide = HudDeclutterConfig.DeclutterHud && Plugin.FeaturesActive && HudDeclutterConfig.HideMinimap && !DynamicMap.mapMaximized;
            if (hide)
            {
                DynamicMap.EnableCanvas(false);       // idempotent; re-asserts after Minimize()
                ResolveMinimapExtras(map);
                if (_minimapBox != null) _minimapBox.enabled = false;
                foreach (GameObject o in _minimapOrnaments) if (o != null) o.SetActive(false);
                _minimapHidden = true;
            }
            else if (_minimapHidden)
            {
                DynamicMap.EnableCanvas(true);
                if (_minimapBox != null) _minimapBox.enabled = true;
                foreach (GameObject o in _minimapOrnaments) if (o != null) o.SetActive(true);
                _minimapHidden = false;
            }
        }

        // The box background is the LowerLeftPanel Image (parent of HUDMapAnchor); the ornaments are
        // the map markers parented directly under HUDMapAnchor (everything except the MapCanvas,
        // which EnableCanvas already handles, and which hosts the persistent DynamicMap logic).
        private void ResolveMinimapExtras(DynamicMap map)
        {
            if (_minimapBox != null || map.hudMapAnchor == null) return;
            Transform anchor = map.hudMapAnchor.transform;

            if (anchor.parent != null) anchor.parent.TryGetComponent(out _minimapBox);

            _minimapOrnaments.Clear();
            for (int i = 0; i < anchor.childCount; i++)
            {
                Transform c = anchor.GetChild(i);
                if (c.name != "MapCanvas") _minimapOrnaments.Add(c.gameObject);
            }
        }

        // ---- helpers ---------------------------------------------------------------------------

        private static bool HasAssignedBorder(Component inst)
        {
            Type t = inst.GetType();
            if (!_borderFields.TryGetValue(t, out FieldInfo? f))
            {
                f = t.GetField("border", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                _borderFields[t] = f;
            }
            return f != null && f.GetValue(inst) is Image img && img != null;
        }

        private void OnDestroy()
        {
            _hiddenGraphics.Clear();
            _minimapOrnaments.Clear();
        }
    }
}

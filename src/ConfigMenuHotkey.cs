using System;
using System.Reflection;
using BepInEx.Bootstrap;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NOXMFD
{
    // Opens the ConfigurationManager config menu with Ctrl+H.
    //
    // Why we need this: ConfigurationManager (GUID com.bepis.bepinex.configurationmanager) toggles
    // its window with a *legacy* UnityEngine.Input hotkey (default F1). Nuclear Option runs on
    // Unity's *new* Input System with legacy input inactive, so CM never sees any keypress and its
    // menu can't be opened the normal way — which is why F1/Insert do nothing in-game.
    //
    // CM publishes two hooks for exactly this case: OverrideHotkey (tell CM to stop handling its
    // own hotkey) and DisplayingWindow (open/close the window). We read our key through the new
    // Input System (which works here) and drive DisplayingWindow. The menu itself — our HUD
    // Declutter checkboxes plus every other mod's settings — is still CM's, fully intact.
    //
    // Resolved by reflection so CM stays a soft dependency: if it isn't installed this component
    // logs once and goes dormant. Lives on the persistent Worker GameObject so the menu is
    // reachable everywhere, including the main menu.
    internal class ConfigMenuHotkey : MonoBehaviour
    {
        private const string CmGuid = "com.bepis.bepinex.configurationmanager";

        private object?       _cm;          // the ConfigurationManager plugin instance
        private PropertyInfo? _displaying;  // DisplayingWindow { get; set; }

        private void Start()
        {
            try
            {
                if (!Chainloader.PluginInfos.TryGetValue(CmGuid, out var info) || info?.Instance == null)
                {
                    Plugin.Log?.LogInfo("[NOXMFD] ConfigurationManager not installed — Ctrl+H config menu unavailable.");
                    return;
                }

                _cm = info.Instance;
                Type t = _cm.GetType();
                _displaying = t.GetProperty("DisplayingWindow", BindingFlags.Instance | BindingFlags.Public);
                if (_displaying == null || !_displaying.CanWrite)
                {
                    Plugin.Log?.LogWarning("[NOXMFD] ConfigurationManager.DisplayingWindow not settable — config menu hotkey disabled.");
                    _cm = null;
                    return;
                }

                // Tell CM to stop processing its own (dead) hotkey; we own the window now.
                t.GetProperty("OverrideHotkey", BindingFlags.Instance | BindingFlags.Public)?.SetValue(_cm, true, null);
                Plugin.Log?.LogInfo("[NOXMFD] Config menu bound to Ctrl+H (ConfigurationManager).");
            }
            catch (Exception e)
            {
                _cm = null;
                Plugin.Log?.LogWarning("[NOXMFD] Failed to bind config menu hotkey: " + e.Message);
            }
        }

        private void Update()
        {
            if (_cm == null) return;

            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            bool ctrl = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;
            if (!ctrl || !kb.hKey.wasPressedThisFrame) return;

            bool open = (bool)_displaying!.GetValue(_cm, null);
            _displaying.SetValue(_cm, !open, null);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace NOXMFD
{
    internal static class ConfigEndpoint
    {
        internal static void ServeConfig(HttpListenerContext ctx)
        {
            try
            {
                string json = string.Format(CultureInfo.InvariantCulture,
                    "{{\"localhost\":\"http://localhost:{0}\",\"lanUrl\":\"{1}\",\"port\":{0}}}",
                    TelemetryServer.Port, TelemetryServer.EscapeJson(TelemetryServer.LanUrl ?? string.Empty));
                TelemetryServer.WriteJson(ctx, json);
            }
            catch { }
            finally { try { ctx.Response.Close(); } catch { } }
        }

        // The keybind registry as JSON for the /keybinds page: every bind's identity + current values,
        // plus which bind (if any) is armed for joystick capture — the page polls this while open, and
        // it's also how a capture result comes back. Safe off the main thread: the registry list is
        // built once at Awake and never mutated, and ConfigEntry/CapturingId reads are plain field reads
        // (worst case one poll stale).
        internal static void ServeKeybindsConfig(HttpListenerContext ctx)
        {
            try
            {
                var sb = new StringBuilder(512);
                sb.Append("{\"binds\":[");
                bool first = true;
                foreach (var b in Keybinds.Binds)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append("{\"id\":\"").Append(TelemetryServer.EscapeJson(b.Id))
                      .Append("\",\"section\":\"").Append(TelemetryServer.EscapeJson(Keybinds.SectionTitle(b.Section)))
                      .Append("\",\"label\":\"").Append(TelemetryServer.EscapeJson(b.Label))
                      .Append("\",\"description\":\"").Append(TelemetryServer.EscapeJson(b.Description)).Append('"');
                    // Digital source — absent for an axis-only bind (docs/map-cursor.md); the page
                    // renders no key/joy cell for a row that has no key/joyButton field. The two are
                    // independently optional: a key-only bind (issue #51's SAVE/LOAD LAYOUT — browser-
                    // side only, no joystick/HOTAS) has KeyEntry but no JoyEntry, so joyButton/joyNum
                    // are omitted too — the page renders one wide key cell for that row instead of an
                    // always-empty joystick cell next to it.
                    if (b.KeyEntry != null)
                    {
                        KeyCode key = b.KeyEntry.Value.MainKey;
                        sb.Append(",\"key\":\"").Append(key == KeyCode.None ? string.Empty : TelemetryServer.EscapeJson(key.ToString())).Append('"');
                        if (b.JoyEntry != null)
                            sb.Append(",\"joyButton\":").Append(b.JoyEntry.Value.ToString(CultureInfo.InvariantCulture))
                              .Append(",\"joyNum\":").Append(b.JoyNumEntry!.Value.ToString(CultureInfo.InvariantCulture));
                    }
                    // Analog source — present only for the MAP cursor's axis-capable rows.
                    if (b.AxisEntry != null)
                    {
                        sb.Append(",\"axis\":").Append(b.AxisEntry.Value.ToString(CultureInfo.InvariantCulture))
                          .Append(",\"axisNum\":").Append(b.AxisJoyNumEntry!.Value.ToString(CultureInfo.InvariantCulture))
                          .Append(",\"axisInvert\":").Append(b.AxisInvertEntry!.Value ? "true" : "false");
                    }
                    sb.Append('}');
                }
                // Per-section notes (shared behaviour text under a section header), keyed by the
                // display title the binds carry in "section".
                sb.Append("],\"notes\":{");
                bool firstNote = true;
                var seen = new List<string>(4);
                foreach (var b in Keybinds.Binds)
                {
                    if (seen.Contains(b.Section)) continue;
                    seen.Add(b.Section);
                    string? note = Keybinds.SectionNote(b.Section);
                    if (note == null) continue;
                    if (!firstNote) sb.Append(',');
                    firstNote = false;
                    sb.Append('"').Append(TelemetryServer.EscapeJson(Keybinds.SectionTitle(b.Section)))
                      .Append("\":\"").Append(TelemetryServer.EscapeJson(note)).Append('"');
                }
                string? cap = Keybinds.CapturingId;
                string? capKind = Keybinds.CapturingKind;
                sb.Append("},\"capturing\":").Append(cap == null ? "null" : "\"" + TelemetryServer.EscapeJson(cap) + "\"")
                  .Append(",\"capturingKind\":").Append(capKind == null ? "null" : "\"" + TelemetryServer.EscapeJson(capKind) + "\"")
                  .Append(",\"bgInput\":").Append(Keybinds.BackgroundInput ? "true" : "false")
                  .Append(",\"radarOnOnStart\":").Append(ImmersionConfig.RadarOnOnStart ? "true" : "false")
                  .Append(",\"engineOnOnStart\":").Append(ImmersionConfig.EngineOnOnStart ? "true" : "false")
                  .Append(",\"masterArmsOnOnStart\":").Append(ImmersionConfig.MasterArmsOnOnStart ? "true" : "false")
                  .Append(",\"hudFiltersOnCombatMode\":").Append(ImmersionConfig.HudFiltersOnCombatMode ? "true" : "false")
                  .Append(",\"remoteKeybindsSamePc\":").Append(IsSameMachineRequest(ctx) ? "true" : "false")
                  .Append('}');

                TelemetryServer.WriteJson(ctx, sb.ToString());
            }
            catch { }
            finally { try { ctx.Response.Close(); } catch { } }
        }

        private static HashSet<string>? _localAddressCache;

        private static bool IsSameMachineRequest(HttpListenerContext ctx)
        {
            IPAddress? remote = ctx.Request.RemoteEndPoint?.Address;
            if (remote == null) return false;
            if (IPAddress.IsLoopback(remote)) return true;
            string addr = NormalizeAddress(remote);
            if (addr.Length == 0) return false;
            return LocalAddresses().Contains(addr);
        }

        private static HashSet<string> LocalAddresses()
        {
            if (_localAddressCache != null) return _localAddressCache;
            var set = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    foreach (UnicastIPAddressInformation uni in ni.GetIPProperties().UnicastAddresses)
                    {
                        string normalized = NormalizeAddress(uni.Address);
                        if (normalized.Length > 0) set.Add(normalized);
                    }
                }
            }
            catch { }
            _localAddressCache = set;
            return set;
        }

        private static string NormalizeAddress(IPAddress ip)
        {
            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
            if (ip.AddressFamily != AddressFamily.InterNetwork && ip.AddressFamily != AddressFamily.InterNetworkV6)
                return string.Empty;
            return ip.ToString();
        }

        internal static void ServeHudOptions(HttpListenerContext ctx)
        {
            try
            {
                TelemetryServer.WriteJson(ctx, TelemetryServer.HudOptionsJson ?? "{}");
            }
            catch { }
            finally { try { ctx.Response.Close(); } catch { } }
        }

        internal static void ServeWptOptions(HttpListenerContext ctx)
        {
            try
            {
                TelemetryServer.WriteJson(ctx, RouteStore.RoutesJson ?? "{\"activeRouteId\":null,\"routes\":[]}");
            }
            catch { }
            finally { try { ctx.Response.Close(); } catch { } }
        }

        internal static void ServeLayoutOptions(HttpListenerContext ctx)
        {
            try
            {
                TelemetryServer.WriteJson(ctx, LayoutStore.LayoutsJson ?? "{\"layouts\":[]}");
            }
            catch { }
            finally { try { ctx.Response.Close(); } catch { } }
        }

        internal static void ServeHudPresets(HttpListenerContext ctx)
        {
            try
            {
                TelemetryServer.WriteJson(ctx, HudPresetStore.PresetsJson ?? "{\"current\":1,\"presets\":[]}");
            }
            catch { }
            finally { try { ctx.Response.Close(); } catch { } }
        }

        internal static void ServeRatesConfig(HttpListenerContext ctx)
        {
            try
            {
                string json = string.Format(CultureInfo.InvariantCulture,
                    "{{\"fastHz\":{0},\"contactHz\":{1},\"tgpHz\":{2},\"tgpResolution\":\"{3}\",\"tgpJpegQuality\":\"{4}\",\"tgpQuality\":\"{5}\",\"tgpSuppressNative\":{6}}}",
                    RatesConfig.FastHz, RatesConfig.ContactHz, RatesConfig.TgpHz, RatesConfig.TgpResolutionName,
                    RatesConfig.TgpJpegQualityName, RatesConfig.TgpLegacyQualityName,
                    RatesConfig.TgpSuppressNative ? "true" : "false");
                TelemetryServer.WriteJson(ctx, json);
            }
            catch { }
            finally { try { ctx.Response.Close(); } catch { } }
        }
    }
}

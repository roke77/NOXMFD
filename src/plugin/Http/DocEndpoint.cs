using System;
using System.IO;
using System.Net;

namespace NOXMFD
{
    // DOC (kneeboard image viewer, issue #82) HTTP surface. Unlike every other CapturedAssetEndpoint
    // asset, these images are never embedded or captured in-process — they're dropped onto disk by
    // the player, into a folder dedicated to this feature (not the shared BepInEx/plugins/ root
    // CapturedAssetEndpoint.ServeMap falls back to for map.png), so a collision with another mod's
    // files there isn't a concern. The list is re-read from disk on every request rather than cached:
    // the folder is small (a personal set of reference images), so the read is cheap, and it means a
    // file a player adds or removes mid-session just starts/stops appearing with no stale-list state
    // to invalidate.
    internal static class DocEndpoint
    {
        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg" };

        internal static string KneeboardDir()
        {
            string dir = Path.Combine(BepInEx.Paths.PluginPath, "NOXMFD", "kneeboard");
            try { Directory.CreateDirectory(dir); } catch (Exception ex) { Plugin.Log?.LogWarning($"[NOXMFD] Could not create kneeboard folder {dir}: {ex.Message}"); }
            return dir;
        }

        // Filesystem/OS order (whatever Directory.GetFiles returns) — no explicit sort, per the
        // ticket's own decision (issue #82).
        internal static string[] ListImages()
        {
            string dir = KneeboardDir();
            try
            {
                var names = new System.Collections.Generic.List<string>();
                foreach (string path in Directory.GetFiles(dir))
                {
                    string ext = Path.GetExtension(path).ToLowerInvariant();
                    if (Array.IndexOf(ImageExtensions, ext) >= 0) names.Add(Path.GetFileName(path));
                }
                return names.ToArray();
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"[NOXMFD] Could not list kneeboard folder {dir}: {ex.Message}");
                return Array.Empty<string>();
            }
        }

        internal static void ServeList(HttpListenerContext ctx) => TelemetryServer.WriteJsonStringArray(ctx, ListImages(), "/doc-list");

        // Only ever serves a file this same request's own ListImages() call just returned —
        // DocNameMatch.Find (its own pure file, unit-tested) is the matching rule; see its own
        // header comment for the path-traversal reasoning.
        internal static void ServeImage(HttpListenerContext ctx)
        {
            string requested = ctx.Request.QueryString["name"] ?? string.Empty;
            string? match = DocNameMatch.Find(ListImages(), requested);

            if (match == null)
            {
                ctx.Response.StatusCode = 404;
                try { ctx.Response.Close(); } catch { }
                return;
            }

            string ext = Path.GetExtension(match).ToLowerInvariant();
            string contentType = ext == ".png" ? "image/png" : "image/jpeg";

            try
            {
                byte[] bytes = File.ReadAllBytes(Path.Combine(KneeboardDir(), match));
                TelemetryServer.WriteBinary(ctx, bytes, contentType, "/doc-image");
            }
            catch (Exception ex) { TelemetryServer.LogHttpFailure(ctx, "/doc-image", ex); }
            finally { try { ctx.Response.Close(); } catch { } }
        }
    }
}

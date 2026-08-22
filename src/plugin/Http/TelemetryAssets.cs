using System;
using System.IO;
using System.Net;
using System.Reflection;

namespace NOXMFD
{
    internal static class TelemetryAssets
    {
        // Real files under src/web/ are baked into the DLL as embedded resources and served under
        // /assets/. MSBuild names a resource like "<RootNamespace>.src.web.<dotted path>" (and may
        // mangle odd characters), so match by the stable ".web.<dotted path>" suffix against the
        // manifest rather than reconstructing the whole name.
        private static readonly Assembly _asm = typeof(TelemetryAssets).Assembly;
        private static string[]? _resourceNames;
        private static string[] ResourceNames => _resourceNames ??= _asm.GetManifestResourceNames();

        // Assets are immutable for a build and all change together on rebuild, so one module-MVID
        // ETag validates every embedded web file while still forcing revalidation on each load.
        private static readonly string AssetETag =
            "\"" + _asm.ManifestModule.ModuleVersionId.ToString("N") + "\"";

        internal static void ServeAsset(HttpListenerContext ctx, string path)
            => ServeAssetRel(ctx, path.Substring("/assets/".Length).Trim('/'));

        internal static void ServeAssetRel(HttpListenerContext ctx, string rel)
        {
            try
            {
                string? resourceName = FindResourceName(rel);
                if (resourceName == null)
                {
                    ctx.Response.StatusCode = 404;
                    return;
                }

                ctx.Response.Headers["ETag"] = AssetETag;
                ctx.Response.Headers["Cache-Control"] = "no-cache";
                if (ctx.Request.Headers["If-None-Match"] == AssetETag)
                {
                    ctx.Response.StatusCode = 304;
                    ctx.Response.ContentLength64 = 0;
                    return;
                }

                using Stream? s = _asm.GetManifestResourceStream(resourceName);
                if (s == null)
                {
                    ctx.Response.StatusCode = 404;
                    return;
                }

                byte[] body;
                using (var ms = new MemoryStream())
                {
                    s.CopyTo(ms);
                    body = ms.ToArray();
                }

                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = ContentTypeFor(rel);
                ctx.Response.ContentLength64 = body.Length;
                ctx.Response.OutputStream.Write(body, 0, body.Length);
            }
            catch { }
            finally { try { ctx.Response.Close(); } catch { } }
        }

        internal static string ContentTypeFor(string path)
        {
            int dot = path.LastIndexOf('.');
            string ext = dot >= 0 ? path.Substring(dot).ToLowerInvariant() : "";
            switch (ext)
            {
                case ".html": return "text/html; charset=utf-8";
                case ".css":  return "text/css; charset=utf-8";
                case ".js":   return "text/javascript; charset=utf-8";
                case ".json": return "application/json; charset=utf-8";
                case ".svg":  return "image/svg+xml";
                case ".woff2": return "font/woff2";
                case ".woff": return "font/woff";
                case ".png":  return "image/png";
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".txt":  return "text/plain; charset=utf-8";
                default:      return "application/octet-stream";
            }
        }

        private static string? FindResourceName(string rel)
        {
            string suffix = "." + ("web/" + rel).Replace('/', '.');
            foreach (string n in ResourceNames)
            {
                if (n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return n;
            }
            return null;
        }
    }
}

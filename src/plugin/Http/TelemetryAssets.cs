using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
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

        // Maps the stable ".web.<dotted path>" suffix straight to its resource name, built once
        // instead of linear-scanning all ~109 manifest names with EndsWith on every single request —
        // a fully-cached reload still paid that scan 24-70 times, once per asset a page loads
        // (docs/plugin-efficiency-audit.md finding 13). Anchored on ".src.web." (not just ".web.") so
        // a filename that happens to contain "web" elsewhere can't collide with the real split point;
        // every resource here is embedded from under src/web/ (NOXMFD.csproj), so that token is always
        // present exactly once.
        private static Dictionary<string, string>? _bySuffix;
        private static Dictionary<string, string> BySuffix
        {
            get
            {
                if (_bySuffix != null) return _bySuffix;
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string n in ResourceNames)
                {
                    int i = n.LastIndexOf(".src.web.", StringComparison.OrdinalIgnoreCase);
                    if (i < 0) continue;
                    map[n.Substring(i + 4)] = n;   // +4 skips ".src", leaving the ".web...." suffix
                }
                return _bySuffix = map;
            }
        }

        // Assets are immutable for a build and all change together on rebuild, so one module-MVID
        // ETag validates every embedded web file while still forcing revalidation on each load. A
        // second, distinct ETag for the gzip variant: a client's If-None-Match otherwise carries
        // whichever ETag it was last given, and if that ever matched a representation this request's
        // own Accept-Encoding can't decode, a 304 would wrongly tell it to keep using bytes it cannot
        // read (docs/plugin-efficiency-audit.md finding 14's review annex).
        private static readonly string AssetETag =
            "\"" + _asm.ManifestModule.ModuleVersionId.ToString("N") + "\"";
        private static readonly string AssetETagGzip =
            "\"" + _asm.ManifestModule.ModuleVersionId.ToString("N") + "-gz\"";

        // Decompressed bytes are re-read from the assembly (a fresh decompress, a doubling-growth
        // MemoryStream, and a ToArray copy) on every single request for content that cannot change
        // within a run — cached here instead, keyed by resource name (docs/plugin-efficiency-audit.md
        // finding 13). Holds both representations so a request never needs to gzip on the fly.
        private sealed class CachedAsset
        {
            internal readonly byte[] Raw;
            internal readonly byte[]? Gzip;   // null for content types finding 14 says never to gzip
            internal CachedAsset(byte[] raw, byte[]? gzip) { Raw = raw; Gzip = gzip; }
        }
        private static readonly ConcurrentDictionary<string, CachedAsset> _cache =
            new ConcurrentDictionary<string, CachedAsset>(StringComparer.Ordinal);

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

                if (!_cache.TryGetValue(resourceName, out CachedAsset? asset))
                {
                    using Stream? s = _asm.GetManifestResourceStream(resourceName);
                    if (s == null)
                    {
                        ctx.Response.StatusCode = 404;
                        return;
                    }
                    byte[] raw;
                    using (var ms = new MemoryStream())
                    {
                        s.CopyTo(ms);
                        raw = ms.ToArray();
                    }
                    string contentTypeForBuild = ContentTypeFor(rel);
                    byte[]? gzip = IsCompressibleContentType(contentTypeForBuild) ? Gzip(raw) : null;
                    asset = _cache.GetOrAdd(resourceName, new CachedAsset(raw, gzip));
                }

                string contentType = ContentTypeFor(rel);
                // Tells any cache between this response and the browser (there normally isn't one on
                // a LAN mod, but this is the correct thing to say regardless) that the body depends on
                // Accept-Encoding — set whenever a gzip variant exists, even on the response that
                // didn't use it, since the *resource* varies by encoding regardless of this request.
                if (asset.Gzip != null) ctx.Response.Headers["Vary"] = "Accept-Encoding";

                bool useGzip = asset.Gzip != null && AcceptsGzip(ctx.Request.Headers["Accept-Encoding"]);
                string etag = useGzip ? AssetETagGzip : AssetETag;
                ctx.Response.Headers["ETag"] = etag;
                ctx.Response.Headers["Cache-Control"] = "no-cache";
                if (ctx.Request.Headers["If-None-Match"] == etag)
                {
                    ctx.Response.StatusCode = 304;
                    ctx.Response.ContentLength64 = 0;
                    return;
                }

                byte[] body = useGzip ? asset.Gzip! : asset.Raw;
                if (useGzip) ctx.Response.Headers["Content-Encoding"] = "gzip";
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = contentType;
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

        // text/*, application/json, or SVG (docs/plugin-efficiency-audit.md finding 14) — never PNG,
        // woff/woff2, or anything else already binary-compressed, where gzip would only add overhead.
        internal static bool IsCompressibleContentType(string contentType) =>
            contentType.StartsWith("text/", StringComparison.Ordinal) ||
            contentType.StartsWith("application/json", StringComparison.Ordinal) ||
            contentType == "image/svg+xml";

        // Full token parse rather than a substring search — a plain Contains would treat "gzip;q=0"
        // (explicitly refused) as accepted, and would also match an unrelated coding that merely
        // contains the word "gzip" (e.g. a hypothetical "x-gzip-experimental").
        internal static bool AcceptsGzip(string? acceptEncodingHeader)
        {
            if (string.IsNullOrEmpty(acceptEncodingHeader)) return false;
            foreach (string rawToken in acceptEncodingHeader.Split(','))
            {
                string token = rawToken.Trim();
                int semi = token.IndexOf(';');
                string coding = (semi < 0 ? token : token.Substring(0, semi)).Trim();
                if (!string.Equals(coding, "gzip", StringComparison.OrdinalIgnoreCase)) continue;
                if (semi < 0) return true;   // "gzip" with no qvalue
                string param = token.Substring(semi + 1).Trim();
                if (param.StartsWith("q=", StringComparison.OrdinalIgnoreCase) &&
                    double.TryParse(param.Substring(2), NumberStyles.Float, CultureInfo.InvariantCulture, out double q))
                    return q > 0;
                return true;   // a parameter other than q= doesn't refuse it
            }
            return false;
        }

        private static byte[] Gzip(byte[] raw)
        {
            using var ms = new MemoryStream();
            using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
                gz.Write(raw, 0, raw.Length);
            return ms.ToArray();
        }

        private static string? FindResourceName(string rel)
        {
            string suffix = "." + ("web/" + rel).Replace('/', '.');
            return BySuffix.TryGetValue(suffix, out string? name) ? name : null;
        }
    }
}

using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace NOXMFD
{
    // Async sprite → PNG/JPEG capture (docs/performance.md item #A).
    //
    // Capturing a sprite means Blit → read the pixels back off the GPU → encode. Done
    // synchronously on the Unity main thread that stalls the frame hard (~670 ms on map load,
    // plus recurring 17–78 ms stutters whenever a new unit/aircraft type first appears in a
    // busy match), so the readback and encode are kept off the critical path:
    //
    // Pipeline:
    //   1. (main) Blit the sprite's atlas sub-rect into a temp RT (optionally downscaled).
    //   2. (main) AsyncGPUReadback — no pipeline stall; the callback fires 1–3 frames later.
    //   3. (callback, main) copy the raw bytes out, then offload to a background Task.
    //   4. (background) optional alpha synth + ImageConversion.EncodeArray* → onEncoded(bytes).
    //
    // So neither the GPU readback wait nor the encode runs on the main-thread critical path.
    // onEncoded is invoked on a BACKGROUND thread (TelemetryServer's setters are lock-guarded,
    // so registering there is safe); it receives null on any failure so callers can register a
    // fallback. Falls back to a fully synchronous path on the rare GPU without async readback.
    internal static class SpriteCapture
    {
        internal enum Encoding { Png, Jpg }

        // Same RGBA8 sRGB format on the RT, the readback, and the encode so no conversion is
        // needed and the byte order handed to EncodeArray* is exactly R,G,B,A.
        private const GraphicsFormat Fmt = GraphicsFormat.R8G8B8A8_SRGB;

        // Kick off a capture. maxDim>0 caps the longer side (downscale); 0 = native size.
        // quality is JPEG 0–100 (ignored for PNG). Returns true if a capture was started/done.
        public static bool Request(Sprite sprite, Encoding enc, bool synthAlpha, int quality,
                                   int maxDim, Action<byte[]?> onEncoded)
        {
            if (sprite == null || sprite.texture == null) return false;

            Texture2D tex = sprite.texture;
            Rect r = sprite.textureRect;
            int rw = Mathf.RoundToInt(r.width);
            int rh = Mathf.RoundToInt(r.height);
            if (rw <= 0 || rh <= 0) { rw = tex.width; rh = tex.height; r = new Rect(0f, 0f, rw, rh); }

            int tw = rw, th = rh;
            if (maxDim > 0)
            {
                int maxSide = Mathf.Max(rw, rh);
                if (maxSide > maxDim)
                {
                    float k = (float)maxDim / maxSide;
                    tw = Mathf.Max(1, Mathf.RoundToInt(rw * k));
                    th = Mathf.Max(1, Mathf.RoundToInt(rh * k));
                }
            }

            // Atlas-safe sub-rect blit: scale/offset are in normalized source-texture coords, so
            // we copy exactly the sprite's region out of the shared atlas texture.
            Vector2 scale  = new Vector2(r.width / tex.width, r.height / tex.height);
            Vector2 offset = new Vector2(r.x / tex.width,     r.y / tex.height);

            if (SystemInfo.supportsAsyncGPUReadback)
            {
                var rt = new RenderTexture(tw, th, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rt.Create();
                Graphics.Blit(tex, rt, scale, offset);
                AsyncGPUReadback.Request(rt, 0, Fmt,
                    req => OnReadback(req, rt, tw, th, enc, synthAlpha, quality, onEncoded));
                return true;
            }

            // Synchronous fallback (GPU without async readback): same result, on the main thread.
            return RequestSync(tex, scale, offset, tw, th, enc, synthAlpha, quality, onEncoded);
        }

        private static void OnReadback(AsyncGPUReadbackRequest req, RenderTexture rt, int w, int h,
                                       Encoding enc, bool synthAlpha, int quality, Action<byte[]?> onEncoded)
        {
            try
            {
                if (req.hasError) { onEncoded(null); return; }

                // The NativeArray is only valid during this callback — copy out before scheduling.
                var data = req.GetData<byte>();
                byte[] bytes = new byte[data.Length];
                data.CopyTo(bytes);

                Task.Run(() => EncodeAndDeliver(bytes, w, h, enc, synthAlpha, quality, onEncoded));
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"[NOXMFD] SpriteCapture readback failed: {ex.Message}");
                onEncoded(null);
            }
            finally
            {
                rt.Release();
                UnityEngine.Object.Destroy(rt);
            }
        }

        private static bool RequestSync(Texture2D tex, Vector2 scale, Vector2 offset, int w, int h,
                                        Encoding enc, bool synthAlpha, int quality, Action<byte[]?> onEncoded)
        {
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            try
            {
                Graphics.Blit(tex, rt, scale, offset);
                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = rt;
                var readable = new Texture2D(w, h, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0f, 0f, w, h), 0, 0);
                readable.Apply();
                RenderTexture.active = prev;

                byte[] bytes = readable.GetRawTextureData();
                UnityEngine.Object.Destroy(readable);
                EncodeAndDeliver(bytes, w, h, enc, synthAlpha, quality, onEncoded);
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"[NOXMFD] SpriteCapture (sync) failed: {ex.Message}");
                onEncoded(null);
                return false;
            }
            finally { RenderTexture.ReleaseTemporary(rt); }
        }

        // Pure-CPU: safe on a background thread. ImageConversion.EncodeArray* operate on raw
        // pixel data (no UnityEngine.Object access), so they don't need the main thread.
        private static void EncodeAndDeliver(byte[] rgba, int w, int h, Encoding enc,
                                             bool synthAlpha, int quality, Action<byte[]?> onEncoded)
        {
            try
            {
                if (synthAlpha) SynthesizeAlphaIfOpaque(rgba);
                byte[] outp = enc == Encoding.Jpg
                    ? ImageConversion.EncodeArrayToJPG(rgba, Fmt, (uint)w, (uint)h, 0, quality)
                    : ImageConversion.EncodeArrayToPNG(rgba, Fmt, (uint)w, (uint)h, 0);
                onEncoded(outp);
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"[NOXMFD] SpriteCapture encode failed: {ex.Message}");
                onEncoded(null);
            }
        }

        // Icons stored without alpha (DXT1/RGB24) read as fully opaque, so a straight tint gives
        // a solid square. The game draws them light-on-dark, so when an icon comes back with no
        // transparency we derive alpha from luminance. Operates on RGBA bytes (R,G,B,A order).
        private static void SynthesizeAlphaIfOpaque(byte[] px)
        {
            byte minA = 255;
            for (int i = 3; i < px.Length; i += 4)
                if (px[i] < minA) { minA = px[i]; if (minA == 0) return; }  // real transparency — leave it
            if (minA < 250) return;

            for (int i = 0; i < px.Length; i += 4)
            {
                int r = px[i], g = px[i + 1], b = px[i + 2];
                px[i + 3] = (byte)((r * 77 + g * 150 + b * 29) >> 8); // ~Rec.601 luminance
            }
        }
    }
}

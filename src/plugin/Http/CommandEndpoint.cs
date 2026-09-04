using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace NOXMFD
{
    internal static class CommandEndpoint
    {
        // The web client POSTs JSON commands to /command (e.g. tap-to-target, TGT deselect).
        // HttpListener dispatches this on a threadpool thread, where touching Unity/game state is
        // illegal — so we only parse + validate + enqueue here. CommandDispatcher drains the queue
        // on the Unity main thread and invokes the actual game-side handlers.
        private const int MaxQueuedCommands = 64;
        private static readonly Queue<CommandEnvelope> _cmdQueue = new Queue<CommandEnvelope>();
        private static readonly object                 _cmdLock  = new object();

        // A command envelope is a few hundred bytes at most; 16 KB leaves headroom without letting
        // a single request allocate an arbitrarily large string. Checks Content-Length first, but
        // also caps the actual bytes read when the length is unknown.
        private const int MaxCommandBodyBytes = 16 * 1024;

        internal static void HandleCommand(HttpListenerContext ctx)
        {
            try
            {
                if (ctx.Request.HttpMethod != "POST")
                {
                    ctx.Response.StatusCode = 405;
                    ctx.Response.Close();
                    return;
                }
                if (!TryRequireJsonContentType(ctx)) return;
                if (!TryReadBoundedBody(ctx, out string body)) return;

                CommandEnvelope? env = null;
                try { env = UnityEngine.JsonUtility.FromJson<CommandEnvelope>(body); }
                catch { /* malformed JSON -> handled below as 400 */ }

                if (env == null || string.IsNullOrEmpty(env.cmd))
                {
                    Plugin.Log?.LogInfo($"[NOXMFD] /command: malformed or missing cmd — body: {Truncate(body, 200)}");
                    ctx.Response.StatusCode = 400;
                }
                else if (!CommandDispatcher.IsKnown(env.cmd))
                {
                    Plugin.Log?.LogInfo($"[NOXMFD] /command: unknown cmd '{env.cmd}' — 422.");
                    ctx.Response.StatusCode = 422;
                }
                else
                {
                    bool queued = false;
                    lock (_cmdLock)
                    {
                        if (_cmdQueue.Count < MaxQueuedCommands) { _cmdQueue.Enqueue(env); queued = true; }
                    }
                    if (!queued) Plugin.Log?.LogDebug("[NOXMFD] command queue full — dropped.");
                    ctx.Response.StatusCode = 204;
                }
                ctx.Response.Close();
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogDebug($"[NOXMFD] /command error: {ex.Message}");
                try { ctx.Response.Abort(); } catch { /* client gone */ }
            }
        }

        // Drained by the Unity main thread (CommandDispatcher) once per frame. False when empty.
        internal static bool TryDequeueCommand(out CommandEnvelope? env)
        {
            lock (_cmdLock)
            {
                if (_cmdQueue.Count > 0) { env = _cmdQueue.Dequeue(); return true; }
            }
            env = null;
            return false;
        }

        // Same accepted-fire-and-forget shape as HandleCommand above. Extension command payloads are
        // deliberately opaque to NOXMFD core, but the endpoint is still a JSON command endpoint.
        internal static void HandleExtensionCommand(HttpListenerContext ctx, string id)
        {
            try
            {
                if (!TryRequireJsonContentType(ctx)) return;
                if (!TryReadBoundedBody(ctx, out string body)) return;

                if (!ExtensionRegistry.TryEnqueueCommand(id, body))
                    Plugin.Log?.LogDebug($"[NOXMFD] extension '{id}' command queue full — dropped.");
                ctx.Response.StatusCode = 204;
                ctx.Response.Close();
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogDebug($"[NOXMFD] /ext/{id}/command error: {ex.Message}");
                try { ctx.Response.Abort(); } catch { }
            }
        }

        private static bool TryRequireJsonContentType(HttpListenerContext ctx)
        {
            if (CommandContentType.IsJson(ctx.Request.ContentType))
                return true;

            Plugin.Log?.LogInfo($"[NOXMFD] /command: rejected Content-Type '{ctx.Request.ContentType}' — 415.");
            ctx.Response.StatusCode = 415;
            ctx.Response.Close();
            return false;
        }

        private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max) + "…";

        private static bool TryReadBoundedBody(HttpListenerContext ctx, out string body)
        {
            body = string.Empty;
            if (ctx.Request.ContentLength64 > MaxCommandBodyBytes)
            {
                Plugin.Log?.LogInfo($"[NOXMFD] /command: body too large (Content-Length {ctx.Request.ContentLength64}) — 413.");
                ctx.Response.StatusCode = 413;
                ctx.Response.Close();
                return false;
            }

            using var ms = new MemoryStream();
            var buffer = new byte[4096];
            Stream input = ctx.Request.InputStream;
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (ms.Length + read > MaxCommandBodyBytes)
                {
                    Plugin.Log?.LogInfo("[NOXMFD] /command: body exceeded size cap while streaming — 413.");
                    ctx.Response.StatusCode = 413;
                    ctx.Response.Close();
                    return false;
                }
                ms.Write(buffer, 0, read);
            }
            body = Encoding.UTF8.GetString(ms.ToArray());
            return true;
        }
    }
}

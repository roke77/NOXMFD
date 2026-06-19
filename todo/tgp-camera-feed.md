# TGP camera feed — feasibility investigation

## Goal

Find out whether we can extract a live camera feed from Nuclear Option
that frames the player's currently locked target, similar to a real TGP
(Targeting Pod) display, and stream it to the MFD's TGP page over the
existing HTTP server.

End state if it works: the MFD's TGP page shows a small live video tile
of the locked unit instead of being a placeholder.

## What we already know

- The aircraft renders its main camera through Unity's standard
  `Camera`/render-target pipeline.
- We already have public access to the player's `WeaponManager`,
  `GetTargetList()` and `CheckIsTarget(Unit)` — so the *what* (which unit
  to point at) is solved.
- We have no decompiled file referencing "TGP", "Pod", "camera follow",
  or similar — see `decompiled/` (none of `cam`, `tgp`, `pod`, `target`,
  `view`, `render` match a filename). The game may not ship a dedicated
  TGP camera object; we may need to build one.
- The mod already serves PNGs over `/icon`, `/weapon`, `/cm` by reading
  Unity `Texture2D` → `EncodeToPNG`. Same primitive works for camera RTs.

## Open questions (in priority order)

1. **Is there an existing camera we can borrow?** Look for any
   secondary `Camera` in the player aircraft prefab — gun camera, missile
   seeker camera, external camera. Dump the active scene at runtime
   (`Resources.FindObjectsOfTypeAll<Camera>()`) and log each camera's
   name, target texture, culling mask, position. If one of them is
   already aimed at the target, we're 90% done — we just steal its
   render target.
2. **If no existing camera, can we add one?** Spawn a new `Camera`
   GameObject attached to the player, with a small `RenderTexture` (say
   256×256). Update its transform every frame to look at the locked
   target's world position. Performance cost = one extra camera pass per
   frame at low resolution — should be tolerable.
3. **Encoding throughput.** At what frame rate can we PNG-encode and
   serve? PNG is heavy; for a true video feel we likely want JPEG or
   MJPEG. Check whether the game ships a JPEG encoder (Unity's
   `EncodeToJPG` is built-in but may need `using UnityEngine`-side
   reference we don't have).
4. **Transport.** MJPEG over a `multipart/x-mixed-replace` HTTP stream
   is the simplest path — every modern browser renders it in an `<img>`
   tag with zero JS. Alternative: WebSocket + raw JPEG frames (more
   code, no advantage for our use). Decide based on (3).
5. **What does the target look like at distance?** A 5 km out F-18 in a
   256-px tile is one or two pixels. We probably need a zoom factor
   (narrow FOV) on the dedicated camera. Confirm we can set
   `Camera.fieldOfView` dynamically.
6. **Occlusion / culling.** Friendly aircraft cockpit interior, own
   weapons, smoke trails — what does the camera include? May need a
   custom culling mask.

## Plan of attack

1. Add a one-shot dev log dump in TelemetryReader: enumerate all active
   cameras and write name + target texture + parent transform to the
   BepInEx log. Look for any TGP-like camera the game already maintains.
2. If found: bind to it, expose its `targetTexture` over `/tgp.mjpg`.
3. If not found: prototype a synthetic camera attached to the player
   aircraft with a `LookAt(target.transform)` per frame and a narrow
   FOV. 256×256 render texture, 10–15 fps MJPEG.
4. Wire the MFD's TGP page to `<img src="/tgp.mjpg">` (or fall back to a
   "NO TGP" placeholder if the feed isn't available).

## Things to decide

- Default resolution and frame rate (CPU/bandwidth budget). Start small
  (256×256 @ 10 fps) and tune up.
- Whether to gate the feed on having a real target — easier UX, also
  cheaper.
- Whether to render the feed in greyscale + crosshair overlay for the
  TGP-style look, or leave it raw.

## Risks

- Performance: a second camera + a JPEG encoder running every frame
  could measurably hit framerate. Acceptable budget is whatever doesn't
  show up in the player's FPS counter — test on a low-end run too.
- Game updates may rename or move what we hook. We've been resilient so
  far by using public APIs and small reflection touches; this is no
  different.
- If the game uses URP/HDRP custom passes and the camera needs special
  treatment to render correctly, the dev log dump in step 1 will tell us.

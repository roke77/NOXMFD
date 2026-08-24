using System;
using System.Collections.Generic;
using NuclearOption.Networking;
using UnityEngine;

namespace NOXMFD
{
    // TGP text/status overlay data, peeled out of TgpFeed — mirrors TargetScreenUI.UpdateTargetInfo
    // (the game's in-cockpit TGP overlay) field for field, using the same public TargetCam accessors
    // and the same Unit/FactionHQ state it reads. Native mode gets this baked into the video for
    // free (the game's own stacked-camera UICam); HQ mode (TgpMirrorCam) has no such camera, so
    // TgpFeed derives it here every capture tick instead, and the client renders it (tgp.js's
    // applyOverlay) — see the class-level comment on Tgp* in TelemetrySnapshot.cs for the full
    // picture. Owned by TgpFeed (TgpFeed.Overlay); has no capture/GPU concerns of its own.
    internal sealed class TgpOverlay
    {
        // Defaults are the "no target" values TargetScreenUI itself falls back to.
        public float  Mag          { get; private set; }
        public float  RangeM       { get; private set; }
        public string Grid         { get; private set; } = "";
        public bool   IR           { get; private set; }
        public float  BearingDeg   { get; private set; }
        public int    TargetCount  { get; private set; }
        public string TargetType   { get; private set; } = "";
        public string Pilot        { get; private set; } = "";
        public string Status       { get; private set; } = "normal";
        public bool   HasDetail    { get; private set; }
        public float  HeadingDeg   { get; private set; }
        public float  AltitudeM    { get; private set; }
        public float  RelAltitudeM { get; private set; }
        public float  SpeedMps     { get; private set; }
        public float  RelSpeedMps  { get; private set; }
        public TgpBoxInfo[] Boxes  { get; private set; } = Array.Empty<TgpBoxInfo>();

        public void Populate(TargetCam tc, List<Unit>? targets, Aircraft player, Func<Vector3, Vector3> project)
        {
            if (targets == null || targets.Count == 0) { Clear(); return; }

            Mag        = tc.GetMag();
            RangeM     = tc.GetDist();
            Grid       = tc.GetGrid();
            IR         = tc.UsingIR();
            Transform? mount = tc.GetCamMount();
            BearingDeg = mount != null ? mount.localEulerAngles.y : 0f;
            TargetCount = targets.Count;

            FactionHQ? hq = player.NetworkHQ;
            Unit primary = targets[0];
            bool isAircraftOrMissile = primary is Aircraft || primary is Missile;

            if (targets.Count > 1)
            {
                TargetType = $"{targets.Count} targets";
                HasDetail  = false;
            }
            else
            {
                TargetType = primary is Aircraft ? primary.definition.unitName : primary.unitName;
                HasDetail  = isAircraftOrMissile && hq != null && hq.IsTargetPositionAccurate(primary, 20f);
            }

            Pilot = "";
            if (isAircraftOrMissile && primary is Aircraft pilotedAc && pilotedAc.pilots.Length > 0
                && pilotedAc.pilots[0].player != null)
            {
                Pilot = pilotedAc.pilots[0].player.GetDisplayName(PlayerNameContext.Other);
            }

            if (HasDetail)
            {
                GlobalPosition targetPos = primary.GlobalPosition();
                Vector3 rel = targetPos - player.GlobalPosition();
                HeadingDeg   = primary.transform.eulerAngles.y;
                AltitudeM    = targetPos.y;
                RelAltitudeM = rel.y;
                SpeedMps     = primary.speed;
                RelSpeedMps  = Vector3.Dot(player.rb.velocity, rel.normalized) - Vector3.Dot(primary.rb.velocity, rel.normalized);
            }
            else
            {
                HeadingDeg = AltitudeM = RelAltitudeM = SpeedMps = RelSpeedMps = 0f;
            }

            Status = TargetStatus(primary, hq);

            // Lock box per target — TargetScreenUI.LateUpdate's own reference calculation
            // (targetBoxes[i].transform.localPosition = cam.WorldToScreenPoint(knownPosition.
            // ToLocalPosition())), adapted to WorldToViewportPoint for a resolution-independent
            // client-side position (see TgpBoxInfo's doc comment in TelemetrySnapshot.cs).
            var boxes = new TgpBoxInfo[targets.Count];
            for (int i = 0; i < targets.Count; i++)
            {
                Unit u = targets[i];
                string status = i == 0 ? Status : TargetStatus(u, hq);
                if (hq == null || !hq.TryGetKnownPosition(u, out GlobalPosition known))
                {
                    boxes[i] = new TgpBoxInfo { Visible = false, Status = status };
                    continue;
                }
                Vector3 vp = project(known.ToLocalPosition());
                boxes[i] = new TgpBoxInfo { X = vp.x, Y = vp.y, Visible = vp.z > 0f, Status = status };
            }
            Boxes = boxes;
        }

        // TgpFeed.CaptureFrame()'s early-return guards (no aircraft, no TGP component, reflection
        // failed, cam disabled/timed out) all stop the video frame the same way — call this from
        // every one of them so TargetCount/Boxes don't linger at their last successful-tick values,
        // which would otherwise keep the HQ overlay (driven by these, not by TgpFeed.Active) showing
        // a stale lock after the feed itself had already gone dark.
        public void Clear()
        {
            TargetCount = 0;
            Boxes       = Array.Empty<TgpBoxInfo>();
        }

        // Mirrors the sprite-selection logic in TargetScreenUI.UpdateTargetInfo's targetBoxes loop.
        private static string TargetStatus(Unit u, FactionHQ? hq)
        {
            if (hq == null) return "normal";
            if (u.NetworkHQ == hq) return "friendly";
            string status = "normal";
            if (u.HasRadarEmission() && u.radar is Radar radar && radar.IsJammed()) status = "jammed";
            if (hq.IsTargetLased(u)) status = "lased";
            if (!hq.IsTargetPositionAccurate(u, 20f)) status = "outdated";
            return status;
        }
    }
}

using System.Globalization;
using System.Text;

namespace NOXMFD
{
    // Telemetry-frame JSON serialization. Pure string-building over TelemetrySnapshot and its
    // DTOs — no Unity/BepInEx references, no live server/session state — which lets tools/tests/
    // compile and cover it directly. Callers pass in the handful of values that live outside the
    // snapshot (SOI focus, mod-global immersion state, extension slices) rather than this class
    // reaching for TelemetryServer/ImmersionState/ExtensionRegistry itself.
    internal static class TelemetryJson
    {
        internal static string Serialize(TelemetrySnapshot s, string soiJson, bool masterArmsOn, string combatModeLabel, string extSlicesJson)
        {
            var sb = new StringBuilder(2048);
            AppendFrameHeader(sb, s, soiJson, masterArmsOn, combatModeLabel);
            AppendFramePayload(sb, s, extSlicesJson);
            return sb.Append('}').ToString();
        }

        private static void AppendFrameHeader(StringBuilder sb, TelemetrySnapshot s, string soiJson, bool masterArmsOn, string combatModeLabel)
        {
            sb.AppendFormat(CultureInfo.InvariantCulture,
                "{{\"ping\":false,\"t\":{0:0.000},\"name\":\"{1}\"," +
                "\"mission\":\"{2}\",\"mapName\":\"{3}\"," +
                "\"world\":{{\"x\":{4:0.0},\"y\":{5:0.0},\"z\":{6:0.0}}}," +
                "\"hdg\":{7:0.0},\"tas\":{8:0.0},\"agl\":{9:0.0},\"gear\":\"{10}\"," +
                "\"units\":{11},\"aircraft\":{12}," +
                "\"map\":{{\"valid\":{13},\"w\":{14:0.0},\"h\":{15:0.0},\"ox\":{16},\"oy\":{17}}}," +
                "\"iconOrient\":{18},\"iconScale\":{19:0.000}," +
                "\"flares\":{20},\"flaresMax\":{21},\"ewKJ\":{22:0.0},\"ewKJMax\":{23:0.0}," +
                "\"selWeapon\":\"{24}\",\"cmCat\":{25},\"tgpActive\":{26}," +
                "\"fuel\":{27:0.000},\"thr\":{28:0.000},\"hasAb\":{29},\"abStart\":{30:0.000}," +
                "\"softGun\":\"{31}\",\"softRel\":\"{32}\",\"masterArmsOn\":{33},\"combatMode\":\"{34}\",",
                s.Time,
                JsonLite.EscapeJson(s.PlaneName ?? string.Empty),
                JsonLite.EscapeJson(s.MissionName ?? string.Empty),
                JsonLite.EscapeJson(s.MapName ?? string.Empty),
                s.WorldX, s.WorldY, s.WorldZ,
                s.Heading, s.TAS, s.AGL,
                s.GearDown ? "down" : "up",
                s.TotalUnits, s.TotalAircraft,
                JsonBool(s.MapValid),
                s.MapW, s.MapH,
                s.GridOffsetX, s.GridOffsetY,
                JsonBool(s.IconOrient),
                s.IconScale,
                s.Flares, s.FlaresMax, s.EwKJ, s.EwKJMax,
                JsonLite.EscapeJson(s.SelWeapon ?? string.Empty), s.CmCategory,
                JsonBool(s.TgpActive),
                s.Fuel, s.Throttle,
                JsonBool(s.HasAfterburner), s.AbStart,
                JsonLite.EscapeJson(s.SoftGun ?? string.Empty),
                JsonLite.EscapeJson(s.SoftRel ?? string.Empty),
                JsonBool(masterArmsOn),
                JsonLite.EscapeJson(combatModeLabel ?? string.Empty));
            sb.Append("\"tgpQuality\":\"").Append(JsonLite.EscapeJson(s.TgpQuality ?? "native")).Append("\",");
            sb.Append(soiJson).Append(',');
        }

        private static void AppendFramePayload(StringBuilder sb, TelemetrySnapshot s, string extSlicesJson)
        {
            sb.Append("\"loadout\":").Append(LoadoutArray(s.Loadout))
              .Append(",\"colors\":{\"f\":\"").Append(JsonLite.EscapeJson(s.ColFriendly ?? "#39ff14"))
              .Append("\",\"e\":\"").Append(JsonLite.EscapeJson(s.ColHostile ?? "#ff4040"))
              .Append("\",\"n\":\"").Append(JsonLite.EscapeJson(s.ColNeutral ?? "#9aa0a6")).Append("\"}")
              .Append(",\"contacts\":").Append(UnitsArray(s.Units))
              .Append(",\"playerId\":").Append(s.PlayerId)
              .Append(",\"pjm\":").Append(JsonBool(s.PlayerJammed))
              .Append(",\"pjb\":").Append(s.PlayerJammedBy)
              .Append(",\"parts\":").Append(PartsArray(s.Parts))
              .Append(",\"pylons\":").Append(PylonsArray(s.Pylons))
              .Append(",\"rwr\":").Append(RwrArray(s.Rwr))
              .Append(",\"mw\":").Append(MwArray(s.Mw))
              .Append(",\"rdr\":").Append(RdrBlock(s))
              .Append(",\"tgp\":").Append(TgpBlock(s))
              .Append(",\"radar\":").Append(JsonBool(s.RadarOn))
              .Append(",\"guns\":").Append(JsonBool(s.GunsLinked))
              .Append(",\"ign\":").Append(JsonBool(s.Ignition))
              .Append(",\"assist\":").Append(JsonBool(s.FlightAssist))
              .Append(",\"turret\":").Append(JsonBool(s.TurretAuto))
              .Append(",\"nvg\":").Append(JsonBool(s.NightVision))
              .Append(",\"navlt\":").Append(JsonBool(s.NavLightsOn))
              .Append(",\"heat\":").Append(s.Heat.ToString("0.000", CultureInfo.InvariantCulture))
              .Append(",\"heatColor\":\"").Append(JsonLite.EscapeJson(s.HeatColor ?? "#39ff14")).Append('"')
              .Append(",\"rpm\":").Append(s.Rpm.ToString("0.000", CultureInfo.InvariantCulture))
              .Append(",\"failures\":").Append(StringArray(s.Failures))
              .Append(",\"tgt\":").Append(TgtBlock(s))
              .Append(",\"bdf\":").Append(BdfBlock(s))
              .Append(",\"pal\":").Append(PalBlock(s))
              .Append(",\"mis\":").Append(MisBlock(s))
              .Append(",\"obj\":").Append(ObjBlock(s))
              .Append(",\"akf\":").Append(AkfBlock(s))
              .Append(",\"ext\":").Append(extSlicesJson);
        }

        private static string JsonBool(bool value) => value ? "true" : "false";

        // AKF advanced kill feed (docs/akf-page.md). Always present while a mission runs (no "faction
        // has no HQ yet" gate like MIS/OBJ — an empty session just reads as all-zero). Kills are
        // scoped to the local player's own kills; all is everyone's, matching the game's own feed.
        // rank is the player's persistent Player.PlayerRank, not session-scoped.
        private static string AkfBlock(TelemetrySnapshot s)
        {
            return "{\"all\":" + AkfArray(s.AkfAll) + ",\"player\":" + AkfArray(s.AkfPlayer)
                + string.Format(CultureInfo.InvariantCulture,
                    ",\"kills\":{{\"aircraft\":{0},\"ship\":{1},\"vehicle\":{2},\"building\":{3}}}" +
                    ",\"rank\":{4},\"fundsGained\":{5:0.0},\"fundsSpent\":{6:0.0}}}",
                    s.AkfKillsAircraft, s.AkfKillsShip, s.AkfKillsVehicle, s.AkfKillsBuilding,
                    s.AkfRank, s.AkfFundsGained, s.AkfFundsSpent);
        }

        private static string AkfArray(AkfKillEntry[]? items)
        {
            if (items == null || items.Length == 0) return "[]";
            var sb = new StringBuilder("[");
            for (int i = 0; i < items.Length; i++)
            {
                if (i > 0) sb.Append(',');
                AkfKillEntry e = items[i];
                sb.Append('{');
                if (e.Attacker != null)
                    sb.Append("\"a\":\"").Append(JsonLite.EscapeJson(e.Attacker)).Append("\",\"h\":").Append(e.AttackerHostile ? "true" : "false").Append(',');
                sb.Append("\"v\":\"").Append(JsonLite.EscapeJson(e.Victim)).Append("\",\"vh\":").Append(e.VictimHostile ? "true" : "false")
                  .Append(",\"verb\":\"").Append(JsonLite.EscapeJson(e.Verb)).Append('"');
                if (e.Weapon != null)
                    sb.Append(",\"w\":\"").Append(JsonLite.EscapeJson(e.Weapon)).Append('"');
                if (e.PlayerIsVictim)
                    sb.Append(",\"pv\":true");
                sb.Append('}');
            }
            return sb.Append(']').ToString();
        }

        // MIS mission-info panel (docs/md-pages.md). {present:false} in multiplayer or between
        // missions. level: 0 Conventional, 1 Tactical, 2 Strategic (TelemetryReader.BuildMis).
        private static string MisBlock(TelemetrySnapshot s)
        {
            if (!s.MisPresent) return "{\"present\":false}";
            return string.Format(CultureInfo.InvariantCulture,
                "{{\"present\":true,\"name\":\"{0}\",\"description\":\"{1}\",\"tod\":{2:0.000},\"duration\":{3:0.0},\"score\":{4:0.0},\"level\":{5}}}",
                JsonLite.EscapeJson(s.MissionName ?? string.Empty), JsonLite.EscapeJson(s.MisDescription ?? string.Empty),
                s.MisTimeOfDay, s.MisDuration, s.MisScore, s.MisLevel);
        }

        // OBJ active-objectives list (docs/md-pages.md). {present:false} when the player faction's
        // HQ isn't resolved yet.
        private static string ObjBlock(TelemetrySnapshot s)
        {
            if (!s.ObjPresent) return "{\"present\":false}";
            return "{\"present\":true,\"items\":" + ObjArray(s.Obj) + "}";
        }

        private static string ObjArray(ObjEntry[]? items)
        {
            if (items == null || items.Length == 0) return "[]";
            var sb = new StringBuilder("[");
            for (int i = 0; i < items.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(string.Format(CultureInfo.InvariantCulture,
                    "{{\"n\":\"{0}\",\"s\":{1},\"p\":{2:0.000},\"pos\":{3}}}",
                    JsonLite.EscapeJson(items[i].Name ?? string.Empty), items[i].Status, items[i].Percent,
                    ObjPositionArray(items[i].Positions)));
            }
            return sb.Append(']').ToString();
        }

        // Position sub-rows under one objective (ObjectiveInfoList_Item — "DestroyUnits / Lb105 /
        // 18km"). x/z are true world coords; the page derives the grid label and live distance itself.
        private static string ObjPositionArray(ObjPosition[]? items)
        {
            if (items == null || items.Length == 0) return "[]";
            var sb = new StringBuilder("[");
            for (int i = 0; i < items.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(string.Format(CultureInfo.InvariantCulture,
                    "{{\"n\":\"{0}\",\"x\":{1:0.0},\"z\":{2:0.0}}}",
                    JsonLite.EscapeJson(items[i].Name ?? string.Empty), items[i].X, items[i].Z));
            }
            return sb.Append(']').ToString();
        }

        // TGT filter panel state (docs/tgt-page.md). {present:false} when the game's TargetListSelector
        // isn't up; otherwise the three toggle groups (ordered as the tgt.* commands index them) plus
        // the two standalone toggles.
        private static string TgtBlock(TelemetrySnapshot s)
        {
            if (!s.TgtPresent) return "{\"present\":false}";
            return "{\"present\":true"
                 + ",\"laser\":" + (s.TgtLaser ? "true" : "false")
                 + ",\"hud\":"   + (s.TgtHud   ? "true" : "false")
                 + ",\"faction\":"  + TgtToggleArray(s.TgtFaction)
                 + ",\"category\":" + TgtToggleArray(s.TgtCategory)
                 + ",\"vehicle\":"  + TgtToggleArray(s.TgtVehicle)
                 + "}";
        }

        private static string TgtToggleArray(TgtToggleInfo[]? items)
        {
            if (items == null || items.Length == 0) return "[]";
            var sb = new StringBuilder("[");
            for (int i = 0; i < items.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"n\":\"").Append(JsonLite.EscapeJson(items[i].Name ?? string.Empty))
                  .Append("\",\"on\":").Append(items[i].On ? "true" : "false").Append('}');
            }
            return sb.Append(']').ToString();
        }

        // BDF faction-forces panel (docs/bdf-page.md) — always BOSCALI, a fixed identity.
        // {present:false} when Boscali has no FactionHQ yet; otherwise the header scalars plus the
        // four breakdown rows.
        private static string BdfBlock(TelemetrySnapshot s)
        {
            if (!s.BdfPresent) return "{\"present\":false}";
            return string.Format(CultureInfo.InvariantCulture,
                "{{\"present\":true,\"faction\":\"{0}\",\"funds\":{1:0.000},\"score\":{2:0.0},\"warheads\":{3},",
                JsonLite.EscapeJson(s.BdfFaction ?? string.Empty), s.BdfFunds, s.BdfScore, s.BdfWarheads)
                + "\"ships\":"     + BdfCountArray(s.BdfShips)
                + ",\"vehicles\":"  + BdfCountArray(s.BdfVehicles)
                + ",\"buildings\":" + BdfCountArray(s.BdfBuildings)
                + ",\"aircraft\":"  + BdfCountArray(s.BdfAircraft)
                + "}";
        }

        // PAL — the same faction-forces panel as BDF, always PRIMEVA (a fixed identity, like BDF is
        // always BOSCALI — docs/bdf-page.md). {present:false} when Primeva has no FactionHQ yet.
        private static string PalBlock(TelemetrySnapshot s)
        {
            if (!s.PalPresent) return "{\"present\":false}";
            return string.Format(CultureInfo.InvariantCulture,
                "{{\"present\":true,\"faction\":\"{0}\",\"funds\":{1:0.000},\"score\":{2:0.0},\"warheads\":{3},",
                JsonLite.EscapeJson(s.PalFaction ?? string.Empty), s.PalFunds, s.PalScore, s.PalWarheads)
                + "\"ships\":"     + BdfCountArray(s.PalShips)
                + ",\"vehicles\":"  + BdfCountArray(s.PalVehicles)
                + ",\"buildings\":" + BdfCountArray(s.PalBuildings)
                + ",\"aircraft\":"  + BdfCountArray(s.PalAircraft)
                + "}";
        }

        private static string BdfCountArray(BdfCountInfo[]? items)
        {
            if (items == null || items.Length == 0) return "[]";
            var sb = new StringBuilder("[");
            for (int i = 0; i < items.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"n\":\"").Append(JsonLite.EscapeJson(items[i].Name ?? string.Empty))
                  .Append("\",\"c\":").Append(items[i].Count).Append('}');
            }
            return sb.Append(']').ToString();
        }

        private static string MwArray(MwContact[]? items)
        {
            if (items == null || items.Length == 0) return "[]";
            var sb = new StringBuilder("[");
            for (int i = 0; i < items.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "{{\"x\":{0:0.0},\"z\":{1:0.0},\"st\":\"{2}\",\"nb\":{3:0.0},\"h\":{4:0.0}}}",
                    items[i].X, items[i].Z, JsonLite.EscapeJson(items[i].Seeker ?? string.Empty), items[i].Notch, items[i].Heading);
            }
            return sb.Append(']').ToString();
        }

        // RDR page (docs/rdr-page.md). {present:false} when the aircraft has no radar; otherwise the
        // scope's range scale + cone half-angle and the air contacts the own radar detects. Contacts
        // carry world x/z (client derives bearing/range from the player's own position), altitude,
        // travel heading (velocity stub), lock state (tg) and label.
        // TGP stat overlay (mag/range/grid/mode/target detail) — see the Tgp* fields' doc comment
        // in TelemetrySnapshot.cs. "cnt":0 is the client's cue to hide the overlay entirely (no
        // lock at all, as distinct from a lock present but hasDetail:false for a multi-target or
        // stale-position case).
        private static string TgpBlock(TelemetrySnapshot s)
        {
            if (s.TgpTargetCount <= 0) return "{\"cnt\":0}";
            string head = string.Format(CultureInfo.InvariantCulture,
                "{{\"cnt\":{0},\"mag\":{1:0.0},\"range\":{2:0.0},\"grid\":\"{3}\",\"ir\":{4}," +
                "\"brg\":{5:0.0},\"type\":\"{6}\",\"pilot\":\"{7}\",\"status\":\"{8}\",\"hasDetail\":{9}," +
                "\"hdg\":{10:0.0},\"alt\":{11:0.0},\"relAlt\":{12:0.0},\"spd\":{13:0.0},\"relSpd\":{14:0.0}",
                s.TgpTargetCount, s.TgpMag, s.TgpRangeM, JsonLite.EscapeJson(s.TgpGrid ?? ""), JsonBool(s.TgpIR),
                s.TgpBearingDeg, JsonLite.EscapeJson(s.TgpType ?? ""), JsonLite.EscapeJson(s.TgpPilot ?? ""),
                JsonLite.EscapeJson(s.TgpStatus ?? "normal"), JsonBool(s.TgpHasDetail),
                s.TgpHeadingDeg, s.TgpAltitudeM, s.TgpRelAltitudeM, s.TgpSpeedMps, s.TgpRelSpeedMps);
            return head + ",\"boxes\":" + TgpBoxArray(s.TgpBoxes) + "}";
        }

        // Screen-projected lock boxes, one per locked target — see TgpBoxInfo's doc comment in
        // TelemetrySnapshot.cs. x/y are viewport fractions (0-1); the client flips y and hides a
        // box outside [0,1] or with vis:false (behind the camera).
        private static string TgpBoxArray(TgpBoxInfo[]? items)
        {
            if (items == null || items.Length == 0) return "[]";
            var sb = new StringBuilder("[");
            for (int i = 0; i < items.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "{{\"x\":{0:0.000},\"y\":{1:0.000},\"vis\":{2},\"status\":\"{3}\"}}",
                    items[i].X, items[i].Y, JsonBool(items[i].Visible), JsonLite.EscapeJson(items[i].Status ?? "target"));
            }
            return sb.Append(']').ToString();
        }

        private static string RdrBlock(TelemetrySnapshot s)
        {
            string pb = PitbullArray(s.Pitbull);
            if (!s.RadarPresent) return "{\"present\":false,\"pb\":" + pb + "}";
            return string.Format(CultureInfo.InvariantCulture,
                "{{\"present\":true,\"range\":{0:0.0},\"cone\":{1:0.0},\"metric\":{2},\"lvlt\":{3:0.000},\"items\":{4},\"pb\":{5}}}",
                s.RadarRange, s.RadarConeDeg, s.RdrMetric ? "true" : "false", s.RdrLevelTime, RdrArray(s.Rdr), pb);
        }

        // Pitbull missiles (issue #40): the player's own AA missiles with an active-radar seeker
        // currently locked. tid is the designated target's persistentID.Id, 0 if none/unresolved —
        // the client only draws the dashed target line when it can resolve tid against a live
        // RDR/MAP contact.
        private static string PitbullArray(PitbullContact[]? items)
        {
            if (items == null || items.Length == 0) return "[]";
            var sb = new StringBuilder("[");
            for (int i = 0; i < items.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "{{\"id\":{0},\"x\":{1:0.0},\"z\":{2:0.0},\"alt\":{3:0.0},\"hdg\":{4:0.0},\"tid\":{5}}}",
                    items[i].Id, items[i].X, items[i].Z, items[i].Alt, items[i].Heading, items[i].TargetId);
            }
            return sb.Append(']').ToString();
        }

        private static string RdrArray(RdrContact[]? items)
        {
            if (items == null || items.Length == 0) return "[]";
            var sb = new StringBuilder("[");
            for (int i = 0; i < items.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "{{\"id\":{0},\"x\":{1:0.0},\"z\":{2:0.0},\"alt\":{3:0.0},\"hdg\":{4:0.0},\"tg\":{5},\"rd\":{6},\"dl\":{7},\"n\":\"{8}\"}}",
                    items[i].Id, items[i].X, items[i].Z, items[i].Alt, items[i].Heading,
                    items[i].Targeted ? 1 : 0, items[i].Radar ? 1 : 0, items[i].Datalink ? 1 : 0,
                    JsonLite.EscapeJson(items[i].Name ?? string.Empty));
            }
            return sb.Append(']').ToString();
        }

        private static string RwrArray(RwrContact[]? items)
        {
            if (items == null || items.Length == 0) return "[]";
            var sb = new StringBuilder("[");
            for (int i = 0; i < items.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "{{\"x\":{0:0.0},\"z\":{1:0.0},\"tr\":{2},\"pw\":{3:0.000},\"fr\":{4:0.000},\"n\":\"{5}\",\"k\":{6}}}",
                    items[i].X, items[i].Z, items[i].Tier, items[i].Power, items[i].Fresh,
                    JsonLite.EscapeJson(items[i].Name ?? string.Empty), items[i].Kind);
            }
            return sb.Append(']').ToString();
        }

        private static string StringArray(string[]? items)
        {
            if (items == null || items.Length == 0) return "[]";
            var sb = new StringBuilder("[");
            for (int i = 0; i < items.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(JsonLite.EscapeJson(items[i] ?? string.Empty)).Append('"');
            }
            return sb.Append(']').ToString();
        }

        private static string PartsArray(PartHp[]? parts)
        {
            if (parts == null || parts.Length == 0) return "[]";
            var sb = new StringBuilder("[");
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "{{\"n\":\"{0}\",\"hp\":{1:0.#},\"d\":{2}}}",
                    JsonLite.EscapeJson(parts[i].Name ?? string.Empty),
                    parts[i].Hp,
                    parts[i].Detached ? 1 : 0);
            }
            return sb.Append(']').ToString();
        }

        private static string PylonsArray(PylonMarker[]? pylons)
        {
            if (pylons == null || pylons.Length == 0) return "[]";
            var sb = new StringBuilder("[");
            for (int i = 0; i < pylons.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"n\":\"").Append(JsonLite.EscapeJson(pylons[i].Name ?? string.Empty)).Append("\",")
                  .Append("\"s\":\"").Append(JsonLite.EscapeJson(pylons[i].State ?? "empty")).Append("\"}");
            }
            return sb.Append(']').ToString();
        }

        private static string UnitsArray(UnitInfo[]? units)
        {
            if (units == null || units.Length == 0) return "[]";
            var sb = new StringBuilder("[");
            for (int i = 0; i < units.Length; i++)
            {
                UnitInfo u = units[i];
                if (i > 0) sb.Append(',');
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "{{\"id\":{8},\"t\":\"{0}\",\"x\":{1:0.0},\"z\":{2:0.0},\"h\":{3:0.0},\"f\":{4},\"o\":{5},\"s\":{6:0.000},\"tg\":{7},\"jm\":{9},\"jb\":{10},\"dl\":{11},\"st\":{12}}}",
                    JsonLite.EscapeJson(u.Type ?? string.Empty),
                    u.X, u.Z, u.Heading, u.Faction,
                    u.Orient ? "true" : "false", u.Scale,
                    u.Targeted ? 1 : 0,
                    u.Id,
                    u.Jammed ? 1 : 0,
                    u.JammedBy,
                    u.Datalink ? 1 : 0,
                    u.Stale ? 1 : 0);
            }
            return sb.Append(']').ToString();
        }

        private static string LoadoutArray(LoadoutEntry[]? items)
        {
            if (items == null || items.Length == 0) return "[]";
            var sb = new StringBuilder("[");
            for (int i = 0; i < items.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "{{\"n\":\"{0}\",\"a\":{1},\"f\":{2}}}",
                    JsonLite.EscapeJson(items[i].Name ?? string.Empty), items[i].Ammo, items[i].FullAmmo);
            }
            return sb.Append(']').ToString();
        }
    }
}

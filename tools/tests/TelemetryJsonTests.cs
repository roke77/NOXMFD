using System.Collections.Generic;
using NOXMFD;

namespace NOXMFD.Tests
{
    // Stable-snapshot fixtures for TelemetryJson.Serialize. Asserts by parsing the output back
    // through JsonLite rather than matching substrings, so a field
    // reordering doesn't break these — only an actual value/shape change does.
    public class TelemetryJsonTests
    {
        private static Dictionary<string, object?> Root(TelemetrySnapshot s) =>
            (Dictionary<string, object?>)JsonLite.Parse(
                TelemetryJson.Serialize(s, "\"soiTarget\":\"MAP\",\"soiPane\":-1,\"soiSeq\":3,\"soiAct\":\"\"",
                    masterArmsOn: true, combatModeLabel: "aa", extSlicesJson: "{}"))!;

        private static Dictionary<string, object?> Obj(object? v) => (Dictionary<string, object?>)v!;
        private static List<object?> Arr(object? v) => (List<object?>)v!;

        [Fact]
        public void Default_snapshot_reports_every_optional_block_absent_and_every_array_empty()
        {
            var root = Root(default);

            Assert.False((bool)root["ping"]!);
            Assert.True((bool)root["masterArmsOn"]!);
            Assert.Equal("aa", root["combatMode"]);
            Assert.Equal("MAP", root["soiTarget"]);
            Assert.False((bool)Obj(root["mis"])["present"]!);
            Assert.False((bool)Obj(root["obj"])["present"]!);
            Assert.False((bool)Obj(root["tgt"])["present"]!);
            Assert.False((bool)Obj(root["bdf"])["present"]!);
            Assert.False((bool)Obj(root["pal"])["present"]!);
            Assert.False((bool)Obj(root["rdr"])["present"]!);
            Assert.False((bool)root["tgpManual"]!);
            Assert.Empty(Arr(Obj(root["rdr"])["pb"]));
            Assert.Empty(Arr(root["contacts"]));
            Assert.Empty(Arr(root["loadout"]));
            Assert.Empty(Arr(root["parts"]));
            Assert.Empty(Arr(root["pylons"]));
            Assert.Empty(Arr(root["rwr"]));
            Assert.Empty(Arr(root["mw"]));
            Assert.Empty(Arr(root["failures"]));

            // AKF has no present flag — always emitted, all-zero when nothing happened yet.
            var akf = Obj(root["akf"]);
            Assert.Empty(Arr(akf["all"]));
            Assert.Empty(Arr(akf["player"]));
            Assert.Equal(0.0, Obj(akf["kills"])["aircraft"]);
        }

        [Fact]
        public void Tgp_manual_state_round_trips_as_top_level_flag()
        {
            var s = default(TelemetrySnapshot);
            s.TgpActive = true;
            s.TgpResolution = "high";
            s.TgpQuality = "hq";
            s.TgpManualActive = true;

            var root = Root(s);

            Assert.True((bool)root["tgpActive"]!);
            Assert.Equal("high", root["tgpResolution"]);
            Assert.Equal("hq", root["tgpQuality"]);
            Assert.True((bool)root["tgpManual"]!);
        }

        [Fact]
        public void Tgp_block_carries_manual_data_even_with_zero_target_count()
        {
            // Manual mode never has a real lock (TgpManualControl.Tick() auto-exits the instant one
            // exists), so TgpTargetCount stays 0 the whole time it's on — the "cnt <= 0" shortcut
            // that hides the LOCKED-target overlay must not also swallow manual-mode data.
            var s = default(TelemetrySnapshot);
            s.TgpTargetCount = 0;
            s.TgpManualActive = true;
            s.TgpManualPointTrack = true;
            s.TgpMag = 4.5f;
            s.TgpRangeM = 2400f;
            s.TgpGrid = "Kf53";
            s.TgpElevationDeg = -8f;
            s.TgpBearingDeg = 135f;
            s.TgpAltitudeM = 68f;
            s.TgpRelAltitudeM = -934f;
            s.TgpRelSpeedMps = -33f;
            s.TgpClosureReading = "-119km/h";

            var tgp = Obj(Root(s)["tgp"]);

            Assert.Equal(0.0, tgp["cnt"]);
            Assert.True((bool)tgp["manual"]!);
            Assert.True((bool)tgp["pointTrack"]!);
            Assert.Equal(-8.0, tgp["el"]);
            Assert.Equal(4.5, tgp["mag"]);
            Assert.Equal(2400.0, tgp["range"]);
            Assert.Equal("Kf53", tgp["grid"]);
            // Pre-formatted server-side (UnitConverter.SpeedReading) rather than a raw m/s number —
            // closure is new to the web page, so it matches the in-cockpit overlay's units exactly
            // instead of inheriting the page's pre-existing raw-units simplification for RNG/ALT/etc.
            Assert.Equal("-119km/h", tgp["clo"]);
        }

        [Fact]
        public void Tgp_block_hides_entirely_when_no_lock_and_not_manual()
        {
            var s = default(TelemetrySnapshot);
            s.TgpTargetCount = 0;
            s.TgpManualActive = false;

            var tgp = Obj(Root(s)["tgp"]);

            Assert.Equal(0.0, tgp["cnt"]);
            Assert.False(tgp.ContainsKey("manual"));
        }

        [Fact]
        public void Soi_and_ext_are_spliced_through_verbatim_not_reserialized()
        {
            var s = default(TelemetrySnapshot);
            string json = TelemetryJson.Serialize(s, "\"soiTarget\":\"RDR\",\"soiPane\":2,\"soiSeq\":9,\"soiAct\":\"lock\"",
                masterArmsOn: false, combatModeLabel: "ag", extSlicesJson: "{\"rc\":{\"armed\":true}}");
            var root = (Dictionary<string, object?>)JsonLite.Parse(json)!;

            Assert.Equal("RDR", root["soiTarget"]);
            Assert.Equal(2.0, root["soiPane"]);
            Assert.False((bool)root["masterArmsOn"]!);
            Assert.Equal("ag", root["combatMode"]);
            Assert.True((bool)Obj(Obj(root["ext"])["rc"])["armed"]!);
        }

        [Fact]
        public void Unit_contact_round_trips_every_field_including_a_quote_in_the_name()
        {
            var s = default(TelemetrySnapshot);
            s.Units = new[]
            {
                new UnitInfo
                {
                    Id = 42, Type = "F-14 \"Tomcat\"", X = 100.3f, Z = -50.5f, Heading = 90f,
                    Faction = 2, Orient = true, Scale = 1.5f, Targeted = true,
                    Jammed = true, JammedBy = 7, Datalink = true, Stale = true,
                },
            };
            var contact = Obj(Arr(Root(s)["contacts"])[0]);

            Assert.Equal(42.0, contact["id"]);
            Assert.Equal("F-14 \"Tomcat\"", contact["t"]);
            Assert.Equal(100.3, contact["x"]);
            Assert.Equal(-50.5, contact["z"]);
            Assert.Equal(2.0, contact["f"]);
            Assert.True((bool)contact["o"]!);
            Assert.Equal(1.0, contact["tg"]);
            Assert.Equal(1.0, contact["jm"]);
            Assert.Equal(7.0, contact["jb"]);
            Assert.Equal(1.0, contact["dl"]);
            Assert.Equal(1.0, contact["st"]);
        }

        [Fact]
        public void Akf_entry_omits_attacker_fields_when_there_is_no_killer()
        {
            var s = default(TelemetrySnapshot);
            s.AkfAll = new[]
            {
                new AkfKillEntry { Attacker = null, Victim = "Pilot A", VictimHostile = false, Verb = "crashed" },
                new AkfKillEntry { Attacker = "Pilot B", AttackerHostile = true, Victim = "Pilot C", Verb = "downed", Weapon = "AIM-9", PlayerIsVictim = true },
            };
            var akf = Arr(Obj(Root(s)["akf"])["all"]);

            var noKiller = Obj(akf[0]);
            Assert.False(noKiller.ContainsKey("a"));
            Assert.Equal("Pilot A", noKiller["v"]);

            var withKiller = Obj(akf[1]);
            Assert.Equal("Pilot B", withKiller["a"]);
            Assert.Equal("AIM-9", withKiller["w"]);
            Assert.True((bool)withKiller["pv"]!);
        }

        [Fact]
        public void Bdf_and_pal_are_independent_present_flags_over_the_same_shape()
        {
            var s = default(TelemetrySnapshot);
            s.BdfPresent = true;
            s.BdfFaction = "BOSCALI";
            s.BdfFunds = 12.5f;
            s.BdfShips = new[] { new BdfCountInfo { Name = "CV", Count = 1 } };
            // PalPresent left false.

            var root = Root(s);
            var bdf = Obj(root["bdf"]);
            Assert.True((bool)bdf["present"]!);
            Assert.Equal("BOSCALI", bdf["faction"]);
            Assert.Equal("CV", Obj(Arr(bdf["ships"])[0])["n"]);
            Assert.False((bool)Obj(root["pal"])["present"]!);
        }

        [Fact]
        public void Obj_entries_carry_their_position_sub_rows()
        {
            var s = default(TelemetrySnapshot);
            s.ObjPresent = true;
            s.Obj = new[]
            {
                new ObjEntry
                {
                    Name = "Destroy radar site", Status = 1, Percent = 0.5f,
                    Positions = new[] { new ObjPosition { Name = "DestroyUnits", X = 10f, Z = 20f } },
                },
            };

            var entry = Obj(Arr(Obj(Root(s)["obj"])["items"])[0]);
            Assert.Equal("Destroy radar site", entry["n"]);
            Assert.Equal(1.0, entry["s"]);
            var pos = Obj(Arr(entry["pos"])[0]);
            Assert.Equal("DestroyUnits", pos["n"]);
            Assert.Equal(10.0, pos["x"]);
        }

        [Fact]
        public void Rdr_present_carries_contacts_and_pitbull_independently_of_radar_presence()
        {
            var s = default(TelemetrySnapshot);
            s.RadarPresent = true;
            s.RadarRange = 80f;
            s.Rdr = new[] { new RdrContact { Id = 1, Name = "Bogey", Targeted = true } };
            s.Pitbull = new[] { new PitbullContact { Id = 2, TargetId = 1 } };

            var rdr = Obj(Root(s)["rdr"]);
            Assert.True((bool)rdr["present"]!);
            Assert.Equal(80.0, rdr["range"]);
            Assert.Equal("Bogey", Obj(Arr(rdr["items"])[0])["n"]);
            Assert.Equal(2.0, Obj(Arr(rdr["pb"])[0])["id"]);
        }

        [Fact]
        public void Pitbull_present_even_when_radar_itself_is_absent()
        {
            var s = default(TelemetrySnapshot);
            s.RadarPresent = false;
            s.Pitbull = new[] { new PitbullContact { Id = 5 } };

            var rdr = Obj(Root(s)["rdr"]);
            Assert.False((bool)rdr["present"]!);
            Assert.Equal(5.0, Obj(Arr(rdr["pb"])[0])["id"]);
        }
    }
}

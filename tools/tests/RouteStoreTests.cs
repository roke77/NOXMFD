using System;
using System.Collections.Generic;
using System.IO;
using NOXMFD;

namespace NOXMFD.Tests
{
    // A fresh RouteStoreTests instance is constructed before every [Fact] (xUnit's default), so the
    // reset here keeps RouteStore's static route list from leaking between tests in this class.
    public class RouteStoreTests
    {
        public RouteStoreTests()
        {
            RouteStore.PersistToDisk = false;
            RouteStore.ResetForTests();
        }

        // Guards the fix to the ordering bug from the 0.36.1 keybinds incident: a backup taken
        // AFTER writing the new file is just a copy of the new content, no help if that write is
        // the bad one. Save() must back up whatever was on disk BEFORE overwriting it.
        [Fact]
        public void Save_backs_up_the_previous_content_before_overwriting()
        {
            string dir = Path.Combine(Path.GetTempPath(), "noxmfd-routestore-test-" + Guid.NewGuid());
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "com.roque.NOXMFD.routes.json");
            string bak = file + ".bak";
            try
            {
                RouteStore.ConfigDir = dir;
                RouteStore.PersistToDisk = true;
                RouteStore.ResetForTests();

                RouteStore.CreateRoute("Alpha");   // first save: nothing on disk yet, so no .bak
                Assert.False(File.Exists(bak));
                string afterAlpha = File.ReadAllText(file);

                RouteStore.CreateRoute("Bravo");   // second save: backs up the "Alpha" state first
                Assert.True(File.Exists(bak));
                Assert.Equal(afterAlpha, File.ReadAllText(bak));
                Assert.NotEqual(File.ReadAllText(file), File.ReadAllText(bak));
            }
            finally
            {
                RouteStore.ConfigDir = null;
                RouteStore.PersistToDisk = false;
                RouteStore.ResetForTests();
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void CreateRoute_becomes_the_active_route()
        {
            Route r = RouteStore.CreateRoute("Alpha");
            Assert.Equal("Alpha", r.Name);
            Assert.Contains("\"activeRouteId\":\"" + r.Id + "\"", RouteStore.RoutesJson);
        }

        [Fact]
        public void CreateRoute_dedupes_a_repeated_name_with_a_numeric_suffix()
        {
            RouteStore.CreateRoute("Alpha");
            Route second = RouteStore.CreateRoute("Alpha");
            Assert.Equal("Alpha (2)", second.Name);
        }

        [Fact]
        public void DeleteRoute_falls_active_back_to_a_remaining_route()
        {
            Route a = RouteStore.CreateRoute("A");
            Route b = RouteStore.CreateRoute("B"); // active after creation
            Assert.True(RouteStore.DeleteRoute(b.Id));
            RouteStore.AddWaypoint(1f, 1f, "W1"); // adds to whichever route is now active
            Assert.Contains("\"id\":\"" + a.Id + "\"", RouteStore.RoutesJson);
            Assert.DoesNotContain("\"id\":\"" + b.Id + "\"", RouteStore.RoutesJson);
        }

        // With only 2 routes, deleting the active one always lands on "the other one" regardless
        // of policy — this case needs 3 to actually distinguish nearest-neighbor (land on C, the
        // one now at B's old position — same policy DeleteSteerPoint already uses) from "always
        // jump to the first route" (would land on A instead).
        [Fact]
        public void DeleteRoute_lands_on_the_nearest_remaining_route_not_always_the_first()
        {
            RouteStore.CreateRoute("A");
            Route b = RouteStore.CreateRoute("B");
            Route c = RouteStore.CreateRoute("C");
            RouteStore.SetActiveRoute(b.Id);
            Assert.True(RouteStore.DeleteRoute(b.Id));
            Assert.Contains("\"activeRouteId\":\"" + c.Id + "\"", RouteStore.RoutesJson);
        }

        [Fact]
        public void DeleteRoute_on_unknown_id_returns_false()
        {
            RouteStore.CreateRoute("A");
            Assert.False(RouteStore.DeleteRoute("does-not-exist"));
        }

        [Fact]
        public void RemoveWaypoint_before_next_index_shifts_progress_down()
        {
            RouteStore.CreateRoute("R");
            RouteStore.AddWaypoint(0, 0, "W0");
            RouteStore.AddWaypoint(1, 1, "W1");
            RouteStore.AddWaypoint(2, 2, "W2");
            RouteStore.ResetWaypoint(2); // nextIndex = 2 (W0, W1 already "done")
            RouteStore.RemoveWaypoint(0); // remove W0, ahead of nextIndex
            Assert.True(RouteStore.TryGetActiveWaypoint(out _, out _, out string name, out int index));
            Assert.Equal(1, index); // one fewer completed waypoint ahead of it
            Assert.Equal("W2", name);
        }

        [Fact]
        public void ResetWaypoint_clamps_to_the_waypoint_count()
        {
            RouteStore.CreateRoute("R");
            RouteStore.AddWaypoint(0, 0, "W0");
            Assert.True(RouteStore.ResetWaypoint(99)); // only 1 waypoint exists
            Assert.Contains("\"nextIndex\":1", RouteStore.RoutesJson); // clamped, not the out-of-range 99
        }

        [Fact]
        public void CycleActiveRoute_wraps_through_a_none_state()
        {
            Route a = RouteStore.CreateRoute("A");
            Route b = RouteStore.CreateRoute("B"); // active
            RouteStore.CycleActiveRoute(+1); // wraps past the end to "none"
            Assert.Contains("\"activeRouteId\":null", RouteStore.RoutesJson);
            RouteStore.CycleActiveRoute(+1); // "none" -> first route
            Assert.Contains("\"activeRouteId\":\"" + a.Id + "\"", RouteStore.RoutesJson);
        }

        [Fact]
        public void AdvanceIfNear_increments_next_index_within_threshold_only()
        {
            RouteStore.CreateRoute("R");
            RouteStore.AddWaypoint(0f, 0f, "W0");
            RouteStore.AdvanceIfNear(5000f, 5000f); // far away — no advance
            Assert.True(RouteStore.TryGetActiveWaypoint(out _, out _, out _, out int index));
            Assert.Equal(0, index);

            RouteStore.AdvanceIfNear(1f, 1f); // within the 1000m threshold
            Assert.False(RouteStore.TryGetActiveWaypoint(out _, out _, out _, out _)); // only waypoint consumed
        }

        [Fact]
        public void ImportRoute_rejects_a_waypoint_missing_coordinates()
        {
            Assert.False(RouteStore.ImportRoute("{\"name\":\"R\",\"waypoints\":[{\"name\":\"W\"}]}"));
        }

        [Fact]
        public void ImportRoute_accepts_a_valid_export_and_starts_progress_at_zero()
        {
            bool ok = RouteStore.ImportRoute(
                "{\"name\":\"Imported\",\"waypoints\":[{\"name\":\"W0\",\"x\":10,\"z\":20}]}");
            Assert.True(ok);
            Assert.True(RouteStore.TryGetActiveWaypoint(out float x, out float z, out string name, out int index));
            Assert.Equal(10f, x);
            Assert.Equal(20f, z);
            Assert.Equal("W0", name);
            Assert.Equal(0, index);
        }

        [Fact]
        public void ImportRoute_rejects_null_or_empty_text()
        {
            Assert.False(RouteStore.ImportRoute(null));
            Assert.False(RouteStore.ImportRoute(""));
        }

        [Fact]
        public void SteerPoint_is_the_navigation_target_only_without_an_active_route()
        {
            SteerPoint point = RouteStore.AddSteerPoint(30f, 40f, "IP");
            Assert.True(RouteStore.TryGetActiveNavigationPoint(
                out float x, out float z, out string name, out _, out bool isSteerPoint));
            Assert.True(isSteerPoint);
            Assert.Equal((30f, 40f, "IP"), (x, z, name));

            Route route = RouteStore.CreateRoute("R");
            RouteStore.AddWaypoint(10f, 20f, "W1");
            Assert.True(RouteStore.TryGetActiveNavigationPoint(
                out x, out z, out name, out _, out isSteerPoint));
            Assert.False(isSteerPoint);
            Assert.Equal((10f, 20f, "W1"), (x, z, name));

            RouteStore.SetActiveRoute(null);
            Assert.True(RouteStore.TryGetActiveNavigationPoint(
                out _, out _, out name, out _, out isSteerPoint));
            Assert.True(isSteerPoint);
            Assert.Equal("IP", name);
            Assert.Contains("\"activeSteerPointId\":\"" + point.Id + "\"", RouteStore.RoutesJson);
        }

        [Fact]
        public void Completed_active_route_does_not_fall_back_to_a_steer_point()
        {
            RouteStore.AddSteerPoint(30f, 40f, "IP");
            RouteStore.CreateRoute("R");
            RouteStore.AddWaypoint(10f, 20f, "W1");
            RouteStore.ResetWaypoint(1);

            Assert.False(RouteStore.TryGetActiveNavigationPoint(
                out _, out _, out _, out _, out bool isSteerPoint));
            Assert.False(isSteerPoint);
        }

        [Fact]
        public void StepNavigation_cycles_steer_points_with_wraparound_when_no_route_is_active()
        {
            SteerPoint first = RouteStore.AddSteerPoint(1f, 1f, "A");
            RouteStore.AddSteerPoint(2f, 2f, "B");

            RouteStore.StepNavigation(+1);
            Assert.Contains("\"activeSteerPointId\":\"" + first.Id + "\"", RouteStore.RoutesJson);
            RouteStore.StepNavigation(-1);
            Assert.True(RouteStore.TryGetActiveNavigationPoint(
                out _, out _, out string name, out _, out bool isSteerPoint));
            Assert.True(isSteerPoint);
            Assert.Equal("B", name);
        }

        [Fact]
        public void AddNavigationPoint_uses_the_current_route_mode()
        {
            RouteStore.AddNavigationPoint(1f, 2f, "S1");
            Assert.Contains("\"steerPoints\":[{", RouteStore.RoutesJson);
            Assert.Contains("\"name\":\"S1\"", RouteStore.RoutesJson);

            RouteStore.CreateRoute("R");
            RouteStore.AddNavigationPoint(3f, 4f, "W1");
            Assert.True(RouteStore.TryGetActiveWaypoint(
                out float x, out float z, out string name, out int index));
            Assert.Equal((3f, 4f, "W1", 0), (x, z, name, index));
        }

        [Fact]
        public void AdvanceIfNear_never_changes_a_steer_point()
        {
            RouteStore.AddSteerPoint(0f, 0f, "Static");
            RouteStore.AdvanceIfNear(0f, 0f);

            Assert.True(RouteStore.TryGetActiveNavigationPoint(
                out _, out _, out string name, out _, out bool isSteerPoint));
            Assert.True(isSteerPoint);
            Assert.Equal("Static", name);
        }

        [Fact]
        public void ImportSteerPoints_appends_fresh_local_points_and_selects_the_first_import()
        {
            RouteStore.AddSteerPoint(1f, 2f, "Existing");
            Assert.True(RouteStore.ImportSteerPoints(
                "{\"steerPoints\":[{\"name\":\"A\",\"x\":10,\"z\":20},{\"name\":\"B\",\"x\":30,\"z\":40}]}"));

            Assert.Contains("Existing", RouteStore.RoutesJson);
            Assert.Contains("\"name\":\"A\"", RouteStore.RoutesJson);
            Assert.Contains("\"name\":\"B\"", RouteStore.RoutesJson);
            Assert.True(RouteStore.TryGetActiveNavigationPoint(
                out _, out _, out string name, out _, out bool isSteerPoint));
            Assert.True(isSteerPoint);
            Assert.Equal("A", name);
        }

        [Fact]
        public void ImportSteerPoints_rejects_empty_or_malformed_lists()
        {
            Assert.False(RouteStore.ImportSteerPoints("{\"steerPoints\":[]}"));
            Assert.False(RouteStore.ImportSteerPoints("{\"steerPoints\":[{\"name\":\"Missing position\"}]}"));
        }

        // ── squad-shared routes (docs/squadron-transport.md) ──────────────────────────────────

        [Fact]
        public void ShareRoute_marks_shared_with_squad_and_returns_the_delegate_result()
        {
            Route r = RouteStore.CreateRoute("R");
            RouteStore.SendSquadData = (type, payload) => type == "wpt.route";
            Assert.True(RouteStore.ShareRoute(r.Id));
            Assert.Contains("\"sharedWithSquad\":true", RouteStore.RoutesJson);
        }

        [Fact]
        public void ShareRoute_with_no_delegate_wired_still_marks_shared_but_returns_false()
        {
            Route r = RouteStore.CreateRoute("R");
            Assert.False(RouteStore.ShareRoute(r.Id));   // SendSquadData is null after ResetForTests
        }

        [Fact]
        public void ShareRoute_rejects_a_route_that_is_itself_someone_elses_shared_content()
        {
            RouteStore.ReceiveSharedRoute(
                "{\"id\":\"r_shared\",\"name\":\"S\",\"waypoints\":[{\"name\":\"W\",\"x\":1,\"z\":1}]}",
                "Leader");
            RouteStore.AcceptShared("r_shared");
            Assert.False(RouteStore.ShareRoute("r_shared"));   // can't re-share someone else's route
        }

        [Fact]
        public void Edit_after_share_rebroadcasts_via_the_delegate()
        {
            Route r = RouteStore.CreateRoute("R");
            int sends = 0;
            RouteStore.SendSquadData = (type, payload) => { sends++; return true; };
            RouteStore.ShareRoute(r.Id);       // 1st send
            RouteStore.RenameRoute(r.Id, "R2"); // auto-reshare on edit
            Assert.Equal(2, sends);
        }

        [Fact]
        public void ReceiveSharedRoute_creates_a_pending_entry()
        {
            bool ok = RouteStore.ReceiveSharedRoute(
                "{\"id\":\"r_x\",\"name\":\"Shared\",\"waypoints\":[{\"name\":\"W\",\"x\":1,\"z\":2}]}",
                "Leader");
            Assert.True(ok);
            Assert.Contains("\"id\":\"r_x\",\"name\":\"Shared\",\"fromName\":\"Leader\",\"waypointCount\":1",
                RouteStore.RoutesJson);
        }

        [Fact]
        public void ReceiveSharedRoute_repeat_of_a_pending_share_updates_it_in_place()
        {
            RouteStore.ReceiveSharedRoute(
                "{\"id\":\"r_x\",\"name\":\"First\",\"waypoints\":[{\"name\":\"W\",\"x\":1,\"z\":2}]}", "Leader");
            RouteStore.ReceiveSharedRoute(
                "{\"id\":\"r_x\",\"name\":\"Renamed\",\"waypoints\":[{\"name\":\"W\",\"x\":1,\"z\":2}]}", "Leader");
            Assert.Contains("\"waypointCount\":1", RouteStore.RoutesJson);
            Assert.Contains("\"name\":\"Renamed\"", RouteStore.RoutesJson);
            Assert.DoesNotContain("\"name\":\"First\"", RouteStore.RoutesJson);
        }

        [Fact]
        public void AcceptShared_moves_a_pending_entry_into_a_readonly_route()
        {
            RouteStore.ReceiveSharedRoute(
                "{\"id\":\"r_x\",\"name\":\"Shared\",\"waypoints\":[{\"name\":\"W\",\"x\":1,\"z\":2}]}", "Leader");
            Assert.True(RouteStore.AcceptShared("r_x"));
            Assert.Contains("\"id\":\"r_x\"", RouteStore.RoutesJson);
            Assert.Contains("\"sharedBy\":\"Leader\"", RouteStore.RoutesJson);
            Assert.DoesNotContain("\"pendingShared\":[{", RouteStore.RoutesJson);   // no longer pending
        }

        [Fact]
        public void RejectShared_removes_the_pending_entry_without_creating_a_route()
        {
            RouteStore.ReceiveSharedRoute(
                "{\"id\":\"r_x\",\"name\":\"Shared\",\"waypoints\":[{\"name\":\"W\",\"x\":1,\"z\":2}]}", "Leader");
            Assert.True(RouteStore.RejectShared("r_x"));
            Assert.DoesNotContain("\"id\":\"r_x\"", RouteStore.RoutesJson);
        }

        [Fact]
        public void Mutators_reject_edits_on_an_accepted_shared_route()
        {
            RouteStore.ReceiveSharedRoute(
                "{\"id\":\"r_x\",\"name\":\"Shared\",\"waypoints\":[{\"name\":\"W\",\"x\":1,\"z\":2}]}", "Leader");
            RouteStore.AcceptShared("r_x");
            RouteStore.SetActiveRoute("r_x");

            Assert.False(RouteStore.RenameRoute("r_x", "Mine now"));
            Assert.False(RouteStore.RenameWaypoint(0, "Mine now"));
            Assert.False(RouteStore.RemoveWaypoint(0));
            RouteStore.AddWaypoint(9f, 9f, "Sneaky");   // no-op: active route is shared
            Assert.DoesNotContain("Sneaky", RouteStore.RoutesJson);
        }

        [Fact]
        public void ReceiveSharedRoute_reshare_preserves_progress_by_matching_the_surviving_waypoint()
        {
            RouteStore.ReceiveSharedRoute(
                "{\"id\":\"r_x\",\"name\":\"Shared\",\"waypoints\":[" +
                "{\"name\":\"WA\",\"x\":1,\"z\":1},{\"name\":\"WB\",\"x\":2,\"z\":2}]}", "Leader");
            RouteStore.AcceptShared("r_x");
            RouteStore.SetActiveRoute("r_x");
            RouteStore.ResetWaypoint(1);   // NextIndex = 1, pointing at WB

            // Leader re-shares: WA dropped, WB kept (same name/x/z), WC appended.
            RouteStore.ReceiveSharedRoute(
                "{\"id\":\"r_x\",\"name\":\"Shared\",\"waypoints\":[" +
                "{\"name\":\"WB\",\"x\":2,\"z\":2},{\"name\":\"WC\",\"x\":3,\"z\":3}]}", "Leader");

            Assert.True(RouteStore.TryGetActiveWaypoint(out _, out _, out string name, out _));
            Assert.Equal("WB", name);   // progress followed WB to its new index (0), not reset to 0 blindly
        }

        [Fact]
        public void OnSquadEnded_clears_pending_shares_and_unlocks_accepted_routes()
        {
            RouteStore.ReceiveSharedRoute(
                "{\"id\":\"r_pending\",\"name\":\"P\",\"waypoints\":[{\"name\":\"W\",\"x\":1,\"z\":1}]}", "Leader");
            RouteStore.ReceiveSharedRoute(
                "{\"id\":\"r_x\",\"name\":\"Shared\",\"waypoints\":[{\"name\":\"W\",\"x\":1,\"z\":2}]}", "Leader");
            RouteStore.AcceptShared("r_x");

            RouteStore.OnSquadEnded();

            Assert.DoesNotContain("\"id\":\"r_pending\"", RouteStore.RoutesJson);   // pending share dropped
            Assert.Contains("\"id\":\"r_x\"", RouteStore.RoutesJson);
            Assert.Contains("\"sharedBy\":\"\"", RouteStore.RoutesJson);            // unlocked, not deleted
            Assert.True(RouteStore.RenameRoute("r_x", "Now mine"));                 // editable again
        }

        [Fact]
        public void DeleteRoute_of_a_shared_route_broadcasts_a_delete_tombstone()
        {
            Route r = RouteStore.CreateRoute("R");
            int sends = 0;
            string? lastType = null, lastPayload = null;
            RouteStore.SendSquadData = (type, payload) => { sends++; lastType = type; lastPayload = payload; return true; };
            RouteStore.ShareRoute(r.Id);   // 1st send: the share itself

            Assert.True(RouteStore.DeleteRoute(r.Id));

            Assert.Equal(2, sends);
            Assert.Equal("wpt.route-deleted", lastType);
            Assert.Equal(r.Id, lastPayload);
        }

        [Fact]
        public void DeleteRoute_of_a_never_shared_route_sends_nothing()
        {
            Route r = RouteStore.CreateRoute("R");
            int sends = 0;
            RouteStore.SendSquadData = (type, payload) => { sends++; return true; };

            Assert.True(RouteStore.DeleteRoute(r.Id));
            Assert.Equal(0, sends);
        }

        [Fact]
        public void RemoveSharedRoute_drops_a_pending_share()
        {
            RouteStore.ReceiveSharedRoute(
                "{\"id\":\"r_x\",\"name\":\"Shared\",\"waypoints\":[{\"name\":\"W\",\"x\":1,\"z\":2}]}", "Leader");
            Assert.True(RouteStore.RemoveSharedRoute("r_x"));
            Assert.DoesNotContain("\"id\":\"r_x\"", RouteStore.RoutesJson);
        }

        [Fact]
        public void RemoveSharedRoute_drops_an_already_accepted_route()
        {
            RouteStore.ReceiveSharedRoute(
                "{\"id\":\"r_x\",\"name\":\"Shared\",\"waypoints\":[{\"name\":\"W\",\"x\":1,\"z\":2}]}", "Leader");
            RouteStore.AcceptShared("r_x");
            RouteStore.SetActiveRoute("r_x");

            Assert.True(RouteStore.RemoveSharedRoute("r_x"));

            Assert.DoesNotContain("\"id\":\"r_x\"", RouteStore.RoutesJson);
            Assert.Contains("\"activeRouteId\":null", RouteStore.RoutesJson);   // fell back cleanly
        }

        [Fact]
        public void RemoveSharedRoute_never_touches_a_route_the_pilot_made_themselves()
        {
            Route r = RouteStore.CreateRoute("Mine");   // SharedBy empty — not a shared copy
            Assert.False(RouteStore.RemoveSharedRoute(r.Id));
            Assert.Contains("\"id\":\"" + r.Id + "\"", RouteStore.RoutesJson);
        }

        [Fact]
        public void RemoveSharedRoute_on_an_unknown_id_returns_false()
        {
            Assert.False(RouteStore.RemoveSharedRoute("does-not-exist"));
        }

        // ── correctness fixes from the pre-merge review ────────────────────────────────────────

        [Fact]
        public void Load_from_disk_does_not_restore_SharedBy_across_a_restart()
        {
            string dir = Path.Combine(Path.GetTempPath(), "noxmfd-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                RouteStore.ConfigDir = dir;
                File.WriteAllText(Path.Combine(dir, "com.roque.NOXMFD.routes.json"),
                    "{\"activeRouteId\":\"r_x\",\"routes\":[{\"id\":\"r_x\",\"name\":\"Shared\"," +
                    "\"nextIndex\":0,\"sharedBy\":\"OldLeader\",\"waypoints\":[]}]}");

                RouteStore.Load();

                // The squad session that justified the lock is long gone by the time a save file is
                // ever re-loaded (OnSquadEnded only runs while the plugin is live) — a route must
                // come back editable, not stuck read-only forever.
                Assert.Contains("\"sharedBy\":\"\"", RouteStore.RoutesJson);
                Assert.True(RouteStore.RenameRoute("r_x", "Mine now"));
            }
            finally
            {
                RouteStore.ConfigDir = null;
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void ClearRoutes_broadcasts_tombstones_for_every_previously_shared_route()
        {
            Route a = RouteStore.CreateRoute("A");
            Route b = RouteStore.CreateRoute("B");
            var deletedIds = new List<string>();
            RouteStore.SendSquadData = (type, payload) => { if (type == "wpt.route-deleted") deletedIds.Add(payload); return true; };
            RouteStore.ShareRoute(a.Id);
            RouteStore.ShareRoute(b.Id);

            RouteStore.ClearRoutes();

            Assert.Contains(a.Id, deletedIds);
            Assert.Contains(b.Id, deletedIds);
        }

        [Fact]
        public void OnSquadEnded_stops_auto_resharing_a_route_into_a_later_squad()
        {
            Route r = RouteStore.CreateRoute("R");
            int sends = 0;
            RouteStore.SendSquadData = (type, payload) => { sends++; return true; };
            RouteStore.ShareRoute(r.Id);   // shared with the first squad
            RouteStore.OnSquadEnded();     // that squad is gone

            RouteStore.RenameRoute(r.Id, "R2");   // edited after (hypothetically) joining a new squad

            Assert.Equal(1, sends);   // only the original share — no auto-reshare into an unrelated squad
        }

        [Fact]
        public void ReceiveSharedRoute_does_not_overwrite_a_route_the_pilot_now_owns_locally()
        {
            RouteStore.ReceiveSharedRoute(
                "{\"id\":\"r_x\",\"name\":\"Shared\",\"waypoints\":[{\"name\":\"W\",\"x\":1,\"z\":2}]}", "Leader");
            RouteStore.AcceptShared("r_x");
            RouteStore.OnSquadEnded();   // unlocks it — now this pilot's own content
            RouteStore.RenameRoute("r_x", "My own edit");

            // The same leader re-shares a route with the identical (never-changing) id — must not
            // silently clobber the local edit made after this pilot stopped being locked to it.
            bool ok = RouteStore.ReceiveSharedRoute(
                "{\"id\":\"r_x\",\"name\":\"Reshared\",\"waypoints\":[{\"name\":\"WZ\",\"x\":9,\"z\":9}]}", "Leader");

            Assert.False(ok);
            Assert.Contains("\"name\":\"My own edit\"", RouteStore.RoutesJson);
        }

        [Fact]
        public void SharedSteerPoint_uses_pending_accept_readonly_and_reshare_flow()
        {
            Assert.True(RouteStore.ReceiveSharedSteerPoint(
                "{\"id\":\"s_shared\",\"name\":\"IP\",\"x\":1,\"z\":2}", "Leader"));
            Assert.Contains("\"pendingSharedSteerPoints\":[{\"id\":\"s_shared\"", RouteStore.RoutesJson);
            Assert.True(RouteStore.AcceptSharedSteerPoint("s_shared"));
            Assert.Contains("\"sharedBy\":\"Leader\"", RouteStore.RoutesJson);
            Assert.False(RouteStore.RenameSteerPoint("s_shared", "Mine"));

            Assert.True(RouteStore.ReceiveSharedSteerPoint(
                "{\"id\":\"s_shared\",\"name\":\"IP 2\",\"x\":3,\"z\":4}", "Leader"));
            Assert.Contains("\"name\":\"IP 2\",\"x\":3.0,\"z\":4.0", RouteStore.RoutesJson);
        }

        [Fact]
        public void SharedSteerPoint_edits_and_delete_rebroadcast_after_first_share()
        {
            SteerPoint point = RouteStore.AddSteerPoint(1f, 2f, "IP");
            var types = new List<string>();
            RouteStore.SendSquadData = (type, payload) => { types.Add(type); return true; };

            Assert.True(RouteStore.ShareSteerPoint(point.Id));
            Assert.True(RouteStore.RenameSteerPoint(point.Id, "IP 2"));
            Assert.True(RouteStore.DeleteSteerPoint(point.Id));

            Assert.Equal(new[] { "wpt.steerpoint", "wpt.steerpoint", "wpt.steerpoint-deleted" }, types);
        }

        [Fact]
        public void RejectSharedSteerPoint_removes_the_pending_entry_without_creating_a_point()
        {
            RouteStore.ReceiveSharedSteerPoint("{\"id\":\"s_x\",\"name\":\"IP\",\"x\":1,\"z\":2}", "Leader");
            Assert.True(RouteStore.RejectSharedSteerPoint("s_x"));
            Assert.DoesNotContain("\"id\":\"s_x\"", RouteStore.RoutesJson);
        }

        [Fact]
        public void RejectSharedSteerPoint_on_an_unknown_id_returns_false()
        {
            Assert.False(RouteStore.RejectSharedSteerPoint("does-not-exist"));
        }

        [Fact]
        public void RemoveSharedSteerPoint_drops_a_pending_share()
        {
            RouteStore.ReceiveSharedSteerPoint("{\"id\":\"s_x\",\"name\":\"IP\",\"x\":1,\"z\":2}", "Leader");
            Assert.True(RouteStore.RemoveSharedSteerPoint("s_x"));
            Assert.DoesNotContain("\"id\":\"s_x\"", RouteStore.RoutesJson);
        }

        [Fact]
        public void RemoveSharedSteerPoint_drops_an_already_accepted_point()
        {
            RouteStore.ReceiveSharedSteerPoint("{\"id\":\"s_x\",\"name\":\"IP\",\"x\":1,\"z\":2}", "Leader");
            RouteStore.AcceptSharedSteerPoint("s_x");
            Assert.True(RouteStore.RemoveSharedSteerPoint("s_x"));
            Assert.DoesNotContain("\"id\":\"s_x\"", RouteStore.RoutesJson);
        }

        [Fact]
        public void RemoveSharedSteerPoint_never_touches_a_point_the_pilot_made_themselves()
        {
            SteerPoint p = RouteStore.AddSteerPoint(1f, 2f, "Mine");   // SharedBy empty
            Assert.False(RouteStore.RemoveSharedSteerPoint(p.Id));
            Assert.Contains("\"id\":\"" + p.Id + "\"", RouteStore.RoutesJson);
        }

        [Fact]
        public void SetActiveSteerPoint_rejects_an_unknown_id()
        {
            SteerPoint p = RouteStore.AddSteerPoint(1f, 2f, "A");   // active after creation
            RouteStore.SetActiveSteerPoint("does-not-exist");
            Assert.Contains("\"activeSteerPointId\":null", RouteStore.RoutesJson);
            RouteStore.SetActiveSteerPoint(p.Id);
            Assert.Contains("\"activeSteerPointId\":\"" + p.Id + "\"", RouteStore.RoutesJson);
        }

        [Fact]
        public void CycleSteerPoint_is_a_noop_while_a_route_is_active()
        {
            RouteStore.AddSteerPoint(1f, 2f, "A");
            SteerPoint b = RouteStore.AddSteerPoint(3f, 4f, "B");   // becomes the active steer point
            RouteStore.CreateRoute("R");                            // becomes the active route
            RouteStore.CycleSteerPoint(+1);
            // Without the ActiveRoute guard, +1 from B (index 1 of 2) would wrap to A (index 0) —
            // asserting it's still B proves the guard actually deferred to the active route.
            Assert.Contains("\"activeSteerPointId\":\"" + b.Id + "\"", RouteStore.RoutesJson);
        }

        [Fact]
        public void OnSquadEnded_clears_pending_steerpoint_shares_and_unlocks_accepted_ones()
        {
            RouteStore.ReceiveSharedSteerPoint("{\"id\":\"s_pending\",\"name\":\"P\",\"x\":1,\"z\":1}", "Leader");
            RouteStore.ReceiveSharedSteerPoint("{\"id\":\"s_x\",\"name\":\"IP\",\"x\":1,\"z\":2}", "Leader");
            RouteStore.AcceptSharedSteerPoint("s_x");

            RouteStore.OnSquadEnded();

            Assert.DoesNotContain("\"id\":\"s_pending\"", RouteStore.RoutesJson);   // pending share dropped
            Assert.Contains("\"id\":\"s_x\"", RouteStore.RoutesJson);
            Assert.True(RouteStore.RenameSteerPoint("s_x", "Now mine"));            // unlocked, editable again
        }
    }
}

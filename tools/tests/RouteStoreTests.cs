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
    }
}

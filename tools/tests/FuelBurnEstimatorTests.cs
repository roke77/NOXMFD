using NOXMFD;
using Xunit;

namespace NOXMFD.Tests
{
    public class FuelBurnEstimatorTests
    {
        [Fact]
        public void No_rate_until_a_full_sample_window_has_passed()
        {
            var est = new FuelBurnEstimator();
            est.Sample(1000f, 0f);
            Assert.Equal(-1f, est.RateKgPerSec);
            est.Sample(999.5f, 0.5f);
            Assert.Equal(-1f, est.RateKgPerSec);
            est.Sample(999f, 1f);
            Assert.Equal(1f, est.RateKgPerSec, 3);
        }

        [Fact]
        public void Steady_burn_converges_and_a_throttle_change_moves_toward_the_new_rate()
        {
            var est = new FuelBurnEstimator();
            float kg = 5000f;
            for (int t = 0; t <= 60; t++) { est.Sample(kg, t); kg -= 2f; }
            Assert.Equal(2f, est.RateKgPerSec, 2);

            for (int t = 61; t <= 66; t++) { est.Sample(kg, t); kg -= 6f; }
            Assert.InRange(est.RateKgPerSec, 2.5f, 6f);
        }

        [Fact]
        public void Refuel_clears_the_rate_and_paused_time_adds_no_sample()
        {
            var est = new FuelBurnEstimator();
            est.Sample(1000f, 0f);
            est.Sample(990f, 1f);
            Assert.Equal(10f, est.RateKgPerSec, 3);

            est.Sample(990f, 1f);                       // paused: same game time
            Assert.Equal(10f, est.RateKgPerSec, 3);

            est.Sample(3000f, 2f);                      // refuelled
            Assert.Equal(-1f, est.RateKgPerSec);
            est.Sample(2996f, 3f);
            Assert.Equal(4f, est.RateKgPerSec, 3);
        }

        [Fact]
        public void Clock_going_backwards_starts_a_new_baseline()
        {
            var est = new FuelBurnEstimator();
            est.Sample(1000f, 100f);
            est.Sample(990f, 101f);
            est.Sample(5000f, 0f);                      // mission restart
            est.Sample(4999f, 1f);
            Assert.InRange(est.RateKgPerSec, 1f, 10f);  // smoothed from the old 10 toward the new 1
        }
    }
}

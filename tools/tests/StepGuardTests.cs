using System;
using System.Collections.Generic;
using NOXMFD;

namespace NOXMFD.Tests
{
    public class StepGuardTests
    {
        [Fact]
        public void LogsFirstFailureThenSummarisesRepeatsPerInterval()
        {
            var logs = new List<string>();
            StepGuard.LogWarning = logs.Add;
            var ex = new InvalidOperationException("boom");
            try
            {
                StepGuard.Failed("guard-test A", ex, 1000);
                StepGuard.Failed("guard-test A", ex, 1016);
                StepGuard.Failed("guard-test A", ex, 1032);
                Assert.Single(logs);
                Assert.Contains("guard-test A failed", logs[0]);

                StepGuard.Failed("guard-test A", ex, 1000 + StepGuard.RepeatMs);
                Assert.Equal(2, logs.Count);
                Assert.Contains("3 times since the last report", logs[1]);

                // Steps are tracked independently.
                StepGuard.Failed("guard-test B", ex, 1000 + StepGuard.RepeatMs);
                Assert.Equal(3, logs.Count);
            }
            finally { StepGuard.LogWarning = null; }
        }
    }
}

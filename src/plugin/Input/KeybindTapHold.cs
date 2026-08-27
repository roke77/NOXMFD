namespace NOXMFD
{
    // Pure tap-vs-hold state machine for Keybinds.PollTapHold. The caller owns the two scratch
    // fields so the live BindDef can keep storing them beside the physical input state, while tests
    // can exercise the timing rule without UnityEngine.Time or Rewired.
    internal static class KeybindTapHold
    {
        internal const float DefaultHoldSeconds = 0.35f;

        internal enum Event
        {
            None,
            Tap,
            Hold,
        }

        internal static Event Poll(bool activeNow, float now, ref float pressStartTime, ref bool holdFired)
        {
            return Poll(activeNow, now, DefaultHoldSeconds, ref pressStartTime, ref holdFired);
        }

        internal static Event Poll(bool activeNow, float now, float holdSeconds, ref float pressStartTime, ref bool holdFired)
        {
            if (activeNow)
            {
                if (pressStartTime < 0f)
                {
                    pressStartTime = now;
                    holdFired = false;
                    return Event.Tap;
                }

                if (!holdFired && now - pressStartTime >= holdSeconds)
                {
                    holdFired = true;
                    return Event.Hold;
                }

                return Event.None;
            }

            pressStartTime = -1f;
            return Event.None;
        }
    }
}

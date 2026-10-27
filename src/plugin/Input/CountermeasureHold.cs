namespace NOXMFD
{
    // Pure hold-window rule for the countermeasure keybinds (Keybinds.ReconcileCountermeasure), free
    // of UnityEngine.Time so tests can drive the frame timing. A request holds until now + seconds;
    // Poll runs once per frame, before that frame's requests, and reports whether a request is still
    // live. It compares against the previous Poll's time, not this one's: a held local key makes a
    // 0-second request after each Poll, and that request has to survive to the next frame's Poll.
    internal sealed class CountermeasureHold
    {
        // Times are Time.unscaledTime, never negative, so 0 as the first "previous poll" never
        // matches the initial -infinity request time.
        private float _until = float.NegativeInfinity;
        private float _lastPoll;

        internal byte Category { get; private set; }

        internal void Request(byte category, float now, float seconds)
        {
            Category = category;
            if (now + seconds > _until) _until = now + seconds;
        }

        internal bool Poll(float now)
        {
            bool live = _until >= _lastPoll;
            _lastPoll = now;
            return live;
        }
    }
}

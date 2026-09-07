namespace NOXMFD
{
    // Implemented by each internal-MFD page's content (InternalMfdRwrPage, InternalMfdTgpPage, and
    // whatever comes next) — InternalMfdController builds/owns one page instance per pane and only ever
    // calls Refresh on it each tick; it doesn't need to know what a given pane actually contains.
    // Construction is deliberately NOT part of this interface: each page type takes whatever
    // parameters it needs (parent RectTransform, font, ...) via its own constructor, which is a
    // per-type concern the controller already knows about at the call site.
    internal interface IInternalMfdPage
    {
        void Refresh(TelemetrySnapshot snap);

        // Called once, right before the controller drops its reference to this page (aircraft
        // change, toggle-off, mission end) — the only place a page gets to release anything it
        // owns outside its own UI subtree (a camera rig parented to a TargetCam mount, say),
        // which Destroy(_overlay) alone won't reach. A no-op for a page with nothing but UI.
        void Teardown();
    }
}

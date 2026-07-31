namespace NOXMFD
{
    // Local, minimal copy of ConfigurationManager's attribute bag. ConfigurationManager (the F1 settings
    // menu) is a SOFT dependency — we never reference its assembly. Instead it finds these per-entry hints
    // by reflecting over the ConfigDescription tags and matching THIS class BY TYPE NAME + field name. So
    // the class must be named exactly "ConfigurationManagerAttributes" and the fields must match the ones
    // ConfigurationManager reads; we keep only the one we use. If ConfigurationManager isn't installed the
    // tag is simply ignored and the setting still persists/works.
    // ponytail: minimal on purpose — add a field here only when we actually use it (the upstream class has
    // ~20). Reference: BepInEx ConfigurationManager, ConfigurationManagerAttributes.cs.
    internal sealed class ConfigurationManagerAttributes
    {
        // false = don't show this entry in the menu at all (it still persists + works).
        public bool? Browsable;
    }
}

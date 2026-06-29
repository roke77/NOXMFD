using System;

namespace NOXMFD
{
    // Local, minimal copy of ConfigurationManager's attribute bag. ConfigurationManager (the F1 settings
    // menu) is a SOFT dependency — we never reference its assembly. Instead it finds these per-entry hints
    // by reflecting over the ConfigDescription tags and matching THIS class BY TYPE NAME + field name. So
    // the class must be named exactly "ConfigurationManagerAttributes" and the fields must match the ones
    // ConfigurationManager reads; we keep only the two we use. If ConfigurationManager isn't installed the
    // tag is simply ignored and the setting still persists/works — it just lacks the custom UI.
    // ponytail: minimal on purpose — add a field here only when we actually use it (the upstream class has
    // ~20). Reference: BepInEx ConfigurationManager, ConfigurationManagerAttributes.cs.
    internal sealed class ConfigurationManagerAttributes
    {
        // Replaces the default value editor for this entry with our own IMGUI drawer.
        public Action<BepInEx.Configuration.ConfigEntryBase>? CustomDrawer;

        // Hide the default "Reset"/"Default" button so our drawer owns the whole row.
        public bool? HideDefaultButton;

        // false = don't show this entry in the menu at all (it still persists + works).
        public bool? Browsable;
    }
}

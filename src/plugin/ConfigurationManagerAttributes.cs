namespace NOXMFD
{
    // ConfigurationManager (F1 menu) is a soft dependency: it reflects over ConfigDescription tags,
    // matching this class by exact type name and field name. Fields must match what it reads.
    // ponytail: only fields we use are present (upstream has ~20); add more only as needed.
    internal sealed class ConfigurationManagerAttributes
    {
        // false = hide entry from the menu; setting still persists/works.
        public bool? Browsable;
    }
}

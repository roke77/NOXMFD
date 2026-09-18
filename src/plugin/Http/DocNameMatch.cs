using System;

namespace NOXMFD
{
    // The path-traversal-safe file-name matching rule behind DocEndpoint's /doc-image (issue #82),
    // pulled into its own pure file (no BepInEx/HTTP touchpoint) so it's directly unit-tested
    // (tools/tests/DocNameMatchTests.cs) — same "pure decision rule split into its own file for the
    // standalone test project" shape as CommandContentType.cs/SoiRing.cs.
    internal static class DocNameMatch
    {
        // requested is matched by exact, case-sensitive string equality against files — the live
        // listing the caller already read, never Path.Combine'd with the raw value here — so a
        // path-traversal name ("../../secrets.png") simply won't match anything and the caller 404s
        // rather than resolving outside the kneeboard folder.
        internal static string? Find(string[] files, string requested)
        {
            if (string.IsNullOrEmpty(requested)) return null;
            foreach (string name in files)
                if (string.Equals(name, requested, StringComparison.Ordinal)) return name;
            return null;
        }
    }
}

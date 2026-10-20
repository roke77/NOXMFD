"""Build NOXMFD_<version>_thunderstore.zip for upload to thunderstore.io (team roke77).

Run after `dotnet build -c Release`. version_number comes from <Version> in NOXMFD.csproj,
so thunderstore/manifest.json never carries one of its own.
"""
import json
import re
import struct
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "thunderstore"
DLL = ROOT / "bin" / "Release" / "netstandard2.1" / "NOXMFD.dll"


def main():
    version = re.search(r"<Version>(\d+\.\d+\.\d+)</Version>", (ROOT / "NOXMFD.csproj").read_text()).group(1)
    manifest = json.loads((SRC / "manifest.json").read_text(encoding="utf-8"))
    manifest["version_number"] = version

    # Thunderstore rejects the upload on any of these; fail here instead.
    assert re.fullmatch(r"\w{1,128}", manifest["name"], re.ASCII), "name: letters, digits, _ only"
    assert len(manifest["description"]) <= 250, f"description is {len(manifest['description'])} chars, max 250"
    icon = (SRC / "icon.png").read_bytes()
    assert icon[:8] == b"\x89PNG\r\n\x1a\n" and struct.unpack(">II", icon[16:24]) == (256, 256), "icon.png must be a 256x256 PNG"
    if not DLL.exists():
        sys.exit(f"{DLL} not found: run `dotnet build -c Release` first")

    out = ROOT / f"NOXMFD_{version}_thunderstore.zip"
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
        z.writestr("manifest.json", json.dumps(manifest, indent=2))
        z.writestr("icon.png", icon)
        z.write(SRC / "README.md", "README.md")
        z.write(DLL, "plugins/NOXMFD.dll")
    print(out)


if __name__ == "__main__":
    main()

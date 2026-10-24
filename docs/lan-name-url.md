# LAN name URL

## Status

**Merged to `main` and released in 0.75.0; confirmed in game from a Mac and an Android phone.** See
"Still needs a manual in-game check" for the rest.

## Goal

A URL for the display that a player can write down once and that keeps working on any device on
their network, even after the router hands the game PC a new IP.

## Design

Two layers behind one name URL, `nameUrl` in `/config`. The classic MAIN card (full view and split
pane, `pages/main/main-urls.js`) shows it alone, with a "URL not working? Check here" hint whose
link reveals the PC (localhost) and NET (IP) URLs; the F-35 strip shows it alone (localhost when
there is none):

1. **PC name, fallback.** Windows 10/11 answers mDNS for its own host name, and the server's
   wildcard `http://+:<port>/` binding accepts any `Host` header, so `http://<pc-name>.local:<port>`
   already reaches the server. The plugin only displays it (`Dns.GetHostName()`, lowercased; hidden
   when the name isn't a valid DNS label).
2. **Fixed name, default.** `[Network] LanName` (`noxmfd` by default; cleared = the PC name above,
   which is also the fallback when the responder can't start). When set and LAN access
   is up, `MdnsResponder` joins 224.0.0.251:5353 (shared with Windows' own responder through
   `ReuseAddress`) and answers A questions for `<LanName>.local` with the local address on the
   asker's subnet. AAAA gets an NSEC denial. No probing or conflict detection: each PC on a network
   needs its own name, which the setting's description and NETWORKING.md say.

A DHCP reservation in the router is the fallback that works everywhere; NETWORKING.md covers it.

Rejected: a cloud relay mapping public IP to LAN IP (browsers block an https page reaching into
an http LAN address, and it needs hosted infrastructure); a separate add-on package for the
responder (a config setting in the same DLL covers turning it off without a second release
pipeline).

## Verification

Done on the dev PC (Windows 11, wired, Private profile), with `MdnsResponder` in a scratch
console host:

- `Dns.GetHostAddresses("<name>.local")` resolves to the PC's LAN IP while the responder runs,
  fails after it stops.
- Windows' resolver sends A and AAAA together and ignores the NSEC, so the first lookup from a
  Windows client waits out its AAAA retries (~2.7s); later lookups come from cache.
- The first run of a fresh executable that binds UDP 5353 raised a Windows Firewall prompt. The
  game's own executable typically already has an inbound allow rule from Steam's first launch.

Still needs a manual in-game check:

- With `LanName` empty: MAIN (bezel and F-35 strip) shows `http://<pc-name>.local:5005` and it
  opens from a phone and another PC.
- With `LanName = noxmfd`: the log shows `LAN name: http://noxmfd.local:5005/`, and the URL opens
  from other devices (confirmed in-game from a Mac and an Android phone); still to try from an
  iPhone/iPad and another Windows PC.
- An invalid `LanName` (e.g. `my_pc`) logs a warning and MAIN falls back to the PC name.
- Legacy unicast queries (one-shot resolvers on a port other than 5353) get a reply. Unit-tested
  only; a local multicast probe from the same PC didn't loop back to the responder.
- Under Proton: whether the bind to 5353 coexists with Avahi, and whether the PC-name URL resolves.

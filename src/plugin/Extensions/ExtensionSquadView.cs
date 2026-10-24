using System.Collections.Generic;

namespace NOXMFD
{
    // A faction-mate running NOXMFD, as the extension API reports them (docs/extension-messaging.md).
    // Designation and the squad fields come from the pilot's own presence beat (FactionIdentity.cs):
    // "" / 0 when they have no callsign, are in no squad, or haven't announced one yet.
    public sealed class FactionPilot
    {
        public readonly ulong SteamId;
        public readonly string Name;            // Steam name, never a designation
        public readonly string Designation;     // e.g. "VIPER 2-1"
        public readonly string SquadCallsign;
        public readonly int SquadFlight;
        public readonly ulong SquadLeaderId;

        public FactionPilot(ulong steamId, string name, string designation, string squadCallsign, int squadFlight, ulong squadLeaderId)
        {
            SteamId = steamId; Name = name; Designation = designation;
            SquadCallsign = squadCallsign; SquadFlight = squadFlight; SquadLeaderId = squadLeaderId;
        }
    }

    public enum SquadRole { None, Leader, Member }

    // This pilot's own squad standing. Callsign/Flight/LeaderId are "" / 0 outside a squad.
    public sealed class LocalSquad
    {
        public readonly SquadRole Role;
        public readonly string Callsign;
        public readonly int Flight;
        public readonly ulong LeaderId;
        public readonly string Designation;     // this pilot's own, "" when no callsign is set

        public LocalSquad(SquadRole role, string callsign, int flight, ulong leaderId, string designation)
        {
            Role = role; Callsign = callsign; Flight = flight; LeaderId = leaderId; Designation = designation;
        }
    }

    // Builds the two views above from PlayerRoster, Presence and Squad. Main thread only: all three
    // keep main-thread state.
    internal static class ExtensionSquadView
    {
        internal static List<FactionPilot> FactionPilots()
        {
            var result = new List<FactionPilot>();
            ulong self = Squadron.SelfId();
            foreach (ulong id in PlayerRoster.FactionSteamIds())
            {
                if (id == self || !Presence.HasNoxmfd(id)) continue;
                string name = PlayerRoster.SteamNameFor(id);
                // An older client's beat carries no identity record: listed, with no callsign or squad.
                result.Add(Presence.TryGetIdentity(id, out FactionIdentity.Identity who)
                    ? new FactionPilot(id, name, who.Designation, who.SquadCallsign, who.SquadFlight, who.LeaderId)
                    : new FactionPilot(id, name, string.Empty, string.Empty, 0, 0));
            }
            return result;
        }

        internal static LocalSquad Local()
        {
            FactionIdentity.Identity me = Squad.SelfIdentity(null);
            SquadRole role = Squad.IsLeader ? SquadRole.Leader : Squad.IsMember ? SquadRole.Member : SquadRole.None;
            return new LocalSquad(role, me.SquadCallsign, me.SquadFlight, me.LeaderId, me.Designation);
        }
    }
}

using Session.Core.Identity;

namespace Session.Core.Attendant
{
    /// <summary>
    /// The four things the building objects to. Not noise, not sprinting, not screaming —
    /// the Attendant does not care that players are frightened. It cares about the third principle:
    /// no room may be left unfinished.
    /// </summary>
    public enum ViolationKind
    {
        None = 0,

        /// <summary>Left a room whose puzzles are not finished. The cardinal sin.</summary>
        LeftRoomUnfinished = 1,

        /// <summary>Re-entered a room already completed. Patients do not walk back down the corridor.</summary>
        BacktrackedIntoCompletedRoom = 2,

        /// <summary>Forced a door rather than answering the honest question.</summary>
        ForcedDoor = 3,

        /// <summary>Exceeded the room's time allowance. The room is patient. It is not infinitely patient.</summary>
        TimeAllowanceExceeded = 4
    }

    public readonly struct ProtocolViolation
    {
        public readonly ViolationKind Kind;
        public readonly PlayerId Player;
        public readonly RoomId Room;
        public readonly float AtTime;

        public ProtocolViolation(ViolationKind kind, PlayerId player, RoomId room, float atTime)
        {
            Kind = kind;
            Player = player;
            Room = room;
            AtTime = atTime;
        }

        public bool IsNone => Kind == ViolationKind.None;

        public override string ToString()
        {
            return Kind + " by " + Player + " in " + Room + " at t=" + AtTime.ToString("0.00");
        }
    }
}

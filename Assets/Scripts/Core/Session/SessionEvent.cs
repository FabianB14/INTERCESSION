using Session.Core.Identity;

namespace Session.Core.Session
{
    public enum SessionEventKind
    {
        None = 0,

        /// <summary>A puzzle node was solved. <c>Node</c> identifies which.</summary>
        PuzzleSolved = 1,

        /// <summary>Every exit node in a room is solved. The door opens.</summary>
        RoomCompleted = 2,

        /// <summary>A protocol violation was recorded. <c>Payload</c> is the ViolationKind.</summary>
        ProtocolViolation = 3,

        /// <summary>The Attendant changed state. <c>Payload</c> is the AttendantState.</summary>
        AttendantStateChanged = 4
    }

    /// <summary>
    /// Something the server decided that clients need to know about. Flat and unmanaged so it can
    /// sit in a ring buffer and be copied into a network message without allocating.
    ///
    /// Note what is absent: no puzzle solutions, no lens contents, no other player's variant ids.
    /// Events say what happened, never what the answer was.
    /// </summary>
    public readonly struct SessionEvent
    {
        public readonly SessionEventKind Kind;
        public readonly PlayerId Player;
        public readonly RoomId Room;
        public readonly PuzzleNodeId Node;
        public readonly float AtTime;

        /// <summary>Kind-specific extra. See <see cref="SessionEventKind"/>.</summary>
        public readonly int Payload;

        public SessionEvent(
            SessionEventKind kind, PlayerId player, RoomId room, PuzzleNodeId node, float atTime, int payload = 0)
        {
            Kind = kind;
            Player = player;
            Room = room;
            Node = node;
            AtTime = atTime;
            Payload = payload;
        }

        public override string ToString()
            => Kind + " player=" + Player + " room=" + Room + " node=" + Node + " t=" + AtTime.ToString("0.00");
    }
}

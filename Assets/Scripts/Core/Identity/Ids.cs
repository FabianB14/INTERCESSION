using System;

namespace Session.Core.Identity
{
    /// <summary>A player slot in one session. Value is a dense index 0..MaxPlayers-1, not a Steam ID.</summary>
    public readonly struct PlayerId : IEquatable<PlayerId>
    {
        public static readonly PlayerId None = new PlayerId(-1);

        public readonly int Value;

        public PlayerId(int value)
        {
            Value = value;
        }

        public bool IsNone => Value < 0;

        public bool Equals(PlayerId other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is PlayerId other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => IsNone ? "Player(none)" : "Player(" + Value + ")";

        public static bool operator ==(PlayerId a, PlayerId b) => a.Value == b.Value;
        public static bool operator !=(PlayerId a, PlayerId b) => a.Value != b.Value;
    }

    /// <summary>Rooms are numbered, never named. See LORE.md — signage says "Room 9", never "the nursery".</summary>
    public readonly struct RoomId : IEquatable<RoomId>
    {
        public static readonly RoomId None = new RoomId(0);

        public readonly int Value;

        public RoomId(int value)
        {
            Value = value;
        }

        public bool IsNone => Value == 0;

        public bool Equals(RoomId other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is RoomId other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => IsNone ? "Room(none)" : "Room " + Value;

        public static bool operator ==(RoomId a, RoomId b) => a.Value == b.Value;
        public static bool operator !=(RoomId a, RoomId b) => a.Value != b.Value;
    }

    /// <summary>A physical object in the canonical room. The same PropId for every player.</summary>
    public readonly struct PropId : IEquatable<PropId>
    {
        public static readonly PropId None = new PropId(0);

        public readonly int Value;

        public PropId(int value)
        {
            Value = value;
        }

        public bool IsNone => Value == 0;

        public bool Equals(PropId other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is PropId other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => IsNone ? "Prop(none)" : "Prop(" + Value + ")";

        public static bool operator ==(PropId a, PropId b) => a.Value == b.Value;
        public static bool operator !=(PropId a, PropId b) => a.Value != b.Value;
    }

    /// <summary>
    /// One piece of information a puzzle needs. Canonical: the clue "the third digit is 4" is the
    /// same fact for everyone. Only the surface it is written on differs per lens.
    /// </summary>
    public readonly struct ClueId : IEquatable<ClueId>
    {
        public static readonly ClueId None = new ClueId(0);

        public readonly int Value;

        public ClueId(int value)
        {
            Value = value;
        }

        public bool IsNone => Value == 0;

        public bool Equals(ClueId other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is ClueId other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => IsNone ? "Clue(none)" : "Clue(" + Value + ")";

        public static bool operator ==(ClueId a, ClueId b) => a.Value == b.Value;
        public static bool operator !=(ClueId a, ClueId b) => a.Value != b.Value;
    }

    /// <summary>
    /// One intake tape. Recordings are canonical — every player hears the same words, because a
    /// tape is a recording of a real man's voice, not a reconstruction the building assembled.
    /// </summary>
    public readonly struct TapeId : IEquatable<TapeId>
    {
        public static readonly TapeId None = new TapeId(0);

        public readonly int Value;

        public TapeId(int value)
        {
            Value = value;
        }

        public bool IsNone => Value == 0;

        public bool Equals(TapeId other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is TapeId other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => IsNone ? "Tape(none)" : "Tape(" + Value + ")";

        public static bool operator ==(TapeId a, TapeId b) => a.Value == b.Value;
        public static bool operator !=(TapeId a, TapeId b) => a.Value != b.Value;
    }

    /// <summary>A node in a room's puzzle graph.</summary>
    public readonly struct PuzzleNodeId : IEquatable<PuzzleNodeId>
    {
        public static readonly PuzzleNodeId None = new PuzzleNodeId(0);

        public readonly int Value;

        public PuzzleNodeId(int value)
        {
            Value = value;
        }

        public bool IsNone => Value == 0;

        public bool Equals(PuzzleNodeId other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is PuzzleNodeId other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => IsNone ? "Node(none)" : "Node(" + Value + ")";

        public static bool operator ==(PuzzleNodeId a, PuzzleNodeId b) => a.Value == b.Value;
        public static bool operator !=(PuzzleNodeId a, PuzzleNodeId b) => a.Value != b.Value;
    }
}

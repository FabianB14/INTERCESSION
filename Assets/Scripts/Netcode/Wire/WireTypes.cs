using Session.Core.Identity;
using Session.Core.Session;
using Unity.Netcode;

namespace Session.Netcode.Wire
{
    /// <summary>
    /// Network-serialisable mirror of <see cref="SessionEvent"/>.
    ///
    /// Session.Core cannot implement <c>INetworkSerializable</c> — that interface lives in
    /// Unity.Netcode, and Core compiles without any Unity assembly at all. So the wire format
    /// lives here, at the boundary, and Core stays testable in milliseconds. The cost is one
    /// struct copy per event, which is nothing next to what it buys.
    /// </summary>
    public struct SessionEventWire : INetworkSerializable
    {
        public byte Kind;
        public int Player;
        public int Room;
        public int Node;
        public float AtTime;
        public int Payload;

        public static SessionEventWire From(in SessionEvent source)
        {
            return new SessionEventWire
            {
                Kind = (byte)source.Kind,
                Player = source.Player.Value,
                Room = source.Room.Value,
                Node = source.Node.Value,
                AtTime = source.AtTime,
                Payload = source.Payload
            };
        }

        public SessionEvent ToEvent()
        {
            return new SessionEvent(
                (SessionEventKind)Kind,
                new PlayerId(Player),
                new RoomId(Room),
                new PuzzleNodeId(Node),
                AtTime,
                Payload);
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Kind);
            serializer.SerializeValue(ref Player);
            serializer.SerializeValue(ref Room);
            serializer.SerializeValue(ref Node);
            serializer.SerializeValue(ref AtTime);
            serializer.SerializeValue(ref Payload);
        }
    }
}

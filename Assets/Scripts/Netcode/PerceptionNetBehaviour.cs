using Session.Core.Identity;
using Session.Core.Perception;
using Session.Core.Rooms;
using Session.Runtime.Tuning;
using Session.Runtime.View;
using Unity.Netcode;
using UnityEngine;

namespace Session.Netcode
{
    /// <summary>
    /// Applies this peer's lens to the props in a room.
    ///
    /// Nothing about perception is replicated. The server sends one 64-bit session seed; every peer
    /// runs the same deterministic <see cref="LensAssigner"/> locally and arrives at the same
    /// answer. That is worth being explicit about, because the obvious implementation — server
    /// computes lenses and tells each client what it sees — has two problems this one does not:
    ///
    ///   1. Bandwidth: a variant id per prop per player, re-sent on every restage.
    ///   2. Cheating: a modified client that receives the whole assignment can read every other
    ///      player's room, and the entire game collapses. Here, a client can only derive its own
    ///      lens, because deriving another player's requires nothing it does not already have —
    ///      which is precisely why the lens must never be the thing that hides the answer. It
    ///      isn't: the canonical solution lives only on the server, and only tokens are submitted.
    ///
    /// Point 2 is worth stating plainly: a determined cheat can compute every lens from the seed.
    /// That is acceptable, and by design. Knowing what another player sees does not solve a room —
    /// the puzzle graph still requires clue inputs the server never sends, and the solution is
    /// never on the wire. The lens split is a co-operation mechanic, not a security boundary.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PerceptionNetBehaviour : NetworkBehaviour
    {
        [SerializeField] private SessionCatalogSO _catalog;

        [Tooltip("Room this instance renders. Matches the RoomLayoutSO's room number.")]
        [SerializeField, Min(1)] private int _roomNumber = 1;

        [Tooltip("Prop views in this room, in any order. Each declares its own PropId.")]
        [SerializeField] private PropView[] _props = new PropView[0];

        private RoomDefinition _definition;
        private Lens _lens;
        private bool _applied;

        public override void OnNetworkSpawn()
        {
            SessionDirectorNetBehaviour director = SessionDirectorNetBehaviour.Instance;
            if (director != null)
            {
                // Someone joining or leaving restages every room, so the lens this peer derived is
                // stale the moment the count changes. Without this, a mid-run join leaves everyone
                // rendering a room the server is no longer staging.
                director.StagedPlayerCountChanged += OnStagedPlayerCountChanged;
            }

            TryApply();
        }

        public override void OnNetworkDespawn()
        {
            SessionDirectorNetBehaviour director = SessionDirectorNetBehaviour.Instance;
            if (director != null)
            {
                director.StagedPlayerCountChanged -= OnStagedPlayerCountChanged;
            }
        }

        private void OnStagedPlayerCountChanged(int playerCount) => Invalidate();

        private void Update()
        {
            // The seed and the slot arrive over the network and may not be ready on the frame this
            // spawns. Retry until both exist, then stop checking.
            if (!_applied)
            {
                TryApply();
            }
        }

        private void TryApply()
        {
            SessionDirectorNetBehaviour director = SessionDirectorNetBehaviour.Instance;
            if (director == null || director.SessionSeed == 0UL || director.LocalSlot < 0)
            {
                return;
            }

            if (_catalog == null)
            {
                Debug.LogError("[Session] PerceptionNetBehaviour on '" + name + "' has no catalog. Props will not render correctly.");
                enabled = false;
                return;
            }

            if (_definition == null && !TryBuildDefinition())
            {
                enabled = false;
                return;
            }

            // The count comes from the server, not from NetworkManager.ConnectedClientsIds — that
            // list is only populated on the server, so on a client it would silently produce a
            // different number, a different lens, and a room nobody else is looking at. That
            // failure only shows up with three or more players and reads as "perception is
            // broken" rather than "one integer came from the wrong place".
            int playerCount = director.StagedPlayerCount;
            if (playerCount <= 0)
            {
                // Rooms have not been staged yet. Retry next frame.
                return;
            }

            ILensRules rules = _catalog.LensRules == null ? DefaultLensRules.Instance : _catalog.LensRules;

            if (!LensAssigner.TryAssign(
                    _definition, director.SessionSeed, playerCount, rules,
                    out LensAssignment assignment, out LensAssignmentFailure failure))
            {
                // Not fatal while the lobby is still filling — rooms stage once the group is big
                // enough. Anything else is an authoring bug and must be loud.
                if (failure != LensAssignmentFailure.TooFewPlayers)
                {
                    Debug.LogError("[Session] Room " + _roomNumber + " could not assign lenses: " + failure);
                    enabled = false;
                }

                return;
            }

            if (director.LocalSlot >= assignment.PlayerCount)
            {
                return;
            }

            _lens = assignment.For(director.LocalSlot);
            ApplyToProps();
            _applied = true;
        }

        private bool TryBuildDefinition()
        {
            var rooms = _catalog.BuildRooms();
            for (int i = 0; i < rooms.Count; i++)
            {
                if (rooms[i].Id.Value == _roomNumber)
                {
                    _definition = rooms[i];
                    return true;
                }
            }

            Debug.LogError("[Session] Catalog has no room numbered " + _roomNumber + ".");
            return false;
        }

        private void ApplyToProps()
        {
            for (int i = 0; i < _props.Length; i++)
            {
                PropView view = _props[i];
                if (view == null)
                {
                    continue;
                }

                if (!_definition.TryGetOrdinal(new PropId(view.PropId), out int ordinal))
                {
                    Debug.LogWarning(
                        "[Session] Prop " + view.PropId + " on '" + view.name +
                        "' is not in room " + _roomNumber + "'s layout. It will render its default variant.");
                    continue;
                }

                PropDefinition prop = _definition.PropAt(ordinal);
                int variantIndex = _lens.VariantIndex(ordinal);

                view.Apply(variantIndex, prop.VariantAt(variantIndex), _lens.RevealsClue(ordinal));
            }
        }

        /// <summary>Re-derive and re-apply. Call when the group size changes and rooms restage.</summary>
        public void Invalidate()
        {
            _applied = false;
            enabled = true;
        }
    }
}

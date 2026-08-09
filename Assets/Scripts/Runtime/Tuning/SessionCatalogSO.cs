using System.Collections.Generic;
using Session.Core.Rooms;
using UnityEngine;

namespace Session.Runtime.Tuning
{
    /// <summary>
    /// Everything a run is assembled from: the rooms in Protocol order, plus the tuning assets the
    /// server needs. One of these is referenced by the session's NetworkBehaviour.
    /// </summary>
    [CreateAssetMenu(menuName = "Session/Session Catalog", fileName = "SO_SessionCatalog")]
    public sealed class SessionCatalogSO : ScriptableObject
    {
        [Header("Rooms, in Protocol order")]
        [SerializeField] private List<RoomLayoutSO> _rooms = new List<RoomLayoutSO>();

        [Header("Tuning")]
        // Nullable annotations are deliberately absent: nullable is enabled in Session.Core only,
        // so `?` here would just produce CS8632. Unity serialised references can always be null;
        // callers check.
        [SerializeField] private LensRulesSO _lensRules;

        [SerializeField] private AttendantProfileSO _attendantProfile;

        [SerializeField] private MovementRulesSO _movementRules;

        [SerializeField] private VoiceRulesSO _voiceRules;

        public LensRulesSO LensRules => _lensRules;

        public AttendantProfileSO AttendantProfile => _attendantProfile;

        public MovementRulesSO MovementRules => _movementRules;

        public VoiceRulesSO VoiceRules => _voiceRules;

        public int RoomCount => _rooms.Count;

        /// <summary>
        /// Build every room's runtime definition. Called once on the server when a run starts.
        /// Throws with the offending asset named if any room is authored inconsistently.
        /// </summary>
        public List<RoomDefinition> BuildRooms()
        {
            var built = new List<RoomDefinition>(_rooms.Count);

            for (int i = 0; i < _rooms.Count; i++)
            {
                RoomLayoutSO layout = _rooms[i];
                if (layout == null)
                {
                    Debug.LogWarning("[Session] Catalog '" + name + "' has an empty room slot at index " + i + ".");
                    continue;
                }

                built.Add(layout.Build());
            }

            return built;
        }
    }
}

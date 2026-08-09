using Session.Core.Rooms;
using UnityEngine;

namespace Session.Runtime.View
{
    /// <summary>
    /// The visible half of one prop. Swaps to whichever variant this player's lens selected.
    ///
    /// Variants are authored as child GameObjects rather than swapped materials, because a variant
    /// is a different object — a hospital privacy curtain and a childhood shower curtain do not
    /// share a mesh. Exactly one child is enabled at a time.
    ///
    /// Nothing here decides which variant to show. That is the lens's job, and the lens is derived
    /// in Session.Core from the session seed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PropView : MonoBehaviour
    {
        [Tooltip("Canonical prop id. Must match the PropId in the room's RoomLayoutSO.")]
        [SerializeField, Min(1)] private int _propId = 1;

        [Tooltip("One child per variant, in the same order as the RoomLayoutSO's variant list.")]
        [SerializeField] private GameObject[] _variantObjects = new GameObject[0];

        [Tooltip("Optional. Enabled only when this player's lens can read the prop's clue.")]
        [SerializeField] private GameObject _clueSurface;

        [Header("Interaction")]
        [Tooltip("The accent colour #FF8A3D means interactable and is never decorative. Only set this on props players can use.")]
        [SerializeField] private bool _isInteractable;

        private int _activeVariant = -1;

        public int PropId => _propId;

        public bool IsInteractable => _isInteractable;

        /// <summary>Content key for the name this player would use out loud. Set when a variant is applied.</summary>
        public int DisplayNameKey { get; private set; }

        /// <summary>Whether this player can read the prop's clue. Drives whether the clue surface is legible.</summary>
        public bool RevealsClue { get; private set; }

        /// <summary>
        /// Which variant this player's lens selected, or -1 before one has been applied. Paper
        /// props read this to pick the matching document — the same sheet is a different document
        /// per lens.
        /// </summary>
        public int ActiveVariant => _activeVariant;

        /// <summary>Raised when a variant is applied, so companions like PaperPropView can follow.</summary>
        public event System.Action<int, bool> VariantApplied;

        public void Apply(int variantIndex, in PropVariant variant, bool revealsClue)
        {
            DisplayNameKey = variant.DisplayNameKey;
            RevealsClue = revealsClue;

            if (_clueSurface != null)
            {
                _clueSurface.SetActive(revealsClue);
            }

            if (variantIndex == _activeVariant)
            {
                return;
            }

            if (_variantObjects.Length == 0)
            {
                // No swappable children — a paper prop whose variance is entirely in its text, for
                // instance. Still record the variant and notify, or companions never hear about it.
                _activeVariant = variantIndex;
                VariantApplied?.Invoke(variantIndex, revealsClue);
                return;
            }

            if (variantIndex < 0 || variantIndex >= _variantObjects.Length)
            {
                Debug.LogError(
                    "[Session] Prop " + _propId + " on '" + name + "' was asked for variant " + variantIndex +
                    " but only has " + _variantObjects.Length +
                    " variant objects. The prefab and the RoomLayoutSO have drifted apart.");
                return;
            }

            for (int i = 0; i < _variantObjects.Length; i++)
            {
                GameObject child = _variantObjects[i];
                if (child != null)
                {
                    child.SetActive(i == variantIndex);
                }
            }

            _activeVariant = variantIndex;
            VariantApplied?.Invoke(variantIndex, revealsClue);
        }
    }
}

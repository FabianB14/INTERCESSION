using Session.Core.Documents;
using Session.Runtime.Tuning;
using UnityEngine;

namespace Session.Runtime.View
{
    /// <summary>
    /// A prop that can be picked up and read. Sits alongside <see cref="PropView"/> and supplies
    /// the document matching whichever variant the lens selected.
    ///
    /// One entry per variant, in the same order as the prop's variant list in the RoomLayoutSO —
    /// because the two players holding "the same" sheet are holding genuinely different documents.
    /// One has the admission form with the ward number on it; the other has the discharge
    /// checklist, which is a real, complete, honest document that simply never mentions it.
    ///
    /// That is the difference between this and a censored document, and it is worth the extra
    /// authoring: the Institute is not hiding anything from anyone. It is showing each person the
    /// honest version.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PropView))]
    public sealed class PaperPropView : MonoBehaviour
    {
        [Tooltip("One document per prop variant, in the same order as the RoomLayoutSO's variant list.")]
        [SerializeField] private DocumentSO[] _documentsByVariant = new DocumentSO[0];

        private PropView _prop;
        private DocumentDefinition[] _built;
        private int _variant = -1;
        private bool _revealsClue;

        /// <summary>The document this player would read. Null until a lens has been applied.</summary>
        public DocumentDefinition Document
        {
            get
            {
                if (_built == null || _variant < 0 || _variant >= _built.Length)
                {
                    return null;
                }

                return _built[_variant];
            }
        }

        public bool RevealsClue => _revealsClue;

        public DocumentSO Source =>
            _variant >= 0 && _variant < _documentsByVariant.Length ? _documentsByVariant[_variant] : null;

        private void Awake()
        {
            _prop = GetComponent<PropView>();

            // Built once. Parsing content keys on every pickup would be a lot of hashing for
            // something a player does dozens of times a run.
            _built = new DocumentDefinition[_documentsByVariant.Length];
            for (int i = 0; i < _documentsByVariant.Length; i++)
            {
                if (_documentsByVariant[i] == null)
                {
                    continue;
                }

                _built[i] = _documentsByVariant[i].Build();
            }
        }

        private void OnEnable()
        {
            _prop.VariantApplied += OnVariantApplied;

            // The lens may already have been applied before this enabled.
            if (_prop.ActiveVariant >= 0)
            {
                OnVariantApplied(_prop.ActiveVariant, _prop.RevealsClue);
            }
        }

        private void OnDisable()
        {
            _prop.VariantApplied -= OnVariantApplied;
        }

        private void OnVariantApplied(int variant, bool revealsClue)
        {
            _variant = variant;
            _revealsClue = revealsClue;

            if (variant >= 0 && variant < _documentsByVariant.Length && _documentsByVariant[variant] == null)
            {
                Debug.LogError(
                    "[Session] Paper prop '" + name + "' has no document for variant " + variant +
                    ". That lens will pick up a blank sheet.");
            }
        }
    }
}

using System;
using Session.Core.Lobby;
using Session.Runtime.Tuning;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Session.UI.Lobby
{
    /// <summary>
    /// Host / join / invite, plus the roster and the ready gate.
    ///
    /// The start button's enabled state comes from <see cref="LobbyRoster.CanStart"/> in Core, not
    /// from a check written here. That matters: a room is authored so that every player holds a
    /// clue nobody else does, so starting a run below the minimum group size does not produce a
    /// hard game — it produces an unsolvable one. The gate is a rule, and rules live in Core.
    ///
    /// Presentation only. It raises events; a binder in Session.Steam does the Steam work, which is
    /// what keeps this assembly compiling with no packages installed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LobbyView : MonoBehaviour
    {
        [Serializable]
        public sealed class SlotWidgets
        {
            public GameObject Root;
            public TMP_Text NameLabel;
            public Image ReadyLight;
            public GameObject EmptyPlaceholder;
        }

        [SerializeField] private Button _hostButton;

        [SerializeField] private Button _inviteButton;

        [SerializeField] private Button _readyButton;

        [SerializeField] private Button _startButton;

        [SerializeField] private Button _leaveButton;

        [SerializeField] private TMP_Text _statusLabel;

        [SerializeField] private UiPaletteSO _palette;

        [SerializeField] private SlotWidgets[] _slots = new SlotWidgets[4];

        private LobbyRoster _roster;
        private int _localSlot = -1;
        private bool _isHost;

        public event Action HostRequested;

        public event Action InviteRequested;

        public event Action<bool> ReadyToggled;

        public event Action StartRequested;

        public event Action LeaveRequested;

        private void Awake()
        {
            if (_hostButton != null)
            {
                _hostButton.onClick.AddListener(() => HostRequested?.Invoke());
            }

            if (_inviteButton != null)
            {
                _inviteButton.onClick.AddListener(() => InviteRequested?.Invoke());
            }

            if (_readyButton != null)
            {
                _readyButton.onClick.AddListener(OnReadyClicked);
            }

            if (_startButton != null)
            {
                _startButton.onClick.AddListener(() => StartRequested?.Invoke());
            }

            if (_leaveButton != null)
            {
                _leaveButton.onClick.AddListener(() => LeaveRequested?.Invoke());
            }
        }

        /// <summary>Attach the roster this view reflects. Called by the binder once connected.</summary>
        public void Bind(LobbyRoster roster, int localSlot, bool isHost)
        {
            if (_roster != null)
            {
                _roster.Changed -= Redraw;
            }

            _roster = roster;
            _localSlot = localSlot;
            _isHost = isHost;

            if (_roster != null)
            {
                _roster.Changed += Redraw;
            }

            Redraw();
        }

        private void OnDestroy()
        {
            if (_roster != null)
            {
                _roster.Changed -= Redraw;
            }
        }

        private void OnReadyClicked()
        {
            if (_roster == null || _localSlot < 0)
            {
                return;
            }

            ReadyToggled?.Invoke(!_roster.IsReady(_localSlot));
        }

        /// <summary>Show an error without dressing it up. Steam failures are not the Institute talking.</summary>
        public void ShowError(string message)
        {
            if (_statusLabel == null)
            {
                return;
            }

            _statusLabel.text = message;

            if (_palette != null)
            {
                _statusLabel.color = _palette.OxideRed;
            }
        }

        private void Redraw()
        {
            if (_roster == null)
            {
                return;
            }

            for (int i = 0; i < _slots.Length; i++)
            {
                SlotWidgets widgets = _slots[i];
                if (widgets == null)
                {
                    continue;
                }

                bool occupied = i < _roster.Capacity && _roster.IsOccupied(i);

                if (widgets.Root != null)
                {
                    widgets.Root.SetActive(occupied);
                }

                if (widgets.EmptyPlaceholder != null)
                {
                    widgets.EmptyPlaceholder.SetActive(!occupied);
                }

                if (widgets.ReadyLight != null)
                {
                    bool ready = occupied && _roster.IsReady(i);
                    widgets.ReadyLight.enabled = ready;

                    if (ready && _palette != null)
                    {
                        widgets.ReadyLight.color = _palette.InstitutionalGreen;
                    }
                }
            }

            // Only the host can begin, and only when Core says the group is startable.
            if (_startButton != null)
            {
                _startButton.gameObject.SetActive(_isHost);
                _startButton.interactable = _roster.CanStart;
            }

            if (_statusLabel != null && _palette != null)
            {
                _statusLabel.color = _palette.Cream;
            }
        }

        /// <summary>
        /// Set a display name on a slot. Steam names arrive asynchronously, so this is pushed in
        /// rather than pulled during redraw.
        /// </summary>
        public void SetSlotName(int slot, string displayName)
        {
            if (slot < 0 || slot >= _slots.Length || _slots[slot] == null || _slots[slot].NameLabel == null)
            {
                return;
            }

            _slots[slot].NameLabel.text = displayName;
        }
    }
}

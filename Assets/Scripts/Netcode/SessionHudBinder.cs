using Session.Core.Attendant;
using Session.Core.Content;
using Session.Core.Identity;
using Session.Core.Puzzles;
using Session.Core.Session;
using Session.UI.Hud;
using Session.UI.Puzzles;
using UnityEngine;

namespace Session.Netcode
{
    /// <summary>
    /// Wires session events to the HUD.
    ///
    /// This exists so Session.UI never has to reference NGO. The UI assembly stays package-free
    /// and testable-by-inspection; everything that knows what a NetworkBehaviour is lives here.
    ///
    /// The mapping from event to copy is intentionally thin and intentionally quiet. Most of what
    /// happens in a run should reach the player as sound and as what their friends say, not as
    /// text — see the tone rules in LORE.md. A line only appears for things a player could
    /// otherwise miss entirely.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SessionHudBinder : MonoBehaviour
    {
        [SerializeField] private SessionLogView _log;

        [SerializeField] private KeypadView _keypad;

        [Header("Puzzle target")]
        [Tooltip("Which room and node this HUD's keypad belongs to. Set by the room prefab.")]
        [SerializeField, Min(1)] private int _currentRoomNumber = 1;

        [SerializeField, Min(1)] private int _currentNodeId = 1;

        [Header("Content keys")]
        [Tooltip("Shown when a room's exit opens. Institute tone: helpful, never congratulatory.")]
        [SerializeField] private string _roomCompleteKey = "ui.log.room_complete";

        [Tooltip("Shown when the player leaves a room unfinished.")]
        [SerializeField] private string _leftUnfinishedKey = "ui.log.left_unfinished";

        [Tooltip("Shown when the room's time allowance runs out.")]
        [SerializeField] private string _overrunKey = "ui.log.overrun";

        private SessionDirectorNetBehaviour _director;
        private int _roomComplete;
        private int _leftUnfinished;
        private int _overrun;

        private void Awake()
        {
            _roomComplete = ContentKey.Of(_roomCompleteKey);
            _leftUnfinished = ContentKey.Of(_leftUnfinishedKey);
            _overrun = ContentKey.Of(_overrunKey);
        }

        private void OnEnable()
        {
            _director = SessionDirectorNetBehaviour.Instance;

            if (_director == null)
            {
                return;
            }

            _director.SessionEventReceived += OnSessionEvent;
            _director.PuzzleAttemptResolved += OnPuzzleResolved;

            if (_keypad != null)
            {
                _keypad.Submitted += OnKeypadSubmitted;
            }
        }

        private void OnDisable()
        {
            if (_director != null)
            {
                _director.SessionEventReceived -= OnSessionEvent;
                _director.PuzzleAttemptResolved -= OnPuzzleResolved;
            }

            if (_keypad != null)
            {
                _keypad.Submitted -= OnKeypadSubmitted;
            }
        }

        private void OnKeypadSubmitted(System.ReadOnlyMemory<int> tokens)
        {
            if (_director == null)
            {
                return;
            }

            // The keypad knows the digits entered and nothing else. The server decides.
            // ToArray allocates, which is fine here — this is one deliberate button press, not a
            // per-frame path, and golden rule 6 is about Update.
            _director.SubmitPuzzleRpc(_currentRoomNumber, _currentNodeId, tokens.ToArray());
        }

        private void OnPuzzleResolved(RoomId room, PuzzleNodeId node, AttemptOutcome outcome)
        {
            if (_keypad == null)
            {
                return;
            }

            switch (outcome)
            {
                case AttemptOutcome.Accepted:
                    _keypad.ShowAccepted();
                    break;

                // Locked reads as a rejection to the player: they tried and nothing happened. The
                // distinction between "wrong" and "not yet unlocked" is the room's to communicate,
                // not a status message's.
                case AttemptOutcome.Rejected:
                case AttemptOutcome.Locked:
                    _keypad.ShowRejected();
                    break;
            }
        }

        private void OnSessionEvent(SessionEvent sessionEvent)
        {
            if (_log == null)
            {
                return;
            }

            switch (sessionEvent.Kind)
            {
                case SessionEventKind.RoomCompleted:
                    _log.PostWithRoom(_roomComplete, sessionEvent.Room.Value);
                    break;

                case SessionEventKind.ProtocolViolation:
                    PostViolation((ViolationKind)sessionEvent.Payload, sessionEvent.Room.Value);
                    break;
            }
        }

        private void PostViolation(ViolationKind kind, int roomNumber)
        {
            switch (kind)
            {
                case ViolationKind.LeftRoomUnfinished:
                    _log.PostWithRoom(_leftUnfinished, roomNumber);
                    break;

                case ViolationKind.TimeAllowanceExceeded:
                    _log.PostWithRoom(_overrun, roomNumber);
                    break;

                // Forcing a door and backtracking are things the player just did on purpose. They
                // do not need to be told; they need to hear footsteps.
            }
        }
    }
}

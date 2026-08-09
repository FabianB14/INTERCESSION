using System;
using System.Threading.Tasks;
using Netcode.Transports.Facepunch;
using Steamworks;
using Steamworks.Data;
using Unity.Netcode;
using UnityEngine;

namespace Session.Steam
{
    /// <summary>
    /// Steam lobby lifecycle and Steam relay connection.
    ///
    /// The Facepunch transport handles sockets but does not initialise Steam itself — it only
    /// checks <c>SteamClient.IsValid</c>. This does the init, owns the lobby, and hands the host's
    /// SteamId to the transport before starting the client.
    ///
    /// Traffic goes over Steam's relay, so no player ever learns another's IP. For a game played
    /// by four friends on voice chat that matters more than the small latency cost.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SteamLobbyService : MonoBehaviour
    {
        public static SteamLobbyService Instance { get; private set; }

        [Tooltip("Steam App ID. 480 is Spacewar, fine for testing; replace before any public build.")]
        [SerializeField] private uint _steamAppId = 480;

        [SerializeField, Range(2, 4)] private int _maxPlayers = 4;

        [Tooltip("Friends-only by default. The Institute does not take walk-ins.")]
        [SerializeField] private bool _friendsOnly = true;

        private FacepunchTransport _transport;
        private Lobby? _currentLobby;
        private bool _steamInitialised;

        public bool IsInLobby => _currentLobby.HasValue;

        public Lobby? CurrentLobby => _currentLobby;

        public event Action<string> LobbyError;

        public event Action LobbyJoined;

        public event Action LobbyLeft;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            try
            {
                // asyncCallbacks: false — the transport pumps RunCallbacks while a session is live,
                // and this component pumps it the rest of the time. Letting Facepunch also run its
                // own dispatch loop means two pumps racing over the same callback queue.
                SteamClient.Init(_steamAppId, false);
                _steamInitialised = true;
            }
            catch (Exception exception)
            {
                Debug.LogError("[Session] Steam failed to initialise: " + exception.Message +
                               ". Is the Steam client running?");
                enabled = false;
                return;
            }

            SteamMatchmaking.OnLobbyCreated += OnLobbyCreated;
            SteamMatchmaking.OnLobbyEntered += OnLobbyEntered;
            SteamMatchmaking.OnLobbyMemberLeave += OnLobbyMemberLeave;
            SteamFriends.OnGameLobbyJoinRequested += OnGameLobbyJoinRequested;
        }

        private void Start()
        {
            if (NetworkManager.Singleton != null)
            {
                _transport = NetworkManager.Singleton.GetComponent<FacepunchTransport>();
            }

            if (_transport == null)
            {
                Debug.LogError("[Session] No FacepunchTransport on the NetworkManager. " +
                               "Add the component and set it as the transport in NetworkManager's config.");
            }
        }

        private void Update()
        {
            // While a session is live the transport pumps callbacks in its OnEarlyUpdate. Outside
            // one — main menu, lobby browsing — nothing else will, so do it here.
            if (_steamInitialised &&
                (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening))
            {
                SteamClient.RunCallbacks();
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            if (!_steamInitialised)
            {
                return;
            }

            SteamMatchmaking.OnLobbyCreated -= OnLobbyCreated;
            SteamMatchmaking.OnLobbyEntered -= OnLobbyEntered;
            SteamMatchmaking.OnLobbyMemberLeave -= OnLobbyMemberLeave;
            SteamFriends.OnGameLobbyJoinRequested -= OnGameLobbyJoinRequested;

            SteamClient.Shutdown();
        }

        // ---- host ---------------------------------------------------------------------------

        public async Task HostAsync()
        {
            if (_transport == null)
            {
                LobbyError?.Invoke("No Steam transport configured.");
                return;
            }

            NetworkManager.Singleton.StartHost();

            Lobby? lobby = await SteamMatchmaking.CreateLobbyAsync(_maxPlayers);
            if (!lobby.HasValue)
            {
                NetworkManager.Singleton.Shutdown();
                LobbyError?.Invoke("Steam would not create a lobby.");
            }
        }

        private void OnLobbyCreated(Result result, Lobby lobby)
        {
            if (result != Result.OK)
            {
                LobbyError?.Invoke("Lobby creation failed: " + result);
                return;
            }

            lobby.SetJoinable(true);
            lobby.SetData("game", "session");
            lobby.SetData("host", SteamClient.SteamId.ToString());

            if (_friendsOnly)
            {
                lobby.SetFriendsOnly();
            }
            else
            {
                lobby.SetPublic();
            }

            _currentLobby = lobby;
        }

        // ---- join ---------------------------------------------------------------------------

        public async Task JoinAsync(SteamId lobbyId)
        {
            // JoinLobbyAsync joins outright — do not also call Join() on the result, or Steam sees
            // two entry attempts for one member. Success surfaces through OnLobbyEntered, which is
            // where the relay connection is actually made.
            Lobby? lobby = await SteamMatchmaking.JoinLobbyAsync(lobbyId);

            if (!lobby.HasValue)
            {
                LobbyError?.Invoke("Could not join lobby " + lobbyId + ".");
            }
        }

        private void OnGameLobbyJoinRequested(Lobby lobby, SteamId inviterId)
        {
            // Accepting an invite from the Steam overlay lands here.
            _ = lobby.Join();
        }

        private void OnLobbyEntered(Lobby lobby)
        {
            _currentLobby = lobby;

            // The host is already running as host; do not connect to yourself.
            if (NetworkManager.Singleton.IsHost)
            {
                LobbyJoined?.Invoke();
                return;
            }

            if (_transport == null)
            {
                LobbyError?.Invoke("No Steam transport configured.");
                return;
            }

            _transport.targetSteamId = lobby.Owner.Id;

            if (!NetworkManager.Singleton.StartClient())
            {
                LobbyError?.Invoke("Steam relay connection to the host failed.");
                return;
            }

            LobbyJoined?.Invoke();
        }

        private void OnLobbyMemberLeave(Lobby lobby, Friend friend)
        {
            if (friend.Id != lobby.Owner.Id)
            {
                return;
            }

            // The host left. Everyone else's session is over — the run is server-authoritative and
            // there is no migration path that could preserve puzzle state honestly.
            Leave();
        }

        public void Leave()
        {
            if (_currentLobby.HasValue)
            {
                _currentLobby.Value.Leave();
                _currentLobby = null;
            }

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                NetworkManager.Singleton.Shutdown();
            }

            LobbyLeft?.Invoke();
        }

        /// <summary>Open the Steam overlay's invite dialog for the current lobby.</summary>
        public void InviteFriends()
        {
            if (_currentLobby.HasValue)
            {
                SteamFriends.OpenGameInviteOverlay(_currentLobby.Value.Id);
            }
        }
    }
}

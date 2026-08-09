using Session.Core.Lobby;
using Session.UI.Lobby;
using Steamworks;
using Steamworks.Data;
using Unity.Netcode;
using UnityEngine;

namespace Session.Steam
{
    /// <summary>
    /// Connects <see cref="LobbyView"/> to Steam and to the roster in Core.
    ///
    /// Keeps Session.UI free of both Steam and NGO, so the lobby screen can be opened, laid out and
    /// iterated on in a project where neither package has finished importing.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LobbyUiBinder : MonoBehaviour
    {
        [SerializeField] private LobbyView _view;

        [SerializeField, Range(2, 4)] private int _maxPlayers = 4;

        private LobbyRoster _roster;
        private int _localSlot = -1;

        private void Awake()
        {
            _roster = new LobbyRoster(_maxPlayers);
        }

        private void OnEnable()
        {
            if (_view != null)
            {
                _view.HostRequested += OnHostRequested;
                _view.InviteRequested += OnInviteRequested;
                _view.ReadyToggled += OnReadyToggled;
                _view.StartRequested += OnStartRequested;
                _view.LeaveRequested += OnLeaveRequested;
            }

            if (SteamLobbyService.Instance != null)
            {
                SteamLobbyService.Instance.LobbyJoined += OnLobbyJoined;
                SteamLobbyService.Instance.LobbyLeft += OnLobbyLeft;
                SteamLobbyService.Instance.LobbyError += OnLobbyError;
            }

            SteamMatchmaking.OnLobbyMemberJoined += OnMemberJoined;
            SteamMatchmaking.OnLobbyMemberLeave += OnMemberLeft;
        }

        private void OnDisable()
        {
            if (_view != null)
            {
                _view.HostRequested -= OnHostRequested;
                _view.InviteRequested -= OnInviteRequested;
                _view.ReadyToggled -= OnReadyToggled;
                _view.StartRequested -= OnStartRequested;
                _view.LeaveRequested -= OnLeaveRequested;
            }

            if (SteamLobbyService.Instance != null)
            {
                SteamLobbyService.Instance.LobbyJoined -= OnLobbyJoined;
                SteamLobbyService.Instance.LobbyLeft -= OnLobbyLeft;
                SteamLobbyService.Instance.LobbyError -= OnLobbyError;
            }

            SteamMatchmaking.OnLobbyMemberJoined -= OnMemberJoined;
            SteamMatchmaking.OnLobbyMemberLeave -= OnMemberLeft;
        }

        private async void OnHostRequested()
        {
            if (SteamLobbyService.Instance != null)
            {
                await SteamLobbyService.Instance.HostAsync();
            }
        }

        private void OnInviteRequested() => SteamLobbyService.Instance?.InviteFriends();

        private void OnLeaveRequested() => SteamLobbyService.Instance?.Leave();

        private void OnReadyToggled(bool ready)
        {
            if (_localSlot < 0)
            {
                return;
            }

            _roster.SetReady(_localSlot, ready);

            // Ready state is lobby metadata rather than game state, so it rides on Steam's lobby
            // member data instead of a NetworkVariable — it has to work before NGO is connected.
            if (SteamLobbyService.Instance?.CurrentLobby is Lobby lobby)
            {
                lobby.SetMemberData("ready", ready ? "1" : "0");
            }
        }

        private void OnStartRequested()
        {
            // Core owns the gate. Re-check rather than trusting the button's interactable state,
            // which is a frame behind at best and forgeable at worst.
            if (!_roster.CanStart)
            {
                _view?.ShowError("Everyone has to be ready first.");
                return;
            }

            if (SteamLobbyService.Instance?.CurrentLobby is Lobby lobby)
            {
                lobby.SetJoinable(false);
            }
        }

        private void OnLobbyJoined()
        {
            RebuildRoster();

            bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
            _view?.Bind(_roster, _localSlot, isHost);
        }

        private void OnLobbyLeft()
        {
            _roster.Clear();
            _localSlot = -1;
        }

        private void OnLobbyError(string message) => _view?.ShowError(message);

        private void OnMemberJoined(Lobby lobby, Friend friend) => RebuildRoster();

        private void OnMemberLeft(Lobby lobby, Friend friend) => RebuildRoster();

        private void RebuildRoster()
        {
            if (SteamLobbyService.Instance?.CurrentLobby is not Lobby lobby)
            {
                return;
            }

            _roster.Clear();
            _localSlot = -1;

            foreach (Friend member in lobby.Members)
            {
                if (!_roster.TryAdd(member.Id, out int slot))
                {
                    continue;
                }

                if (member.Id == SteamClient.SteamId)
                {
                    _localSlot = slot;
                }

                _roster.SetReady(slot, lobby.GetMemberData(member, "ready") == "1");
                _view?.SetSlotName(slot, member.Name);
            }
        }
    }
}

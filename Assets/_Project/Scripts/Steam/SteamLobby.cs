using System;
using System.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace YourFinalOrder.Steam
{
    /// <summary>
    /// Лобби Steam: создание, приглашения друзей, вход по приглашению/коду, список участников.
    /// Лобби хранит SteamID хоста; сама игра идёт через SteamNetworkTransport (P2P).
    /// </summary>
    public class SteamLobby : MonoBehaviour
    {
        public const string HostKey = "host";
        public const string GameKey = "game";
        public const string GameId = "your-last-order";

        public CSteamID LobbyId { get; private set; } = CSteamID.Nil;
        public bool InLobby => LobbyId != CSteamID.Nil;
        public bool IsOwner => InLobby && SteamMatchmaking.GetLobbyOwner(LobbyId) == SteamUser.GetSteamID();

        /// <summary>Лобби создано нами (мы хост).</summary>
        public event Action<CSteamID> Created;
        /// <summary>Мы вошли в чужое лобби: передаётся SteamID хоста.</summary>
        public event Action<CSteamID> JoinedAsClient;
        public event Action MembersChanged;
        public event Action<string> Failed;

        CallResult<LobbyCreated_t> createdResult;
        CallResult<LobbyEnter_t> enterResult;
        Callback<GameLobbyJoinRequested_t> joinRequested;
        Callback<GameRichPresenceJoinRequested_t> richJoinRequested;
        Callback<LobbyChatUpdate_t> chatUpdate;
        Callback<LobbyDataUpdate_t> dataUpdate;
        bool callbacksReady;
        bool creating;

        void Start()
        {
            if (SteamBootstrap.Initialized) Setup();
            else SteamBootstrap.Ready += Setup;
        }

        void OnDestroy()
        {
            SteamBootstrap.Ready -= Setup;
        }

        void Setup()
        {
            if (callbacksReady) return;
            callbacksReady = true;
            createdResult = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
            enterResult = CallResult<LobbyEnter_t>.Create(OnLobbyEntered);
            joinRequested = Callback<GameLobbyJoinRequested_t>.Create(r => Join(r.m_steamIDLobby));
            richJoinRequested = Callback<GameRichPresenceJoinRequested_t>.Create(r =>
            {
                if (TryParseConnect(r.m_rgchConnect, out var id)) Join(id);
            });
            chatUpdate = Callback<LobbyChatUpdate_t>.Create(u =>
            {
                if (u.m_ulSteamIDLobby == LobbyId.m_SteamID) MembersChanged?.Invoke();
            });
            dataUpdate = Callback<LobbyDataUpdate_t>.Create(u =>
            {
                if (u.m_ulSteamIDLobby == LobbyId.m_SteamID) MembersChanged?.Invoke();
            });

            // Запуск через приглашение: Steam передаёт "+connect_lobby <id>"
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "+connect_lobby" && ulong.TryParse(args[i + 1], out var lobby))
                    Join(new CSteamID(lobby));
        }

        public void Create(int maxMembers)
        {
            if (!SteamBootstrap.Initialized) { Failed?.Invoke("Steam не запущен"); return; }
            if (creating) return;
            Leave();
            creating = true;
            createdResult.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, maxMembers));
        }

        void OnLobbyCreated(LobbyCreated_t result, bool ioFailure)
        {
            creating = false;
            if (ioFailure || result.m_eResult != EResult.k_EResultOK)
            {
                Failed?.Invoke($"Не удалось создать лобби ({result.m_eResult})");
                return;
            }
            LobbyId = new CSteamID(result.m_ulSteamIDLobby);
            SteamMatchmaking.SetLobbyData(LobbyId, HostKey, SteamUser.GetSteamID().ToString());
            SteamMatchmaking.SetLobbyData(LobbyId, GameKey, GameId);
            SteamMatchmaking.SetLobbyData(LobbyId, "name", $"{SteamFriends.GetPersonaName()} · Your Last Order");
            SetPresence();
            Created?.Invoke(LobbyId);
            MembersChanged?.Invoke();
        }

        public void Join(CSteamID lobby)
        {
            if (!SteamBootstrap.Initialized) { Failed?.Invoke("Steam не запущен"); return; }
            if (lobby == LobbyId) return;
            Leave();
            enterResult.Set(SteamMatchmaking.JoinLobby(lobby));
        }

        public bool JoinByCode(string code)
        {
            code = (code ?? "").Trim();
            if (!ulong.TryParse(code, out var id)) { Failed?.Invoke("Неверный код лобби"); return false; }
            Join(new CSteamID(id));
            return true;
        }

        void OnLobbyEntered(LobbyEnter_t result, bool ioFailure)
        {
            if (ioFailure || result.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            {
                Failed?.Invoke("Не удалось войти в лобби (оно закрыто или заполнено)");
                return;
            }

            var lobby = new CSteamID(result.m_ulSteamIDLobby);
            if (SteamMatchmaking.GetLobbyData(lobby, GameKey) != GameId)
            {
                SteamMatchmaking.LeaveLobby(lobby);
                Failed?.Invoke("Это лобби другой игры");
                return;
            }

            LobbyId = lobby;
            SetPresence();
            MembersChanged?.Invoke();

            if (ulong.TryParse(SteamMatchmaking.GetLobbyData(lobby, HostKey), out var host) &&
                host != SteamUser.GetSteamID().m_SteamID)
            {
                JoinedAsClient?.Invoke(new CSteamID(host));
            }
        }

        public void Leave()
        {
            if (!InLobby) return;
            if (SteamBootstrap.Initialized)
            {
                SteamMatchmaking.LeaveLobby(LobbyId);
                SteamFriends.ClearRichPresence();
            }
            LobbyId = CSteamID.Nil;
            MembersChanged?.Invoke();
        }

        public void SetJoinable(bool joinable)
        {
            if (InLobby && IsOwner) SteamMatchmaking.SetLobbyJoinable(LobbyId, joinable);
        }

        /// <summary>Открывает оверлей Steam со списком друзей для приглашения.</summary>
        public void OpenInviteOverlay()
        {
            if (InLobby) SteamFriends.ActivateGameOverlayInviteDialog(LobbyId);
        }

        public static bool OverlayEnabled => SteamBootstrap.Initialized && SteamUtils.IsOverlayEnabled();

        public List<(CSteamID id, string name)> GetMembers()
        {
            var list = new List<(CSteamID, string)>();
            if (!InLobby) return list;
            int n = SteamMatchmaking.GetNumLobbyMembers(LobbyId);
            for (int i = 0; i < n; i++)
            {
                var id = SteamMatchmaking.GetLobbyMemberByIndex(LobbyId, i);
                list.Add((id, SteamFriends.GetFriendPersonaName(id)));
            }
            return list;
        }

        public CSteamID Owner => InLobby ? SteamMatchmaking.GetLobbyOwner(LobbyId) : CSteamID.Nil;

        void SetPresence()
        {
            // Позволяет друзьям нажать «Присоединиться» в списке друзей Steam
            SteamFriends.SetRichPresence("connect", $"+connect_lobby {LobbyId.m_SteamID}");
            SteamFriends.SetRichPresence("status", "В лобби Your Last Order");
        }

        static bool TryParseConnect(string connect, out CSteamID lobby)
        {
            lobby = CSteamID.Nil;
            if (string.IsNullOrEmpty(connect)) return false;
            var parts = connect.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (parts[i] == "+connect_lobby" && ulong.TryParse(parts[i + 1], out var id))
                {
                    lobby = new CSteamID(id);
                    return true;
                }
            }
            return false;
        }
    }
}

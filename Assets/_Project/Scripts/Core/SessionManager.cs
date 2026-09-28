using System;
using Steamworks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;
using YourFinalOrder.Steam;

namespace YourFinalOrder.Core
{
    /// <summary>
    /// Сессия игры: хост/клиент через Steam или по IP, лобби, старт игры, выход в меню.
    /// Лежит на префабе NetworkRoot вместе с NetworkManager (живёт всю игру).
    /// </summary>
    [RequireComponent(typeof(NetworkManager))]
    public class SessionManager : MonoBehaviour
    {
        public enum Mode { None, Steam, Lan }

        public const string MenuScene = "Menu";
        public const string GameScene = "Game";
        public const int MaxPlayers = 4;

        public static SessionManager Instance { get; private set; }

        public NetworkManager Network { get; private set; }
        public SteamLobby Lobby { get; private set; }
        public Mode CurrentMode { get; private set; }
        public bool IsBusy { get; private set; }
        public bool InGame => SceneManager.GetActiveScene().name == GameScene;
        /// <summary>Последнее сообщение статуса (меню показывает его после перезагрузки сцены).</summary>
        public string LastStatus { get; private set; } = "";

        /// <summary>Сообщение для UI (ошибки, статус подключения).</summary>
        public event Action<string> StatusChanged;
        /// <summary>Сессия началась (мы в лобби как хост или клиент).</summary>
        public event Action SessionStarted;
        /// <summary>Сессия завершена (вышли, отключились).</summary>
        public event Action SessionEnded;

        UnityTransport lanTransport;
        SteamNetworkTransport steamTransport;
        bool leaving;
        bool userLeft;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            Network = GetComponent<NetworkManager>();
            Lobby = GetComponent<SteamLobby>();
            lanTransport = GetComponent<UnityTransport>();
            steamTransport = GetComponent<SteamNetworkTransport>();
        }

        void Start()
        {
            Network.NetworkConfig.ConnectionApproval = true;
            Network.ConnectionApprovalCallback = Approve;
            Network.OnClientDisconnectCallback += OnClientDisconnect;
            Network.OnClientConnectedCallback += OnClientConnected;

            Lobby.Created += OnSteamLobbyCreated;
            Lobby.JoinedAsClient += OnSteamLobbyJoined;
            Lobby.Failed += Fail;
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            if (Network != null)
            {
                Network.OnClientDisconnectCallback -= OnClientDisconnect;
                Network.OnClientConnectedCallback -= OnClientConnected;
            }
        }

        // ------------------------------------------------------------ хост / клиент

        public void HostSteam()
        {
            userLeft = false;
            if (IsBusy || Network.IsListening) return;
            if (!SteamBootstrap.Initialized) { Fail("Steam не запущен. Запустите Steam или играйте по IP."); return; }
            IsBusy = true;
            Status("Создаём лобби...");
            Lobby.Create(MaxPlayers);
        }

        void OnSteamLobbyCreated(CSteamID lobby)
        {
            Network.NetworkConfig.NetworkTransport = steamTransport;
            if (!Network.StartHost())
            {
                Lobby.Leave();
                Fail("Не удалось запустить хост");
                return;
            }
            CurrentMode = Mode.Steam;
            IsBusy = false;
            Status("");
            SessionStarted?.Invoke();
        }

        public void JoinSteamLobby(string code)
        {
            userLeft = false;
            if (IsBusy || Network.IsListening) return;
            if (!SteamBootstrap.Initialized) { Fail("Steam не запущен"); return; }
            if (Lobby.JoinByCode(code))
            {
                IsBusy = true;
                Status("Входим в лобби...");
            }
        }

        /// <summary>Вызывается и при входе по коду, и по приглашению из Steam.</summary>
        void OnSteamLobbyJoined(CSteamID host)
        {
            userLeft = false;
            IsBusy = true;
            Status($"Подключение к {SteamFriends.GetFriendPersonaName(host)}...");
            StartCoroutine(ConnectToSteamHost(host));
        }

        System.Collections.IEnumerator ConnectToSteamHost(CSteamID host)
        {
            // Если мы сами были хостом (приняли приглашение), дожидаемся остановки сети.
            // Лобби не трогаем: SteamLobby уже перешёл в новое.
            if (Network.IsListening) Network.Shutdown();
            while (Network.ShutdownInProgress) yield return null;

            Network.NetworkConfig.NetworkTransport = steamTransport;
            steamTransport.ConnectToSteamId = host.m_SteamID;
            if (!Network.StartClient())
            {
                Lobby.Leave();
                Fail("Не удалось подключиться к хосту");
                yield break;
            }
            CurrentMode = Mode.Steam;
        }

        public void HostLan(ushort port)
        {
            userLeft = false;
            if (IsBusy || Network.IsListening) return;
            lanTransport.SetConnectionData("127.0.0.1", port, "0.0.0.0");
            Network.NetworkConfig.NetworkTransport = lanTransport;
            if (!Network.StartHost()) { Fail("Не удалось запустить хост (порт занят?)"); return; }
            CurrentMode = Mode.Lan;
            Status("");
            SessionStarted?.Invoke();
        }

        public void JoinLan(string address, ushort port)
        {
            userLeft = false;
            if (IsBusy || Network.IsListening) return;
            lanTransport.SetConnectionData(address.Trim(), port);
            Network.NetworkConfig.NetworkTransport = lanTransport;
            if (!Network.StartClient()) { Fail("Не удалось подключиться"); return; }
            CurrentMode = Mode.Lan;
            IsBusy = true;
            Status($"Подключение к {address}:{port}...");
        }

        void OnClientConnected(ulong clientId)
        {
            if (Network.IsServer || clientId != Network.LocalClientId) return;
            IsBusy = false;
            Status("");
            SessionStarted?.Invoke();
        }

        void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            // Игроков создаёт GameSession после загрузки сцены игры
            response.CreatePlayerObject = false;
            if (Network.ConnectedClientsIds.Count >= MaxPlayers)
            {
                response.Approved = false;
                response.Reason = "Лобби заполнено";
                return;
            }
            response.Approved = true;
        }

        // ------------------------------------------------------------ игра

        public bool CanStartGame => Network.IsHost && !InGame;

        public void StartGame()
        {
            if (!CanStartGame) return;
            var result = Network.SceneManager.LoadScene(GameScene, LoadSceneMode.Single);
            if (result != SceneEventProgressStatus.Started)
                Fail($"Не удалось загрузить уровень: {result}");
        }

        public void OpenInvites()
        {
            if (CurrentMode == Mode.Steam) Lobby.OpenInviteOverlay();
        }

        public void Leave()
        {
            userLeft = true;
            Shutdown(true);
        }

        void Shutdown(bool returnToMenu)
        {
            if (leaving) return;
            leaving = true;
            if (Network.IsListening) Network.Shutdown();
            Lobby.Leave();
            CurrentMode = Mode.None;
            IsBusy = false;
            leaving = false;
            SessionEnded?.Invoke();

            if (returnToMenu && SceneManager.GetActiveScene().name != MenuScene)
                SceneManager.LoadScene(MenuScene);
        }

        void OnClientDisconnect(ulong clientId)
        {
            if (userLeft) return;
            // Нас отключили (хост вышел, кик, таймаут)
            if ((!Network.IsServer && clientId == Network.LocalClientId) || !Network.IsListening)
            {
                var reason = string.IsNullOrEmpty(Network.DisconnectReason) ? "Соединение с хостом потеряно" : Network.DisconnectReason;
                Shutdown(true);
                Fail(reason);
            }
        }

        void Fail(string message)
        {
            IsBusy = false;
            Debug.LogWarning("[Session] " + message);
            Status(message);
        }

        void Status(string message)
        {
            LastStatus = message;
            StatusChanged?.Invoke(message);
        }
    }
}

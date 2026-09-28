using System;
using Steamworks;
using UnityEngine;

namespace YourFinalOrder.Steam
{
    /// <summary>
    /// Инициализация Steamworks. Живёт всю сессию (DontDestroyOnLoad).
    /// Если Steam не запущен, игра продолжает работать: доступна сетевая игра по IP.
    ///
    /// App ID берётся из steam_appid.txt рядом с .exe (и в корне проекта для редактора).
    /// Пока своего App ID нет, используется 480 (Spacewar), тестовое приложение Valve.
    /// </summary>
    public class SteamBootstrap : MonoBehaviour
    {
        public static SteamBootstrap Instance { get; private set; }
        public static bool Initialized { get; private set; }
        public static string Error { get; private set; }

        public static event Action Ready;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Create()
        {
            if (Instance != null) return;
            var go = new GameObject("[Steam]");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<SteamBootstrap>();
        }

        void Awake()
        {
            if (!Packsize.Test())
            {
                Error = "Steamworks.NET: неверная платформа (Packsize)";
                Debug.LogError(Error);
                return;
            }
            if (!DllCheck.Test())
            {
                Error = "Steamworks.NET: неверная версия steam_api";
                Debug.LogError(Error);
                return;
            }

            try
            {
                var result = SteamAPI.InitEx(out var message);
                Initialized = result == ESteamAPIInitResult.k_ESteamAPIInitResult_OK;
                if (!Initialized)
                {
                    Error = result == ESteamAPIInitResult.k_ESteamAPIInitResult_NoSteamClient
                        ? "Steam не запущен"
                        : $"Steam недоступен: {message}";
                    Debug.LogWarning(Error);
                    return;
                }
            }
            catch (DllNotFoundException e)
            {
                Error = "Не найдена библиотека steam_api";
                Debug.LogError(Error + "\n" + e);
                return;
            }

            Debug.Log($"Steam: вошли как {SteamFriends.GetPersonaName()} ({SteamUser.GetSteamID()})");
            Ready?.Invoke();
        }

        void Update()
        {
            if (Initialized) SteamAPI.RunCallbacks();
        }

        void OnApplicationQuit()
        {
            if (!Initialized) return;
            Initialized = false;
            SteamAPI.Shutdown();
        }

        public static string LocalName => Initialized ? SteamFriends.GetPersonaName() : Environment.UserName;
    }
}

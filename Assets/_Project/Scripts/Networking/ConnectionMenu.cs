using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace YourFinalOrder.Networking
{
    /// <summary>
    /// Временное меню подключения (IMGUI): создать игру или подключиться по IP.
    /// </summary>
    public class ConnectionMenu : MonoBehaviour
    {
        public string address = "127.0.0.1";
        public string port = "7777";

        NetworkManager nm;
        string status = "";
        GUIStyle titleStyle;

        void Start()
        {
            nm = NetworkManager.Singleton;
            nm.OnClientDisconnectCallback += OnDisconnected;
        }

        void OnDestroy()
        {
            if (nm != null) nm.OnClientDisconnectCallback -= OnDisconnected;
        }

        void OnDisconnected(ulong clientId)
        {
            if (nm.IsServer || clientId != nm.LocalClientId) return;
            status = string.IsNullOrEmpty(nm.DisconnectReason) ? "Соединение потеряно" : nm.DisconnectReason;
        }

        bool ApplyTransport()
        {
            if (!ushort.TryParse(port, out var p))
            {
                status = "Неверный порт";
                return false;
            }
            nm.GetComponent<UnityTransport>().SetConnectionData(address.Trim(), p, "0.0.0.0");
            return true;
        }

        void OnGUI()
        {
            if (nm == null) return;
            titleStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 32, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };

            bool running = nm.IsClient || nm.IsServer;
            if (running)
            {
                if (Cursor.lockState != CursorLockMode.Locked)
                {
                    GUILayout.BeginArea(new Rect(Screen.width - 230, 10, 220, 120));
                    GUILayout.Label(nm.IsHost ? $"Хост · игроков: {nm.ConnectedClientsIds.Count}" : "Клиент");
                    if (GUILayout.Button("Отключиться", GUILayout.Height(30))) nm.Shutdown();
                    GUILayout.Label("Esc — вернуться в игру");
                    GUILayout.EndArea();
                }
                return;
            }

            float w = 360, h = 300;
            GUILayout.BeginArea(new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h), GUI.skin.box);
            GUILayout.Label("YOUR FINAL ORDER", titleStyle, GUILayout.Height(50));
            GUILayout.Space(10);

            GUILayout.BeginHorizontal();
            GUILayout.Label("IP", GUILayout.Width(50));
            address = GUILayout.TextField(address);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Порт", GUILayout.Width(50));
            port = GUILayout.TextField(port);
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            if (GUILayout.Button("Создать игру (хост)", GUILayout.Height(36)) && ApplyTransport())
            {
                status = nm.StartHost() ? "" : "Не удалось запустить хост";
            }
            if (GUILayout.Button("Подключиться", GUILayout.Height(36)) && ApplyTransport())
            {
                status = nm.StartClient() ? "Подключение..." : "Не удалось подключиться";
            }
            if (GUILayout.Button("Выход", GUILayout.Height(24)))
            {
                Application.Quit();
            }

            if (!string.IsNullOrEmpty(status)) GUILayout.Label(status);
            GUILayout.EndArea();
        }
    }
}

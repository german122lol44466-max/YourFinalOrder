using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using YourFinalOrder.Game;

namespace YourFinalOrder.Core
{
    /// <summary>
    /// Лежит в сцене игры. На сервере создаёт персонажей всем игрокам после загрузки сцены
    /// и тем, кто подключился позже.
    /// </summary>
    public class GameSession : MonoBehaviour
    {
        public GameObject playerPrefab;
        [Tooltip("Для запуска сцены игры напрямую из редактора: создаёт сеть и хост по IP")]
        public GameObject networkRootPrefab;

        NetworkManager nm;
        int spawnIndex;

        System.Collections.IEnumerator Start()
        {
            nm = NetworkManager.Singleton;

            // Запуск сцены напрямую (без меню) — поднимаем локальный хост для тестов
            if (nm == null && networkRootPrefab != null)
            {
                Instantiate(networkRootPrefab).name = "NetworkRoot";
                yield return null;
                nm = NetworkManager.Singleton;
                SessionManager.Instance.HostLan(7777);
            }

            if (nm == null || !nm.IsServer) yield break;

            nm.SceneManager.OnLoadEventCompleted += OnLoadCompleted;
            nm.SceneManager.OnSynchronizeComplete += OnLateJoinerSynced;

            // Хост уже в сцене; остальных создаём, когда они её загрузят
            SpawnPlayer(nm.LocalClientId);
        }

        void OnDestroy()
        {
            if (nm == null || nm.SceneManager == null) return;
            nm.SceneManager.OnLoadEventCompleted -= OnLoadCompleted;
            nm.SceneManager.OnSynchronizeComplete -= OnLateJoinerSynced;
        }

        void OnLoadCompleted(string sceneName, LoadSceneMode mode, System.Collections.Generic.List<ulong> completed,
            System.Collections.Generic.List<ulong> timedOut)
        {
            if (sceneName != gameObject.scene.name) return;
            foreach (var id in completed) SpawnPlayer(id);
        }

        void OnLateJoinerSynced(ulong clientId) => SpawnPlayer(clientId);

        void SpawnPlayer(ulong clientId)
        {
            if (!nm.IsServer || playerPrefab == null) return;
            if (nm.ConnectedClients.TryGetValue(clientId, out var client) && client.PlayerObject != null) return;

            var spawn = LevelMarker.GetPlayerSpawn(spawnIndex++);
            var pos = spawn != null ? spawn.transform.position : Vector3.up;
            var rot = spawn != null ? spawn.transform.rotation : Quaternion.identity;
            var player = Instantiate(playerPrefab, pos, rot);
            player.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId, true);
        }
    }
}

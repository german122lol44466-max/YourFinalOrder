using Unity.AI.Navigation;
using Unity.Netcode;
using UnityEngine;
using YourFinalOrder.Game;

namespace YourFinalOrder.Networking
{
    /// <summary>
    /// Серверная инициализация: одобрение подключений, выдача точек спавна,
    /// сборка NavMesh и создание сетевых объектов (GameManager, монстр).
    /// </summary>
    [RequireComponent(typeof(NetworkManager))]
    public class ServerBootstrap : MonoBehaviour
    {
        public GameObject gameManagerPrefab;
        public GameObject monsterPrefab;
        public NavMeshSurface navSurface;
        [Range(1, 8)] public int maxPlayers = 4;

        NetworkManager nm;

        void Start()
        {
            nm = GetComponent<NetworkManager>();
            nm.NetworkConfig.ConnectionApproval = true;
            nm.ConnectionApprovalCallback = Approve;
            nm.OnServerStarted += OnServerStarted;
        }

        void OnDestroy()
        {
            if (nm == null) return;
            nm.OnServerStarted -= OnServerStarted;
            nm.ConnectionApprovalCallback = null;
        }

        void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            int connected = nm.ConnectedClientsIds.Count;
            if (connected >= maxPlayers)
            {
                response.Approved = false;
                response.Reason = "Сервер заполнен";
                return;
            }

            response.Approved = true;
            response.CreatePlayerObject = true;

            var spawn = LevelMarker.GetPlayerSpawn(connected);
            if (spawn != null)
            {
                response.Position = spawn.transform.position;
                response.Rotation = spawn.transform.rotation;
            }
        }

        void OnServerStarted()
        {
            if (navSurface != null) navSurface.BuildNavMesh();

            var monsterSpawn = LevelMarker.GetFirst(LevelMarker.MarkerType.MonsterSpawn);
            if (monsterPrefab != null)
            {
                var pos = monsterSpawn != null ? monsterSpawn.transform.position : Vector3.zero;
                var rot = monsterSpawn != null ? monsterSpawn.transform.rotation : Quaternion.identity;
                Instantiate(monsterPrefab, pos, rot).GetComponent<NetworkObject>().Spawn(true);
            }

            if (gameManagerPrefab != null)
                Instantiate(gameManagerPrefab).GetComponent<NetworkObject>().Spawn(true);
        }
    }
}

using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using YourFinalOrder.AI;
using YourFinalOrder.Player;

namespace YourFinalOrder.Game
{
    public enum GamePhase : byte { Playing, Won, Lost }

    /// <summary>
    /// Логика раунда (сервер): предохранители, открытие выхода, победа/поражение, рестарт.
    /// </summary>
    public class GameManager : NetworkBehaviour
    {
        public static GameManager Instance { get; private set; }

        public GameObject fusePrefab;
        [Tooltip("Сколько предохранителей разложить (не больше числа точек FuseSpawn)")]
        public int fusesToSpawn = 5;
        public float exitRadius = 2f;

        public readonly NetworkVariable<int> Collected = new();
        public readonly NetworkVariable<int> Required = new();
        public readonly NetworkVariable<GamePhase> Phase = new(GamePhase.Playing);

        public bool IsPlaying => Phase.Value == GamePhase.Playing;
        public bool ExitOpen => Required.Value > 0 && Collected.Value >= Required.Value;

        readonly List<NetworkObject> spawnedFuses = new();

        public override void OnNetworkSpawn()
        {
            Instance = this;
            if (IsServer) ServerStartRound();
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
        }

        public void ServerAddFuse()
        {
            if (!IsServer || !IsPlaying) return;
            Collected.Value++;
        }

        public void ServerStartRound()
        {
            if (!IsServer) return;

            foreach (var f in spawnedFuses)
                if (f != null && f.IsSpawned) f.Despawn(true);
            spawnedFuses.Clear();

            var points = LevelMarker.GetAll(LevelMarker.MarkerType.FuseSpawn);
            Shuffle(points);
            int count = Mathf.Min(fusesToSpawn, points.Count);
            for (int i = 0; i < count; i++)
            {
                var no = Instantiate(fusePrefab, points[i].transform.position, Quaternion.identity).GetComponent<NetworkObject>();
                no.Spawn(true);
                spawnedFuses.Add(no);
            }

            Collected.Value = 0;
            Required.Value = count;

            int index = 0;
            foreach (var p in PlayerState.All)
            {
                var spawn = LevelMarker.GetPlayerSpawn(index++);
                if (spawn != null) p.ServerRevive(spawn.transform.position, spawn.transform.eulerAngles.y);
            }

            var monsterSpawn = LevelMarker.GetFirst(LevelMarker.MarkerType.MonsterSpawn);
            if (MonsterAI.Instance != null && monsterSpawn != null)
                MonsterAI.Instance.ServerReset(monsterSpawn.transform.position);

            Phase.Value = GamePhase.Playing;
        }

        void Update()
        {
            if (!IsSpawned || !IsServer) return;

            if (!IsPlaying)
            {
                // Рестарт доступен хосту
                if (Input.GetKeyDown(KeyCode.R)) ServerStartRound();
                return;
            }

            if (PlayerState.All.Count == 0) return;

            int alive = 0;
            foreach (var p in PlayerState.All)
                if (p.IsAlive) alive++;
            if (alive == 0)
            {
                Phase.Value = GamePhase.Lost;
                return;
            }

            if (ExitOpen)
            {
                var exit = LevelMarker.GetFirst(LevelMarker.MarkerType.Exit);
                if (exit == null) return;
                foreach (var p in PlayerState.All)
                {
                    if (!p.IsAlive) continue;
                    var d = p.transform.position - exit.transform.position;
                    d.y = 0f;
                    if (d.sqrMagnitude < exitRadius * exitRadius)
                    {
                        Phase.Value = GamePhase.Won;
                        return;
                    }
                }
            }
        }

        static void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}

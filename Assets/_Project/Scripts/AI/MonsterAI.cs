using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using YourFinalOrder.Audio;
using YourFinalOrder.Game;
using YourFinalOrder.Player;

namespace YourFinalOrder.AI
{
    /// <summary>
    /// Монстр. Мозг работает только на сервере (NavMeshAgent), клиенты получают позицию через NetworkTransform.
    /// Видит игроков в конусе обзора (дальше, если у игрока включён фонарь), слышит бег и ходьбу.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class MonsterAI : NetworkBehaviour
    {
        public enum State : byte { Patrol, Investigate, Chase, Search }

        public static MonsterAI Instance { get; private set; }

        [Header("Скорости")]
        public float patrolSpeed = 2f;
        public float investigateSpeed = 3.4f;
        public float chaseSpeed = 5.3f;

        [Header("Зрение")]
        public Transform eyes;
        public float sightRange = 12f;
        [Tooltip("Множитель дальности, если у игрока включён фонарь")]
        public float flashlightSightMultiplier = 1.8f;
        public float fieldOfView = 110f;
        [Tooltip("На этой дистанции монстр чувствует игрока в любом направлении")]
        public float proximityRange = 2.5f;

        [Header("Слух")]
        public float runNoiseRadius = 16f;
        public float walkNoiseRadius = 6f;
        public float runSpeedThreshold = 4.5f;
        public float walkSpeedThreshold = 1.8f;

        [Header("Поведение")]
        public float catchDistance = 1.6f;
        public float loseSightTime = 4f;
        public float searchTime = 8f;
        public float searchRadius = 7f;
        public float patrolRadius = 30f;

        [Header("Визуал")]
        public Light eyeLight;
        public float eyeIntensityCalm = 0.6f;
        public float eyeIntensityChase = 3f;

        public readonly NetworkVariable<State> CurrentState = new(State.Patrol);
        public bool IsChasing => CurrentState.Value == State.Chase;

        NavMeshAgent agent;
        AudioSource growlSource;
        AudioSource screamSource;

        PlayerState target;
        Vector3 lastKnownPosition;
        float lostTimer;
        float stateTimer;
        float repathTimer;
        readonly Dictionary<PlayerState, Vector3> lastPlayerPositions = new();

        public override void OnNetworkSpawn()
        {
            Instance = this;
            agent = GetComponent<NavMeshAgent>();
            SetupAudio();

            CurrentState.OnValueChanged += OnStateChanged;

            if (IsServer)
            {
                agent.enabled = true;
                agent.Warp(transform.position);
                GoToRandomPoint(transform.position, patrolRadius);
            }
            else
            {
                agent.enabled = false;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
            CurrentState.OnValueChanged -= OnStateChanged;
        }

        void SetupAudio()
        {
            growlSource = gameObject.AddComponent<AudioSource>();
            growlSource.clip = ProceduralAudio.Growl;
            growlSource.loop = true;
            growlSource.spatialBlend = 1f;
            growlSource.rolloffMode = AudioRolloffMode.Linear;
            growlSource.minDistance = 2f;
            growlSource.maxDistance = 25f;
            growlSource.volume = 0.4f;
            growlSource.Play();

            screamSource = gameObject.AddComponent<AudioSource>();
            screamSource.playOnAwake = false;
            screamSource.spatialBlend = 1f;
            screamSource.rolloffMode = AudioRolloffMode.Linear;
            screamSource.minDistance = 3f;
            screamSource.maxDistance = 40f;
        }

        void OnStateChanged(State previous, State current)
        {
            if (current == State.Chase && previous != State.Chase)
                screamSource.PlayOneShot(ProceduralAudio.Scream);
        }

        void Update()
        {
            if (!IsSpawned) return;
            UpdateVisuals();
            if (IsServer) Think(Time.deltaTime);
        }

        void UpdateVisuals()
        {
            bool chase = IsChasing;
            if (eyeLight != null)
            {
                float target = chase ? eyeIntensityChase : eyeIntensityCalm;
                eyeLight.intensity = Mathf.Lerp(eyeLight.intensity, target, Time.deltaTime * 4f);
            }
            if (growlSource != null)
            {
                growlSource.volume = Mathf.Lerp(growlSource.volume, chase ? 1f : 0.35f, Time.deltaTime * 2f);
                growlSource.pitch = Mathf.Lerp(growlSource.pitch, chase ? 1.25f : 0.9f, Time.deltaTime * 2f);
            }
        }

        // ---------------- Сервер ----------------

        void Think(float dt)
        {
            var gm = GameManager.Instance;
            if (gm == null || !gm.IsPlaying || !agent.isOnNavMesh)
            {
                if (agent.isOnNavMesh) agent.isStopped = true;
                return;
            }
            agent.isStopped = false;

            var seen = FindVisiblePlayer();
            if (seen != null)
            {
                target = seen;
                lastKnownPosition = seen.transform.position;
                lostTimer = 0f;
                SetState(State.Chase);
            }
            else if (CurrentState.Value != State.Chase && TryHearNoise(dt, out var noisePos))
            {
                lastKnownPosition = noisePos;
                SetState(State.Investigate);
                agent.SetDestination(noisePos);
            }
            else
            {
                TrackPlayerPositions(dt);
            }

            switch (CurrentState.Value)
            {
                case State.Chase: UpdateChase(dt, seen != null); break;
                case State.Investigate: UpdateInvestigate(); break;
                case State.Search: UpdateSearch(dt); break;
                default: UpdatePatrol(); break;
            }
        }

        void UpdateChase(float dt, bool canSee)
        {
            agent.speed = chaseSpeed;

            if (target == null || !target.IsAlive)
            {
                target = null;
                BeginSearch();
                return;
            }

            if (!canSee)
            {
                lostTimer += dt;
                if (lostTimer > loseSightTime)
                {
                    target = null;
                    SetState(State.Investigate);
                    agent.SetDestination(lastKnownPosition);
                    return;
                }
            }

            repathTimer -= dt;
            if (repathTimer <= 0f)
            {
                repathTimer = 0.2f;
                agent.SetDestination(canSee ? target.transform.position : lastKnownPosition);
            }

            var toTarget = target.transform.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.magnitude <= catchDistance && HasLineOfSight(target))
            {
                target.ServerKill();
                target = null;
                BeginSearch();
            }
        }

        void UpdateInvestigate()
        {
            agent.speed = investigateSpeed;
            if (Arrived()) BeginSearch();
        }

        void BeginSearch()
        {
            SetState(State.Search);
            stateTimer = searchTime;
            GoToRandomPoint(lastKnownPosition, searchRadius);
        }

        void UpdateSearch(float dt)
        {
            agent.speed = patrolSpeed;
            stateTimer -= dt;
            if (stateTimer <= 0f)
            {
                SetState(State.Patrol);
                GoToRandomPoint(transform.position, patrolRadius);
                return;
            }
            if (Arrived()) GoToRandomPoint(lastKnownPosition, searchRadius);
        }

        void UpdatePatrol()
        {
            agent.speed = patrolSpeed;
            if (Arrived()) GoToRandomPoint(transform.position, patrolRadius);
        }

        bool Arrived() => !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.6f;

        void SetState(State s)
        {
            if (CurrentState.Value != s) CurrentState.Value = s;
        }

        void GoToRandomPoint(Vector3 center, float radius)
        {
            for (int i = 0; i < 12; i++)
            {
                var p = center + new Vector3(Random.Range(-radius, radius), 0f, Random.Range(-radius, radius));
                p.y = center.y;
                if (NavMesh.SamplePosition(p, out var hit, 2f, NavMesh.AllAreas))
                {
                    var path = new NavMeshPath();
                    if (agent.CalculatePath(hit.position, path) && path.status == NavMeshPathStatus.PathComplete)
                    {
                        agent.SetPath(path);
                        return;
                    }
                }
            }
        }

        PlayerState FindVisiblePlayer()
        {
            PlayerState best = null;
            float bestDist = float.MaxValue;
            Vector3 eyePos = eyes != null ? eyes.position : transform.position + Vector3.up * 2f;

            foreach (var p in PlayerState.All)
            {
                if (!p.IsAlive) continue;
                var toPlayer = p.HeadPosition - eyePos;
                float dist = toPlayer.magnitude;

                bool near = dist <= proximityRange;
                if (!near)
                {
                    var flashlight = p.GetComponent<Flashlight>();
                    float range = sightRange * (flashlight != null && flashlight.IsOn ? flashlightSightMultiplier : 1f);
                    if (dist > range) continue;
                    var flat = new Vector3(toPlayer.x, 0f, toPlayer.z);
                    if (Vector3.Angle(transform.forward, flat) > fieldOfView * 0.5f) continue;
                }

                if (dist < bestDist && HasLineOfSight(p))
                {
                    best = p;
                    bestDist = dist;
                }
            }
            return best;
        }

        bool HasLineOfSight(PlayerState p)
        {
            Vector3 eyePos = eyes != null ? eyes.position : transform.position + Vector3.up * 2f;
            if (!Physics.Linecast(eyePos, p.HeadPosition, out var hit, ~0, QueryTriggerInteraction.Ignore))
                return true;
            return hit.collider.GetComponentInParent<PlayerState>() == p;
        }

        bool TryHearNoise(float dt, out Vector3 position)
        {
            position = default;
            float bestDist = float.MaxValue;
            bool heard = false;

            foreach (var p in PlayerState.All)
            {
                var pos = p.transform.position;
                bool hasLast = lastPlayerPositions.TryGetValue(p, out var last);
                lastPlayerPositions[p] = pos;
                if (!hasLast || !p.IsAlive || dt <= 0f) continue;

                var delta = pos - last;
                delta.y = 0f;
                float speed = delta.magnitude / dt;
                if (speed > 20f) continue; // телепорт/респавн

                float radius = speed > runSpeedThreshold ? runNoiseRadius
                             : speed > walkSpeedThreshold ? walkNoiseRadius : 0f;
                float dist = Vector3.Distance(pos, transform.position);
                if (dist <= radius && dist < bestDist)
                {
                    bestDist = dist;
                    position = pos;
                    heard = true;
                }
            }
            return heard;
        }

        void TrackPlayerPositions(float dt) => TryHearNoise(0f, out _);

        public void ServerReset(Vector3 position)
        {
            if (!IsServer) return;
            target = null;
            lastPlayerPositions.Clear();
            agent.Warp(position);
            SetState(State.Patrol);
            GoToRandomPoint(position, patrolRadius);
        }
    }
}

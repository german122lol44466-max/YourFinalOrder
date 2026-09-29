using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using YourFinalOrder.AI;
using YourFinalOrder.Core;

namespace YourFinalOrder.Player
{
    /// <summary>
    /// Человекоподобный робот игрока (скелет + анимации из Tools/Blender/build_robots.py).
    /// Модель выбирается в настройках и синхронизируется по сети. Animator проигрывает
    /// стойку/ходьбу/бег/присед по скорости; поверх — поворот головы за взглядом,
    /// антенна крутится, пока игрок говорит, на визоре — анимированное лицо.
    /// </summary>
    public class RobotAppearance : NetworkBehaviour
    {
        [Tooltip("Модели в порядке перечисления RobotModel: Can, Toaster, Lantern")]
        public GameObject[] models;
        public RuntimeAnimatorController animator;
        [Tooltip("Ожидаемая высота модели с антенной — страховка от неверного масштаба FBX")]
        public float expectedHeight = 1.94f;
        public Transform bodyRoot;
        public Color faceColor = Color.white;
        public float faceIntensity = 2.2f;
        public KeyCode talkKey = KeyCode.V;

        [Header("Скорости клипов (м/с) — для синхронизации шагов")]
        public float walkClipSpeed = 1.67f;
        public float runClipSpeed = 4.75f;
        public float crouchClipSpeed = 1.4f;

        readonly NetworkVariable<byte> model = new(0,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<bool> talking = new(false,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<bool> crouching = new(false,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public bool IsTalking => talking.Value;
        public IReadOnlyList<Renderer> Renderers => renderers;

        static readonly int SpeedParam = Animator.StringToHash("Speed");
        static readonly int CrouchParam = Animator.StringToHash("Crouch");
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        static readonly int EmissionMap = Shader.PropertyToID("_EmissionMap");
        static readonly int BaseMap = Shader.PropertyToID("_BaseMap");

        GameObject instance;
        Animator anim;
        readonly List<Renderer> renderers = new();
        Transform head, antenna;
        Renderer visorRenderer;
        int visorIndex = -1;
        MaterialPropertyBlock block;
        RobotFace face;
        NetworkPlayer player;
        PlayerState state;
        FirstPersonController fpc;
        Vector3 lastPos;
        float speed, antennaAngle, antennaSpeed, talkLevel;
        bool visible = true;

        public override void OnNetworkSpawn()
        {
            player = GetComponent<NetworkPlayer>();
            state = GetComponent<PlayerState>();
            fpc = GetComponent<FirstPersonController>();
            if (bodyRoot == null) bodyRoot = transform;
            block = new MaterialPropertyBlock();
            face = new RobotFace((int)(NetworkObjectId * 7919 % int.MaxValue));

            model.OnValueChanged += OnModelChanged;
            if (IsOwner)
            {
                model.Value = (byte)GameSettings.Robot;
                GameSettings.Changed += OnSettingsChanged;
            }
            Build(model.Value);
            lastPos = transform.position;
        }

        public override void OnNetworkDespawn()
        {
            model.OnValueChanged -= OnModelChanged;
            GameSettings.Changed -= OnSettingsChanged;
            face?.Dispose();
        }

        void OnSettingsChanged()
        {
            if (IsOwner && model.Value != (byte)GameSettings.Robot) model.Value = (byte)GameSettings.Robot;
        }

        void OnModelChanged(byte previous, byte current) => Build(current);

        // ------------------------------------------------------------ сборка модели

        void Build(int index)
        {
            if (instance != null) Destroy(instance);
            renderers.Clear();
            head = antenna = null;
            visorRenderer = null;
            visorIndex = -1;
            if (models == null || models.Length == 0) return;
            index = Mathf.Clamp(index, 0, models.Length - 1);
            if (models[index] == null) return;

            instance = Instantiate(models[index], bodyRoot);
            instance.name = "Robot";
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            Align(instance.transform);

            head = FindDeep(instance.transform, "Head");
            antenna = FindDeep(instance.transform, "Antenna");

            anim = instance.GetComponentInChildren<Animator>();
            if (anim == null) anim = instance.AddComponent<Animator>();
            anim.runtimeAnimatorController = animator;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            instance.GetComponentsInChildren(renderers);
            foreach (var r in renderers)
            {
                // своё тело не мешает обзору, но отбрасывает тень
                r.shadowCastingMode = IsOwner ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
                r.enabled = visible;
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] != null && mats[i].name.Contains("Visor"))
                    {
                        visorRenderer = r;
                        visorIndex = i;
                    }
                }
            }
        }

        /// <summary>
        /// Выравнивает модель по пустышкам Front (вперёд) и Top (вверх), подгоняет масштаб и ставит на землю.
        /// Так модель стоит правильно при любых настройках осей FBX.
        /// </summary>
        void Align(Transform t)
        {
            var front = FindDeep(t, "Front");
            var top = FindDeep(t, "Top");
            if (front != null && top != null)
            {
                var f = t.InverseTransformPoint(front.position);
                var u = t.InverseTransformPoint(top.position);
                if (f.sqrMagnitude > 1e-6f && u.sqrMagnitude > 1e-6f)
                    t.localRotation = Quaternion.Inverse(Quaternion.LookRotation(f, u));
            }

            var rs = t.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return;
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            if (b.size.y > 0.01f && Mathf.Abs(b.size.y / expectedHeight - 1f) > 0.25f)
                t.localScale *= expectedHeight / b.size.y;
        }

        static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        public void SetVisible(bool value)
        {
            visible = value;
            foreach (var r in renderers)
                if (r != null) r.enabled = value;
        }

        // ------------------------------------------------------------ анимация

        void Update()
        {
            if (!IsSpawned || instance == null) return;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);

            if (IsOwner)
            {
                bool talk = Input.GetKey(talkKey) && Cursor.lockState == CursorLockMode.Locked;
                if (talking.Value != talk) talking.Value = talk;
                bool crouch = fpc != null && fpc.IsCrouching;
                if (crouching.Value != crouch) crouching.Value = crouch;
            }

            // скорость по перемещению — одинаково для своих и чужих игроков
            var delta = transform.position - lastPos;
            lastPos = transform.position;
            delta.y = 0f;
            float s = delta.magnitude / dt;
            if (s > 15f) s = 0f; // телепорт
            speed = Mathf.Lerp(speed, s, dt * 10f);

            if (anim != null && anim.runtimeAnimatorController != null)
            {
                bool crouch = crouching.Value;
                anim.SetBool(CrouchParam, crouch);
                anim.SetFloat(SpeedParam, speed, 0.1f, dt);
                // шаги в такт движению: клип ускоряется/замедляется под реальную скорость
                float nominal = crouch ? crouchClipSpeed
                    : speed <= walkClipSpeed ? walkClipSpeed
                    : Mathf.Lerp(walkClipSpeed, runClipSpeed, Mathf.InverseLerp(walkClipSpeed, runClipSpeed, speed));
                anim.speed = speed < 0.3f ? 1f : Mathf.Clamp(speed / nominal, 0.7f, 1.4f);
            }

            talkLevel = Mathf.MoveTowards(talkLevel, talking.Value ? 0.4f + 0.6f * Mathf.PerlinNoise(Time.time * 9f, NetworkObjectId) : 0f, dt * 6f);
            UpdateFace(dt);
        }

        void LateUpdate()
        {
            if (!IsSpawned || instance == null) return;

            // голова смотрит туда же, куда игрок (поверх анимации)
            if (head != null && player != null && (state == null || state.IsAlive))
            {
                float pitch = Mathf.Clamp(player.LookPitch, -45f, 45f) * 0.75f;
                head.rotation = Quaternion.AngleAxis(pitch, transform.right) * head.rotation;
            }

            // антенна крутится, пока игрок говорит
            antennaSpeed = Mathf.MoveTowards(antennaSpeed, talking.Value ? 1080f : 0f, Time.deltaTime * 1800f);
            antennaAngle = (antennaAngle + antennaSpeed * Time.deltaTime) % 360f;
            if (antenna != null && head != null)
                antenna.rotation = Quaternion.AngleAxis(antennaAngle, head.up) * antenna.rotation;
        }

        void UpdateFace(float dt)
        {
            if (visorRenderer == null || face == null) return;

            float fear = 0f;
            if (MonsterAI.Instance != null)
                fear = 1f - Mathf.Clamp01(Vector3.Distance(MonsterAI.Instance.transform.position, transform.position) / 14f);

            var mood = state != null && !state.IsAlive ? RobotFace.Mood.Dead
                : fear > 0.45f ? RobotFace.Mood.Scared
                : talking.Value ? RobotFace.Mood.Talking
                : RobotFace.Mood.Normal;
            face.Update(dt, mood, fear, talkLevel);

            // старый экран: лёгкое мерцание яркости, иногда проседает
            float k = 0.9f + 0.1f * Mathf.PerlinNoise(Time.time * 4f, NetworkObjectId * 0.37f);
            if (Mathf.PerlinNoise(Time.time * 1.7f, 3.3f + NetworkObjectId) > 0.82f) k *= 0.45f;

            visorRenderer.GetPropertyBlock(block, visorIndex);
            block.SetTexture(BaseMap, RobotFace.DirtyGlass);
            block.SetTexture(EmissionMap, face.Texture);
            block.SetColor(EmissionColor, faceColor * (faceIntensity * k));
            visorRenderer.SetPropertyBlock(block, visorIndex);
        }
    }
}

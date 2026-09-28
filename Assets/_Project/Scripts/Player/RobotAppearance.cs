using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using YourFinalOrder.AI;
using YourFinalOrder.Core;

namespace YourFinalOrder.Player
{
    /// <summary>
    /// Модель робота игрока: выбирается в настройках и синхронизируется по сети.
    /// Анимирует детали из FBX (Tools/Blender/build_robots.py): покачивание корпуса, руки при ходьбе,
    /// голову за взглядом, антенну при разговоре и барахлящее светящееся «лицо».
    /// </summary>
    public class RobotAppearance : NetworkBehaviour
    {
        [Tooltip("Модели в порядке перечисления RobotModel: Can, Toaster, Lantern")]
        public GameObject[] models;
        [Tooltip("Ожидаемая высота моделей (с антенной) — для страховки от неверного масштаба FBX")]
        public float[] expectedHeights = { 1.83f, 1.99f, 2.13f };
        public Transform bodyRoot;
        public Color faceColor = new(0.3f, 1f, 0.5f);
        public float faceIntensity = 4f;
        public KeyCode talkKey = KeyCode.V;

        readonly NetworkVariable<byte> model = new(0,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<bool> talking = new(false,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public bool IsTalking => talking.Value;
        public IReadOnlyList<Renderer> Renderers => renderers;

        GameObject instance;
        readonly List<Renderer> renderers = new();
        Transform head, armL, armR, antenna;
        Quaternion headBase, armLBase, armRBase;
        Renderer face;
        MaterialPropertyBlock faceBlock;
        Texture2D screen;
        float screenTimer, blinkTimer = 2f, glitchTimer;
        Vector3 instanceBase;
        Vector3 lastPos;
        float speed, walkPhase, antennaSpeed;
        bool visible = true;
        NetworkPlayer player;

        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        static readonly int EmissionMap = Shader.PropertyToID("_EmissionMap");

        public override void OnNetworkSpawn()
        {
            player = GetComponent<NetworkPlayer>();
            if (bodyRoot == null) bodyRoot = transform;
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
            if (screen != null) Destroy(screen);
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
            head = armL = armR = antenna = null;
            face = null;
            if (models == null || models.Length == 0) return;
            index = Mathf.Clamp(index, 0, models.Length - 1);
            if (models[index] == null) return;

            instance = Instantiate(models[index], bodyRoot);
            instance.name = "Robot";
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            Align(instance.transform, index);
            instanceBase = instance.transform.localPosition;

            head = Find("Head");
            armL = Find("ArmL");
            armR = Find("ArmR");
            antenna = Find("Antenna");
            var faceT = Find("Face");
            face = faceT != null ? faceT.GetComponent<Renderer>() : null;
            if (head) headBase = head.localRotation;
            if (armL) armLBase = armL.localRotation;
            if (armR) armRBase = armR.localRotation;

            instance.GetComponentsInChildren(renderers);
            foreach (var r in renderers)
            {
                r.shadowCastingMode = IsOwner ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
                r.enabled = visible;
            }

            faceBlock ??= new MaterialPropertyBlock();
            // «Тостеру» рисуем глаза на экране
            if (index == (int)RobotModel.Toaster && face != null)
            {
                if (screen == null)
                {
                    screen = new Texture2D(48, 32, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                }
                DrawScreen(false, 0);
            }
        }

        /// <summary>
        /// Выравнивает модель по пустышкам Front и Top: Front — вперёд (+Z), Top — вверх.
        /// Так модель стоит правильно независимо от настроек осей при импорте FBX.
        /// </summary>
        void Align(Transform t, int index)
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

            // Страховка от неверного масштаба (например, ×100 при импорте)
            var rs = t.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return;
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            float expected = index < expectedHeights.Length ? expectedHeights[index] : 1.8f;
            float h = b.size.y;
            if (h > 0.01f && Mathf.Abs(h / expected - 1f) > 0.25f)
                t.localScale *= expected / h;

            // ноги — на уровне земли
            b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            t.position += Vector3.up * (bodyRoot.position.y - b.min.y);
        }

        Transform Find(string name) => instance != null ? FindDeep(instance.transform, name) : null;

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
            }

            // скорость по перемещению — одинаково для своих и чужих
            var delta = transform.position - lastPos;
            lastPos = transform.position;
            delta.y = 0f;
            float s = delta.magnitude / dt;
            if (s > 15f) s = 0f; // телепорт
            speed = Mathf.Lerp(speed, s, dt * 8f);
            float move = Mathf.Clamp01(speed / 5f);
            walkPhase += dt * Mathf.Lerp(0f, 11f, move);

            // покачивание корпуса
            instance.transform.localPosition = instanceBase + Vector3.up * (Mathf.Abs(Mathf.Sin(walkPhase)) * 0.04f * move);

            // руки
            float swing = Mathf.Sin(walkPhase) * 32f * move;
            float idle = Mathf.Sin(Time.time * 1.3f) * 2f;
            if (armL) armL.localRotation = armLBase * Quaternion.Euler(swing + idle, 0f, 0f);
            if (armR) armR.localRotation = armRBase * Quaternion.Euler(-swing + idle, 0f, 0f);

            // голова за взглядом
            if (head && player != null)
                head.localRotation = headBase * Quaternion.Euler(Mathf.Clamp(player.LookPitch, -40f, 40f) * 0.7f, 0f, 0f);

            // антенна: крутится, пока игрок говорит
            antennaSpeed = Mathf.MoveTowards(antennaSpeed, talking.Value ? 900f : 0f, dt * 1500f);
            if (antenna && antennaSpeed > 0.1f) antenna.Rotate(Vector3.up, antennaSpeed * dt, Space.Self);

            UpdateFace(dt);
        }

        void UpdateFace(float dt)
        {
            if (face == null) return;

            // страх: рядом монстр — лицо барахлит сильнее
            float fear = 0f;
            if (MonsterAI.Instance != null)
                fear = 1f - Mathf.Clamp01(Vector3.Distance(MonsterAI.Instance.transform.position, transform.position) / 15f);

            float k = 0.85f + 0.15f * Mathf.PerlinNoise(Time.time * 3f, transform.position.x);
            glitchTimer -= dt;
            if (glitchTimer <= 0f)
            {
                glitchTimer = Random.Range(0.05f, Mathf.Lerp(3f, 0.3f, fear));
                if (Random.value < 0.35f + fear * 0.5f) k = Random.Range(0f, 0.3f);
            }
            if (talking.Value) k *= 1f + 0.35f * Mathf.Abs(Mathf.Sin(Time.time * 18f));

            face.GetPropertyBlock(faceBlock);
            faceBlock.SetColor(EmissionColor, faceColor * (faceIntensity * k));

            if (screen != null && model.Value == (byte)RobotModel.Toaster)
            {
                screenTimer -= dt;
                if (screenTimer <= 0f)
                {
                    screenTimer = 0.08f;
                    blinkTimer -= 0.08f;
                    bool blink = blinkTimer < 0.12f;
                    if (blinkTimer <= 0f) blinkTimer = Random.Range(2f, 5f);
                    int glitchRows = Random.value < 0.15f + fear * 0.6f ? Random.Range(1, 6) : 0;
                    DrawScreen(blink, glitchRows);
                }
                faceBlock.SetTexture(BaseMap, screen);
                faceBlock.SetTexture(EmissionMap, screen);
            }
            face.SetPropertyBlock(faceBlock);
        }

        /// <summary>Пиксельное лицо на экране «Тостера»: два глаза, сканлайны, сдвиг строк при глитче.</summary>
        void DrawScreen(bool blink, int glitchRows)
        {
            int w = screen.width, h = screen.height;
            var px = new Color32[w * h];
            var bg = new Color32(8, 26, 14, 255);
            var fg = new Color32(120, 255, 150, 255);
            for (int i = 0; i < px.Length; i++) px[i] = bg;

            int eyeH = blink ? 1 : 8;
            int talkOffset = talking.Value ? Random.Range(-1, 2) : 0;
            foreach (int cx in new[] { 15, 33 })
                for (int y = 16 - eyeH / 2; y < 16 + (eyeH + 1) / 2; y++)
                for (int x = cx - 3; x <= cx + 3; x++)
                    px[(y + talkOffset) * w + x] = fg;
            if (talking.Value)
                for (int x = 19; x < 29; x++) px[7 * w + x] = fg;

            for (int y = 0; y < h; y += 2) // сканлайны
            for (int x = 0; x < w; x++)
            {
                var c = px[y * w + x];
                px[y * w + x] = new Color32((byte)(c.r * 0.7f), (byte)(c.g * 0.7f), (byte)(c.b * 0.7f), 255);
            }

            for (int g = 0; g < glitchRows; g++) // горизонтальный сдвиг строк
            {
                int row = Random.Range(0, h);
                int shift = Random.Range(-6, 7);
                var line = new Color32[w];
                for (int x = 0; x < w; x++) line[x] = px[row * w + (x - shift + w) % w];
                for (int x = 0; x < w; x++) px[row * w + x] = line[x];
            }

            screen.SetPixels32(px);
            screen.Apply(false);
        }
    }
}

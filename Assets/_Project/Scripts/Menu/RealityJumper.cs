using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using YourFinalOrder.Audio;

namespace YourFinalOrder.Menu
{
    /// <summary>
    /// Фон меню: каждые N секунд шаттл «прыгает» в другую реальность —
    /// разгон звёзд, вспышка, искажения, затем новые цвета туманностей и света.
    /// Также создаёт постобработку (bloom, виньетка, зерно, аберрации).
    /// </summary>
    public class RealityJumper : MonoBehaviour
    {
        [Serializable]
        public struct Reality
        {
            public string name;
            public Color background;
            public Color nebulaA;
            public Color nebulaB;
            public Color rimLight;
            public Color reactor;
        }

        public ShuttleFlyby ship;
        public SpaceParticles space;
        public MenuCameraRig cameraRig;
        public Light rimLight;
        public float interval = 16f;

        public Reality[] realities =
        {
            new() { name = "Уровень 0 · Жёлтые комнаты", background = new Color(0.035f, 0.026f, 0.01f),
                nebulaA = new Color(0.95f, 0.72f, 0.22f), nebulaB = new Color(0.45f, 0.3f, 0.05f),
                rimLight = new Color(1f, 0.8f, 0.45f), reactor = new Color(1f, 0.7f, 0.3f) },
            new() { name = "Уровень 1 · Мёртвый город", background = new Color(0.01f, 0.02f, 0.03f),
                nebulaA = new Color(0.2f, 0.6f, 0.75f), nebulaB = new Color(0.08f, 0.18f, 0.35f),
                rimLight = new Color(0.55f, 0.8f, 1f), reactor = new Color(0.35f, 0.75f, 1f) },
            new() { name = "Уровень 2 · Игрушечный пригород", background = new Color(0.01f, 0.03f, 0.015f),
                nebulaA = new Color(0.35f, 0.9f, 0.45f), nebulaB = new Color(0.1f, 0.4f, 0.6f),
                rimLight = new Color(0.65f, 1f, 0.75f), reactor = new Color(0.4f, 1f, 0.6f) },
            new() { name = "Уровень 3 · Ночная школа", background = new Color(0.012f, 0.014f, 0.045f),
                nebulaA = new Color(0.3f, 0.35f, 0.95f), nebulaB = new Color(0.5f, 0.2f, 0.7f),
                rimLight = new Color(0.65f, 0.65f, 1f), reactor = new Color(0.55f, 0.5f, 1f) },
            new() { name = "??? · Разлом", background = new Color(0.045f, 0.005f, 0.012f),
                nebulaA = new Color(0.95f, 0.15f, 0.2f), nebulaB = new Color(0.4f, 0.05f, 0.3f),
                rimLight = new Color(1f, 0.4f, 0.4f), reactor = new Color(1f, 0.3f, 0.35f) },
        };

        public static event Action<string> RealityChanged;
        public static string CurrentName { get; private set; } = "";

        Camera cam;
        Volume baseVolume;
        Volume jumpVolume;
        AudioSource hum;
        AudioSource whoosh;
        int index;
        float timer;

        void Start()
        {
            cam = cameraRig != null ? cameraRig.GetComponent<Camera>() : Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                var data = cam.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = true;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            }
            CreateVolumes();
            CreateAudio();

            index = UnityEngine.Random.Range(0, realities.Length);
            Apply(realities[index], refill: false);
            timer = interval;
        }

        void CreateVolumes()
        {
            baseVolume = CreateVolume("PostFX", 0);
            var p = baseVolume.profile;
            var bloom = p.Add<Bloom>(true);
            bloom.intensity.Override(1.4f);
            bloom.threshold.Override(0.85f);
            bloom.scatter.Override(0.72f);
            var tone = p.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);
            var vig = p.Add<Vignette>(true);
            vig.intensity.Override(0.42f);
            vig.smoothness.Override(0.5f);
            var grain = p.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Medium3);
            grain.intensity.Override(0.35f);
            var ca = p.Add<ChromaticAberration>(true);
            ca.intensity.Override(0.12f);
            var color = p.Add<ColorAdjustments>(true);
            color.contrast.Override(12f);
            color.saturation.Override(-8f);

            jumpVolume = CreateVolume("JumpFX", 10);
            jumpVolume.weight = 0f;
            var j = jumpVolume.profile;
            var jca = j.Add<ChromaticAberration>(true);
            jca.intensity.Override(1f);
            var lens = j.Add<LensDistortion>(true);
            lens.intensity.Override(-0.55f);
            lens.scale.Override(0.92f);
            var jc = j.Add<ColorAdjustments>(true);
            jc.postExposure.Override(2.2f);
            var jb = j.Add<Bloom>(true);
            jb.intensity.Override(6f);
        }

        Volume CreateVolume(string name, int priority)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var v = go.AddComponent<Volume>();
            v.isGlobal = true;
            v.priority = priority;
            v.profile = ScriptableObject.CreateInstance<VolumeProfile>();
            return v;
        }

        void CreateAudio()
        {
            hum = gameObject.AddComponent<AudioSource>();
            hum.clip = ProceduralAudio.ReactorHum;
            hum.loop = true;
            hum.volume = 0.35f;
            hum.spatialBlend = 0f;
            hum.Play();
            whoosh = gameObject.AddComponent<AudioSource>();
            whoosh.playOnAwake = false;
            whoosh.spatialBlend = 0f;
        }

        void Update()
        {
            timer -= Time.deltaTime;
            if (timer <= 0f)
            {
                timer = interval;
                StartCoroutine(Jump());
            }
        }

        IEnumerator Jump()
        {
            whoosh.PlayOneShot(ProceduralAudio.WarpJump, 0.9f);

            // Разгон
            yield return Animate(1.6f, t =>
            {
                float e = t * t;
                SetIntensity(e, e * 0.35f);
            });

            // Смена реальности на пике вспышки
            index = (index + 1) % realities.Length;
            Apply(realities[index], refill: true);

            // Выход из прыжка
            yield return Animate(1.4f, t =>
            {
                float e = 1f - t;
                e = e * e * (3f - 2f * e);
                SetIntensity(e, e * 0.35f);
            });
            SetIntensity(0f, 0f);
        }

        void SetIntensity(float warp, float flash)
        {
            if (space != null) space.SpeedMultiplier = 1f + warp * 7f;
            if (ship != null) ship.Thrust = 1f + warp;
            if (cameraRig != null)
            {
                cameraRig.FovKick = warp * 22f;
                cameraRig.Shake = warp;
            }
            if (jumpVolume != null) jumpVolume.weight = Mathf.Clamp01(warp * 0.85f + flash);
            if (hum != null) hum.pitch = 1f + warp * 0.6f;
        }

        static IEnumerator Animate(float duration, Action<float> step)
        {
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                step(t / duration);
                yield return null;
            }
            step(1f);
        }

        void Apply(Reality r, bool refill)
        {
            if (cam != null) cam.backgroundColor = r.background;
            if (space != null) space.SetNebulaColors(r.nebulaA, r.nebulaB, refill);
            if (rimLight != null) rimLight.color = r.rimLight;
            if (ship != null) ship.SetReactorColor(r.reactor);
            RenderSettings.ambientLight = r.background * 4f;
            CurrentName = r.name;
            RealityChanged?.Invoke(r.name);
        }
    }
}

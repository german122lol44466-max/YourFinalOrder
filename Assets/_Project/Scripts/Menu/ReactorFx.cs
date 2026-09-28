using UnityEngine;

namespace YourFinalOrder.Menu
{
    /// <summary>Пламя реактора: вытянутые частицы, свечение и мерцающий свет.</summary>
    public class ReactorFx : MonoBehaviour
    {
        public float Thrust { get; set; } = 1f;

        ParticleSystem flame;
        ParticleSystem sparks;
        Light glow;
        Color color;
        float seed;

        public void Init(Material mat, Color c, Vector3 exhaustDirection)
        {
            color = c;
            seed = Random.value * 100f;

            var lightGo = new GameObject("ReactorLight");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.position = transform.position + exhaustDirection * 1.2f;
            glow = lightGo.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.range = 14f;
            glow.intensity = 6f;
            glow.color = c;

            flame = CreateSystem("Flame", mat, exhaustDirection);
            var main = flame.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(28f, 36f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.9f, 1.3f);
            main.maxParticles = 400;
            var emission = flame.emission;
            emission.rateOverTime = 160f;
            var shape = flame.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 2.5f;
            shape.radius = 0.35f;
            var size = flame.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));
            var renderer = flame.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.12f;
            renderer.lengthScale = 2f;

            sparks = CreateSystem("Sparks", mat, exhaustDirection);
            var sm = sparks.main;
            sm.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
            sm.startSpeed = new ParticleSystem.MinMaxCurve(15f, 30f);
            sm.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
            sm.maxParticles = 200;
            var se = sparks.emission;
            se.rateOverTime = 25f;
            var ss = sparks.shape;
            ss.shapeType = ParticleSystemShapeType.Cone;
            ss.angle = 12f;
            ss.radius = 0.3f;
            var sr = sparks.GetComponent<ParticleSystemRenderer>();
            sr.renderMode = ParticleSystemRenderMode.Stretch;
            sr.velocityScale = 0.05f;

            SetColor(c);
        }

        ParticleSystem CreateSystem(string name, Material mat, Vector3 direction)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.rotation = Quaternion.LookRotation(direction);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = true;
            main.loop = true;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        public void SetColor(Color c)
        {
            color = c;
            if (glow != null) glow.color = c;
            if (flame != null)
            {
                var col = flame.colorOverLifetime;
                col.enabled = true;
                var g = new Gradient();
                g.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(c, 0.25f), new GradientColorKey(c * 0.5f, 1f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.7f, 0.4f), new GradientAlphaKey(0f, 1f) });
                col.color = g;
            }
            if (sparks != null)
            {
                var m = sparks.main;
                m.startColor = Color.Lerp(c, Color.white, 0.5f);
            }
        }

        void Update()
        {
            float flicker = 0.8f + 0.4f * Mathf.PerlinNoise(seed, Time.time * 18f);
            if (glow != null) glow.intensity = 6f * flicker * Thrust;
            if (flame != null)
            {
                var main = flame.main;
                main.startSpeedMultiplier = 32f * Mathf.Lerp(1f, 2.2f, Thrust - 1f);
                main.startSizeMultiplier = 1.1f * flicker * Mathf.Min(Thrust, 1.8f);
            }
        }
    }
}

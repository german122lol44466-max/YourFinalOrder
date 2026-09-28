using UnityEngine;

namespace YourFinalOrder.Menu
{
    /// <summary>
    /// Звёзды на варп-скорости и туманности, пролетающие мимо шаттла.
    /// Всё создаётся в коде, нужен только аддитивный материал с мягкой точкой.
    /// </summary>
    public class SpaceParticles : MonoBehaviour
    {
        public Material additiveMaterial;
        [Tooltip("Направление полёта корабля (звёзды летят навстречу)")]
        public Transform ship;
        public float starSpeed = 90f;
        public float nebulaSpeed = 12f;

        public float SpeedMultiplier { get; set; } = 1f;

        Color nebulaA = new(0.4f, 0.5f, 0.9f);
        Color nebulaB = new(0.2f, 0.1f, 0.4f);

        ParticleSystem stars;
        ParticleSystem farStars;
        ParticleSystem nebula;
        ParticleSystem dust;

        void Start()
        {
            var dir = ship != null ? ship.forward : Vector3.forward;
            // эмиттеры стоят впереди по курсу, частицы летят назад
            stars = Create("WarpStars", dir * 160f, dir, new Vector3(160f, 110f, 10f));
            ConfigureStars(stars, 900, 0.1f, 0.35f, 2.2f);

            dust = Create("Dust", dir * 50f, dir, new Vector3(60f, 40f, 10f));
            ConfigureStars(dust, 300, 0.04f, 0.1f, 1.3f);

            farStars = Create("FarStars", Vector3.zero, dir, Vector3.one);
            ConfigureFarStars(farStars);

            nebula = Create("Nebula", dir * 260f, dir, new Vector3(420f, 260f, 20f));
            ConfigureNebula(nebula);
        }

        ParticleSystem Create(string name, Vector3 offset, Vector3 dir, Vector3 boxSize)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = offset;
            // ось Z системы смотрит против курса — частицы летят назад
            go.transform.rotation = Quaternion.LookRotation(-dir);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = boxSize;
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.loop = true;
            main.playOnAwake = true;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = additiveMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        void ConfigureStars(ParticleSystem ps, int max, float minSize, float maxSize, float lifetime)
        {
            var main = ps.main;
            main.maxParticles = max;
            main.startLifetime = lifetime;
            main.startSpeed = starSpeed;
            main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.8f, 0.85f, 1f), new Color(1f, 0.9f, 0.75f));
            var e = ps.emission;
            e.rateOverTime = max / lifetime;
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.speedModifier = 1f;
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.04f;
            r.lengthScale = 1f;
            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.85f), new GradientAlphaKey(0f, 1f) });
            fade.color = g;
            ps.Play();
            ps.Simulate(lifetime, true, true);
            ps.Play();
        }

        void ConfigureFarStars(ParticleSystem ps)
        {
            var main = ps.main;
            main.maxParticles = 2000;
            main.startLifetime = 1e6f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.8f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 900f;
            shape.radiusThickness = 0f;
            var e = ps.emission;
            e.rateOverTime = 0f;
            ps.Play();
            ps.Emit(2000);
        }

        void ConfigureNebula(ParticleSystem ps)
        {
            var main = ps.main;
            main.maxParticles = 160;
            main.startLifetime = 45f;
            main.startSpeed = nebulaSpeed;
            main.startSize = new ParticleSystem.MinMaxCurve(90f, 220f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(nebulaA, nebulaB);
            var e = ps.emission;
            e.rateOverTime = 3.2f;
            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.14f, 0.2f), new GradientAlphaKey(0.14f, 0.8f), new GradientAlphaKey(0f, 1f) });
            fade.color = g;
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sortingFudge = 50f;
            r.maxParticleSize = 3f;
            ps.Play();
            ps.Simulate(40f, true, true);
            ps.Play();
        }

        /// <summary>Сменить цвета туманностей (новая реальность).</summary>
        public void SetNebulaColors(Color a, Color b, bool refill)
        {
            nebulaA = a;
            nebulaB = b;
            if (nebula == null) return;
            var main = nebula.main;
            main.startColor = new ParticleSystem.MinMaxGradient(a, b);
            if (refill)
            {
                nebula.Clear();
                nebula.Simulate(40f, true, true);
                nebula.Play();
            }
        }

        void Update()
        {
            if (stars == null) return;
            SetSpeed(stars, SpeedMultiplier, 0.04f * SpeedMultiplier);
            SetSpeed(dust, SpeedMultiplier, 0.03f * SpeedMultiplier);
            var nm = nebula.main;
            nm.simulationSpeed = Mathf.Lerp(1f, 6f, (SpeedMultiplier - 1f) / 6f);
        }

        static void SetSpeed(ParticleSystem ps, float multiplier, float stretch)
        {
            // speedModifier действует и на уже летящие частицы
            var vel = ps.velocityOverLifetime;
            vel.speedModifier = multiplier;
            ps.GetComponent<ParticleSystemRenderer>().velocityScale = stretch;
        }
    }
}

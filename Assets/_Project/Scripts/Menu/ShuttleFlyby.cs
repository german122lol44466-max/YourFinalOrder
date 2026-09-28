using System.Collections.Generic;
using UnityEngine;

namespace YourFinalOrder.Menu
{
    /// <summary>
    /// Шаттл на фоне меню: выравнивает модель по направлению полёта (по пустышкам Engine_*),
    /// покачивается и создаёт эффекты реактора на каждом сопле.
    /// </summary>
    public class ShuttleFlyby : MonoBehaviour
    {
        public Transform model;
        public Material additiveMaterial;
        public Color reactorColor = new(0.35f, 0.75f, 1f);
        public float expectedLength = 18f;

        [Header("Покачивание")]
        public float bobAmplitude = 0.6f;
        public float bobSpeed = 0.35f;
        public float rollAmplitude = 6f;
        public float yawAmplitude = 3f;

        /// <summary>Множитель тяги реактора (растёт во время прыжка).</summary>
        public float Thrust { get; set; } = 1f;

        readonly List<ReactorFx> reactors = new();
        Vector3 basePosition;
        float seed;

        void Start()
        {
            basePosition = transform.position;
            seed = Random.value * 10f;
            if (model == null && transform.childCount > 0) model = transform.GetChild(0);
            if (model == null) return;

            var engines = new List<Transform>();
            foreach (var t in model.GetComponentsInChildren<Transform>())
                if (t.name.StartsWith("Engine_")) engines.Add(t);
            engines.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

            AlignModel(engines);

            foreach (var e in engines)
            {
                var fx = e.gameObject.AddComponent<ReactorFx>();
                fx.Init(additiveMaterial, reactorColor, -transform.forward);
                reactors.Add(fx);
            }
        }

        void AlignModel(List<Transform> engines)
        {
            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0 || engines.Count == 0) return;

            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);

            Vector3 engineCenter = Vector3.zero;
            foreach (var e in engines) engineCenter += e.position;
            engineCenter /= engines.Count;

            Vector3 forward = (bounds.center - engineCenter).normalized;
            Vector3 up = Vector3.up;
            if (engines.Count >= 3)
            {
                // Engine_0 — верхнее сопло, Engine_1/2 — нижние
                var lower = (engines[1].position + engines[2].position) * 0.5f;
                up = Vector3.ProjectOnPlane(engines[0].position - lower, forward).normalized;
            }

            var fix = Quaternion.LookRotation(transform.forward, transform.up) *
                      Quaternion.Inverse(Quaternion.LookRotation(forward, up));
            model.rotation = fix * model.rotation;

            // Страховка от неверного масштаба FBX: шаттл должен быть ~18 м в длину
            bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            float length = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (length > 0.01f && Mathf.Abs(length / expectedLength - 1f) > 0.4f)
                model.localScale *= expectedLength / length;

            bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            model.position += transform.position - bounds.center;
        }

        void Update()
        {
            float t = Time.time * bobSpeed + seed;
            transform.position = basePosition + new Vector3(
                Mathf.Sin(t * 0.7f) * bobAmplitude * 0.6f,
                Mathf.Sin(t) * bobAmplitude,
                0f);
            transform.rotation = Quaternion.Euler(
                Mathf.Sin(t * 0.8f + 1.3f) * 1.5f,
                Mathf.Sin(t * 0.5f) * yawAmplitude,
                Mathf.Sin(t * 0.9f) * rollAmplitude);

            foreach (var r in reactors) r.Thrust = Thrust;
        }

        public void SetReactorColor(Color c)
        {
            reactorColor = c;
            foreach (var r in reactors) r.SetColor(c);
        }
    }
}

using UnityEngine;

namespace YourFinalOrder.World
{
    /// <summary>
    /// Мерцающая лампа с редкими полными отключениями. Чисто локальный эффект.
    /// </summary>
    [RequireComponent(typeof(Light))]
    public class FlickerLight : MonoBehaviour
    {
        public float flickerSpeed = 8f;
        [Range(0f, 1f)] public float minIntensity = 0.5f;
        public float blackoutChancePerSecond = 0.08f;
        public Vector2 blackoutDuration = new(0.1f, 1.2f);
        public Renderer emissiveRenderer;

        Light lamp;
        float baseIntensity;
        float seed;
        float blackoutTimer;

        void Awake()
        {
            lamp = GetComponent<Light>();
            baseIntensity = lamp.intensity;
            seed = Random.value * 100f;
        }

        void Update()
        {
            if (blackoutTimer > 0f)
            {
                blackoutTimer -= Time.deltaTime;
                SetLit(0f);
                return;
            }
            if (Random.value < blackoutChancePerSecond * Time.deltaTime)
                blackoutTimer = Random.Range(blackoutDuration.x, blackoutDuration.y);

            float n = Mathf.PerlinNoise(seed, Time.time * flickerSpeed);
            SetLit(Mathf.Lerp(minIntensity, 1f, n));
        }

        void SetLit(float k)
        {
            lamp.intensity = baseIntensity * k;
            if (emissiveRenderer != null) emissiveRenderer.enabled = k > 0.05f;
        }
    }
}

using UnityEngine;
using YourFinalOrder.AI;
using YourFinalOrder.Audio;

namespace YourFinalOrder.Player
{
    /// <summary>
    /// Эффекты страха у локального игрока: сердцебиение и дрожь камеры, когда монстр рядом.
    /// </summary>
    public class FearEffects : LocalOnlyBehaviour
    {
        public Transform shakeTarget;
        public float fearRadius = 18f;
        public float maxShake = 0.03f;

        AudioSource heart;
        PlayerState state;
        float beatTimer;

        public float Fear01 { get; private set; }

        void Awake()
        {
            state = GetComponent<PlayerState>();
            heart = gameObject.AddComponent<AudioSource>();
            heart.playOnAwake = false;
            heart.spatialBlend = 0f;
        }

        void Update()
        {
            float fear = 0f;
            var monster = MonsterAI.Instance;
            if (monster != null && (state == null || state.IsAlive))
            {
                float d = Vector3.Distance(monster.transform.position, transform.position);
                fear = 1f - Mathf.Clamp01(d / fearRadius);
                if (monster.IsChasing) fear = Mathf.Max(fear, 0.45f);
            }
            Fear01 = Mathf.MoveTowards(Fear01, fear, Time.deltaTime * 0.8f);

            if (Fear01 > 0.05f)
            {
                beatTimer -= Time.deltaTime;
                if (beatTimer <= 0f)
                {
                    beatTimer = Mathf.Lerp(1.3f, 0.4f, Fear01);
                    heart.PlayOneShot(ProceduralAudio.Heartbeat, Mathf.Lerp(0.2f, 1f, Fear01));
                }
            }

            if (shakeTarget != null)
            {
                float a = maxShake * Fear01 * Fear01;
                shakeTarget.localPosition = new Vector3(
                    (Mathf.PerlinNoise(Time.time * 25f, 0f) - 0.5f) * a,
                    (Mathf.PerlinNoise(0f, Time.time * 25f) - 0.5f) * a,
                    0f);
            }
        }
    }
}

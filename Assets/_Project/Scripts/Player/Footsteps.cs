using UnityEngine;
using YourFinalOrder.Audio;

namespace YourFinalOrder.Player
{
    /// <summary>
    /// Шаги, слышимые всеми (скорость считается по перемещению, поэтому работает и для чужих игроков).
    /// </summary>
    public class Footsteps : MonoBehaviour
    {
        public float stepDistance = 2.1f;
        public float minSpeed = 0.5f;

        AudioSource source;
        Vector3 lastPos;
        float travelled;
        PlayerState state;

        void Awake()
        {
            state = GetComponent<PlayerState>();
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 1f;
            source.maxDistance = 22f;
            lastPos = transform.position;
        }

        void Update()
        {
            var delta = transform.position - lastPos;
            lastPos = transform.position;
            delta.y = 0f;
            float dist = delta.magnitude;
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            float speed = dist / dt;

            if ((state != null && !state.IsAlive) || speed < minSpeed || speed > 15f)
            {
                travelled = 0f;
                return;
            }

            travelled += dist;
            if (travelled >= stepDistance)
            {
                travelled = 0f;
                source.pitch = Random.Range(0.85f, 1.1f);
                float volume = Mathf.InverseLerp(1f, 6f, speed);
                source.PlayOneShot(ProceduralAudio.Footstep, Mathf.Lerp(0.15f, 1f, volume));
            }
        }
    }
}

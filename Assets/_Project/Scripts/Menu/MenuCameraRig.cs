using UnityEngine;

namespace YourFinalOrder.Menu
{
    /// <summary>
    /// Камера меню: медленно облетает шаттл от третьего лица, держит его справа от меню,
    /// слегка дрожит и увеличивает FOV во время прыжка.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class MenuCameraRig : MonoBehaviour
    {
        public Transform target;
        public float distance = 30f;
        public float height = 6f;
        [Tooltip("Смещение цели влево от камеры, чтобы корабль стоял справа от меню")]
        public float screenOffset = 7f;
        public float orbitSpeed = 3f;
        [Tooltip("Диапазон угла облёта относительно носа корабля")]
        public Vector2 yawRange = new(110f, 250f);
        public float baseFov = 45f;

        public float Shake { get; set; }
        public float FovKick { get; set; }

        Camera cam;
        float phase;

        void Awake()
        {
            cam = GetComponent<Camera>();
            phase = Random.value * 10f;
        }

        void LateUpdate()
        {
            if (target == null) return;
            phase += Time.deltaTime * orbitSpeed * Mathf.Deg2Rad;

            // Плавно качаемся между ракурсами «три четверти сзади» и «сбоку»
            float k = (Mathf.Sin(phase) + 1f) * 0.5f;
            float yaw = Mathf.Lerp(yawRange.x, yawRange.y, k);
            float h = height + Mathf.Sin(phase * 1.7f) * 3f;

            var rot = Quaternion.Euler(0f, yaw, 0f);
            var pos = target.position + rot * (Vector3.forward * distance) + Vector3.up * h;

            var noise = new Vector3(
                Mathf.PerlinNoise(Time.time * 0.3f, 1f) - 0.5f,
                Mathf.PerlinNoise(2f, Time.time * 0.3f) - 0.5f, 0f) * 1.5f;
            var shake = Random.insideUnitSphere * Shake * 0.25f;
            transform.position = pos + noise + shake;

            var toTarget = target.position - transform.position;
            var right = Vector3.Cross(Vector3.up, toTarget).normalized;
            var aim = target.position - right * screenOffset;
            transform.rotation = Quaternion.LookRotation(aim - transform.position, Vector3.up) *
                                 Quaternion.Euler(0f, 0f, Mathf.Sin(phase * 0.8f) * 2f);

            cam.fieldOfView = baseFov + FovKick;
        }
    }
}

using UnityEngine;

namespace YourFinalOrder.Player
{
    /// <summary>
    /// Камера меню: медленно вращается, пока локальный игрок не заспавнен.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class MenuCamera : MonoBehaviour
    {
        public static MenuCamera Instance { get; private set; }
        public float rotateSpeed = 4f;

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        public static void SetActive(bool active)
        {
            if (Instance != null) Instance.gameObject.SetActive(active);
        }

        void Update() => transform.Rotate(0f, rotateSpeed * Time.deltaTime, 0f, Space.World);
    }
}

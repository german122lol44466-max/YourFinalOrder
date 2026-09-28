using Unity.Netcode;
using UnityEngine;
using YourFinalOrder.Player;

namespace YourFinalOrder.Game
{
    /// <summary>
    /// Предохранитель — предмет, который нужно собрать, чтобы открыть выход.
    /// </summary>
    public class Fuse : NetworkBehaviour, IInteractable
    {
        public Transform visual;
        public float bobHeight = 0.08f;
        public float spinSpeed = 60f;

        public string Prompt => "[E] Взять предохранитель";
        public bool CanInteract => IsSpawned && GameManager.Instance != null && GameManager.Instance.IsPlaying;

        public void ServerInteract(PlayerInteractor by)
        {
            if (!IsServer || !CanInteract) return;
            GameManager.Instance.ServerAddFuse();
            NetworkObject.Despawn(true);
        }

        void Update()
        {
            if (visual == null) return;
            visual.localPosition = Vector3.up * (Mathf.Sin(Time.time * 2f) * bobHeight);
            visual.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);
        }
    }
}

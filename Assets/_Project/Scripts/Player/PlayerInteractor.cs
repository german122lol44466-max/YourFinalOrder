using Unity.Netcode;
using UnityEngine;
using YourFinalOrder.Game;

namespace YourFinalOrder.Player
{
    /// <summary>
    /// Луч из камеры владельца: подсказка и взаимодействие (E) через серверный RPC.
    /// </summary>
    public class PlayerInteractor : NetworkBehaviour
    {
        public Camera cam;
        public float range = 2.5f;

        public string CurrentPrompt { get; private set; }

        PlayerState state;

        public override void OnNetworkSpawn()
        {
            state = GetComponent<PlayerState>();
        }

        void Update()
        {
            if (!IsSpawned || !IsOwner) return;
            CurrentPrompt = null;
            if (cam == null || (state != null && !state.IsAlive)) return;

            var ray = new Ray(cam.transform.position, cam.transform.forward);
            if (!Physics.Raycast(ray, out var hit, range, ~0, QueryTriggerInteraction.Collide)) return;

            var interactable = hit.collider.GetComponentInParent<IInteractable>();
            if (interactable == null || !interactable.CanInteract) return;

            CurrentPrompt = interactable.Prompt;

            if (Cursor.lockState == CursorLockMode.Locked && Input.GetKeyDown(KeyCode.E))
            {
                var no = ((Component)interactable).GetComponent<NetworkObject>();
                if (no != null) InteractRpc(no);
            }
        }

        [Rpc(SendTo.Server)]
        void InteractRpc(NetworkObjectReference target)
        {
            if (!target.TryGet(out var no)) return;
            if (state != null && !state.IsAlive) return;
            // Проверка дистанции на сервере (с запасом на задержку)
            if (Vector3.Distance(no.transform.position, transform.position) > range + 2.5f) return;

            var interactable = no.GetComponent<IInteractable>();
            if (interactable != null && interactable.CanInteract) interactable.ServerInteract(this);
        }
    }
}

using Unity.Netcode;
using UnityEngine;
using YourFinalOrder.Core;

namespace YourFinalOrder.Player
{
    /// <summary>
    /// Связка сетевого игрока: включает камеру/управление у владельца,
    /// синхронизирует наклон головы (куда светит фонарь), режим наблюдателя после смерти.
    /// </summary>
    public class NetworkPlayer : NetworkBehaviour
    {
        public static NetworkPlayer Local { get; private set; }

        public Camera playerCamera;
        public AudioListener audioListener;
        public Transform head;
        public Renderer[] bodyRenderers;

        readonly NetworkVariable<float> pitch = new(0f,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        FirstPersonController fpc;
        PlayerState state;
        PlayerState spectateTarget;

        public PlayerState SpectateTarget => spectateTarget;

        public override void OnNetworkSpawn()
        {
            fpc = GetComponent<FirstPersonController>();
            state = GetComponent<PlayerState>();

            bool owner = IsOwner;
            if (playerCamera != null) playerCamera.enabled = owner;
            if (audioListener != null) audioListener.enabled = owner;
            if (fpc != null) fpc.enabled = owner;
            foreach (var c in GetComponents<LocalOnlyBehaviour>()) c.enabled = owner;

            if (owner)
            {
                Local = this;
                SetCursorLocked(true);
                if (playerCamera != null) playerCamera.fieldOfView = GameSettings.FieldOfView;
                GameSettings.Changed += ApplySettings;
                // Своё тело не мешает обзору, но отбрасывает тень
                foreach (var r in bodyRenderers)
                    if (r != null) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (Local == this)
            {
                Local = null;
                SetCursorLocked(false);
                GameSettings.Changed -= ApplySettings;
            }
        }

        void ApplySettings()
        {
            if (playerCamera != null) playerCamera.fieldOfView = GameSettings.FieldOfView;
        }

        public static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        void Update()
        {
            if (!IsSpawned) return;

            if (IsOwner)
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                    SetCursorLocked(Cursor.lockState != CursorLockMode.Locked);

                if (fpc != null && Mathf.Abs(pitch.Value - fpc.Pitch) > 0.5f)
                    pitch.Value = fpc.Pitch;
            }
            else if (head != null)
            {
                var target = Quaternion.Euler(pitch.Value, 0f, 0f);
                head.localRotation = Quaternion.Slerp(head.localRotation, target, 15f * Time.deltaTime);
            }
        }

        void LateUpdate()
        {
            if (!IsSpawned || !IsOwner || playerCamera == null) return;

            if (state != null && !state.IsAlive)
            {
                if (spectateTarget == null || !spectateTarget.IsAlive || Input.GetMouseButtonDown(0))
                    spectateTarget = NextSpectateTarget();

                if (spectateTarget != null)
                {
                    var targetHead = spectateTarget.GetComponent<NetworkPlayer>().head;
                    var t = targetHead != null ? targetHead : spectateTarget.transform;
                    // Камера за плечом наблюдаемого
                    var desired = t.position - t.forward * 1.6f + Vector3.up * 0.4f;
                    playerCamera.transform.SetPositionAndRotation(desired, Quaternion.LookRotation(t.position + t.forward * 3f - desired));
                }
            }
            else if (spectateTarget != null)
            {
                spectateTarget = null;
                playerCamera.transform.localPosition = Vector3.zero;
                playerCamera.transform.localRotation = Quaternion.identity;
            }
        }

        PlayerState NextSpectateTarget()
        {
            var all = PlayerState.All;
            if (all.Count == 0) return null;
            int start = spectateTarget != null ? all.IndexOf(spectateTarget) : -1;
            for (int i = 1; i <= all.Count; i++)
            {
                var p = all[(start + i + all.Count) % all.Count];
                if (p != state && p.IsAlive) return p;
            }
            return null;
        }
    }

    /// <summary>
    /// Базовый класс для компонентов, которые должны работать только у владельца (HUD, эффекты страха).
    /// NetworkPlayer выключает их у чужих игроков.
    /// </summary>
    public abstract class LocalOnlyBehaviour : MonoBehaviour { }
}

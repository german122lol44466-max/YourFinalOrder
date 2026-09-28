using Unity.Netcode;
using UnityEngine;

namespace YourFinalOrder.Player
{
    /// <summary>
    /// Фонарик: включение синхронизируется по сети (видят все игроки и монстр).
    /// Батарея садится при работе и медленно заряжается, когда фонарь выключен.
    /// </summary>
    public class Flashlight : NetworkBehaviour
    {
        public Light spot;
        public float maxBattery = 120f;
        public float rechargePerSecond = 0.4f;
        [Range(0f, 1f)] public float lowBatteryThreshold = 0.2f;

        readonly NetworkVariable<bool> isOn = new(false,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public bool IsOn => isOn.Value;
        public float Battery01 => battery / maxBattery;

        float battery;
        float baseIntensity;
        PlayerState state;

        public override void OnNetworkSpawn()
        {
            battery = maxBattery;
            state = GetComponent<PlayerState>();
            if (spot != null) baseIntensity = spot.intensity;
            isOn.OnValueChanged += OnChanged;
            ApplyVisual();
        }

        public override void OnNetworkDespawn()
        {
            isOn.OnValueChanged -= OnChanged;
        }

        void OnChanged(bool previous, bool current) => ApplyVisual();

        void ApplyVisual()
        {
            if (spot != null) spot.enabled = isOn.Value;
        }

        public void SetOn(bool on)
        {
            if (!IsOwner || isOn.Value == on) return;
            if (on && battery <= 0f) return;
            isOn.Value = on;
        }

        void Update()
        {
            if (!IsSpawned) return;

            if (IsOwner)
            {
                bool alive = state == null || state.IsAlive;
                if (alive && Cursor.lockState == CursorLockMode.Locked && Input.GetKeyDown(KeyCode.F))
                    SetOn(!isOn.Value);

                if (isOn.Value)
                {
                    battery = Mathf.Max(0f, battery - Time.deltaTime);
                    if (battery <= 0f) SetOn(false);
                }
                else
                {
                    battery = Mathf.Min(maxBattery, battery + rechargePerSecond * Time.deltaTime);
                }

                // Мерцание при низком заряде (только у себя — чужой заряд не синхронизируется)
                if (spot != null && isOn.Value)
                {
                    float k = 1f;
                    if (Battery01 < lowBatteryThreshold)
                    {
                        float n = Mathf.PerlinNoise(Time.time * 12f, 0.37f);
                        k = n < 0.25f ? 0.15f : Mathf.Lerp(0.5f, 1f, n);
                    }
                    spot.intensity = baseIntensity * k;
                }
            }
        }
    }
}

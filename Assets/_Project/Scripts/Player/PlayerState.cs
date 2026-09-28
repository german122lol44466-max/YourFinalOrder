using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using YourFinalOrder.Networking;

namespace YourFinalOrder.Player
{
    /// <summary>
    /// Жив/мёртв (решает сервер), реестр всех игроков, респавн.
    /// </summary>
    public class PlayerState : NetworkBehaviour
    {
        public static readonly List<PlayerState> All = new();
        public static event Action<PlayerState> Died;

        public Renderer[] bodyRenderers;

        public readonly NetworkVariable<bool> IsDead = new(false);

        public bool IsAlive => IsSpawned && !IsDead.Value;
        public Vector3 HeadPosition => transform.position + Vector3.up * 1.5f;

        CharacterController cc;
        FirstPersonController fpc;
        Flashlight flashlight;

        public override void OnNetworkSpawn()
        {
            cc = GetComponent<CharacterController>();
            fpc = GetComponent<FirstPersonController>();
            flashlight = GetComponent<Flashlight>();

            All.Add(this);
            IsDead.OnValueChanged += OnDeadChanged;
            Apply(IsDead.Value);
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            IsDead.OnValueChanged -= OnDeadChanged;
        }

        public void ServerKill()
        {
            if (!IsServer || IsDead.Value) return;
            IsDead.Value = true;
        }

        public void ServerRevive(Vector3 position, float yaw)
        {
            if (!IsServer) return;
            IsDead.Value = false;
            TeleportRpc(position, yaw);
        }

        [Rpc(SendTo.Owner)]
        void TeleportRpc(Vector3 position, float yaw)
        {
            bool ccWasEnabled = cc.enabled;
            cc.enabled = false;
            var rotation = Quaternion.Euler(0f, yaw, 0f);
            var nt = GetComponent<OwnerNetworkTransform>();
            if (nt != null) nt.Teleport(position, rotation, transform.localScale);
            else transform.SetPositionAndRotation(position, rotation);
            if (fpc != null) fpc.ResetState(yaw);
            cc.enabled = ccWasEnabled;
        }

        void OnDeadChanged(bool previous, bool current)
        {
            Apply(current);
            if (current && !previous) Died?.Invoke(this);
        }

        void Apply(bool dead)
        {
            if (bodyRenderers != null)
                foreach (var r in bodyRenderers)
                    if (r != null) r.enabled = !dead;

            // Мёртвые не сталкиваются с живыми и монстром
            if (cc != null) cc.enabled = !dead;

            if (IsOwner)
            {
                if (fpc != null) fpc.InputEnabled = !dead;
                if (dead && flashlight != null) flashlight.SetOn(false);
            }
        }
    }
}

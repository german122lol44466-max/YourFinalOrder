using Unity.Netcode.Components;
using UnityEngine;

namespace YourFinalOrder.Networking
{
    /// <summary>
    /// NetworkTransform, которым управляет владелец объекта (клиент), а не сервер.
    /// Используется для игроков: движение считается локально, без задержки ввода.
    /// </summary>
    [DisallowMultipleComponent]
    public class OwnerNetworkTransform : NetworkTransform
    {
        protected override bool OnIsServerAuthoritative() => false;
    }
}

using UnityEngine;

namespace YourFinalOrder.Game
{
    /// <summary>
    /// Ворота выхода: закрыты, пока не собраны все предохранители.
    /// Состояние берётся из сетевого GameManager, поэтому одинаково у всех.
    /// </summary>
    public class ExitGate : MonoBehaviour
    {
        public GameObject gate;
        public Light signalLight;
        public Color closedColor = new(1f, 0.1f, 0.05f);
        public Color openColor = new(0.1f, 1f, 0.2f);

        void Update()
        {
            bool open = GameManager.Instance != null && GameManager.Instance.ExitOpen;
            if (gate != null && gate.activeSelf == open) gate.SetActive(!open);
            if (signalLight != null) signalLight.color = open ? openColor : closedColor;
        }
    }
}

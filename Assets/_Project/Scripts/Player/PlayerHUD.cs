using UnityEngine;
using YourFinalOrder.Core;
using YourFinalOrder.Steam;

namespace YourFinalOrder.Player
{
    /// <summary>
    /// HUD локального игрока (IMGUI, временный): прицел, подсказки, выносливость, заряд фонаря, меню паузы.
    /// </summary>
    public class PlayerHUD : LocalOnlyBehaviour
    {
        FirstPersonController fpc;
        Flashlight flashlight;
        PlayerInteractor interactor;
        PlayerState state;
        GUIStyle prompt, big, button, small;
        Texture2D white;

        void Awake()
        {
            fpc = GetComponent<FirstPersonController>();
            flashlight = GetComponent<Flashlight>();
            interactor = GetComponent<PlayerInteractor>();
            state = GetComponent<PlayerState>();
            white = Texture2D.whiteTexture;
        }

        void Styles()
        {
            if (prompt != null) return;
            prompt = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 18 };
            big = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 34, fontStyle = FontStyle.Bold };
            small = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            button = new GUIStyle(GUI.skin.button) { fontSize = 20, fixedHeight = 44 };
        }

        void OnGUI()
        {
            Styles();
            float w = Screen.width, h = Screen.height;
            bool paused = Cursor.lockState != CursorLockMode.Locked;

            if (paused)
            {
                DrawPause(w, h);
                return;
            }

            // прицел
            var cross = CrosshairTextures.Get(GameSettings.Crosshair);
            if (GameSettings.Crosshair != CrosshairStyle.None && (state == null || state.IsAlive))
            {
                float s = CrosshairTextures.Size;
                GUI.DrawTexture(new Rect((w - s) / 2, (h - s) / 2, s, s), cross);
            }

            if (interactor != null && !string.IsNullOrEmpty(interactor.CurrentPrompt))
                GUI.Label(new Rect(0, h / 2 + 30, w, 30), interactor.CurrentPrompt, prompt);

            if (state != null && !state.IsAlive)
                GUI.Label(new Rect(0, h * 0.25f, w, 60), "ВАС СХВАТИЛИ\n<size=16>ЛКМ — следить за другим курьером</size>", big);

            // полоски внизу слева
            if (fpc != null) Bar(new Rect(30, h - 60, 220, 8), fpc.Stamina01, new Color(0.9f, 0.85f, 0.7f), "ВЫНОСЛИВОСТЬ");
            if (flashlight != null) Bar(new Rect(30, h - 34, 220, 8), flashlight.Battery01, new Color(1f, 0.75f, 0.3f), "ФОНАРЬ [F]");
        }

        void Bar(Rect r, float value, Color color, string label)
        {
            var old = GUI.color;
            GUI.color = new Color(0, 0, 0, 0.5f);
            GUI.DrawTexture(r, white);
            GUI.color = color;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(value), r.height), white);
            GUI.color = old;
            GUI.Label(new Rect(r.xMax + 10, r.y - 7, 200, 22), label, small);
        }

        void DrawPause(float w, float h)
        {
            var old = GUI.color;
            GUI.color = new Color(0, 0, 0, 0.6f);
            GUI.DrawTexture(new Rect(0, 0, w, h), white);
            GUI.color = old;

            float bw = 320;
            GUILayout.BeginArea(new Rect((w - bw) / 2, h * 0.3f, bw, 360));
            GUILayout.Label("ПАУЗА", big);
            GUILayout.Space(16);
            if (GUILayout.Button("Продолжить", button)) NetworkPlayer.SetCursorLocked(true);
            var session = SessionManager.Instance;
            if (session != null && session.CurrentMode == SessionManager.Mode.Steam && SteamLobby.OverlayEnabled)
            {
                if (GUILayout.Button("Пригласить друзей", button)) session.OpenInvites();
            }
            if (GUILayout.Button("Выйти в меню", button)) session?.Leave();
            GUILayout.Space(10);
            GUILayout.Label("Esc — вернуться в игру", small);
            GUILayout.EndArea();
        }
    }
}

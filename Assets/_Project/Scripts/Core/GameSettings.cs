using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace YourFinalOrder.Core
{
    public enum CrosshairStyle { Dot, Cross, Circle, Brackets, None }
    public enum RobotModel { Can, Toaster, Lantern }

    /// <summary>
    /// Настройки игрока (PlayerPrefs). Применяются сразу при изменении.
    /// </summary>
    public static class GameSettings
    {
        public static event Action Changed;

        // ----- графика
        public static int ResolutionWidth { get => Get("res_w", Screen.currentResolution.width); set => Set("res_w", value); }
        public static int ResolutionHeight { get => Get("res_h", Screen.currentResolution.height); set => Set("res_h", value); }
        public static FullScreenMode WindowMode { get => (FullScreenMode)Get("window", (int)FullScreenMode.FullScreenWindow); set => Set("window", (int)value); }
        public static bool VSync { get => Get("vsync", 1) == 1; set => Set("vsync", value ? 1 : 0); }
        public static int FpsLimit { get => Get("fps", 0); set => Set("fps", value); }
        public static int Quality { get => Get("quality", QualitySettings.GetQualityLevel()); set => Set("quality", value); }
        public static float RenderScale { get => GetF("render_scale", 1f); set => SetF("render_scale", value); }

        // ----- управление
        public static float Sensitivity { get => GetF("sens", 2f); set => SetF("sens", value); }
        public static bool InvertY { get => Get("invert_y", 0) == 1; set => Set("invert_y", value ? 1 : 0); }
        public static float FieldOfView { get => GetF("fov", 75f); set => SetF("fov", value); }

        // ----- звук
        public static float MasterVolume { get => GetF("vol_master", 0.8f); set => SetF("vol_master", value); }
        public static float MusicVolume { get => GetF("vol_music", 0.6f); set => SetF("vol_music", value); }
        public static float VoiceVolume { get => GetF("vol_voice", 1f); set => SetF("vol_voice", value); }
        public static string Microphone { get => PlayerPrefs.GetString("mic", ""); set { PlayerPrefs.SetString("mic", value); Save(); } }
        public static bool PushToTalk { get => Get("ptt", 0) == 1; set => Set("ptt", value ? 1 : 0); }

        // ----- игрок
        public static CrosshairStyle Crosshair { get => (CrosshairStyle)Get("crosshair", 0); set => Set("crosshair", (int)value); }
        public static RobotModel Robot { get => (RobotModel)Get("robot", 0); set => Set("robot", (int)value); }

        public static readonly int[] FpsOptions = { 30, 60, 120, 144, 165, 240, 0 };

        public static readonly Dictionary<CrosshairStyle, string> CrosshairNames = new()
        {
            { CrosshairStyle.Dot, "Точка" },
            { CrosshairStyle.Cross, "Крест" },
            { CrosshairStyle.Circle, "Круг" },
            { CrosshairStyle.Brackets, "Скобки" },
            { CrosshairStyle.None, "Нет" },
        };

        public static readonly Dictionary<RobotModel, string> RobotTitles = new()
        {
            { RobotModel.Can, "«Консерва»" },
            { RobotModel.Toaster, "«Тостер»" },
            { RobotModel.Lantern, "«Фонарь»" },
        };

        public static readonly Dictionary<RobotModel, string> RobotNames = new()
        {
            { RobotModel.Can, "Купольный курьер с окулярами. Ржавый, но надёжный." },
            { RobotModel.Toaster, "Грузчик с ЭЛТ-монитором вместо головы. Моргает пикселями." },
            { RobotModel.Lantern, "Высокий разведчик. Голова — фонарь с единственным глазом." },
        };

        public static readonly Dictionary<FullScreenMode, string> WindowModeNames = new()
        {
            { FullScreenMode.ExclusiveFullScreen, "Полный экран" },
            { FullScreenMode.FullScreenWindow, "Окно без рамки" },
            { FullScreenMode.Windowed, "В окне" },
        };

        /// <summary>Уникальные разрешения экрана, от большего к меньшему.</summary>
        public static List<Vector2Int> AvailableResolutions()
        {
            var list = Screen.resolutions
                .Select(r => new Vector2Int(r.width, r.height))
                .Distinct()
                .OrderByDescending(r => r.x * r.y)
                .ToList();
            var current = new Vector2Int(ResolutionWidth, ResolutionHeight);
            if (!list.Contains(current)) list.Insert(0, current);
            return list;
        }

        public static string AspectName(Vector2Int r)
        {
            float a = r.x / (float)r.y;
            (float v, string n)[] known = { (32f / 9, "32:9"), (21f / 9, "21:9"), (16f / 9, "16:9"), (16f / 10, "16:10"), (4f / 3, "4:3"), (5f / 4, "5:4") };
            foreach (var (v, n) in known)
                if (Mathf.Abs(a - v) < 0.03f) return n;
            // 3440x1440 и подобные «21:9» на деле 43:18
            if (a > 2.2f && a < 2.5f) return "21:9";
            return a.ToString("0.00") + ":1";
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void ApplyAll()
        {
            ApplyDisplay();
            ApplyQuality();
            ApplyAudio();
        }

        public static void ApplyDisplay()
        {
            Screen.SetResolution(ResolutionWidth, ResolutionHeight, WindowMode);
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            Application.targetFrameRate = VSync || FpsLimit <= 0 ? -1 : FpsLimit;
        }

        public static void ApplyQuality()
        {
            int q = Mathf.Clamp(Quality, 0, QualitySettings.names.Length - 1);
            if (QualitySettings.GetQualityLevel() != q) QualitySettings.SetQualityLevel(q, true);
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
                urp.renderScale = Mathf.Clamp(RenderScale, 0.5f, 1.5f);
            // vSync может быть переопределён уровнем качества
            QualitySettings.vSyncCount = VSync ? 1 : 0;
        }

        public static void ApplyAudio()
        {
            AudioListener.volume = MasterVolume;
        }

        // ------------------------------------------------------------ PlayerPrefs

        static int Get(string key, int def) => PlayerPrefs.GetInt(key, def);
        static float GetF(string key, float def) => PlayerPrefs.GetFloat(key, def);

        static void Set(string key, int value)
        {
            PlayerPrefs.SetInt(key, value);
            Save();
        }

        static void SetF(string key, float value)
        {
            PlayerPrefs.SetFloat(key, value);
            Save();
        }

        static void Save()
        {
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }
}

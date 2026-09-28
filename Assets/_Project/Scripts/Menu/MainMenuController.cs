using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;
using YourFinalOrder.Audio;
using YourFinalOrder.Core;
using YourFinalOrder.Steam;

namespace YourFinalOrder.Menu
{
    /// <summary>
    /// Главное меню (UI Toolkit): экраны, лобби, настройки, экран загрузки.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class MainMenuController : MonoBehaviour
    {
        VisualElement root;
        readonly Dictionary<string, VisualElement> screens = new();
        VisualElement settingsPanel;
        Label status;
        Label titleR, titleC;
        VisualElement playerList;
        Label lobbyCode;
        VisualElement lobbyCodeRow;
        Button startButton, inviteButton;
        Label lobbyWait;
        VisualElement loading;
        Label loadingLog;

        AudioSource ui;
        SessionManager session;
        float glitchTimer = 2f;
        float lobbyRefresh;
        string currentScreen = "main";

        void OnEnable()
        {
            root = GetComponent<UIDocument>().rootVisualElement;
            if (!TryGetComponent(out ui)) ui = gameObject.AddComponent<AudioSource>();
            ui.playOnAwake = false;
            ui.spatialBlend = 0f;

            foreach (var name in new[] { "main", "play", "lobby" })
                screens[name] = root.Q($"screen-{name}");
            settingsPanel = root.Q("settings");
            status = root.Q<Label>("status");
            titleR = root.Q<Label>("title-r");
            titleC = root.Q<Label>("title-c");
            playerList = root.Q("player-list");
            lobbyCode = root.Q<Label>("lobby-code");
            lobbyCodeRow = root.Q("lobby-code-row");
            startButton = root.Q<Button>("btn-start");
            inviteButton = root.Q<Button>("btn-invite");
            lobbyWait = root.Q<Label>("lobby-wait");
            loading = root.Q("loading");
            loadingLog = root.Q<Label>("loading-log");

            root.Q("shade").style.backgroundImage = new StyleBackground(MakeShadeTexture());
            root.Q("scanlines").style.backgroundImage = new StyleBackground(MakeScanlineTexture());
            root.Q<Label>("version").text = $"v{Application.version} · прототип";

            // Главный экран
            Bind("btn-play", () => Show("play"));
            Bind("btn-settings", OpenSettings);
            Bind("btn-quit", Quit);

            // Режимы
            Bind("btn-host-steam", () => Session?.HostSteam());
            Bind("btn-join-code", () => Session?.JoinSteamLobby(root.Q<TextField>("field-lobby-code").value));
            Bind("btn-host-lan", () => { if (TryPort(out var p)) Session?.HostLan(p); });
            Bind("btn-join-lan", () => { if (TryPort(out var p)) Session?.JoinLan(root.Q<TextField>("field-ip").value, p); });
            Bind("btn-play-back", () => Show("main"));
            root.Q<TextField>("field-lobby-code").textEdition.placeholder = "Код лобби";

            // Лобби
            Bind("btn-copy-code", () =>
            {
                if (Session != null && Session.Lobby.InLobby)
                {
                    GUIUtility.systemCopyBuffer = Session.Lobby.LobbyId.m_SteamID.ToString();
                    ShowStatus("Код лобби скопирован. Друг вводит его в «Войти по коду».");
                }
            });
            Bind("btn-invite", () => Session?.OpenInvites());
            Bind("btn-start", () =>
            {
                ShowLoading();
                Session?.StartGame();
            });
            Bind("btn-leave", () => Session?.Leave());

            Bind("btn-settings-back", CloseSettings);

            bool steam = SteamBootstrap.Initialized;
            root.Q<Button>("btn-host-steam").SetEnabled(steam);
            root.Q<Button>("btn-join-code").SetEnabled(steam);
            root.Q<Label>("steam-status").text = steam
                ? $"Steam: {SteamBootstrap.LocalName}"
                : $"Steam недоступен ({SteamBootstrap.Error ?? "не запущен"}) — игра по IP";

            RealityJumper.RealityChanged += OnRealityChanged;
            OnRealityChanged(RealityJumper.CurrentName);

            Show("main");
        }

        void OnDisable()
        {
            RealityJumper.RealityChanged -= OnRealityChanged;
            Unsubscribe();
        }

        SessionManager Session
        {
            get
            {
                if (session == null && SessionManager.Instance != null)
                {
                    session = SessionManager.Instance;
                    session.StatusChanged += ShowStatus;
                    session.SessionStarted += OnSessionStarted;
                    session.SessionEnded += OnSessionEnded;
                }
                return session;
            }
        }

        void Unsubscribe()
        {
            if (session == null) return;
            session.StatusChanged -= ShowStatus;
            session.SessionStarted -= OnSessionStarted;
            session.SessionEnded -= OnSessionEnded;
            if (session.Network != null && session.Network.SceneManager != null)
                session.Network.SceneManager.OnSceneEvent -= OnSceneEvent;
            session = null;
        }

        void Start()
        {
            // подписка на события сессии и сообщение, оставшееся с прошлой сцены
            if (Session != null) ShowStatus(Session.LastStatus);
        }

        // ------------------------------------------------------------ экраны

        void Show(string name)
        {
            currentScreen = name;
            foreach (var kv in screens)
                kv.Value.EnableInClassList("hidden", kv.Key != name);
            if (name == "lobby") RefreshLobby();
        }

        void Bind(string name, Action action)
        {
            var b = root.Q<Button>(name);
            if (b == null) { Debug.LogWarning($"Нет кнопки {name}"); return; }
            b.clicked += () =>
            {
                ui.PlayOneShot(ProceduralAudio.UiClick);
                action();
            };
            b.RegisterCallback<MouseEnterEvent>(_ => { if (b.enabledSelf) ui.PlayOneShot(ProceduralAudio.UiHover, 0.6f); });
        }

        void ShowStatus(string message)
        {
            status.text = message;
            status.EnableInClassList("hidden", string.IsNullOrEmpty(message));
            if (!string.IsNullOrEmpty(message)) HideLoading();
        }

        bool TryPort(out ushort port)
        {
            if (ushort.TryParse(root.Q<TextField>("field-port").value, out port)) return true;
            ShowStatus("Неверный порт");
            return false;
        }

        void OnRealityChanged(string name)
        {
            var label = root?.Q<Label>("reality");
            if (label != null) label.text = string.IsNullOrEmpty(name) ? "" : $"◉ {name}";
        }

        void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ------------------------------------------------------------ сессия

        void OnSessionStarted()
        {
            ShowStatus("");
            Show("lobby");
            if (session.Network.SceneManager != null)
            {
                session.Network.SceneManager.OnSceneEvent -= OnSceneEvent;
                session.Network.SceneManager.OnSceneEvent += OnSceneEvent;
            }
        }

        void OnSessionEnded()
        {
            HideLoading();
            Show("main");
        }

        void OnSceneEvent(SceneEvent e)
        {
            if (e.SceneEventType == SceneEventType.Load && e.ClientId == session.Network.LocalClientId)
                ShowLoading();
        }

        void RefreshLobby()
        {
            var s = Session;
            if (s == null) return;
            bool steamMode = s.CurrentMode == SessionManager.Mode.Steam;
            bool host = s.Network.IsHost;

            lobbyCodeRow.style.display = steamMode ? DisplayStyle.Flex : DisplayStyle.None;
            lobbyCode.text = steamMode && s.Lobby.InLobby ? $"Код: {s.Lobby.LobbyId.m_SteamID}" : "";
            inviteButton.style.display = steamMode ? DisplayStyle.Flex : DisplayStyle.None;
            inviteButton.SetEnabled(SteamLobby.OverlayEnabled);
            inviteButton.tooltip = SteamLobby.OverlayEnabled ? "" : "Оверлей Steam выключен: отправьте другу код лобби";
            startButton.style.display = host ? DisplayStyle.Flex : DisplayStyle.None;
            lobbyWait.style.display = host ? DisplayStyle.None : DisplayStyle.Flex;

            var names = new List<(string name, bool isHost)>();
            if (steamMode && s.Lobby.InLobby)
            {
                var owner = s.Lobby.Owner;
                names.AddRange(s.Lobby.GetMembers().Select(m => (m.name, m.id == owner)));
            }
            else if (s.Network.IsListening)
            {
                int count = host ? s.Network.ConnectedClientsIds.Count : 1;
                for (int i = 0; i < count; i++)
                    names.Add((i == 0 && host ? $"{SteamBootstrap.LocalName} (вы)" : $"Курьер #{i + 1}", i == 0 && host));
            }

            playerList.Clear();
            for (int i = 0; i < SessionManager.MaxPlayers; i++)
            {
                var slot = new VisualElement();
                slot.AddToClassList("player-slot");
                var index = new Label($"0{i + 1}");
                index.AddToClassList("player-slot__index");
                slot.Add(index);
                var name = new Label(i < names.Count ? names[i].name : "свободно");
                name.AddToClassList("player-slot__name");
                slot.Add(name);
                if (i < names.Count && names[i].isHost)
                {
                    var tag = new Label("ХОСТ");
                    tag.AddToClassList("player-slot__tag");
                    slot.Add(tag);
                }
                if (i >= names.Count) slot.AddToClassList("player-slot--empty");
                playerList.Add(slot);
            }
        }

        // ------------------------------------------------------------ загрузка

        void ShowLoading()
        {
            loading.RemoveFromClassList("hidden");
            loadingLog.text = string.Join("\n", new[]
            {
                "> синхронизация координат реальности...",
                "> прогрев реактора: 87%",
                "> загрузка манифеста посылок: OK",
                "> предупреждение: обнаружены посторонние формы жизни",
            });
        }

        void HideLoading() => loading?.AddToClassList("hidden");

        // ------------------------------------------------------------ обновление

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (!settingsPanel.ClassListContains("hidden")) CloseSettings();
                else if (currentScreen == "play") Show("main");
            }

            if (currentScreen == "lobby" && (lobbyRefresh -= Time.deltaTime) <= 0f)
            {
                lobbyRefresh = 0.5f;
                RefreshLobby();
            }

            // Глитч заголовка: короткие вспышки со смещёнными цветными копиями
            glitchTimer -= Time.deltaTime;
            if (glitchTimer <= 0f)
            {
                bool on = titleR.style.opacity.value < 0.5f && UnityEngine.Random.value < 0.8f;
                titleR.style.opacity = on ? 1f : 0f;
                titleC.style.opacity = on ? 1f : 0f;
                if (on)
                {
                    float dx = UnityEngine.Random.Range(3f, 7f);
                    titleR.style.translate = new Translate(-dx, UnityEngine.Random.Range(-2f, 2f));
                    titleC.style.translate = new Translate(dx, UnityEngine.Random.Range(-2f, 2f));
                    glitchTimer = UnityEngine.Random.Range(0.05f, 0.18f);
                }
                else
                {
                    glitchTimer = UnityEngine.Random.Range(1.5f, 5f);
                }
            }
        }

        // ------------------------------------------------------------ текстуры

        static Texture2D MakeShadeTexture()
        {
            // Горизонтальный градиент: тёмный слева → прозрачный справа
            var tex = new Texture2D(256, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int x = 0; x < 256; x++)
            {
                float t = x / 255f;
                float a = Mathf.Lerp(0.88f, 0f, Mathf.SmoothStep(0f, 1f, t));
                tex.SetPixel(x, 0, new Color(0.02f, 0.02f, 0.025f, a));
            }
            tex.Apply();
            return tex;
        }

        static Texture2D MakeScanlineTexture()
        {
            var tex = new Texture2D(1, 4, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point,
            };
            tex.SetPixels(new[] { Color.black, Color.clear, Color.clear, Color.clear });
            tex.Apply();
            return tex;
        }

        // ------------------------------------------------------------ настройки

        void OpenSettings()
        {
            settingsPanel.RemoveFromClassList("hidden");
            var tabs = root.Q("tabs");
            tabs.Clear();
            var names = new[] { "Графика", "Управление", "Звук", "Игрок" };
            Action[] builders = { BuildGraphics, BuildControls, BuildAudio, BuildPlayer };
            var buttons = new List<Button>();
            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                var b = new Button { text = names[i] };
                b.AddToClassList("tab");
                b.clicked += () =>
                {
                    ui.PlayOneShot(ProceduralAudio.UiClick);
                    foreach (var other in buttons) other.RemoveFromClassList("tab--active");
                    b.AddToClassList("tab--active");
                    root.Q<ScrollView>("settings-content").Clear();
                    builders[index]();
                };
                buttons.Add(b);
                tabs.Add(b);
            }
            buttons[0].AddToClassList("tab--active");
            root.Q<ScrollView>("settings-content").Clear();
            BuildGraphics();
        }

        void CloseSettings() => settingsPanel.AddToClassList("hidden");

        ScrollView Content => root.Q<ScrollView>("settings-content");

        void Group(string title)
        {
            var l = new Label(title.ToUpperInvariant());
            l.AddToClassList("setting-group");
            Content.Add(l);
        }

        void AddDropdown(string label, List<string> choices, int index, Action<int> onChange)
        {
            var d = new DropdownField(label, choices, Mathf.Clamp(index, 0, choices.Count - 1));
            d.AddToClassList("setting");
            d.RegisterValueChangedCallback(_ => onChange(d.index));
            Content.Add(d);
        }

        void AddToggle(string label, bool value, Action<bool> onChange)
        {
            var t = new Toggle(label) { value = value };
            t.AddToClassList("setting");
            t.RegisterValueChangedCallback(e => onChange(e.newValue));
            Content.Add(t);
        }

        void AddSlider(string label, float min, float max, float value, Func<float, string> format, Action<float> onChange)
        {
            var s = new Slider(label, min, max) { value = value, showInputField = false };
            s.AddToClassList("setting");
            var valueLabel = new Label(format(value));
            valueLabel.style.minWidth = 70;
            valueLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            s.Add(valueLabel);
            s.RegisterValueChangedCallback(e =>
            {
                valueLabel.text = format(e.newValue);
                onChange(e.newValue);
            });
            Content.Add(s);
        }

        void BuildGraphics()
        {
            Group("Экран");
            var resolutions = GameSettings.AvailableResolutions();
            var current = new Vector2Int(GameSettings.ResolutionWidth, GameSettings.ResolutionHeight);
            AddDropdown("Разрешение",
                resolutions.Select(r => $"{r.x} × {r.y}  ({GameSettings.AspectName(r)})").ToList(),
                resolutions.IndexOf(current),
                i =>
                {
                    GameSettings.ResolutionWidth = resolutions[i].x;
                    GameSettings.ResolutionHeight = resolutions[i].y;
                    GameSettings.ApplyDisplay();
                });

            var modes = GameSettings.WindowModeNames.Keys.ToList();
            AddDropdown("Режим окна", modes.Select(m => GameSettings.WindowModeNames[m]).ToList(),
                modes.IndexOf(GameSettings.WindowMode),
                i => { GameSettings.WindowMode = modes[i]; GameSettings.ApplyDisplay(); });

            AddToggle("Вертикальная синхронизация", GameSettings.VSync, v => { GameSettings.VSync = v; GameSettings.ApplyDisplay(); });

            var fps = GameSettings.FpsOptions;
            AddDropdown("Ограничение FPS (без V-Sync)", fps.Select(f => f > 0 ? f.ToString() : "Без ограничения").ToList(),
                Array.IndexOf(fps, GameSettings.FpsLimit),
                i => { GameSettings.FpsLimit = fps[i]; GameSettings.ApplyDisplay(); });

            Group("Качество");
            AddDropdown("Пресет качества", QualitySettings.names.ToList(), GameSettings.Quality,
                i => { GameSettings.Quality = i; GameSettings.ApplyQuality(); });
            AddSlider("Масштаб рендера", 0.5f, 1.5f, GameSettings.RenderScale, v => $"{Mathf.RoundToInt(v * 100)}%",
                v => { GameSettings.RenderScale = v; GameSettings.ApplyQuality(); });
        }

        void BuildControls()
        {
            Group("Мышь");
            AddSlider("Чувствительность", 0.2f, 6f, GameSettings.Sensitivity, v => v.ToString("0.0"), v => GameSettings.Sensitivity = v);
            AddToggle("Инвертировать ось Y", GameSettings.InvertY, v => GameSettings.InvertY = v);
            Group("Камера");
            AddSlider("Поле зрения (FOV)", 60f, 110f, GameSettings.FieldOfView, v => $"{Mathf.RoundToInt(v)}°", v => GameSettings.FieldOfView = v);

            Group("Клавиши");
            foreach (var (key, action) in new[]
                     {
                         ("W A S D", "движение"), ("Shift", "бег"), ("Ctrl / C", "присесть"), ("Пробел", "прыжок"),
                         ("E", "взаимодействие"), ("F", "фонарик"), ("Esc", "пауза"),
                     })
            {
                var l = new Label($"{key,-10}  —  {action}");
                l.AddToClassList("hint");
                Content.Add(l);
            }
        }

        void BuildAudio()
        {
            Group("Громкость");
            AddSlider("Общая", 0f, 1f, GameSettings.MasterVolume, v => $"{Mathf.RoundToInt(v * 100)}%",
                v => { GameSettings.MasterVolume = v; GameSettings.ApplyAudio(); });
            AddSlider("Музыка", 0f, 1f, GameSettings.MusicVolume, v => $"{Mathf.RoundToInt(v * 100)}%", v => GameSettings.MusicVolume = v);
            AddSlider("Голоса игроков", 0f, 1.5f, GameSettings.VoiceVolume, v => $"{Mathf.RoundToInt(v * 100)}%", v => GameSettings.VoiceVolume = v);

            Group("Микрофон");
            var mics = new List<string> { "По умолчанию" };
            mics.AddRange(Microphone.devices);
            AddDropdown("Устройство", mics, Math.Max(0, mics.IndexOf(GameSettings.Microphone)),
                i => GameSettings.Microphone = i == 0 ? "" : mics[i]);
            AddToggle("Режим рации (говорить по V)", GameSettings.PushToTalk, v => GameSettings.PushToTalk = v);
        }

        void BuildPlayer()
        {
            Group("Прицел");
            var styles = GameSettings.CrosshairNames.Keys.ToList();
            var preview = new VisualElement();
            preview.AddToClassList("crosshair-preview");
            preview.style.justifyContent = Justify.Center;
            preview.style.alignItems = Align.Center;
            var image = new VisualElement();
            image.style.width = CrosshairTextures.Size;
            image.style.height = CrosshairTextures.Size;
            image.style.backgroundImage = new StyleBackground(CrosshairTextures.Get(GameSettings.Crosshair));
            preview.Add(image);
            AddDropdown("Вид прицела", styles.Select(s => GameSettings.CrosshairNames[s]).ToList(),
                styles.IndexOf(GameSettings.Crosshair),
                i =>
                {
                    GameSettings.Crosshair = styles[i];
                    image.style.backgroundImage = new StyleBackground(CrosshairTextures.Get(styles[i]));
                });
            Content.Add(preview);

            Group("Робот");
            var robots = GameSettings.RobotNames.Keys.ToList();
            AddDropdown("Модель", robots.Select(r => GameSettings.RobotNames[r]).ToList(),
                robots.IndexOf(GameSettings.Robot), i => GameSettings.Robot = robots[i]);
            var note = new Label("Модели роботов появятся в следующих версиях — выбор уже сохраняется.");
            note.AddToClassList("hint");
            Content.Add(note);
        }
    }
}

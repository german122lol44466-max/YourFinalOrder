using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using YourFinalOrder.Core;
using YourFinalOrder.World;
using YourFinalOrder.Game;
using YourFinalOrder.Menu;
using YourFinalOrder.Networking;
using YourFinalOrder.Player;
using YourFinalOrder.Steam;

namespace YourFinalOrder.EditorTools
{
    /// <summary>
    /// Собирает проект одной командой: URP, материалы, префабы, сцены меню и игры, Build Settings.
    /// Меню Unity: Your Last Order → Собрать проект.
    /// Повторный запуск безопасен: ассеты перезаписываются, GUID префабов сохраняются.
    /// </summary>
    public static class ProjectBuilder
    {
        const string Root = "Assets/_Project";
        const string Gen = Root + "/Generated";
        const string ScenesDir = Root + "/Scenes";
        const string MenuScenePath = ScenesDir + "/Menu.unity";
        const string GameScenePath = ScenesDir + "/Game.unity";
        const string ShuttleDir = Root + "/Art/Models/Shuttle";
        const string RobotsDir = Root + "/Art/Models/Robots";
        static readonly string[] RobotNames = { "Can", "Toaster", "Lantern" }; // порядок = enum RobotModel

        [MenuItem("Your Last Order/Собрать проект", priority = 0)]
        public static void BuildAll() => Generate(interactive: true);

        /// <summary>Создаёт все сцены и ассеты. interactive=false — без окон (сборка в облаке).</summary>
        public static void Generate(bool interactive)
        {
            if (interactive && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            try
            {
                if (interactive) EditorUtility.DisplayProgressBar("Your Last Order", "Настройки проекта...", 0.05f);
                EnsureFolder(Gen);
                EnsureFolder(ScenesDir);
                SetupRenderPipeline();
                SetupPlayerSettings();
                SetupIcon();

                if (interactive) EditorUtility.DisplayProgressBar("Your Last Order", "Материалы...", 0.2f);
                var mats = new MaterialSet();
                SetupShuttleImport(mats);
                SetupRobotImports(mats);

                if (interactive) EditorUtility.DisplayProgressBar("Your Last Order", "Префабы...", 0.4f);
                var player = BuildPlayerPrefab(mats);
                var prefabList = BuildPrefabList(player);
                var networkRoot = BuildNetworkRoot(prefabList);

                if (interactive) EditorUtility.DisplayProgressBar("Your Last Order", "Сцена меню...", 0.6f);
                BuildMenuScene(networkRoot, mats);

                if (interactive) EditorUtility.DisplayProgressBar("Your Last Order", "Сцена игры...", 0.8f);
                BuildGameScene(networkRoot, player, mats);

                EditorBuildSettings.scenes = new[]
                {
                    new EditorBuildSettingsScene(MenuScenePath, true),
                    new EditorBuildSettingsScene(GameScenePath, true),
                };
                AssetDatabase.SaveAssets();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (!interactive) return;
            EditorSceneManager.OpenScene(MenuScenePath);
            EditorUtility.DisplayDialog("Your Last Order",
                "Проект собран.\n\nСцена меню открыта — нажмите Play.\n" +
                "Для игры через Steam запустите Steam (используется тестовый App ID 480).", "OK");
        }

        // ================================================================== проект

        static void SetupRenderPipeline()
        {
            string urpPath = Gen + "/URP.asset";
            string rendererPath = Gen + "/URP_Renderer.asset";

            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(urpPath);
            if (urp == null)
            {
                var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                renderer.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(
                    "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
                AssetDatabase.CreateAsset(renderer, rendererPath);

                urp = UniversalRenderPipelineAsset.Create(renderer);
                urp.supportsHDR = true;
                urp.shadowDistance = 60f;
                urp.msaaSampleCount = 1;
                AssetDatabase.CreateAsset(urp, urpPath);
            }

            GraphicsSettings.defaultRenderPipeline = urp;
            // У всех уровней качества — тот же пайплайн (null = по умолчанию)
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = null;
            }
            QualitySettings.SetQualityLevel(current, false);
        }

        static void SetupPlayerSettings()
        {
            PlayerSettings.productName = "Your Last Order";
            PlayerSettings.companyName = "You Can Buy Everything";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.runInBackground = true; // сеть не должна засыпать в свёрнутом окне
            PlayerSettings.resizableWindow = true;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultIsNativeResolution = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.SplashScreen.show = false; // своя заставка появится позже
            if (string.IsNullOrEmpty(PlayerSettings.bundleVersion) || PlayerSettings.bundleVersion == "1.0")
                PlayerSettings.bundleVersion = "0.1.0";
        }

        // ================================================================== материалы

        class MaterialSet
        {
            public readonly Material Shuttle, ShuttleGlass, EngineGlow, Lamp;
            public readonly Material Floor, Wall, Ceiling, Metal, Cardboard, Tape, Screen, Body, Additive;
            public readonly Material RobotFace, RobotRed, RobotGlass, RobotVisor;

            public MaterialSet()
            {
                var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(ShuttleDir + "/Shuttle_Albedo.png");
                Shuttle = Lit("M_Shuttle", Color.white, 0.12f, 0.35f, albedo);
                ShuttleGlass = Lit("M_ShuttleGlass", Color.white, 0f, 0.93f, albedo, new Color(1f, 0.55f, 0.25f) * 0.6f);
                EngineGlow = Lit("M_EngineGlow", new Color(0.3f, 0.7f, 1f), 0f, 0.5f, null, new Color(0.35f, 0.75f, 1f) * 12f);
                Lamp = Lit("M_Lamp", Color.white, 0f, 0.5f, null, new Color(1f, 0.9f, 0.7f) * 6f);

                Floor = Lit("M_Floor", new Color(0.16f, 0.16f, 0.17f), 0.6f, 0.45f, NoiseTex("T_FloorPlate", 0.75f, 0.2f, plates: true));
                Wall = Lit("M_Wall", new Color(0.32f, 0.31f, 0.29f), 0.3f, 0.3f, NoiseTex("T_WallPanel", 0.8f, 0.25f, plates: true));
                Ceiling = Lit("M_Ceiling", new Color(0.12f, 0.12f, 0.12f), 0.2f, 0.2f, null);
                Metal = Lit("M_Metal", new Color(0.22f, 0.22f, 0.23f), 0.85f, 0.5f, null);
                Cardboard = Lit("M_Cardboard", new Color(0.6f, 0.45f, 0.28f), 0f, 0.15f, NoiseTex("T_Cardboard", 0.85f, 0.15f, plates: false));
                Tape = Lit("M_Tape", new Color(0.85f, 0.7f, 0.25f), 0f, 0.45f, null);
                Screen = Lit("M_Screen", new Color(0.02f, 0.05f, 0.03f), 0f, 0.9f, null, new Color(0.25f, 1f, 0.45f) * 1.8f);
                Body = Lit("M_RobotBody", new Color(0.42f, 0.5f, 0.38f), 0.5f, 0.35f, NoiseTex("T_RobotPaint", 0.8f, 0.3f, plates: false));
                Additive = ParticleAdditive("M_FX_Additive");
                RobotFace = Lit("M_RobotFace", new Color(0.05f, 0.2f, 0.1f), 0f, 0.85f, null, new Color(0.3f, 1f, 0.5f) * 4f);
                RobotRed = Lit("M_RobotRed", new Color(0.6f, 0.05f, 0.03f), 0f, 0.6f, null, new Color(1f, 0.1f, 0.05f) * 3f);
                RobotGlass = Transparent("M_RobotGlass", new Color(0.7f, 0.85f, 0.8f, 0.25f), 0.95f);
                // Визор-экран: базовый цвет и лицо задаёт RobotFace в рантайме (текстура + эмиссия)
                RobotVisor = Lit("M_RobotVisor", Color.white, 0f, 0.93f, null, Color.black);
            }

            public static Material Lit(string name, Color color, float metallic, float smoothness, Texture2D tex, Color? emission = null)
            {
                string path = $"{Gen}/{name}.mat";
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    mat = new Material(shader);
                    AssetDatabase.CreateAsset(mat, path);
                }
                mat.shader = shader;
                mat.SetColor("_BaseColor", color);
                mat.SetTexture("_BaseMap", tex);
                mat.SetFloat("_Metallic", metallic);
                mat.SetFloat("_Smoothness", smoothness);
                if (emission.HasValue)
                {
                    mat.SetColor("_EmissionColor", emission.Value);
                    mat.EnableKeyword("_EMISSION");
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
                else
                {
                    mat.SetColor("_EmissionColor", Color.black);
                    mat.DisableKeyword("_EMISSION");
                }
                BaseShaderGUI.SetMaterialKeywords(mat, LitGUI.SetMaterialKeywords);
                if (emission.HasValue) mat.EnableKeyword("_EMISSION");
                EditorUtility.SetDirty(mat);
                return mat;
            }

            static Material Transparent(string name, Color color, float smoothness)
            {
                string path = $"{Gen}/{name}.mat";
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    mat = new Material(shader);
                    AssetDatabase.CreateAsset(mat, path);
                }
                mat.shader = shader;
                mat.SetColor("_BaseColor", color);
                mat.SetFloat("_Smoothness", smoothness);
                mat.SetFloat("_Metallic", 0f);
                mat.SetFloat("_Surface", 1f); // Transparent
                mat.SetFloat("_Blend", 0f);   // Alpha
                BaseShaderGUI.SetMaterialKeywords(mat, LitGUI.SetMaterialKeywords);
                EditorUtility.SetDirty(mat);
                return mat;
            }

            static Material ParticleAdditive(string name)
            {
                string path = $"{Gen}/{name}.mat";
                var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    mat = new Material(shader);
                    AssetDatabase.CreateAsset(mat, path);
                }
                mat.shader = shader;
                mat.SetTexture("_BaseMap", SoftDot());
                mat.SetColor("_BaseColor", Color.white);
                mat.SetFloat("_Surface", 1f); // Transparent
                mat.SetFloat("_Blend", 2f);   // Additive
                BaseShaderGUI.SetMaterialKeywords(mat, null, ParticleGUI.SetMaterialKeywords);
                EditorUtility.SetDirty(mat);
                return mat;
            }

            static Texture2D SoftDot()
            {
                string path = $"{Gen}/T_SoftDot.png";
                if (!File.Exists(path))
                {
                    const int n = 128;
                    var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
                    for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x - n / 2f + 0.5f) / (n / 2f), dy = (y - n / 2f + 0.5f) / (n / 2f);
                        float r = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.Clamp01(1f - r);
                        a = a * a * (3f - 2f * a);
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                    }
                    File.WriteAllBytes(path, tex.EncodeToPNG());
                    Object.DestroyImmediate(tex);
                    AssetDatabase.ImportAsset(path);
                    var imp = (TextureImporter)AssetImporter.GetAtPath(path);
                    imp.alphaIsTransparency = true;
                    imp.wrapMode = TextureWrapMode.Clamp;
                    imp.SaveAndReimport();
                }
                return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }

            /// <summary>Грязная процедурная текстура (шум + швы панелей).</summary>
            static Texture2D NoiseTex(string name, float baseValue, float dirt, bool plates)
            {
                string path = $"{Gen}/{name}.png";
                if (!File.Exists(path))
                {
                    const int n = 512;
                    var tex = new Texture2D(n, n, TextureFormat.RGBA32, true);
                    var rnd = new System.Random(name.GetHashCode());
                    float ox = rnd.Next(1000), oy = rnd.Next(1000);
                    for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float u = x / (float)n, v = y / (float)n;
                        float big = Tile(u, v, 4f, ox, oy);
                        float small = Tile(u, v, 24f, oy, ox);
                        float streak = Tile(u * 0.5f, v * 6f, 6f, ox + 50, oy);
                        float val = baseValue - dirt * Mathf.SmoothStep(0.35f, 0.75f, big) - 0.08f * small - 0.1f * Mathf.SmoothStep(0.6f, 0.9f, streak);
                        if (plates)
                        {
                            int px = x % 256, py = y % 256;
                            if (px < 2 || py < 2) val *= 0.45f; // шов между плитами
                            if ((px - 12) * (px - 12) + (py - 12) * (py - 12) < 9) val *= 0.6f; // заклёпка
                        }
                        tex.SetPixel(x, y, new Color(val, val, val, 1f));
                    }
                    File.WriteAllBytes(path, tex.EncodeToPNG());
                    Object.DestroyImmediate(tex);
                    AssetDatabase.ImportAsset(path);
                }
                return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }

            // бесшовный шум
            static float Tile(float u, float v, float scale, float ox, float oy)
            {
                float a = Mathf.PerlinNoise(ox + u * scale, oy + v * scale);
                float b = Mathf.PerlinNoise(ox + (u - 1f) * scale, oy + v * scale);
                float c = Mathf.PerlinNoise(ox + u * scale, oy + (v - 1f) * scale);
                float d = Mathf.PerlinNoise(ox + (u - 1f) * scale, oy + (v - 1f) * scale);
                return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
            }
        }

        static void SetupShuttleImport(MaterialSet mats)
        {
            string path = ShuttleDir + "/Shuttle.fbx";
            if (!(AssetImporter.GetAtPath(path) is ModelImporter importer))
            {
                Debug.LogWarning("Не найден Shuttle.fbx — запустите Tools/Blender/build_shuttle.py");
                return;
            }
            importer.importCameras = false;
            importer.importLights = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;

            var map = new Dictionary<string, Material>
            {
                { "Hull", mats.Shuttle }, { "Stripe", mats.Shuttle }, { "DarkMetal", mats.Shuttle },
                { "Glass", mats.ShuttleGlass }, { "EngineGlow", mats.EngineGlow }, { "Lamp", mats.Lamp },
            };
            foreach (var kv in map)
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);
            importer.SaveAndReimport();
        }

        static void SetupRobotImports(MaterialSet mats)
        {
            foreach (var name in RobotNames)
            {
                string path = $"{RobotsDir}/Robot_{name}.fbx";
                if (!(AssetImporter.GetAtPath(path) is ModelImporter importer))
                {
                    Debug.LogWarning($"Не найден {path} — запустите Tools/Blender/build_robots.py");
                    continue;
                }

                // Карта металличности/гладкости — линейная (не sRGB)
                string msPath = $"{RobotsDir}/Robot_{name}_MetalSmooth.png";
                if (AssetImporter.GetAtPath(msPath) is TextureImporter msImp && msImp.sRGBTexture)
                {
                    msImp.sRGBTexture = false;
                    msImp.SaveAndReimport();
                }
                var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>($"{RobotsDir}/Robot_{name}_Albedo.png");
                var metalSmooth = AssetDatabase.LoadAssetAtPath<Texture2D>(msPath);
                var bodyMat = MaterialSet.Lit($"M_Robot_{name}", Color.white, 1f, 1f, albedo);
                if (metalSmooth != null)
                {
                    bodyMat.SetTexture("_MetallicGlossMap", metalSmooth);
                    BaseShaderGUI.SetMaterialKeywords(bodyMat, LitGUI.SetMaterialKeywords);
                }
                else
                {
                    bodyMat.SetFloat("_Metallic", 0.3f);
                    bodyMat.SetFloat("_Smoothness", 0.35f);
                }

                importer.importCameras = false;
                importer.importLights = false;
                importer.animationType = ModelImporterAnimationType.Generic;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importAnimation = true;
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
                foreach (var src in new[] { "Suit", "Armor", "Black", "Rubber", "Rust", "Metal" })
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), src), bodyMat);
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "Visor"), mats.RobotVisor);
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "Led"), mats.RobotRed);

                // Клипы: «Armature|Walk» → «Walk», все зациклены
                var clips = importer.defaultClipAnimations;
                foreach (var c in clips)
                {
                    c.name = c.takeName.Split('|').Last();
                    c.loopTime = true;
                    c.loopPose = false;
                }
                importer.clipAnimations = clips;
                importer.SaveAndReimport();
            }
        }

        /// <summary>
        /// Animator для роботов: стоя — Idle/Walk/Run по скорости, в приседе — CrouchIdle/CrouchWalk.
        /// Скелет у всех трёх моделей одинаковый, поэтому хватает клипов одной модели.
        /// </summary>
        static RuntimeAnimatorController BuildRobotAnimator()
        {
            string path = Gen + "/RobotAnimator.controller";
            AssetDatabase.DeleteAsset(path);
            var ctrl = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(path);
            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("Crouch", AnimatorControllerParameterType.Bool);

            var clips = AssetDatabase.LoadAllAssetsAtPath($"{RobotsDir}/Robot_{RobotNames[0]}.fbx")
                .OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToList();
            AnimationClip Clip(string n) => clips.FirstOrDefault(c => c.name == n || c.name.EndsWith("|" + n));
            if (Clip("Idle") == null)
            {
                Debug.LogWarning("В Robot_Can.fbx нет анимаций — запустите Tools/Blender/build_robots.py");
                return ctrl;
            }

            var sm = ctrl.layers[0].stateMachine;
            var stand = ctrl.CreateBlendTreeInController("Locomotion", out var standTree, 0);
            standTree.blendParameter = "Speed";
            standTree.useAutomaticThresholds = false;
            standTree.AddChild(Clip("Idle"), 0f);
            standTree.AddChild(Clip("Walk"), 1.67f);
            standTree.AddChild(Clip("Run"), 4.75f);

            var crouch = ctrl.CreateBlendTreeInController("Crouch", out var crouchTree, 0);
            crouchTree.blendParameter = "Speed";
            crouchTree.useAutomaticThresholds = false;
            crouchTree.AddChild(Clip("CrouchIdle"), 0f);
            crouchTree.AddChild(Clip("CrouchWalk"), 1.4f);

            sm.defaultState = stand;
            var toCrouch = stand.AddTransition(crouch);
            toCrouch.hasExitTime = false;
            toCrouch.duration = 0.2f;
            toCrouch.AddCondition(UnityEditor.Animations.AnimatorConditionMode.If, 0, "Crouch");
            var toStand = crouch.AddTransition(stand);
            toStand.hasExitTime = false;
            toStand.duration = 0.2f;
            toStand.AddCondition(UnityEditor.Animations.AnimatorConditionMode.IfNot, 0, "Crouch");

            EditorUtility.SetDirty(ctrl);
            AssetDatabase.SaveAssets();
            return ctrl;
        }

        static void SetupIcon()
        {
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Art/Branding/Icon.png");
            if (icon == null) return;
            PlayerSettings.SetIcons(UnityEditor.Build.NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
        }

        // ================================================================== префабы

        static GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale,
            Material mat, bool collider = true)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        static GameObject BuildPlayerPrefab(MaterialSet mats)
        {
            var root = new GameObject("Player");
            var cc = root.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.stepOffset = 0.35f;

            root.AddComponent<NetworkObject>();
            var nt = root.AddComponent<OwnerNetworkTransform>();
            nt.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
            nt.SyncRotAngleX = false;
            nt.SyncRotAngleZ = false;
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
            nt.Interpolate = true;

            // Модель робота подставляется в рантайме (RobotAppearance) по выбору игрока
            var body = new GameObject("Body").transform;
            body.SetParent(root.transform, false);

            var head = new GameObject("CameraRoot").transform;
            head.SetParent(root.transform, false);
            head.localPosition = new Vector3(0f, 1.66f, 0f); // уровень визора робота

            var camGo = new GameObject("Camera");
            camGo.transform.SetParent(head, false);
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 200f;
            cam.fieldOfView = 75f;
            var camData = GetOrAdd<UniversalAdditionalCameraData>(camGo);
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var listener = camGo.AddComponent<AudioListener>();
            listener.enabled = false;

            var flashGo = new GameObject("Flashlight");
            flashGo.transform.SetParent(head, false);
            flashGo.transform.localPosition = new Vector3(0.25f, -0.2f, 0.2f);
            var spot = flashGo.AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.range = 22f;
            spot.spotAngle = 55f;
            spot.innerSpotAngle = 25f;
            spot.intensity = 18f;
            spot.color = new Color(1f, 0.93f, 0.8f);
            spot.shadows = LightShadows.Soft;
            spot.enabled = false;

            var fpc = root.AddComponent<FirstPersonController>();
            fpc.cameraRoot = head;
            var np = root.AddComponent<NetworkPlayer>();
            np.playerCamera = cam;
            np.audioListener = listener;
            np.head = head;
            np.bodyRenderers = new Renderer[0];
            var state = root.AddComponent<PlayerState>();
            state.bodyRenderers = new Renderer[0];
            var robot = root.AddComponent<RobotAppearance>();
            robot.bodyRoot = body;
            robot.models = RobotNames.Select(n => AssetDatabase.LoadAssetAtPath<GameObject>($"{RobotsDir}/Robot_{n}.fbx")).ToArray();
            robot.animator = BuildRobotAnimator();
            root.AddComponent<Flashlight>().spot = spot;
            root.AddComponent<PlayerInteractor>().cam = cam;
            root.AddComponent<Footsteps>();
            var hud = root.AddComponent<PlayerHUD>();
            hud.enabled = false;
            var fear = root.AddComponent<FearEffects>();
            fear.shakeTarget = camGo.transform;
            fear.enabled = false;

            return SavePrefab(root, Gen + "/Player.prefab");
        }

        static GameObject SavePrefab(GameObject go, string path)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            EnsureNetworkHash(prefab);
            return prefab;
        }

        /// <summary>Гарантирует, что у сетевого префаба сгенерирован GlobalObjectIdHash.</summary>
        static void EnsureNetworkHash(GameObject prefab)
        {
            var no = prefab.GetComponent<NetworkObject>();
            if (no == null) return;
            var validate = typeof(NetworkObject).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            validate?.Invoke(no, null);
            EditorUtility.SetDirty(no);
            AssetDatabase.SaveAssets();
        }

        static NetworkPrefabsList BuildPrefabList(params GameObject[] prefabs)
        {
            string path = Gen + "/NetworkPrefabs.asset";
            AssetDatabase.DeleteAsset(path);
            var list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
            foreach (var p in prefabs)
                list.Add(new NetworkPrefab { Prefab = p });
            AssetDatabase.CreateAsset(list, path);
            return list;
        }

        static GameObject BuildNetworkRoot(NetworkPrefabsList prefabs)
        {
            var go = new GameObject("NetworkRoot");
            var nm = go.AddComponent<NetworkManager>();
            var utp = go.AddComponent<UnityTransport>();
            go.AddComponent<SteamNetworkTransport>();
            go.AddComponent<SteamLobby>();
            go.AddComponent<SessionManager>();

            nm.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = utp,
                ConnectionApproval = true,
                EnableSceneManagement = true,
                TickRate = 30,
            };
            nm.NetworkConfig.Prefabs.NetworkPrefabsLists.Clear();
            nm.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(prefabs);
            utp.SetConnectionData("127.0.0.1", 7777, "0.0.0.0");

            return SavePrefab(go, Gen + "/NetworkRoot.prefab");
        }

        // ================================================================== сцена меню

        static void BuildMenuScene(GameObject networkRoot, MaterialSet mats)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.05f, 0.05f, 0.07f);
            RenderSettings.skybox = null;
            RenderSettings.fog = false;

            new GameObject("MenuBootstrap").AddComponent<MenuBootstrap>().networkRootPrefab = networkRoot;

            // Корабль
            var ship = new GameObject("Ship");
            var flyby = ship.AddComponent<ShuttleFlyby>();
            flyby.additiveMaterial = mats.Additive;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ShuttleDir + "/Shuttle.fbx");
            if (model != null)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                instance.transform.SetParent(ship.transform, false);
                flyby.model = instance.transform;
            }

            // Свет
            var key = new GameObject("KeyLight").AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.3f;
            key.color = new Color(1f, 0.92f, 0.82f);
            key.shadows = LightShadows.Soft;
            key.transform.rotation = Quaternion.Euler(35f, -140f, 0f);
            var rim = new GameObject("RimLight").AddComponent<Light>();
            rim.type = LightType.Directional;
            rim.intensity = 2.2f;
            rim.transform.rotation = Quaternion.Euler(-15f, 20f, 0f);

            // Камера
            var camGo = new GameObject("MenuCamera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.farClipPlane = 3000f;
            cam.nearClipPlane = 0.3f;
            GetOrAdd<UniversalAdditionalCameraData>(camGo).renderPostProcessing = true;
            camGo.AddComponent<AudioListener>();
            var rig = camGo.AddComponent<MenuCameraRig>();
            rig.target = ship.transform;
            camGo.transform.position = new Vector3(-20f, 8f, -25f);

            // Космос
            var space = new GameObject("Space").AddComponent<SpaceParticles>();
            space.additiveMaterial = mats.Additive;
            space.ship = ship.transform;

            var jumper = new GameObject("RealityJumper").AddComponent<RealityJumper>();
            jumper.ship = flyby;
            jumper.space = space;
            jumper.cameraRig = rig;
            jumper.rimLight = rim;

            // UI
            var uiGo = new GameObject("MainMenuUI");
            var doc = uiGo.AddComponent<UIDocument>();
            doc.panelSettings = GetPanelSettings();
            doc.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Root + "/UI/MainMenu.uxml");
            uiGo.AddComponent<AudioSource>().playOnAwake = false;
            uiGo.AddComponent<MainMenuController>();

            EditorSceneManager.SaveScene(scene, MenuScenePath);
        }

        static PanelSettings GetPanelSettings()
        {
            string path = Gen + "/PanelSettings.asset";
            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
            if (ps == null)
            {
                ps = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(ps, path);
            }
            ps.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(Root + "/UI/Runtime.tss");
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 1f; // по высоте — одинаково на 16:9, 21:9 и 32:9
            EditorUtility.SetDirty(ps);
            return ps;
        }

        // ================================================================== сцена игры (грузовой отсек)

        static void BuildGameScene(GameObject networkRoot, GameObject player, MaterialSet mats)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.035f, 0.035f, 0.04f);
            RenderSettings.skybox = null;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.035f;
            RenderSettings.fogColor = new Color(0.02f, 0.02f, 0.025f);

            var session = new GameObject("GameSession").AddComponent<GameSession>();
            session.playerPrefab = player;
            session.networkRootPrefab = networkRoot;

            var bay = new GameObject("CargoBay").transform;
            const float w = 8f, len = 18f, h = 3.6f;
            Primitive(PrimitiveType.Cube, "Floor", bay, new Vector3(0, -0.1f, 0), new Vector3(w, 0.2f, len), mats.Floor);
            Primitive(PrimitiveType.Cube, "Ceiling", bay, new Vector3(0, h + 0.1f, 0), new Vector3(w, 0.2f, len), mats.Ceiling);
            Primitive(PrimitiveType.Cube, "WallL", bay, new Vector3(-w / 2 - 0.1f, h / 2, 0), new Vector3(0.2f, h, len), mats.Wall);
            Primitive(PrimitiveType.Cube, "WallR", bay, new Vector3(w / 2 + 0.1f, h / 2, 0), new Vector3(0.2f, h, len), mats.Wall);
            Primitive(PrimitiveType.Cube, "Bulkhead", bay, new Vector3(0, h / 2, len / 2 + 0.1f), new Vector3(w, h, 0.2f), mats.Wall);
            // рампа (закрыта)
            Primitive(PrimitiveType.Cube, "Ramp", bay, new Vector3(0, h / 2, -len / 2 - 0.1f), new Vector3(w, h, 0.2f), mats.Metal);
            Primitive(PrimitiveType.Cube, "RampStripe", bay, new Vector3(0, 0.25f, -len / 2 + 0.01f), new Vector3(w, 0.15f, 0.02f), mats.Tape, false);

            // рёбра жёсткости
            for (int i = -3; i <= 3; i++)
            {
                float z = i * 2.6f;
                Primitive(PrimitiveType.Cube, "RibL", bay, new Vector3(-w / 2 + 0.1f, h / 2, z), new Vector3(0.2f, h, 0.25f), mats.Metal);
                Primitive(PrimitiveType.Cube, "RibR", bay, new Vector3(w / 2 - 0.1f, h / 2, z), new Vector3(0.2f, h, 0.25f), mats.Metal);
                Primitive(PrimitiveType.Cube, "RibTop", bay, new Vector3(0, h - 0.1f, z), new Vector3(w, 0.2f, 0.25f), mats.Metal, false);
            }

            // стеллажи с посылками
            var rnd = new System.Random(7);
            for (int side = -1; side <= 1; side += 2)
            {
                for (int s = 0; s < 3; s++)
                {
                    float z = -4f + s * 4f;
                    var shelf = new GameObject($"Shelf_{side}_{s}").transform;
                    shelf.SetParent(bay, false);
                    shelf.localPosition = new Vector3(side * (w / 2 - 0.7f), 0, z);
                    for (int level = 0; level < 3; level++)
                    {
                        float y = 0.05f + level * 0.9f;
                        Primitive(PrimitiveType.Cube, "Board", shelf, new Vector3(0, y, 0), new Vector3(0.9f, 0.05f, 2.4f), mats.Metal);
                        for (int b = 0; b < 3; b++)
                        {
                            if (rnd.NextDouble() < 0.25) continue;
                            float sx = 0.35f + (float)rnd.NextDouble() * 0.3f;
                            float sy = 0.25f + (float)rnd.NextDouble() * 0.35f;
                            var box = Primitive(PrimitiveType.Cube, "Parcel", shelf,
                                new Vector3(0, y + 0.025f + sy / 2, -0.8f + b * 0.8f), new Vector3(sx, sy, sx), mats.Cardboard);
                            box.transform.localRotation = Quaternion.Euler(0, (float)rnd.NextDouble() * 20f - 10f, 0);
                            Primitive(PrimitiveType.Cube, "Tape", box.transform, new Vector3(0, 0.501f, 0), new Vector3(0.2f, 0.01f, 1.01f), mats.Tape, false);
                        }
                    }
                    Primitive(PrimitiveType.Cube, "PostA", shelf, new Vector3(0, 1.3f, -1.2f), new Vector3(0.06f, 2.6f, 0.06f), mats.Metal);
                    Primitive(PrimitiveType.Cube, "PostB", shelf, new Vector3(0, 1.3f, 1.2f), new Vector3(0.06f, 2.6f, 0.06f), mats.Metal);
                }
            }

            // разбросанные коробки
            for (int i = 0; i < 6; i++)
            {
                float s = 0.4f + (float)rnd.NextDouble() * 0.4f;
                var box = Primitive(PrimitiveType.Cube, "LooseParcel", bay,
                    new Vector3((float)rnd.NextDouble() * 3f - 1.5f, s / 2, (float)rnd.NextDouble() * 10f - 6f), Vector3.one * s, mats.Cardboard);
                box.transform.localRotation = Quaternion.Euler(0, (float)rnd.NextDouble() * 90f, 0);
            }

            // терминал загрузки фото (пока декорация)
            var terminal = new GameObject("UploadTerminal").transform;
            terminal.SetParent(bay, false);
            terminal.localPosition = new Vector3(0, 0, len / 2 - 0.6f);
            Primitive(PrimitiveType.Cube, "Desk", terminal, new Vector3(0, 0.45f, 0), new Vector3(2.2f, 0.9f, 0.8f), mats.Metal);
            Primitive(PrimitiveType.Cube, "Monitor", terminal, new Vector3(0, 1.45f, 0.2f), new Vector3(1.4f, 0.85f, 0.08f), mats.Metal);
            Primitive(PrimitiveType.Cube, "Screen", terminal, new Vector3(0, 1.45f, 0.155f), new Vector3(1.28f, 0.73f, 0.01f), mats.Screen, false);
            var screenLight = new GameObject("ScreenGlow").AddComponent<Light>();
            screenLight.transform.SetParent(terminal, false);
            screenLight.transform.localPosition = new Vector3(0, 1.4f, -0.6f);
            screenLight.type = LightType.Point;
            screenLight.range = 4f;
            screenLight.intensity = 1.5f;
            screenLight.color = new Color(0.4f, 1f, 0.55f);

            // лампы под потолком
            for (int i = -1; i <= 1; i++)
            {
                var lampGo = new GameObject("CeilingLamp");
                lampGo.transform.SetParent(bay, false);
                lampGo.transform.localPosition = new Vector3(0, h - 0.3f, i * 6f);
                var fixture = Primitive(PrimitiveType.Cube, "Fixture", lampGo.transform, Vector3.zero, new Vector3(1.2f, 0.08f, 0.25f), mats.Lamp, false);
                var light = lampGo.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = 8f;
                light.intensity = 3.5f;
                light.color = new Color(1f, 0.85f, 0.65f);
                light.shadows = i == 0 ? LightShadows.Soft : LightShadows.None;
                var flicker = lampGo.AddComponent<FlickerLight>();
                flicker.emissiveRenderer = fixture.GetComponent<Renderer>();
                flicker.blackoutChancePerSecond = i == 1 ? 0.25f : 0.03f;
            }

            // красный аварийный свет у рампы
            var alarm = new GameObject("RampAlarm").AddComponent<Light>();
            alarm.transform.SetParent(bay, false);
            alarm.transform.localPosition = new Vector3(0, h - 0.4f, -len / 2 + 0.8f);
            alarm.type = LightType.Point;
            alarm.range = 6f;
            alarm.intensity = 2f;
            alarm.color = new Color(1f, 0.15f, 0.1f);

            // точки появления
            var spawns = new GameObject("Spawns").transform;
            for (int i = 0; i < 4; i++)
            {
                var m = new GameObject($"Spawn_{i}").AddComponent<LevelMarker>();
                m.type = LevelMarker.MarkerType.PlayerSpawn;
                m.transform.SetParent(spawns, false);
                m.transform.localPosition = new Vector3(-1.5f + i, 0.05f, -2f);
                m.transform.localRotation = Quaternion.Euler(0, 0, 0);
            }

            // постобработка сцены игры
            var volume = new GameObject("PostFX").AddComponent<Volume>();
            volume.isGlobal = true;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.8f);
            bloom.threshold.Override(1f);
            profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.ACES);
            profile.Add<Vignette>(true).intensity.Override(0.35f);
            profile.Add<FilmGrain>(true).intensity.Override(0.3f);
            AssetDatabase.DeleteAsset(Gen + "/GamePostFX.asset");
            AssetDatabase.CreateAsset(profile, Gen + "/GamePostFX.asset");
            foreach (var component in profile.components)
            {
                component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(component, profile);
            }
            volume.sharedProfile = profile;

            EditorSceneManager.SaveScene(scene, GameScenePath);
        }

        static T GetOrAdd<T>(GameObject go) where T : Component
        {
            return go.TryGetComponent<T>(out var c) ? c : go.AddComponent<T>();
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}

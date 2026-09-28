using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace YourFinalOrder.EditorTools
{
    /// <summary>
    /// Сборка игры без участия человека (GitHub Actions / командная строка):
    ///   Unity -batchmode -projectPath . -executeMethod YourFinalOrder.EditorTools.CiBuild.BuildWindows
    ///         -customBuildPath "Build/Your Last Order.exe" -quit
    /// Также доступна из меню: Your Last Order → Собрать игру для Windows.
    /// </summary>
    public static class CiBuild
    {
        const string DefaultPath = "Build/Windows/YourLastOrder.exe";

        [MenuItem("Your Last Order/Собрать игру для Windows", priority = 20)]
        public static void BuildFromMenu()
        {
            var report = Build(DefaultPath, interactive: true);
            if (report.summary.result == BuildResult.Succeeded)
                EditorUtility.RevealInFinder(DefaultPath);
        }

        public static void BuildWindows()
        {
            string path = GetArg("-customBuildPath") ?? DefaultPath;
            BuildReport report;
            try
            {
                report = Build(path, interactive: false);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
                return;
            }
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        static BuildReport Build(string path, bool interactive)
        {
            ProjectBuilder.Generate(interactive);

            // Имя исполняемого файла — всегда YourLastOrder.exe, папка берётся из пути
            string dir = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir)) dir = ".";
            string exe = Path.Combine(dir, "YourLastOrder.exe");

            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = exe,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"[CiBuild] {report.summary.result}: {exe}, {report.summary.totalSize / (1024 * 1024)} МБ, ошибок: {report.summary.totalErrors}");
            return report;
        }

        static string GetArg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}

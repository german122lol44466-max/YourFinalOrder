using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace YourFinalOrder.EditorTools
{
    /// <summary>
    /// Кладёт steam_appid.txt рядом с собранным .exe — без него Steam API не инициализируется,
    /// если игра запущена не из библиотеки Steam как «своя» игра с App ID.
    /// </summary>
    public class SteamAppIdPostBuild : IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPostprocessBuild(BuildReport report)
        {
            var target = report.summary.platform;
            if (target != BuildTarget.StandaloneWindows64 && target != BuildTarget.StandaloneWindows &&
                target != BuildTarget.StandaloneLinux64 && target != BuildTarget.StandaloneOSX)
                return;

            string source = "steam_appid.txt";
            if (!File.Exists(source)) return;
            string dir = Path.GetDirectoryName(report.summary.outputPath);
            File.Copy(source, Path.Combine(dir, "steam_appid.txt"), true);
        }
    }
}

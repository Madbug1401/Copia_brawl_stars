using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace FPSLAN.EditorTools
{
    public class MobileBuildSupport : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public static void BuildAndroidPlayer()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                throw new BuildFailedException("Inicia o Unity com -buildTarget Android para gerar a build Android.");

            EditorUserBuildSettings.buildAppBundle = false;
            var outputPath = System.Environment.GetEnvironmentVariable("FPSLAN_ANDROID_OUTPUT");
            if (string.IsNullOrWhiteSpace(outputPath))
                outputPath = Path.Combine(Directory.GetCurrentDirectory(), "build android", "FPS-LAN.apk");
            BuildPlayer(BuildTarget.Android, outputPath);
        }

        [MenuItem("FPS-LAN/Build & Run Android (USB)", priority = 21)]
        static void BuildAndRunAndroidPlayer()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                throw new BuildFailedException("Muda a plataforma ativa para Android antes de usar Build & Run.");

            EditorUserBuildSettings.buildAppBundle = false;
            var outputPath = Path.Combine(Directory.GetCurrentDirectory(), "Library", "FPSLAN", "FPS-LAN-dev.apk");
            BuildPlayer(BuildTarget.Android, outputPath, BuildOptions.Development | BuildOptions.AutoRunPlayer);
        }

        public static void BuildIOSProject()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.iOS)
                throw new BuildFailedException("Inicia o Unity com -buildTarget iOS para gerar o projeto Xcode.");

            BuildPlayer(BuildTarget.iOS, Path.Combine(Directory.GetCurrentDirectory(), "build iOS", "FPS-LAN-iOS"));
        }

        static void BuildPlayer(BuildTarget target, string outputPath, BuildOptions options = BuildOptions.None)
        {
            var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
            if (scenes.Length == 0)
                throw new BuildFailedException("Não há cenas ativadas em Build Settings.");

            var outputDirectory = target == BuildTarget.Android ? Path.GetDirectoryName(outputPath) : outputPath;
            Directory.CreateDirectory(outputDirectory);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = target,
                options = options,
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException("A build " + target + " terminou com estado " + report.summary.result + ".");
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android && report.summary.platform != BuildTarget.iOS)
                return;

            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;

            if (report.summary.platform == BuildTarget.Android)
            {
                PlayerSettings.Android.forceInternetPermission = true;
                PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.fpslan.game");
            }
            else
            {
                PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.iOS, "com.fpslan.game");
            }
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.iOS) return;

            var plistPath = Path.Combine(report.summary.outputPath, "Info.plist");
            if (!File.Exists(plistPath))
                throw new BuildFailedException("iOS Info.plist não foi encontrado em " + plistPath);

            var plist = File.ReadAllText(plistPath);
            const string key = "<key>NSLocalNetworkUsageDescription</key>";
            if (plist.Contains(key)) return;

            var close = plist.LastIndexOf("</dict>");
            if (close < 0)
                throw new BuildFailedException("O Info.plist gerado não contém o dicionário principal.");

            var entry = key + "<string>A FPS-LAN usa a rede local para ligar jogadores na mesma rede Wi-Fi.</string>";
            File.WriteAllText(plistPath, plist.Insert(close, entry));
        }
    }
}

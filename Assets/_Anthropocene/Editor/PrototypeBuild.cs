using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace Anthropocene.Editor
{
    public static class PrototypeBuild
    {
        [MenuItem("Anthropocene/Build Windows prototype")]
        public static void Windows()
        {
            var args = Environment.GetCommandLineArgs();
            int outputOption = Array.IndexOf(args, "--anthropocene-build-output");
            if (outputOption >= 0 && (outputOption + 1 >= args.Length || args[outputOption + 1].StartsWith("-")))
                throw new ArgumentException("--anthropocene-build-output requires an output directory.");
            string outputDirectory = outputOption < 0 ? "Builds/Windows" : args[outputOption + 1];
            Directory.CreateDirectory(outputDirectory);
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 800;
            PlayerSettings.fullScreenMode = UnityEngine.FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/SampleScene.unity" },
                locationPathName = Path.Combine(outputDirectory, "Anthropocene.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });
            if (result.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Build failed: " + result.summary.result);
        }
    }
}

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
            Directory.CreateDirectory("Builds/Windows");
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 800;
            PlayerSettings.fullScreenMode = UnityEngine.FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/SampleScene.unity" },
                locationPathName = "Builds/Windows/Anthropocene.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });
            if (result.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Build failed: " + result.summary.result);
        }
    }
}

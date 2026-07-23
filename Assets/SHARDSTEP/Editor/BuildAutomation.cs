using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Shardstep.Editor
{
    public static class BuildAutomation
    {
        private const string GeneratedScenePath = "Assets/SHARDSTEP/Generated/Scenes/Bootstrap.unity";

        [MenuItem("SHARDSTEP/Build/WebGL")]
        public static void BuildWebGL()
        {
            EnsureGeneratedScene();

            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL);
            PlayerSettings.companyName = "G925 INTERACTIVE";
            PlayerSettings.productName = "SHARDSTEP";
            PlayerSettings.bundleVersion = "0.5.0-unity-slice";
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.WebGL, ScriptingImplementation.IL2CPP);

            string outputPath = Environment.GetEnvironmentVariable("BUILD_PATH");
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                outputPath = "build/WebGL";
            }

            outputPath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(outputPath);

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = new[] { GeneratedScenePath },
                locationPathName = outputPath,
                target = BuildTarget.WebGL,
                options = BuildOptions.CleanBuildCache
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException($"WebGL build failed: {report.summary.result}, errors={report.summary.totalErrors}");
            }

            Debug.Log($"SHARDSTEP WebGL build complete: {outputPath} ({report.summary.totalSize} bytes)");
        }

        [MenuItem("SHARDSTEP/Generate/Bootstrap Scene")]
        public static void EnsureGeneratedScene()
        {
            string directory = Path.GetDirectoryName(GeneratedScenePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, GeneratedScenePath);
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(GeneratedScenePath, true)
            };

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }
}

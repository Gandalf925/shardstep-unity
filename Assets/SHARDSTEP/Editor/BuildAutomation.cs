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
        private const string WebGLTemplate = "PROJECT:SHARDSTEP";

        [MenuItem("SHARDSTEP/Build/WebGL")]
        public static void BuildWebGL()
        {
            EnsureGeneratedScene();

            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL);
            PlayerSettings.companyName = "G925 INTERACTIVE";
            PlayerSettings.productName = "SHARDSTEP";
            PlayerSettings.bundleVersion = "0.6.0-chronosphere-validation";
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.runInBackground = false;
            PlayerSettings.WebGL.template = WebGLTemplate;

            // Cloudflare Pages rejects any single static asset larger than 25 MiB.
            // Unity's uncompressed WebAssembly output exceeds that limit, so produce
            // native gzip assets and add the response headers expected by Unity.
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = false;
#pragma warning disable CS0618
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.WebGL, ScriptingImplementation.IL2CPP);
#pragma warning restore CS0618

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

            WriteCloudflareHeaders(outputPath);
            ValidateCloudflareAssetSizes(outputPath);

            Debug.Log($"SHARDSTEP WebGL build complete: {outputPath} ({report.summary.totalSize} bytes)");
        }

        private static void WriteCloudflareHeaders(string outputPath)
        {
            string headers =
                "/Build/WebGL.wasm.gz\n" +
                "  Content-Type: application/wasm\n" +
                "  Content-Encoding: gzip\n\n" +
                "/Build/WebGL.framework.js.gz\n" +
                "  Content-Type: application/javascript\n" +
                "  Content-Encoding: gzip\n\n" +
                "/Build/WebGL.data.gz\n" +
                "  Content-Type: application/octet-stream\n" +
                "  Content-Encoding: gzip\n\n" +
                "/Build/*\n" +
                "  Cache-Control: public, max-age=31536000, immutable\n";

            File.WriteAllText(Path.Combine(outputPath, "_headers"), headers);
        }

        private static void ValidateCloudflareAssetSizes(string outputPath)
        {
            const long cloudflareLimitBytes = 25L * 1024L * 1024L;
            foreach (string file in Directory.GetFiles(outputPath, "*", SearchOption.AllDirectories))
            {
                FileInfo info = new FileInfo(file);
                if (info.Length > cloudflareLimitBytes)
                {
                    throw new InvalidOperationException(
                        $"Cloudflare Pages asset limit exceeded: {info.Name} is {info.Length} bytes; limit is {cloudflareLimitBytes} bytes.");
                }
            }
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

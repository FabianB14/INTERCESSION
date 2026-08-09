using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Session.Editor
{
    /// <summary>
    /// Batch-mode build entry points. Invoked by CI and by the command in CLAUDE.md:
    ///
    ///   Unity -batchmode -quit -projectPath . -executeMethod Session.Editor.BuildPipeline.BuildWindows64 -logFile -
    /// </summary>
    public static class BuildPipeline
    {
        private const string DefaultOutputDirectory = "Builds/Windows64";
        private const string ExecutableName = "Session.exe";

        [MenuItem("Session/Build/Windows 64-bit", priority = 200)]
        public static void BuildWindows64()
        {
            string outputDirectory = ArgumentOrDefault("-outputPath", DefaultOutputDirectory);
            string outputPath = Path.Combine(outputDirectory, ExecutableName);

            Directory.CreateDirectory(outputDirectory);

            var options = new BuildPlayerOptions
            {
                scenes = EnabledScenePaths(),
                locationPathName = outputPath,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None
            };

            if (options.scenes.Length == 0)
            {
                Fail("No scenes are enabled in Build Settings. Nothing to build.");
                return;
            }

            BuildReport report = UnityEditor.BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log(
                    "[Session] Build succeeded: " + outputPath +
                    " (" + summary.totalSize / (1024 * 1024) + " MB, " +
                    summary.totalTime.TotalSeconds.ToString("0") + "s)");
                return;
            }

            Fail("Build " + summary.result + " with " + summary.totalErrors + " error(s).");
        }

        private static string[] EnabledScenePaths()
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            var enabled = new System.Collections.Generic.List<string>(scenes.Length);

            for (int i = 0; i < scenes.Length; i++)
            {
                if (scenes[i].enabled)
                {
                    enabled.Add(scenes[i].path);
                }
            }

            return enabled.ToArray();
        }

        private static string ArgumentOrDefault(string flag, string fallback)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }

            return fallback;
        }

        private static void Fail(string message)
        {
            Debug.LogError("[Session] " + message);

            // Batch mode must exit non-zero or CI will treat a failed build as a pass.
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(1);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GoodEnoughRepairs.EditorTools
{
    /// <summary>
    /// The checks that run before the WebGL build. Plain logic with no scene and no
    /// files, so it can be tested alone. Each problem is one message that names
    /// the field.
    /// </summary>
    public static class WebGlFieldCheck
    {
        /// <summary>The five TextAsset fields of GameDataAsset that are not set.</summary>
        public static List<string> GameDataProblems(GameDataAsset asset)
        {
            List<string> problems = new List<string>();
            if (asset == null)
            {
                problems.Add("GameDataAsset is missing.");
                return problems;
            }
            if (asset.balance == null) problems.Add("GameDataAsset field 'balance' is not set.");
            if (asset.dayBriefs == null) problems.Add("GameDataAsset field 'dayBriefs' is not set.");
            if (asset.mechanicLines == null) problems.Add("GameDataAsset field 'mechanicLines' is not set.");
            if (asset.partCatalog == null) problems.Add("GameDataAsset field 'partCatalog' is not set.");
            if (asset.uiText == null) problems.Add("GameDataAsset field 'uiText' is not set.");
            return problems;
        }

        /// <summary>The StationView check: found in the scene, and its gameData field set.</summary>
        public static List<string> StationProblems(bool stationFound, GameDataAsset stationGameData)
        {
            List<string> problems = new List<string>();
            if (!stationFound)
            {
                problems.Add("StationView was not found in Main.unity.");
            }
            else if (stationGameData == null)
            {
                problems.Add("StationView field 'gameData' is not set in Main.unity.");
            }
            return problems;
        }
    }

    /// <summary>
    /// Builds Main.unity for WebGL with the designer's build profile into Build/WebGL.
    /// Run with: -executeMethod GoodEnoughRepairs.EditorTools.WebGlBuild.Build
    /// Changes no Player Settings and no project settings.
    /// Prints "MADE: <path>" for the built page and one result line when done.
    /// </summary>
    public static class WebGlBuild
    {
        public const string ProfilePath = "Assets/Settings/Build Profiles/Web - Desktop - Release.asset";
        public const string ScenePath = "Assets/Scenes/Main.unity";
        public const string OutputFolder = "Build/WebGL";

        public static void Build()
        {
            List<string> problems = new List<string>();

            // Checks first, so a failed check does not wipe the last good build.
            CheckGameData(problems);
            CheckStation(problems);

            BuildProfile profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(ProfilePath);
            if (profile == null)
            {
                problems.Add("Build profile not found or not loadable: " + ProfilePath);
            }
            else
            {
                CheckScenes(profile, problems);
            }

            if (problems.Count > 0) throw new InvalidOperationException(Join(problems));

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string output = Path.Combine(projectRoot, OutputFolder);
            if (Directory.Exists(output)) Directory.Delete(output, true);
            Directory.CreateDirectory(Path.GetDirectoryName(output));

            BuildPlayerWithProfileOptions options = new BuildPlayerWithProfileOptions
            {
                buildProfile = profile,
                locationPathName = output,
                options = BuildOptions.None
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException("WebGlBuild: build failed with result " + summary.result +
                    " (" + summary.totalErrors + " errors).");
            }

            string index = Path.Combine(output, "index.html");
            if (!File.Exists(index))
            {
                throw new InvalidOperationException("WebGlBuild: index.html is missing in " + output);
            }
            if (Directory.Exists(Path.Combine(output, "StreamingAssets")))
            {
                throw new InvalidOperationException("WebGlBuild: the output has a StreamingAssets folder. The game must not use it.");
            }
            Debug.Log("MADE: " + OutputFolder + "/index.html");
            // No screenshot is taken for a web build: the shared result line says so.
            Debug.Log(ScreenshotDecision.BuildLine(OutputFolder, false, false));
            Debug.Log("WebGlBuild: done. Output: " + output);
        }

        private static string Join(List<string> problems)
        {
            return "WebGlBuild failed:\n - " + string.Join("\n - ", problems);
        }

        private static void CheckGameData(List<string> problems)
        {
            string[] guids = AssetDatabase.FindAssets("t:GameDataAsset");
            if (guids.Length == 0)
            {
                problems.Add("No GameDataAsset found in the project. Create one (Assets > Create > Good Enough Repairs > Game Data).");
                return;
            }
            if (guids.Length > 1)
            {
                problems.Add("More than one GameDataAsset found in the project. Keep only one.");
                return;
            }
            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            GameDataAsset asset = AssetDatabase.LoadAssetAtPath<GameDataAsset>(path);
            problems.AddRange(WebGlFieldCheck.GameDataProblems(asset));
        }

        private static void CheckStation(List<string> problems)
        {
            if (!File.Exists(ScenePath))
            {
                problems.Add("Scene not found: " + ScenePath);
                return;
            }

            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool wasOpen = scene.IsValid() && scene.isLoaded;
            if (!wasOpen) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

            try
            {
                StationView station = null;
                GameObject[] roots = scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length && station == null; i++)
                {
                    station = roots[i].GetComponentInChildren<StationView>(true);
                }

                GameDataAsset data = null;
                if (station != null)
                {
                    SerializedObject so = new SerializedObject(station);
                    SerializedProperty p = so.FindProperty("gameData");
                    if (p != null) data = p.objectReferenceValue as GameDataAsset;
                }
                problems.AddRange(WebGlFieldCheck.StationProblems(station != null, data));
            }
            finally
            {
                if (!wasOpen) EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void CheckScenes(BuildProfile profile, List<string> problems)
        {
            EditorBuildSettingsScene[] scenes = profile.scenes;
            if (scenes == null || scenes.Length == 0) scenes = EditorBuildSettings.scenes;

            bool hasMain = false;
            for (int i = 0; i < scenes.Length; i++)
            {
                if (!scenes[i].enabled) continue;
                if (scenes[i].path == ScenePath) hasMain = true;
                else problems.Add("The build profile has another enabled scene: " + scenes[i].path + ". Only Main.unity may be built.");
            }
            if (!hasMain) problems.Add("Main.unity is not in the build profile's scene list: " + ScenePath);
        }
    }
}

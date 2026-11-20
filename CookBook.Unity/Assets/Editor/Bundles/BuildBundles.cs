using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CookBook.Editor
{
    // validates the UI prefabs and builds the bundle from the command line
    // Unity.exe -batchmode -quit -projectPath . -executeMethod CookBook.Editor.BuildBundles.Run [-output <dir>]
    internal static class BuildBundles
    {
        //------------------------------- LifeCycle -------------------------------
        public static void Run()
        {
            string output = GetArg("-output") ?? "AssetBundles";

            if (UIBundleInfo.Prefabs.Length == 0)
            {
                Debug.Log("[BuildBundles] no prefabs listed, nothing to build");
                EditorApplication.Exit(0);
                return;
            }

            var errors = new List<string>();
            var paths = new List<string>();
            foreach (var (name, view) in UIBundleInfo.Prefabs)
            {
                string path = $"{UIBundleInfo.PrefabFolder}/{name}.prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                foreach (var err in ViewValidator.Validate(prefab, view)) errors.Add($"{name}: {err}");
                paths.Add(path);
            }

            // a bad prefab would only fall back in game, fail here instead
            if (errors.Count > 0)
            {
                Debug.LogError("[BuildBundles] invalid prefabs\n  " + string.Join("\n  ", errors));
                EditorApplication.Exit(1);
                return;
            }

            Directory.CreateDirectory(output);
            var build = new AssetBundleBuild { assetBundleName = UIBundleInfo.FileName, assetNames = paths.ToArray() };
            var manifest = BuildPipeline.BuildAssetBundles(output, new[] { build }, BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);

            Debug.Log(manifest ? $"[BuildBundles] built {paths.Count} prefabs to {output}" : "[BuildBundles] build failed");
            EditorApplication.Exit(manifest ? 0 : 1);
        }

        //------------------------------- Helpers -------------------------------
        private static string GetArg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}

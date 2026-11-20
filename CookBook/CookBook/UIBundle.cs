using BepInEx.Logging;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace CookBook
{
    // loads the prefab bundle, the panel needs every prefab or it isn't built
    internal static class UIBundle
    {
        private static readonly Dictionary<string, GameObject> _prefabs = new Dictionary<string, GameObject>();
        private static AssetBundleCreateRequest _bundleLoad;
        private static AssetBundleRequest _assetLoad;
        private static AssetBundle _bundle;
        private static ManualLogSource _log;
        private static Stopwatch _sw;
        private static bool _done;

        //------------------------------- LifeCycle -------------------------------
        internal static void Init(ManualLogSource log, string pluginDir)
        {
            _log = log;
            string path = Path.Combine(pluginDir, UIBundleInfo.FileName);
            if (!File.Exists(path))
            {
                _done = true;
                _log.LogError($"UIBundle: {path} not found, CookBook panel disabled.");
                return;
            }

            _sw = Stopwatch.StartNew();
            _bundleLoad = AssetBundle.LoadFromFileAsync(path);
            _bundleLoad.completed += _ => LoadAssets();
        }

        internal static void Shutdown()
        {
            _prefabs.Clear();
            if (_bundle) _bundle.Unload(true);
            _bundle = null;
            _bundleLoad = null;
            _assetLoad = null;
        }

        //------------------------------- Lookup -------------------------------
        // first open can beat the async load, finishing it here blocks once
        internal static bool EnsureLoaded()
        {
            if (!_done)
            {
                LoadAssets();
                Finish();
            }
            return _prefabs.Count == UIBundleInfo.Prefabs.Length;
        }

        internal static GameObject GetPrefab(string name)
        {
            return _prefabs[name];
        }

        //------------------------------- Event Logic -------------------------------
        private static void LoadAssets()
        {
            if (_done || _assetLoad != null) return;

            // blocks if the bundle is still loading
            _bundle = _bundleLoad.assetBundle;
            if (!_bundle)
            {
                _done = true;
                _log.LogError("UIBundle: failed to load bundle, CookBook panel disabled.");
                return;
            }

            _assetLoad = _bundle.LoadAllAssetsAsync<GameObject>();
            _assetLoad.completed += _ => Finish();
        }

        private static void Finish()
        {
            if (_done) return;
            _done = true;

            // blocks if the assets are still loading
            var assets = _assetLoad.allAssets;
            foreach (var (name, view) in UIBundleInfo.Prefabs)
            {
                var prefab = FindPrefab(assets, name);
                if (!prefab)
                {
                    _log.LogError($"UIBundle: {name} missing.");
                    continue;
                }

                var errors = ViewValidator.Validate(prefab, view);
                if (errors.Count == 0) _prefabs[name] = prefab;
                else _log.LogError($"UIBundle: {name} invalid.\n  " + string.Join("\n  ", errors));
            }

            _sw.Stop();
            _log.LogInfo($"UIBundle: {_prefabs.Count}/{UIBundleInfo.Prefabs.Length} prefabs loaded in {_sw.ElapsedMilliseconds}ms");
            if (_prefabs.Count < UIBundleInfo.Prefabs.Length) _log.LogError("UIBundle: incomplete, CookBook panel disabled.");
        }

        //------------------------------- Helpers -------------------------------
        private static GameObject FindPrefab(Object[] assets, string name)
        {
            foreach (var asset in assets)
            {
                if (asset is GameObject go && go.name == name) return go;
            }
            return null;
        }
    }
}

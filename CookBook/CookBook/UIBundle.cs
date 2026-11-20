using BepInEx.Logging;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace CookBook
{
    // loads the prefab bundle, code templates stand in for anything missing or broken
    internal static class UIBundle
    {
        private static readonly Dictionary<string, GameObject> _prefabs = new Dictionary<string, GameObject>();
        private static AssetBundle _bundle;
        private static ManualLogSource _log;

        //------------------------------- LifeCycle -------------------------------
        internal static void Init(ManualLogSource log, string pluginDir)
        {
            _log = log;
            string path = Path.Combine(pluginDir, UIBundleInfo.FileName);
            if (!File.Exists(path))
            {
                _log.LogInfo("UIBundle: no bundle, using code templates.");
                return;
            }

            var sw = Stopwatch.StartNew();
            AssetBundle.LoadFromFileAsync(path).completed += op =>
            {
                _bundle = ((AssetBundleCreateRequest)op).assetBundle;
                if (!_bundle)
                {
                    _log.LogError($"UIBundle: failed to load {path}, using code templates.");
                    return;
                }

                _bundle.LoadAllAssetsAsync<GameObject>().completed += load => OnLoaded((AssetBundleRequest)load, sw);
            };
        }

        internal static void Shutdown()
        {
            _prefabs.Clear();
            if (_bundle) _bundle.Unload(true);
            _bundle = null;
        }

        //------------------------------- Lookup -------------------------------
        internal static bool TryGetPrefab(string name, out GameObject prefab)
        {
            return _prefabs.TryGetValue(name, out prefab);
        }

        //------------------------------- Event Logic -------------------------------
        private static void OnLoaded(AssetBundleRequest request, Stopwatch sw)
        {
            foreach (var (name, view) in UIBundleInfo.Prefabs)
            {
                var prefab = FindPrefab(request.allAssets, name);
                if (!prefab)
                {
                    _log.LogWarning($"UIBundle: {name} missing, using code template.");
                    continue;
                }

                // a broken prefab falls back alone, the rest still load
                var errors = ViewValidator.Validate(prefab, view);
                if (errors.Count == 0) _prefabs[name] = prefab;
                else _log.LogError($"UIBundle: {name} invalid, using code template.\n  " + string.Join("\n  ", errors));
            }

            sw.Stop();
            _log.LogInfo($"UIBundle: {_prefabs.Count}/{UIBundleInfo.Prefabs.Length} prefabs loaded in {sw.ElapsedMilliseconds}ms");
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

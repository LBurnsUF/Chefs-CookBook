using System;
using ThunderKit.Core.Data;
using UnityEditor;
using UnityEngine;

namespace CookBook.Editor
{
    // runs the ThunderKit game import from the command line
    // Unity.exe -batchmode -projectPath . -executeMethod CookBook.Editor.HeadlessImport.Run -gamePath "<ror2 dir>" [-from <step>]
    [InitializeOnLoad]
    internal static class HeadlessImport
    {
        private const string PathKey = "CookBook.HeadlessImport.Path";
        private const string FromKey = "CookBook.HeadlessImport.From";
        private const string StartedKey = "CookBook.HeadlessImport.Started";
        private const string Exe = "Risk of Rain 2.exe";

        //------------------------------- LifeCycle -------------------------------
        static HeadlessImport()
        {
            // import reloads the domain, rehook each time
            if (!string.IsNullOrEmpty(SessionState.GetString(PathKey, ""))) EditorApplication.update += Watch;
        }

        public static void Run()
        {
            string gamePath = GetArg("-gamePath");
            if (string.IsNullOrEmpty(gamePath))
            {
                Debug.LogError("[HeadlessImport] missing -gamePath");
                EditorApplication.Exit(1);
                return;
            }

            int.TryParse(GetArg("-from"), out int from);
            SessionState.SetString(PathKey, gamePath);
            SessionState.SetInt(FromKey, from);
            EditorApplication.update += Watch;
        }

        //------------------------------- Event Logic -------------------------------
        private static void Watch()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;

            // settings get recreated mid import, keep the path set
            var tk = ThunderKitSetting.GetOrCreateSettings<ThunderKitSettings>();
            string gamePath = SessionState.GetString(PathKey, "");
            if (tk.GamePath != gamePath || tk.GameExecutable != Exe)
            {
                tk.GamePath = gamePath;
                tk.GameExecutable = Exe;
                EditorUtility.SetDirty(tk);
                AssetDatabase.SaveAssets();
            }

            var cfg = ThunderKitSetting.GetOrCreateSettings<ImportConfiguration>();
            int count = cfg.ConfigurationExecutors?.Length ?? 0;

            // executors link a few frames after load
            if (count == 0) return;

            if (!SessionState.GetBool(StartedKey, false))
            {
                SessionState.SetBool(StartedKey, true);
                cfg.ConfigurationIndex = SessionState.GetInt(FromKey, 0);
                Debug.Log($"[HeadlessImport] start at {cfg.ConfigurationIndex} of {count}");
                return;
            }

            if (cfg.ConfigurationIndex < 0) Finish(1, "failed");
            else if (cfg.ConfigurationIndex >= count) Finish(0, "done");
        }

        //------------------------------- Helpers -------------------------------
        private static void Finish(int code, string msg)
        {
            EditorApplication.update -= Watch;
            SessionState.EraseString(PathKey);
            SessionState.EraseInt(FromKey);
            SessionState.EraseBool(StartedKey);
            Debug.Log($"[HeadlessImport] {msg}");
            EditorApplication.Exit(code);
        }

        private static string GetArg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}

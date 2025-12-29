using BepInEx.Logging;
using RoR2;
using System;
using System.Linq;
using UnityEngine;

namespace CookBook
{
    internal static class ChatNetworkHandler
    {
        private const string NetPrefix = "CB_NET:";
        private static ManualLogSource _log;
        private static bool _enabled;

        internal static event Action<NetworkUser, string, int, int> OnIncomingObjective;
        internal static void Init(ManualLogSource log)
        {
            _log = log;
        }

        internal static void Enable()
        {
            if (_enabled) return;
            _enabled = true;
            On.RoR2.Chat.AddMessage_string += OnAddMessage;
            _log.LogInfo("ChatNetworkHandler: Enabled (Listening for hidden objectives)");
        }

        internal static void Disable()
        {
            if (!_enabled) return;
            _enabled = false;
            On.RoR2.Chat.AddMessage_string -= OnAddMessage;
            _log.LogInfo("ChatNetworkHandler: Disabled");
        }

        public static void SendObjectiveRequest(NetworkUser target, string command, int unifiedIdx, int quantity)
        {
            if (!_enabled || target == null) return;

            var localUser = LocalUserManager.GetFirstLocalUser()?.currentNetworkUser;
            if (!localUser) return;

            string packet = $"{NetPrefix}{localUser.netId.Value}:{target.netId.Value}:{command}:{unifiedIdx}:{quantity}";

            _log.LogDebug($"[Net Out] Sending packet to {target.userName}: {packet}");

            RoR2.Console.instance.SubmitCmd(localUser, $"say \"{packet}\"");
        }

        private static void OnAddMessage(On.RoR2.Chat.orig_AddMessage_string orig, string message)
        {
            if (_enabled && message.Contains(NetPrefix))
            {
                int index = message.IndexOf(NetPrefix);
                ParseAndProcess(message.Substring(index + NetPrefix.Length));

                return;
            }
            orig(message);
        }

        private static void ParseAndProcess(string data)
        {
            try
            {
                string cleanData = data;
                int closingTag = data.IndexOf('<');
                if (closingTag != -1) cleanData = data.Substring(0, closingTag);

                string[] parts = cleanData.Split(':');
                if (parts.Length < 5) return;

                if (!uint.TryParse(parts[0], out uint senderID)) return;
                if (!uint.TryParse(parts[1], out uint targetID)) return;

                string cmd = parts[2].ToUpper();

                if (!int.TryParse(parts[3], out int idx)) return;
                if (!int.TryParse(parts[4], out int qty)) return;

                var localUser = LocalUserManager.GetFirstLocalUser()?.currentNetworkUser;
                if (localUser != null && localUser.netId.Value == targetID)
                {
                    var senderUser = NetworkUser.readOnlyInstancesList
                        .FirstOrDefault(u => u.netId.Value == senderID);

                    _log.LogDebug($"[Net In] Valid packet from {senderUser?.userName ?? "Unknown"}: {cmd}");
                    OnIncomingObjective?.Invoke(senderUser, cmd, idx, qty);
                }
            }
            catch (Exception e) { _log.LogError($"[Net] Parse Error: {e.Message} | Raw Data: {data}"); }
        }
    }
}
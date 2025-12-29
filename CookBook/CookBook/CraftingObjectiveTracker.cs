using RoR2;
using RoR2.UI;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CookBook
{
    internal static class CraftingObjectiveTracker
    {
        private static readonly List<ObjectiveToken> _activeObjectives = new List<ObjectiveToken>();

        public class ObjectiveToken : ScriptableObject
        {
            public string RawText;
            public uint SenderNetID;
            public int TrackedItemIdx; // Track exactly which item this request is for
            private GameObject _inputWatcher;

            public void Init(string text, uint senderID = 0, int itemIdx = -1)
            {
                RawText = text;
                SenderNetID = senderID;
                TrackedItemIdx = itemIdx; // Store the index for surgical clearing

                _inputWatcher = new GameObject("CookBook_ObjectiveWatcher");
                var watcher = _inputWatcher.AddComponent<CancelWatcher>();
                watcher.TargetToken = this;
            }

            public void UpdateText(string text) => RawText = text;

            public void Complete()
            {
                if (_inputWatcher) Destroy(_inputWatcher);
                CraftingObjectiveTracker.RemoveToken(this);
                Destroy(this);
            }

            private class CancelWatcher : MonoBehaviour
            {
                public ObjectiveToken TargetToken;
                private float _holdTimer = 0f;
                private const float THRESHOLD = 0.6f;

                private void Update()
                {
                    if (CookBook.AbortKey.Value.IsPressed())
                    {
                        _holdTimer += Time.deltaTime;
                        if (_holdTimer >= THRESHOLD) TargetToken.Complete();
                    }
                    else
                    {
                        _holdTimer = 0f;
                    }
                }
            }
        }

        internal static void Init()
        {
            ObjectivePanelController.collectObjectiveSources += OnCollectObjectiveSources;
        }

        internal static void Cleanup()
        {
            ObjectivePanelController.collectObjectiveSources -= OnCollectObjectiveSources;
            ClearAllObjectives();
        }

        internal static bool HasActiveObjectives() => _activeObjectives.Count > 0;

        internal static ObjectiveToken CreateObjective(string message, uint senderID = 0, int itemIdx = -1)
        {
            var token = ScriptableObject.CreateInstance<ObjectiveToken>();
            token.Init(message, senderID, itemIdx);
            _activeObjectives.Add(token);
            return token;
        }

        internal static void ClearSpecificRequest(uint senderID, int itemIdx)
        {
            for (int i = _activeObjectives.Count - 1; i >= 0; i--)
            {
                var token = _activeObjectives[i];
                if (token.SenderNetID == senderID && token.TrackedItemIdx == itemIdx)
                {
                    token.Complete();
                }
            }
        }

        internal static void ClearObjectivesFromSender(uint senderID)
        {
            for (int i = _activeObjectives.Count - 1; i >= 0; i--)
            {
                if (_activeObjectives[i].SenderNetID == senderID)
                {
                    _activeObjectives[i].Complete();
                }
            }
        }

        internal static void AddAlliedRequest(NetworkUser sender, string command, int unifiedIdx, int quantity)
        {
            if (sender == null) return;

            string senderName = sender.userName;
            string itemName = GetItemName(unifiedIdx);
            string message = string.Empty;

            if (command == "TRADE")
            {
                message = $"<style=cIsUtility>{senderName} needs:</style> {quantity}x {itemName}";
            }
            else if (command == "SCRAP")
            {
                message = $"<style=cIsUtility>{senderName} needs:</style> {itemName} <style=cStack>(Scrap a Drone)</style>";
            }

            if (!string.IsNullOrEmpty(message))
            {
                CreateObjective(message, sender.netId.Value, unifiedIdx);
            }
        }

        private static void RemoveToken(ObjectiveToken token)
        {
            if (_activeObjectives.Contains(token)) _activeObjectives.Remove(token);
        }

        private static void OnCollectObjectiveSources(CharacterMaster viewerMaster, List<ObjectivePanelController.ObjectiveSourceDescriptor> output)
        {
            if (viewerMaster != LocalUserManager.GetFirstLocalUser()?.cachedMaster) return;

            for (int i = _activeObjectives.Count - 1; i >= 0; i--)
            {
                var token = _activeObjectives[i];
                if (!token)
                {
                    _activeObjectives.RemoveAt(i);
                    continue;
                }

                output.Add(new ObjectivePanelController.ObjectiveSourceDescriptor
                {
                    source = token,
                    master = viewerMaster,
                    objectiveType = typeof(ChefObjectiveTracker)
                });
            }
        }

        internal static void ClearAllObjectives()
        {
            for (int i = _activeObjectives.Count - 1; i >= 0; i--)
            {
                if (_activeObjectives[i]) _activeObjectives[i].Complete();
            }
            _activeObjectives.Clear();
        }

        private static string GetItemName(int unifiedIdx)
        {
            int itemLen = ItemCatalog.itemCount;
            if (unifiedIdx < itemLen)
            {
                var def = ItemCatalog.GetItemDef((ItemIndex)unifiedIdx);
                return def ? Language.GetString(def.nameToken) : "Unknown Item";
            }
            var eqDef = EquipmentCatalog.GetEquipmentDef((EquipmentIndex)(unifiedIdx - itemLen));
            return eqDef ? Language.GetString(eqDef.nameToken) : "Unknown Equipment";
        }

        private class ChefObjectiveTracker : ObjectivePanelController.ObjectiveTracker
        {
            private ObjectiveToken MyToken => sourceDescriptor.source as ObjectiveToken;

            protected override bool IsDirty() => MyToken != null && cachedString != GenerateString();

            protected override string GenerateString()
            {
                if (!MyToken) return string.Empty;
                string keyName = CookBook.AbortKey.Value.MainKey.ToString();
                return $"{MyToken.RawText} <style=cSub>(Hold {keyName} to Cancel)</style>";
            }
        }
    }
}
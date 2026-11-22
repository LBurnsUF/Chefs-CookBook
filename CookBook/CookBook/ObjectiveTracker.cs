using RoR2;
using RoR2.UI;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using static CookBook.CraftPlanner;

namespace CookBook
{
    internal static class ObjectiveTracker
    {
        private static readonly List<ObjectiveToken> _activeObjectives = new List<ObjectiveToken>();
        // own pins, by result
        private static readonly Dictionary<int, ObjectiveToken> _pins = new();

        public class ObjectiveToken : ScriptableObject
        {
            public string RawText;
            public uint SenderNetID;
            public int TrackedItemIdx;
            public ChefRecipe TrackedRecipe;
            public int CraftBaseline = -1; // result count when set, a gain means it was crafted
            public bool IsPin;
            private GameObject _inputWatcher;

            public void Init(string text, uint senderID = 0, int itemIdx = -1, bool isPin = false)
            {
                RawText = text;
                SenderNetID = senderID;
                TrackedItemIdx = itemIdx;
                IsPin = isPin;

                // pins come off with unpin, not the abort key
                if (isPin) return;

                _inputWatcher = new GameObject("CookBook_ObjectiveWatcher");
                var watcher = _inputWatcher.AddComponent<CancelWatcher>();
                watcher.TargetToken = this;
            }

            public void UpdateText(string text) => RawText = text;

            public void Complete()
            {
                if (_inputWatcher) Destroy(_inputWatcher);
                RemoveToken(this);
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

        private static readonly FieldInfo StripAnimationsField = typeof(ObjectivePanelController).GetField("objectiveStripAnimations", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly System.Type StripAnimationType = typeof(ObjectivePanelController).GetNestedType("ObjectiveStripAnimation", BindingFlags.NonPublic);
        private static readonly FieldInfo AnimTrackerField = StripAnimationType.GetField("objectiveTracker");
        private static readonly FieldInfo AnimPercentField = StripAnimationType.GetField("percentComplete");

        internal static void Init()
        {
            ObjectivePanelController.collectObjectiveSources += OnCollectObjectiveSources;
            Inventory.onInventoryChangedGlobal += OnInventoryChanged;
            On.RoR2.UI.ObjectivePanelController.RunObjectiveAnimations += OnRunObjectiveAnimations;
        }

        internal static void Shutdown()
        {
            ObjectivePanelController.collectObjectiveSources -= OnCollectObjectiveSources;
            Inventory.onInventoryChangedGlobal -= OnInventoryChanged;
            On.RoR2.UI.ObjectivePanelController.RunObjectiveAnimations -= OnRunObjectiveAnimations;
            ClearAllObjectives();
            _pins.Clear();
        }

        internal static bool HasActiveObjectives() => _activeObjectives.Count > 0;

        internal static ObjectiveToken CreateObjective(string message, uint senderID = 0, int itemIdx = -1, bool isPin = false)
        {
            var token = ScriptableObject.CreateInstance<ObjectiveToken>();
            token.Init(message, senderID, itemIdx, isPin);
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
                message = $"<style=cIsUtility>{senderName} needs:</style> {quantity}x {itemName} <style=cStack>(Trade an Item)</style>";
            }
            else if (command == "SCRAP")
            {
                message = $"<style=cIsUtility>{senderName} needs:</style> {itemName} <style=cStack>(Scrap a Drone)</style>";
            }
            else if (command == "CRAFT")
            {
                // asked again replaces the old one
                ClearSpecificRequest(sender.netId.Value, unifiedIdx);
                message = $"<style=cIsUtility>{senderName} wants you to craft:</style> {itemName}";
            }

            if (string.IsNullOrEmpty(message)) return;

            var token = CreateObjective(message, sender.netId.Value, unifiedIdx);

            // done once it shows up in your inventory
            if (command == "CRAFT") token.CraftBaseline = GetLocalStacks()?[unifiedIdx] ?? 0;
        }

        internal static bool IsPinned(int resultIdx) => _pins.TryGetValue(resultIdx, out var token) && token;

        // pinned again unpins
        internal static void TogglePin(CraftableEntry entry)
        {
            if (IsPinned(entry.ResultIndex))
            {
                _pins[entry.ResultIndex].Complete();
                _pins.Remove(entry.ResultIndex);
                return;
            }

            var stacks = GetLocalStacks();
            if (stacks == null) return;

            var token = CreateObjective(string.Empty, 0, entry.ResultIndex, isPin: true);
            token.TrackedRecipe = entry.ProgressRecipe;
            token.CraftBaseline = stacks[entry.ResultIndex];
            token.UpdateText(GetPinText(token, stacks));
            _pins[entry.ResultIndex] = token;
        }

        // pins tick toward ready, a gain in the result means it was crafted
        private static void OnInventoryChanged(Inventory inv)
        {
            var master = LocalUserManager.GetFirstLocalUser()?.cachedMaster;
            if (!master || inv != master.inventory) return;

            int[] stacks = null;
            for (int i = _activeObjectives.Count - 1; i >= 0; i--)
            {
                var token = _activeObjectives[i];
                if (!token || token.CraftBaseline < 0) continue;

                stacks ??= InventoryTracker.GetUnifiedStacksFor(inv);
                int count = stacks[token.TrackedItemIdx];

                if (count > token.CraftBaseline) token.Complete();
                else
                {
                    // spent ones lower the bar, the next craft still counts
                    token.CraftBaseline = count;
                    if (token.SenderNetID == 0) token.UpdateText(GetPinText(token, stacks));
                }
            }
        }

        // ready once every ingredient is in hand
        private static string GetPinText(ObjectiveToken token, int[] stacks)
        {
            string name = GetItemName(token.TrackedItemIdx);
            var recipe = token.TrackedRecipe;

            var parts = new List<string>();
            bool ready = true;
            void Need(int idx, int count)
            {
                int have = stacks[idx];
                if (have < count) ready = false;
                parts.Add($"{GetItemName(idx)} {Mathf.Min(have, count)}/{count}");
            }

            if (recipe != null)
            {
                if (recipe.HasB && recipe.IngA == recipe.IngB) Need(recipe.IngA, recipe.CountA + recipe.CountB);
                else
                {
                    Need(recipe.IngA, recipe.CountA);
                    if (recipe.HasB) Need(recipe.IngB, recipe.CountB);
                }
            }

            return ready
                ? $"<style=cIsUtility>Craft:</style> {name} <style=cIsHealing>(Ready)</style>"
                : $"<style=cIsUtility>Craft:</style> {name} <style=cStack>({string.Join(", ", parts)})</style>";
        }

        private static int[] GetLocalStacks()
        {
            var master = LocalUserManager.GetFirstLocalUser()?.cachedMaster;
            return master && master.inventory ? InventoryTracker.GetUnifiedStacksFor(master.inventory) : null;
        }

        // vanilla eases strips over seconds and stalls while paused, pins snap
        private static void OnRunObjectiveAnimations(On.RoR2.UI.ObjectivePanelController.orig_RunObjectiveAnimations orig, ObjectivePanelController self)
        {
            foreach (var anim in (IList)StripAnimationsField.GetValue(self))
            {
                if (AnimTrackerField.GetValue(anim) is ChefObjectiveTracker { Snaps: true }) AnimPercentField.SetValue(anim, 1f);
            }
            orig(self);
        }

        private static void RemoveToken(ObjectiveToken token) { if (_activeObjectives.Contains(token)) _activeObjectives.Remove(token); }

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
            for (int i = _activeObjectives.Count - 1; i >= 0; i--) if (_activeObjectives[i]) _activeObjectives[i].Complete();
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

            internal bool Snaps;

            protected override bool IsDirty() => MyToken != null && cachedString != GenerateString();

            // the token is gone by the time it leaves, so remember now
            protected override void OnStripSet() => Snaps = MyToken && MyToken.IsPin;

            protected override string GenerateString()
            {
                if (!MyToken) return string.Empty;
                if (MyToken.IsPin) return MyToken.RawText;

                string keyName = CookBook.AbortKey.Value.MainKey.ToString();
                return $"{MyToken.RawText} <style=cSub>[Hold {keyName} to Cancel]</style>";
            }
        }
    }
}
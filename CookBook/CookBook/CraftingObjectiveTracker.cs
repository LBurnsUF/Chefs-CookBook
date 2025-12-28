using RoR2;
using RoR2.UI;
using System.Collections.Generic;
using UnityEngine;

namespace CookBook
{
    internal static class CraftingObjectiveTracker
    {
        private static readonly List<ObjectiveToken> _activeObjectives = new List<ObjectiveToken>();

        public class ObjectiveToken : ScriptableObject
        {
            public string RawText;

            public void UpdateText(string text)
            {
                RawText = text;
            }

            public void Complete()
            {
                CraftingObjectiveTracker.RemoveToken(this);
                Destroy(this);
            }
        }

        internal static void Init()
        {
            ObjectivePanelController.collectObjectiveSources += OnCollectObjectiveSources;
        }

        internal static void Cleanup()
        {
            ObjectivePanelController.collectObjectiveSources -= OnCollectObjectiveSources;
            foreach (var token in _activeObjectives)
            {
                if (token) Object.Destroy(token);
            }
            _activeObjectives.Clear();
        }

        internal static ObjectiveToken CreateObjective(string message)
        {
            var token = ScriptableObject.CreateInstance<ObjectiveToken>();
            token.RawText = message;
            _activeObjectives.Add(token);
            return token;
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

        private class ChefObjectiveTracker : ObjectivePanelController.ObjectiveTracker
        {
            private ObjectiveToken MyToken => sourceDescriptor.source as ObjectiveToken;

            protected override bool IsDirty()
            {
                return MyToken != null && cachedString != GenerateString();
            }

            protected override string GenerateString()
            {
                if (!MyToken) return string.Empty;

                string keyName = CookBook.AbortKey.Value.MainKey.ToString();
                return $"{MyToken.RawText} <style=cSub>(Hold {keyName} to Cancel)</style>";
            }
        }
    }
}
using System.Collections.Generic;
using NUnit.Framework;
using RoR2.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace CookBook.Tests
{
    // Unity.exe -batchmode -projectPath . -runTests -testPlatform EditMode -testResults <xml>
    public class ViewValidatorTests
    {
        private readonly List<GameObject> _made = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _made) Object.DestroyImmediate(go);
            _made.Clear();
        }

        //------------------------------- Validator -------------------------------
        [Test]
        public void UnsetRefIsReported()
        {
            var root = Make("Slot");
            root.AddComponent<SlotView>();

            var errors = ViewValidator.Validate(root, typeof(SlotView));

            Assert.That(errors, Has.Some.Contains("SlotView.Icon not set"));
            Assert.That(errors, Has.Some.Contains("SlotView.IconLoader not set"));
            Assert.That(errors, Has.Some.Contains("SlotView.StackText not set"));
        }

        [Test]
        public void WiredViewPasses()
        {
            var root = Make("Slot");
            var view = root.AddComponent<SlotView>();
            view.Icon = MakeChild(root, "Icon").AddComponent<Image>();
            view.IconLoader = view.Icon.gameObject.AddComponent<AsyncImage>();
            view.StackText = MakeChild(root, "Stack").AddComponent<TextMeshProUGUI>();

            Assert.That(ViewValidator.Validate(root, typeof(SlotView)), Is.Empty);
        }

        [Test]
        public void MissingRootViewIsReported()
        {
            var root = Make("Slot");

            Assert.That(ViewValidator.Validate(root, typeof(SlotView)), Has.Some.Contains("no SlotView on root"));
        }

        [Test]
        public void NestedViewIsChecked()
        {
            var root = Make("Row");
            MakeChild(root, "Slot").AddComponent<SlotView>();

            Assert.That(ViewValidator.Validate(root, null), Has.Some.StartsWith("Row/Slot: SlotView.Icon"));
        }

        [Test]
        public void OffUILayerIsReported()
        {
            var root = Make("Bar");
            root.layer = 0;

            Assert.That(ViewValidator.Validate(root, null), Has.Some.Contains("Bar: not on the UI layer"));
        }

        //------------------------------- Bundle -------------------------------
        [Test]
        public void BundlePrefabsValidate()
        {
            var errors = new List<string>();
            foreach (var (name, view) in UIBundleInfo.Prefabs)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{UIBundleInfo.PrefabFolder}/{name}.prefab");
                foreach (var err in ViewValidator.Validate(prefab, view)) errors.Add($"{name}: {err}");
            }

            Assert.That(errors, Is.Empty);
        }

        //------------------------------- Helpers -------------------------------
        private GameObject Make(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            _made.Add(go);
            return go;
        }

        private static GameObject MakeChild(GameObject parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent.transform, false);
            return go;
        }
    }
}

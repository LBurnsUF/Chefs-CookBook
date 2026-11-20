using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace CookBook
{
    // checks a view prefab before we trust it
    public static class ViewValidator
    {
        //------------------------------- Validate -------------------------------
        public static List<string> Validate(GameObject root, Type rootView)
        {
            var errors = new List<string>();
            if (!root)
            {
                errors.Add("prefab is null");
                return errors;
            }

            if (rootView != null && !root.GetComponent(rootView)) errors.Add($"{root.name}: no {rootView.Name} on root");

            foreach (var tr in root.GetComponentsInChildren<Transform>(true))
            {
                string path = PathOf(tr, root.transform);

                foreach (var comp in tr.GetComponents<Component>())
                {
                    // missing scripts load as null components
                    if (comp == null)
                    {
                        errors.Add($"{path}: missing script");
                        continue;
                    }

                    // only our views carry refs we wire
                    if (comp.GetType().Assembly != typeof(ViewValidator).Assembly) continue;
                    CheckRefs(comp, path, errors);
                }
            }

            return errors;
        }

        //------------------------------- Helpers -------------------------------
        private static void CheckRefs(Component comp, string path, List<string> errors)
        {
            foreach (var field in comp.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType)) continue;
                // vanilla base fields are optional extras
                if (field.DeclaringType.Assembly != typeof(ViewValidator).Assembly) continue;
                if (field.IsDefined(typeof(OptionalRefAttribute), false)) continue;

                var value = field.GetValue(comp) as UnityEngine.Object;
                if (!value) errors.Add($"{path}: {comp.GetType().Name}.{field.Name} not set");
            }
        }

        private static string PathOf(Transform tr, Transform root)
        {
            string path = tr.name;
            while (tr != root && tr.parent != null)
            {
                tr = tr.parent;
                path = tr.name + "/" + path;
            }
            return path;
        }
    }
}

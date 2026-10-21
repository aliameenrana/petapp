using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PetApp.Editor
{
    public static partial class SceneSetup
    {
        const string LayoutPath = "Assets/PetApp/Editor/EnvironmentLayout.json";

        [Serializable]
        class LayoutEntry
        {
            public string name;
            public string model;
            public Vector3 position;
            public Vector3 rotation;
            public Vector3 scale;
        }

        [Serializable]
        class Layout
        {
            public List<LayoutEntry> props = new List<LayoutEntry>();
            public List<LayoutEntry> environment = new List<LayoutEntry>();
            public List<LayoutEntry> roots = new List<LayoutEntry>();
        }

        // Records hand edits to the generated scene so the next Set scene up keeps them instead of re-scattering.
        [MenuItem("Tools/Save environment layout")]
        static void SaveLayout()
        {
            var layout = new Layout();
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.name != "Environment")
                {
                    layout.roots.Add(Entry(root.transform, null));
                    continue;
                }
                foreach (Transform child in root.transform)
                {
                    var model = NatureModel(child);
                    (model != null ? layout.props : layout.environment).Add(Entry(child, model));
                }
            }
            File.WriteAllText(LayoutPath, JsonUtility.ToJson(layout, true));
            AssetDatabase.ImportAsset(LayoutPath);

            var counts = layout.props.GroupBy(p => p.model).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} x{g.Count()}");
            Debug.Log($"Saved {layout.props.Count} props, {layout.environment.Count} environment objects and {layout.roots.Count} root objects to {LayoutPath}\n" +
                      $"Props: {string.Join(", ", counts)}\nEnvironment: {string.Join(", ", layout.environment.Select(e => e.name))}");
        }

        static string NatureModel(Transform t)
        {
            if (!PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject)) return null;
            var path = AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject));
            return path.StartsWith(Nature + "/") ? Path.GetFileNameWithoutExtension(path) : null;
        }

        static LayoutEntry Entry(Transform t, string model) => new LayoutEntry
        {
            name = t.name,
            model = model,
            position = t.localPosition,
            rotation = t.localEulerAngles,
            scale = t.localScale
        };

        static void Apply(Transform t, LayoutEntry entry)
        {
            t.localPosition = entry.position;
            t.localEulerAngles = entry.rotation;
            t.localScale = entry.scale;
        }

        static Layout LoadLayout() => File.Exists(LayoutPath) ? JsonUtility.FromJson<Layout>(File.ReadAllText(LayoutPath)) : null;

        static void PlaceProps(Layout layout, Transform parent)
        {
            foreach (var prop in layout.props)
            {
                var go = Place(prop.model, prop.position, 1f, 0f, parent);
                if (go != null) Apply(go.transform, prop);
            }
        }

        // Moves generated objects to their saved spots and removes the ones deleted by hand.
        static void ApplyLayout(Scene scene)
        {
            var layout = LoadLayout();
            if (layout == null) return;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == "Environment")
                {
                    foreach (var child in root.transform.Cast<Transform>().ToList())
                    {
                        if (NatureModel(child) != null) continue;
                        var saved = layout.environment.FirstOrDefault(e => e.name == child.name);
                        if (saved != null) Apply(child, saved);
                        else UnityEngine.Object.DestroyImmediate(child.gameObject);
                    }
                    continue;
                }
                var savedRoot = layout.roots.FirstOrDefault(e => e.name == root.name);
                if (savedRoot != null) Apply(root.transform, savedRoot);
            }
        }
    }
}

/*
 * SPEC LAYOUT SAVER
 *
 * The designer arranges the station in the Unity editor; this tool writes those
 * positions back into the prefab specs, so the next builder run keeps them.
 * It is the reverse of PrefabSpecBuilder for layout values only.
 *
 * HOW TO USE (in the editor, with Assets/Scenes/Main.unity open)
 *   Tools > Good Enough Repairs > Save layout to specs
 *   Tools > Good Enough Repairs > Build prefabs from specs (runs PrefabSpecBuilder.BuildAll)
 *
 * WHAT IT WRITES (only values that differ; the file's own formatting is kept)
 *   Each spec object, found by name under the prefab's instance in the open scene
 *   (hidden objects included), or under the prefab asset when the scene has no
 *   single instance of it:
 *     posX, posY        local position
 *     scaleX, scaleY    local scale
 *     textSize          from the TextMesh character size (the builder's rule reversed)
 *   The spec's "scene" block, for a prefab placed once by world values:
 *     worldX, worldY    the instance's position
 *     worldScale        the instance's scale
 * WHAT IT SKIPS
 *   The root object; posZ and scaleZ; specs placed by screen rectangles ("bays":
 *   the ships), because StationView moves the ships at runtime; object names that
 *   appear twice under one prefab.
 * Every change is listed in the Console, one line per value.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GoodEnoughRepairs.EditorTools
{
    public static class SpecLayoutSaver
    {
        private const string SpecFolder = "Assets/Prefabs/UX";
        private const float DefaultTextSize = 0.3f;  // same as PrefabSpecBuilder
        private const float TextSizeDivisor = 6.4f;  // same as PrefabSpecBuilder
        private const float Tolerance = 0.0005f;

        [MenuItem("Tools/Good Enough Repairs/Save layout to specs")]
        public static void SaveLayoutMenu()
        {
            List<string> changes = SaveLayout();
            string text = changes.Count == 0
                ? "The specs already match the layout. Nothing was changed."
                : changes.Count + " value(s) written to the specs. The list is in the Console.\n\n" +
                  "Run Build prefabs from specs next, then commit the specs, prefabs and scene.";
            EditorUtility.DisplayDialog("Save layout to specs", text, "OK");
        }

        [MenuItem("Tools/Good Enough Repairs/Build prefabs from specs")]
        public static void BuildMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            PrefabSpecBuilder.BuildAll();
            Debug.Log("Build prefabs from specs: done.");
        }

        /// <summary>Writes the layout of the open scene into the specs. Returns one line per changed value.</summary>
        public static List<string> SaveLayout()
        {
            var changes = new List<string>();
            string[] files = Directory.GetFiles(SpecFolder, "*.spec.json", SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.Ordinal);
            foreach (string raw in files)
            {
                string path = raw.Replace('\\', '/');
                string text = File.ReadAllText(path);
                string updated = SaveSpec(path, text, changes);
                if (updated != text) File.WriteAllText(path, updated);
            }
            AssetDatabase.Refresh();
            foreach (string line in changes) Debug.Log("Save layout to specs: " + line);
            return changes;
        }

        private static string SaveSpec(string path, string text, List<string> changes)
        {
            string prefabPath = StringValue(text, "prefabPath");
            GameObject prefab = prefabPath == null ? null : AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) return text;
            string file = Path.GetFileName(path);

            List<GameObject> instances = InstancesOf(prefabPath);
            GameObject source = instances.Count == 1 ? instances[0] : prefab;

            // Spec objects: each { "name": ... } in the "objects" array.
            int objectsAt = text.IndexOf("\"objects\"", StringComparison.Ordinal);
            int objectsEnd = objectsAt < 0 ? -1 : ArrayEnd(text, text.IndexOf('[', objectsAt));
            if (objectsAt >= 0 && objectsEnd > objectsAt)
            {
                Dictionary<string, Transform> byName = ChildrenByName(source.transform, out HashSet<string> twice);
                var names = new List<string>();
                foreach (Match m in Regex.Matches(text.Substring(objectsAt, objectsEnd - objectsAt),
                             "\"name\"\\s*:\\s*\"([^\"]+)\""))
                {
                    names.Add(m.Groups[1].Value);
                }
                foreach (string name in names)
                {
                    if (twice.Contains(name) || !byName.TryGetValue(name, out Transform t)) continue;
                    // Found again each time: an earlier edit moves the text after it.
                    Match at = new Regex("\"name\"\\s*:\\s*\"" + Regex.Escape(name) + "\"").Match(text, objectsAt);
                    if (!at.Success) continue;
                    int open = text.LastIndexOf('{', at.Index);
                    int close = ObjectEnd(text, open);
                    if (open < 0 || close < 0) continue;
                    string obj = text.Substring(open, close - open + 1);
                    string before = obj;
                    obj = SetNumber(obj, "posX", t.localPosition.x, 0f, file + " " + name, changes);
                    obj = SetNumber(obj, "posY", t.localPosition.y, 0f, file + " " + name, changes);
                    obj = SetNumber(obj, "scaleX", t.localScale.x, 1f, file + " " + name, changes);
                    obj = SetNumber(obj, "scaleY", t.localScale.y, 1f, file + " " + name, changes);
                    TextMesh tm = t.GetComponent<TextMesh>();
                    if (tm != null)
                    {
                        obj = SetNumber(obj, "textSize", tm.characterSize * TextSizeDivisor, DefaultTextSize,
                            file + " " + name, changes);
                    }
                    if (obj != before) text = text.Substring(0, open) + obj + text.Substring(close + 1);
                }
            }

            // Scene placement by world values (not the ships' screen rectangles).
            int sceneAt = text.IndexOf("\"scene\"", StringComparison.Ordinal);
            if (sceneAt >= 0 && instances.Count == 1)
            {
                int open = text.IndexOf('{', sceneAt);
                int close = ObjectEnd(text, open);
                string block = close < 0 ? null : text.Substring(open, close - open + 1);
                if (block != null && block.IndexOf("\"bays\"", StringComparison.Ordinal) < 0)
                {
                    Transform t = instances[0].transform;
                    string label = file + " scene";
                    string b = SetNumber(block, "worldX", t.position.x, 0f, label, changes);
                    b = SetNumber(b, "worldY", t.position.y, 0f, label, changes);
                    b = SetNumber(b, "worldScale", t.localScale.x, 1f, label, changes);
                    if (b != block) text = text.Substring(0, open) + b + text.Substring(close + 1);
                }
            }
            return text;
        }

        // Outermost instances of the prefab in the open scenes.
        private static List<GameObject> InstancesOf(string prefabPath)
        {
            var found = new List<GameObject>();
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                Scene scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    {
                        GameObject go = t.gameObject;
                        if (PrefabUtility.IsOutermostPrefabInstanceRoot(go) &&
                            PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go) == prefabPath)
                        {
                            found.Add(go);
                        }
                    }
                }
            }
            return found;
        }

        // Every object under the root (hidden ones too) by name; names seen twice are listed apart.
        private static Dictionary<string, Transform> ChildrenByName(Transform root, out HashSet<string> twice)
        {
            var byName = new Dictionary<string, Transform>();
            twice = new HashSet<string>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root) continue;
                if (byName.ContainsKey(t.name)) twice.Add(t.name);
                else byName[t.name] = t;
            }
            return byName;
        }

        // Sets "key": value in one JSON object's text. A missing key is added only when the value is not its default.
        private static string SetNumber(string obj, string key, float value, float defaultValue, string label,
            List<string> changes)
        {
            var re = new Regex("\"" + key + "\"\\s*:\\s*(-?[0-9][0-9.eE+-]*)");
            Match m = re.Match(obj);
            string number = Format(value);
            if (m.Success)
            {
                float old = float.Parse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
                if (Mathf.Abs(old - value) <= Tolerance) return obj;
                changes.Add(label + " " + key + ": " + m.Groups[1].Value + " -> " + number);
                return obj.Substring(0, m.Groups[1].Index) + number +
                       obj.Substring(m.Groups[1].Index + m.Groups[1].Length);
            }
            if (Mathf.Abs(value - defaultValue) <= Tolerance) return obj;
            changes.Add(label + " " + key + ": (" + Format(defaultValue) + ") -> " + number);
            int end = obj.LastIndexOf('}');
            int last = end - 1;
            while (last > 0 && char.IsWhiteSpace(obj[last])) last--;
            string sep = obj[last] == '{' ? " " : ", ";
            return obj.Substring(0, last + 1) + sep + "\"" + key + "\": " + number + " " + obj.Substring(end);
        }

        private static string Format(float value)
        {
            float rounded = (float)Math.Round(value, 3);
            if (rounded == 0f) rounded = 0f;  // no "-0"
            return rounded.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string StringValue(string text, string key)
        {
            Match m = Regex.Match(text, "\"" + key + "\"\\s*:\\s*\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : null;
        }

        // Index of the '}' that closes the object starting at open (strings are skipped).
        private static int ObjectEnd(string text, int open)
        {
            return Closing(text, open, '{', '}');
        }

        // Index of the ']' that closes the array starting at open.
        private static int ArrayEnd(string text, int open)
        {
            return Closing(text, open, '[', ']');
        }

        private static int Closing(string text, int open, char a, char b)
        {
            if (open < 0) return -1;
            int depth = 0;
            bool inString = false;
            for (int i = open; i < text.Length; i++)
            {
                char c = text[i];
                if (inString)
                {
                    if (c == '\\') i++;
                    else if (c == '"') inString = false;
                    continue;
                }
                if (c == '"') inString = true;
                else if (c == a) depth++;
                else if (c == b && --depth == 0) return i;
            }
            return -1;
        }
    }
}

/*
 * BUILDER IN PLACE CHECK
 *
 * Batch-mode check that PrefabSpecBuilder updates an existing prefab in place.
 * It never touches the real specs, prefabs or scenes. It works on a copy of
 * Assets/Prefabs/UX/ship_in_bay.spec.json that is written to a temp folder outside the
 * project, with its own prefab in Assets/GerBuilderCheckTemp (no scene block). Both are
 * deleted at the end.
 *
 * HOW TO RUN
 *   Unity -batchmode -projectPath <project> -quit
 *     -executeMethod GoodEnoughRepairs.EditorTools.BuilderInPlaceCheck.Run
 *
 * STEPS
 *   A. Build the copy (the prefab is made from scratch). Take a snapshot.
 *   B. Change one property (Body scaleX), build again. Snapshot.
 *   C. Remove one object (PartLabelBack_Engine) and add one (InPlaceAdded), build again.
 *      Snapshot.
 *   A snapshot records, for every object of the prefab asset (found by name path):
 *   the GlobalObjectId of the object and of each component, and the value of every
 *   serialized property of each component (object references as name paths).
 *
 * OUTPUT
 *   TEMPDIR: <folder of the temp spec>
 *   RULE <name>: <measured> expected <expected> PASS|FAIL
 *   RESULT: PASS or RESULT: FAIL (last line)
 * Exit code 0 on PASS, 1 on FAIL or an error.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GoodEnoughRepairs.EditorTools
{
    public static class BuilderInPlaceCheck
    {
        private const string SpecPath = "Assets/Prefabs/UX/ship_in_bay.spec.json";
        private const string TempFolder = "Assets/GerBuilderCheckTemp";
        private const string TempPrefab = TempFolder + "/InPlaceCheck.prefab";
        private const string ChangedObject = "Body";
        private const string ChangedKey = "Body|Transform#0|m_LocalScale";
        private const string RemovedObject = "PartLabelBack_Engine";
        private const string AddedObject = "InPlaceAdded";
        private const string RootLabel = "<root>";
        private const float ScaleStep = 0.5f;

        private static bool failed;

        private sealed class Snapshot
        {
            public readonly List<string> Paths = new List<string>();
            public readonly Dictionary<string, string> Ids = new Dictionary<string, string>();
            public readonly Dictionary<string, string> Props = new Dictionary<string, string>();
            public string RootId;
            public string Guid;
        }

        /// <summary>Batch entry method.</summary>
        public static void Run()
        {
            failed = false;
            string tempDir = Path.Combine(Path.GetTempPath(), "ger_inplace_" + Guid.NewGuid().ToString("N"));
            byte[] realBefore = null;
            try
            {
                Directory.CreateDirectory(tempDir);
                Debug.Log("TEMPDIR: " + tempDir);
                realBefore = File.ReadAllBytes(SpecPath);
                Check(tempDir);
            }
            catch (Exception e)
            {
                Line("check ran without error", e.Message.Replace('\n', ' '), "no error", false);
            }

            try
            {
                CleanAssets();
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
            catch (Exception e)
            {
                Line("cleanup ran without error", e.Message.Replace('\n', ' '), "no error", false);
            }

            bool same = false;
            try
            {
                byte[] after = File.ReadAllBytes(SpecPath);
                same = realBefore != null && after.Length == realBefore.Length;
                for (int i = 0; same && i < after.Length; i++) same = after[i] == realBefore[i];
            }
            catch (Exception) { same = false; }
            Line("real spec unchanged", same ? "same bytes" : "changed or unreadable", "same bytes", same);

            bool assetGone = !AssetDatabase.IsValidFolder(TempFolder) && !File.Exists(TempPrefab) &&
                !Directory.Exists(TempFolder);
            bool dirGone = !Directory.Exists(tempDir);
            Line("temp files removed",
                "temp prefab folder " + (assetGone ? "gone" : "left") + ", temp spec folder " +
                (dirGone ? "gone" : "left"), "both gone", assetGone && dirGone);

            Debug.Log(failed ? "RESULT: FAIL" : "RESULT: PASS");
            EditorApplication.Exit(failed ? 1 : 0);
        }

        private static void Line(string name, string measured, string expected, bool pass)
        {
            if (!pass) failed = true;
            Debug.Log("RULE " + name + ": " + measured + " expected " + expected + (pass ? " PASS" : " FAIL"));
        }

        private static void CleanAssets()
        {
            if (AssetDatabase.IsValidFolder(TempFolder)) AssetDatabase.DeleteAsset(TempFolder);
            if (Directory.Exists(TempFolder)) Directory.Delete(TempFolder, true);
            if (File.Exists(TempFolder + ".meta")) File.Delete(TempFolder + ".meta");
            AssetDatabase.Refresh();
        }

        private static void Build(PrefabSpec spec, string file)
        {
            File.WriteAllText(file, JsonUtility.ToJson(spec, true));
            PrefabSpecBuilder.BuildSpecAt(file);
            AssetDatabase.SaveAssets();
        }

        private static string Join(List<string> items)
        {
            if (items.Count == 0) return "none";
            items.Sort(StringComparer.Ordinal);
            return string.Join(",", items.ToArray());
        }

        private static void Check(string tempDir)
        {
            PrefabSpec spec = JsonUtility.FromJson<PrefabSpec>(File.ReadAllText(SpecPath));
            if (spec == null || spec.objects == null || spec.root == null) throw new InvalidOperationException("the real spec could not be read");
            spec.prefabPath = TempPrefab;
            spec.scene = new SpecScene();

            int changeIndex = IndexOf(spec, ChangedObject);
            int removeIndex = IndexOf(spec, RemovedObject);
            if (changeIndex < 0) throw new InvalidOperationException("the spec has no object " + ChangedObject);
            if (removeIndex < 0) throw new InvalidOperationException("the spec has no object " + RemovedObject);
            if (IsUsed(spec, RemovedObject)) throw new InvalidOperationException(RemovedObject + " is a parent or a link end, pick another object");
            if (spec.sprites == null || spec.sprites.Length == 0) throw new InvalidOperationException("the spec has no sprites");

            CleanAssets();
            string file = Path.Combine(tempDir, "changed.spec.json");

            // A. fresh build.
            Build(spec, file);
            Snapshot a = Take();

            // B. one property changed.
            float before = spec.objects[changeIndex].scaleX;
            spec.objects[changeIndex].scaleX = before + ScaleStep;
            Build(spec, file);
            Snapshot b = Take();

            List<string> removedB = Missing(a.Paths, b.Paths);
            List<string> addedB = Missing(b.Paths, a.Paths);
            Line("stage1 prefab not rebuilt",
                "guid " + (a.Guid == b.Guid ? "same" : "different") + ", root id " + (a.RootId == b.RootId ? "same" : "different"),
                "guid same, root id same", a.Guid == b.Guid && a.RootId == b.RootId);
            Line("stage1 structure unchanged", "removed " + Join(removedB) + ", added " + Join(addedB),
                "removed none, added none", removedB.Count == 0 && addedB.Count == 0);

            HashSet<string> common = new HashSet<string>(a.Paths);
            common.IntersectWith(b.Paths);
            List<string> propDiffsB = PropDiffs(a, b, common);
            string expectedChange = ChangedKey;
            bool oneChange = propDiffsB.Count == 1 && propDiffsB[0].StartsWith(expectedChange + " ", StringComparison.Ordinal);
            Line("stage1 changed properties", propDiffsB.Count + " changed: " + Join(propDiffsB),
                "1 changed: " + expectedChange, oneChange);
            List<string> idDiffsB = IdDiffs(a, b, common);
            Line("stage1 other objects identity", IdSummary(a, common, idDiffsB), "0 changed", idDiffsB.Count == 0);

            // C. one object removed, one added.
            List<SpecObject> list = new List<SpecObject>(spec.objects);
            list.RemoveAt(IndexOf(spec, RemovedObject));
            SpecObject added = new SpecObject();
            added.name = AddedObject;
            added.sprite = spec.sprites[0].key;
            added.sortingOrder = 1;
            added.posX = 0.5f;
            added.posY = 0.5f;
            list.Add(added);
            spec.objects = list.ToArray();
            Build(spec, file);
            Snapshot c = Take();

            List<string> removedC = Missing(b.Paths, c.Paths);
            List<string> addedC = Missing(c.Paths, b.Paths);
            Line("stage2 added objects", Join(addedC), AddedObject, addedC.Count == 1 && addedC[0] == AddedObject);
            Line("stage2 removed objects", Join(removedC), RemovedObject, removedC.Count == 1 && removedC[0] == RemovedObject);

            HashSet<string> commonC = new HashSet<string>(b.Paths);
            commonC.IntersectWith(c.Paths);
            List<string> propDiffsC = PropDiffs(b, c, commonC);
            Line("stage2 other property changes", propDiffsC.Count + " changed: " + Join(propDiffsC), "0 changed",
                propDiffsC.Count == 0);
            List<string> idDiffsC = IdDiffs(b, c, commonC);
            bool rootSame = b.RootId == c.RootId && b.Guid == c.Guid;
            Line("stage2 other objects identity",
                IdSummary(b, commonC, idDiffsC) + ", root " + (rootSame ? "same" : "different"),
                "0 changed, root same", idDiffsC.Count == 0 && rootSame);
        }

        private static int IndexOf(PrefabSpec spec, string name)
        {
            for (int i = 0; i < spec.objects.Length; i++)
            {
                if (spec.objects[i] != null && spec.objects[i].name == name) return i;
            }
            return -1;
        }

        private static bool IsUsed(PrefabSpec spec, string name)
        {
            for (int i = 0; i < spec.objects.Length; i++)
            {
                if (spec.objects[i] != null && spec.objects[i].parent == name) return true;
            }
            if (spec.links != null)
            {
                for (int i = 0; i < spec.links.Length; i++)
                {
                    SpecLink l = spec.links[i];
                    if (l != null && (l.owner == name || l.target == name)) return true;
                }
            }
            return false;
        }

        private static List<string> Missing(List<string> from, List<string> other)
        {
            HashSet<string> set = new HashSet<string>(other);
            List<string> result = new List<string>();
            for (int i = 0; i < from.Count; i++)
            {
                if (!set.Contains(from[i])) result.Add(from[i]);
            }
            return result;
        }

        private static string PathPart(string key)
        {
            int k = key.IndexOf('|');
            return k < 0 ? key : key.Substring(0, k);
        }

        private static List<string> PropDiffs(Snapshot x, Snapshot y, HashSet<string> common)
        {
            List<string> diffs = new List<string>();
            foreach (KeyValuePair<string, string> p in x.Props)
            {
                if (!common.Contains(PathPart(p.Key))) continue;
                string other;
                if (!y.Props.TryGetValue(p.Key, out other)) diffs.Add(p.Key + " was " + p.Value + " now missing");
                else if (other != p.Value) diffs.Add(p.Key + " was " + p.Value + " now " + other);
            }
            foreach (KeyValuePair<string, string> p in y.Props)
            {
                if (common.Contains(PathPart(p.Key)) && !x.Props.ContainsKey(p.Key)) diffs.Add(p.Key + " is new");
            }
            return diffs;
        }

        private static List<string> IdDiffs(Snapshot x, Snapshot y, HashSet<string> common)
        {
            List<string> diffs = new List<string>();
            foreach (KeyValuePair<string, string> p in x.Ids)
            {
                if (!common.Contains(PathPart(p.Key))) continue;
                string other;
                if (!y.Ids.TryGetValue(p.Key, out other) || other != p.Value) diffs.Add(p.Key);
            }
            foreach (KeyValuePair<string, string> p in y.Ids)
            {
                if (common.Contains(PathPart(p.Key)) && !x.Ids.ContainsKey(p.Key)) diffs.Add(p.Key + " (new)");
            }
            return diffs;
        }

        private static string IdSummary(Snapshot x, HashSet<string> common, List<string> diffs)
        {
            int n = 0;
            foreach (KeyValuePair<string, string> p in x.Ids)
            {
                if (common.Contains(PathPart(p.Key))) n++;
            }
            string text = n + " ids compared, " + diffs.Count + " changed";
            if (diffs.Count > 0)
            {
                int show = Math.Min(diffs.Count, 5);
                text += " (" + string.Join(",", diffs.GetRange(0, show).ToArray()) + ")";
            }
            return text;
        }

        // ---- snapshot ----

        private static Snapshot Take()
        {
            AssetDatabase.Refresh();
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(TempPrefab);
            if (asset == null) throw new InvalidOperationException("the temp prefab was not made: " + TempPrefab);
            Snapshot s = new Snapshot();
            s.Guid = AssetDatabase.AssetPathToGUID(TempPrefab);
            Walk(asset.transform, asset.transform, s);
            s.RootId = s.Ids.ContainsKey(RootLabel + "|GameObject") ? s.Ids[RootLabel + "|GameObject"] : "";
            return s;
        }

        private static string PathOf(Transform t, Transform root)
        {
            if (t == root) return RootLabel;
            string path = t.name;
            Transform p = t.parent;
            while (p != null && p != root)
            {
                path = p.name + "/" + path;
                p = p.parent;
            }
            return path;
        }

        private static string Gid(UnityEngine.Object o)
        {
            return GlobalObjectId.GetGlobalObjectIdSlow(o).ToString();
        }

        private static void Walk(Transform t, Transform root, Snapshot s)
        {
            string path = PathOf(t, root);
            s.Paths.Add(path);
            GameObject go = t.gameObject;
            s.Ids[path + "|GameObject"] = Gid(go);

            Dictionary<string, int> count = new Dictionary<string, int>();
            Component[] comps = go.GetComponents<Component>();
            for (int i = 0; i < comps.Length; i++)
            {
                Component c = comps[i];
                if (c == null) continue;
                string typeName = c.GetType().Name;
                int n;
                count.TryGetValue(typeName, out n);
                count[typeName] = n + 1;
                string key = path + "|" + typeName + "#" + n;
                s.Ids[key] = Gid(c);
                Props(c, key, root, s);
            }

            for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), root, s);
        }

        private static bool Skipped(string propertyPath)
        {
            return propertyPath == "m_Script" || propertyPath == "m_GameObject" ||
                propertyPath.StartsWith("m_Children", StringComparison.Ordinal) ||
                propertyPath.StartsWith("m_Father", StringComparison.Ordinal) ||
                propertyPath.StartsWith("m_RootOrder", StringComparison.Ordinal) ||
                propertyPath.StartsWith("m_CorrespondingSourceObject", StringComparison.Ordinal) ||
                propertyPath.StartsWith("m_PrefabInstance", StringComparison.Ordinal) ||
                propertyPath.StartsWith("m_PrefabAsset", StringComparison.Ordinal);
        }

        private static void Props(Component c, string key, Transform root, Snapshot s)
        {
            SerializedObject so = new SerializedObject(c);
            SerializedProperty p = so.GetIterator();
            bool enter = true;
            while (p.Next(enter))
            {
                enter = p.propertyType == SerializedPropertyType.Generic;
                if (Skipped(p.propertyPath)) { enter = false; continue; }
                if (p.propertyType == SerializedPropertyType.Generic) continue;
                s.Props[key + "|" + p.propertyPath] = ValueOf(p, root);
            }
        }

        private static string F(float v)
        {
            return v.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string ValueOf(SerializedProperty p, Transform root)
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.ArraySize:
                case SerializedPropertyType.Enum:
                case SerializedPropertyType.Character:
                    return p.longValue.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.Boolean:
                    return p.boolValue ? "true" : "false";
                case SerializedPropertyType.Float:
                    return F(p.floatValue);
                case SerializedPropertyType.String:
                    return p.stringValue;
                case SerializedPropertyType.Color:
                    Color col = p.colorValue;
                    return "(" + F(col.r) + "," + F(col.g) + "," + F(col.b) + "," + F(col.a) + ")";
                case SerializedPropertyType.Vector2:
                    Vector2 v2 = p.vector2Value;
                    return "(" + F(v2.x) + "," + F(v2.y) + ")";
                case SerializedPropertyType.Vector3:
                    Vector3 v3 = p.vector3Value;
                    return "(" + F(v3.x) + "," + F(v3.y) + "," + F(v3.z) + ")";
                case SerializedPropertyType.Vector4:
                    Vector4 v4 = p.vector4Value;
                    return "(" + F(v4.x) + "," + F(v4.y) + "," + F(v4.z) + "," + F(v4.w) + ")";
                case SerializedPropertyType.Quaternion:
                    Quaternion q = p.quaternionValue;
                    return "(" + F(q.x) + "," + F(q.y) + "," + F(q.z) + "," + F(q.w) + ")";
                case SerializedPropertyType.Rect:
                    Rect r = p.rectValue;
                    return "(" + F(r.x) + "," + F(r.y) + "," + F(r.width) + "," + F(r.height) + ")";
                case SerializedPropertyType.Bounds:
                    Bounds bd = p.boundsValue;
                    return "(" + F(bd.center.x) + "," + F(bd.center.y) + "," + F(bd.center.z) + "," +
                        F(bd.size.x) + "," + F(bd.size.y) + "," + F(bd.size.z) + ")";
                case SerializedPropertyType.ObjectReference:
                    return RefOf(p.objectReferenceValue, root);
                default:
                    return p.propertyType.ToString();
            }
        }

        private static string RefOf(UnityEngine.Object o, Transform root)
        {
            if (o == null) return "null";
            GameObject go = o as GameObject;
            Component comp = o as Component;
            if (comp != null) go = comp.gameObject;
            if (go != null && go.transform.root == root)
            {
                return "in:" + PathOf(go.transform, root) + ":" + o.GetType().Name;
            }
            return "asset:" + AssetDatabase.GetAssetPath(o) + ":" + o.name;
        }
    }
}

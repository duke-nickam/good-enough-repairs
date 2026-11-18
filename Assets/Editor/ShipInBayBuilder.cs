using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GoodEnoughRepairs.EditorTools
{
    /// <summary>
    /// Builds the ShipInBay prefab (placeholder ship body and five condition bars,
    /// with ShipView connected), puts one instance at the first bay in Main.unity,
    /// assigns it to StationView.bay1View, saves the scene and takes a screenshot
    /// of a docked ship. Run from Unity: GoodEnoughRepairs.EditorTools.ShipInBayBuilder.Build
    ///
    /// The game's types (ShipView, StationView, BalanceLoader, ...) are found by name
    /// at run time, so this editor script compiles without a reference to the game's
    /// assemblies.
    /// </summary>
    public static class ShipInBayBuilder
    {
        private const string PrefabFolder = "Assets/Prefabs/UX";
        private const string ArtFolder = "Assets/Prefabs/UX/ShipInBayArt";
        private const string PrefabPath = "Assets/Prefabs/UX/ShipInBay.prefab";
        private const string ScenePath = "Assets/Scenes/Main.unity";
        private const string BoxSpritePath = ArtFolder + "/PlaceholderBox.png";
        private const string BarSpritePath = ArtFolder + "/PlaceholderBarLeft.png";

        // Reference screen for placing the bay (the OnGUI bay 1 rect is 20,20,260x150).
        private const float RefWidth = 1280f;
        private const float RefHeight = 720f;
        private const float BayX = 20f;
        private const float BayY = 20f;
        private const float BayW = 260f;
        private const float BayH = 150f;

        private const float PixelsPerUnit = 4f;
        private const float BarWidth = 2.4f;
        private const int SlotCount = 5;

        public static void Build()
        {
            Directory.CreateDirectory(PrefabFolder);
            Directory.CreateDirectory(ArtFolder);

            Sprite box = MakeSprite(BoxSpritePath, new Vector2(0.5f, 0.5f));
            Sprite barLeft = MakeSprite(BarSpritePath, new Vector2(0f, 0.5f));

            MakePrefab(box, barLeft);
            Debug.Log("MADE: " + BoxSpritePath);
            Debug.Log("MADE: " + BarSpritePath);
            Debug.Log("MADE: " + PrefabPath);

            Component instanceView = PlaceInScene();
            Debug.Log("MADE: " + ScenePath);

            TakeScreenshot(instanceView);
        }

        private static Type FindType(string name)
        {
            string full = "GoodEnoughRepairs." + name;
            Assembly[] all = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < all.Length; i++)
            {
                Type t = all[i].GetType(full);
                if (t != null) return t;
            }
            throw new InvalidOperationException("Type not found: " + full);
        }

        private static Color BarColor(string field, Color fallback)
        {
            try
            {
                FieldInfo f = FindType("BarColors").GetField(field, BindingFlags.Public | BindingFlags.Static);
                if (f != null) return (Color)f.GetValue(null);
            }
            catch (Exception)
            {
            }
            return fallback;
        }

        private static Sprite MakeSprite(string path, Vector2 pivot)
        {
            if (!File.Exists(path))
            {
                Texture2D tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                Color32[] pixels = new Color32[16];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
                tex.SetPixels32(pixels);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }

            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = PixelsPerUnit;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;

            TextureImporterSettings settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = pivot;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static SpriteRenderer MakeSpriteChild(Transform parent, string name, Sprite sprite,
            Vector3 localPos, Vector3 localScale, Color color, int order)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order;
            return sr;
        }

        private static void MakePrefab(Sprite box, Sprite barLeft)
        {
            Type viewType = FindType("ShipView");
            Color track = BarColor("Track", new Color(0.10f, 0.10f, 0.12f));
            Color passes = BarColor("Passes", new Color(0.25f, 0.75f, 0.35f));

            GameObject root = new GameObject("ShipInBay");
            Component view = root.AddComponent(viewType);

            SpriteRenderer body = MakeSpriteChild(root.transform, "Body", box,
                new Vector3(0f, 0.9f, 0f), new Vector3(3f, 1.4f, 1f),
                new Color(0.45f, 0.5f, 0.58f), 0);

            SpriteRenderer[] fills = new SpriteRenderer[SlotCount];
            string[] names = { "Engine", "HullPlate", "ShieldGenerator", "Thruster", "PowerCore" };
            for (int i = 0; i < SlotCount; i++)
            {
                float y = -0.1f - 0.3f * i;
                MakeSpriteChild(root.transform, "BarTrack_" + names[i], barLeft,
                    new Vector3(-BarWidth / 2f, y, 0f), new Vector3(BarWidth, 0.2f, 1f),
                    track, 1);
                fills[i] = MakeSpriteChild(root.transform, "BarFill_" + names[i], barLeft,
                    new Vector3(-BarWidth / 2f, y, 0f), new Vector3(BarWidth, 0.12f, 1f),
                    passes, 2);
            }

            SerializedObject so = new SerializedObject(view);
            so.FindProperty("body").objectReferenceValue = body;
            SerializedProperty arr = so.FindProperty("barFills");
            arr.arraySize = SlotCount;
            for (int i = 0; i < SlotCount; i++)
            {
                arr.GetArrayElementAtIndex(i).objectReferenceValue = fills[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
        }

        private static Component PlaceInScene()
        {
            Type viewType = FindType("ShipView");
            Type stationType = FindType("StationView");

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            Camera cam = Camera.main;
            if (cam == null) cam = UnityEngine.Object.FindFirstObjectByType<Camera>();
            if (cam == null)
            {
                GameObject camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                camGo.transform.position = new Vector3(0f, 0f, -10f);
                cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.08f, 0.09f, 0.11f);
            }
            if (!cam.orthographic)
            {
                cam.orthographic = true;
                cam.orthographicSize = 5f;
            }

            Component station = (Component)UnityEngine.Object.FindFirstObjectByType(stationType);
            if (station == null)
            {
                GameObject st = new GameObject("Station");
                station = st.AddComponent(stationType);
            }

            // Remove an older instance so the builder can be run again.
            GameObject[] old = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            for (int i = 0; i < old.Length; i++)
            {
                if (old[i] != null && old[i].name == "ShipInBay") UnityEngine.Object.DestroyImmediate(old[i]);
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);

            float depth = Mathf.Abs(cam.transform.position.z);
            float vx = (BayX + BayW / 2f) / RefWidth;
            float vy = 1f - (BayY + BayH / 2f) / RefHeight;
            Vector3 pos = cam.ViewportToWorldPoint(new Vector3(vx, vy, depth));
            pos.z = 0f;

            // Prefab content spans about 3.4 units tall; fit it in the bay's height.
            float unitsPerPixel = 2f * cam.orthographicSize / RefHeight;
            float scale = BayH * unitsPerPixel / 3.4f;
            instance.transform.position = pos + new Vector3(0f, -0.65f * scale, 0f);
            instance.transform.localScale = new Vector3(scale, scale, 1f);

            Component view = instance.GetComponent(viewType);
            SerializedObject so = new SerializedObject(station);
            SerializedProperty prop = so.FindProperty("bay1View");
            if (prop == null) throw new InvalidOperationException("StationView has no bay1View field.");
            prop.objectReferenceValue = view;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(station);

            EditorSceneManager.MarkSceneDirty(instance.scene);
            EditorSceneManager.SaveScene(instance.scene);
            return view;
        }

        private static int GetInt(object target, string field)
        {
            FieldInfo f = target.GetType().GetField(field, BindingFlags.Public | BindingFlags.Instance);
            if (f == null) throw new InvalidOperationException("Missing field " + field);
            return Convert.ToInt32(f.GetValue(target));
        }

        private static void TakeScreenshot(Component view)
        {
            // Done after the scene is saved: Show changes bar scales and colors,
            // and that must not end up in the scene file.
            object balance = FindType("BalanceLoader")
                .GetMethod("Load", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
            if (balance == null) throw new InvalidOperationException("Balance could not be loaded.");

            int surplusMargin = Mathf.Max(0,
                GetInt(balance, "requiredFighterPercent") - GetInt(balance, "requiredFreighterPercent"));

            Array days = (Array)balance.GetType().GetField("days").GetValue(balance);
            object day0 = days.GetValue(0);

            object ship = FindType("ShipFactory")
                .GetMethod("Create", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[] { balance, day0, new System.Random(1) });

            object bays = Activator.CreateInstance(FindType("DockingBays"));
            bays.GetType().GetMethod("TryDock").Invoke(bays, new object[] { ship });

            view.GetType().GetMethod("Show").Invoke(view, new object[] { ship, surplusMargin });

            Camera cam = Camera.main;
            if (cam == null) cam = UnityEngine.Object.FindFirstObjectByType<Camera>();

            int w = (int)RefWidth;
            int h = (int)RefHeight;
            RenderTexture rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            RenderTexture oldTarget = cam.targetTexture;
            RenderTexture oldActive = RenderTexture.active;

            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();

            cam.targetTexture = oldTarget;
            RenderTexture.active = oldActive;

            string dir = Path.Combine(Path.GetTempPath(), "ger_shots");
            Directory.CreateDirectory(dir);
            string file = Path.GetFullPath(Path.Combine(dir, "ship_in_bay.png"));
            File.WriteAllBytes(file, tex.EncodeToPNG());

            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);

            Debug.Log("SCREENSHOT: " + file);
        }
    }
}

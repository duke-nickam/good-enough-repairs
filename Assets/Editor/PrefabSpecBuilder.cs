/*
 * PREFAB SPEC BUILDER
 *
 * Turns a prefab spec (a JSON file) into a prefab. It can also place instances
 * in a scene, connect them to StationView fields, show a docked sample ship (or a
 * sample inventory, or a sample warning dialog) in edit mode and save a
 * screenshot outside the project.
 *
 * HOW TO RUN
 *   PrefabSpecBuilder.BuildAll()          builds every *.spec.json under Assets/
 *                                         (Unity batch mode:
 *                                         -executeMethod GoodEnoughRepairs.EditorTools.PrefabSpecBuilder.BuildAll)
 *   PrefabSpecBuilder.Build(specPath)     builds one spec file, for example
 *                                         "Assets/Prefabs/UX/ShipInBay.spec.json"
 *
 * The builder prints "MADE: <path>" for every file Unity made (sprite PNG, prefab,
 * scene) and "SCREENSHOT: <absolute path>" for every screenshot. A bad spec stops
 * the build with an error that names the spec file. It never saves a half prefab.
 * Running a spec twice gives the same result: the prefab is overwritten and older
 * instances in the scene are replaced.
 *
 * SPEC FILE RULES
 *   - File name ends with ".spec.json", anywhere under Assets/.
 *   - JSON that Unity's JsonUtility can read: objects and arrays, no dictionaries.
 *   - Leave a field out to get its default. Text defaults to "", numbers to 0.
 *   - Colors are text: "#RRGGBB" or "#RRGGBBAA", or one of these names from the
 *     game's BarColors: "below", "passes", "surplus", "track", "marker", "panel".
 *     No color (empty) means white.
 *
 * SPEC FIELDS
 *   prefabPath    (required) where the prefab is written, for example
 *                 "Assets/Prefabs/UX/ShipInBay.prefab"
 *   sprites[]     sprite files used by the objects:
 *       key            name the objects use to point at it (required)
 *       path           PNG path under Assets/ (required). If the file is missing, the
 *                      builder makes a plain white 4x4 placeholder. The Artist's real
 *                      PNG replaces it later. The builder sets the import settings
 *                      below on the file either way.
 *       pivotX, pivotY pivot in 0-1 (0.5 and 0.5 is the center, 0 and 0.5 is the
 *                      left edge). Default 0 and 0, so write both.
 *       pixelsPerUnit  pixels per world unit (required, more than 0)
 *   root          (required) the top object of the prefab. Same fields as an object
 *                 below, but without "parent".
 *   objects[]     children, in order. A parent must come before its children:
 *       name           unique name in this spec (required)
 *       parent         name of the parent object; empty means the root
 *       sprite         a sprite key from "sprites"; adds a SpriteRenderer with it
 *       color          SpriteRenderer color (only when "sprite" is set)
 *       sortingOrder   SpriteRenderer sorting order (only when "sprite" is set)
 *       posX, posY, posZ     local position
 *       scaleX, scaleY, scaleZ   local scale; 0 or left out means 1
 *       components[]   extra components to attach, from this fixed list:
 *                        "ShipView"        the ship view (UX assembly)
 *                        "InventoryView"   the inventory panel with drag and drop
 *                        "WarningView"     the buy and release warning dialog
 *                        "SpriteRenderer"  an empty SpriteRenderer (not needed
 *                                          when "sprite" is set)
 *                      An unknown name stops the build. A new type needs one line
 *                      in the builder's code.
 *   links[]       sets a reference field on a component to another object:
 *       owner          name of the object that holds the component
 *       component      "ShipView", "InventoryView", "WarningView", "SpriteRenderer"
 *                      or "Transform"
 *       field          the serialized field name, for example "body" or "barFills"
 *       index          -1 for a single field (default is 0, so write -1); 0 or more
 *                      for an element of an array field (the array grows if needed)
 *       subField       optional. Only with index 0 or more, when the array holds
 *                      small records (InventoryView.slots): the name of the field
 *                      inside that record, for example "fill". Leave it out for
 *                      arrays of plain references (barFills, slotAreas, buyButtons).
 *       target         name of the object to point at
 *       targetType     what to point at on the target: "GameObject",
 *                      "SpriteRenderer" or "Transform"
 *   scene         optional. Leave it out to only build the prefab.
 *       path           scene file, for example "Assets/Scenes/Main.unity". It must
 *                      exist (the builder never creates scenes).
 *       stationField   name of the StationView field that gets the instance's
 *                      component, for example "bay1View" (gets the ShipView),
 *                      "inventoryView" (gets the InventoryView) or "warningView"
 *                      (gets the WarningView). Empty means do not connect.
 *                      Not used when "bays" is given.
 *       Placement, option A: fit into a screen rectangle (ship prefabs)
 *         refWidth, refHeight   reference screen size in pixels, for example 1280, 720.
 *                      Before placing, the builder sets the camera aspect to
 *                      refWidth / refHeight, so the position matches a game view of
 *                      that shape.
 *         rectX, rectY, rectW, rectH   the rectangle in reference pixels (top left
 *                      is 0,0). The instance is scaled so that "contentHeight"
 *                      prefab units fill the rectangle's height, then moved so the
 *                      center of its content bounds (the body and all five bar fills
 *                      at full length, ShipView.TryGetContentBounds) is at the
 *                      rectangle's center. StationView centers each bay's ship on
 *                      the same bounds, through the same ShipView code. For a
 *                      prefab without a ShipView, the prefab origin goes to the
 *                      rectangle's center.
 *         contentHeight   height of the prefab's content in prefab units
 *         offsetY         vertical shift in prefab units, applied after scaling and
 *                         centering (for example -0.65)
 *       Placement, option A for several bays: "bays" (see BAYS below).
 *       Placement, option B (used when rectW is 0 and there is no "bays"): worldX,
 *                      worldY, worldScale (worldScale 0 means 1). The prefab origin
 *                      is placed there. Use this for the inventory panel and the
 *                      warning dialog.
 *       screenshot     file name of the screenshot, for example "ship_in_bay.png".
 *                      Empty means no screenshot. It is saved in the system temp
 *                      folder under ger_shots, outside the project. Before the shot
 *                      the builder shows a docked sample ship (day 1, seed 1) on the
 *                      instance, or, for an InventoryView, the sample ship's five
 *                      parts in inventory slots 1-5, or, for a WarningView, the
 *                      warning dialog open on the sample ship. With "bays", the
 *                      first instance gets the seed 1 ship and every other instance
 *                      its own sample ship (seed 2, 3). This is done after
 *                      the scene is saved, so the shown bar sizes and colors do not
 *                      end up in the scene file. With option A the shot has the
 *                      reference width and height. Text drawn with OnGUI (button
 *                      labels, the dialog message) is not in the screenshot.
 *       sampleDialog   only for a WarningView prefab: "release" (default) or "buy".
 *                      Which warning the screenshot shows.
 *       sampleConditions[]   optional. Sets the condition of sample ship parts
 *                      for the screenshot, so it can show red, green and blue bars.
 *                      Parts not listed keep the generated sample ship's condition.
 *                      Each entry:
 *           part           "Engine", "HullPlate", "ShieldGenerator", "Thruster" or
 *                          "PowerCore"
 *           condition      whole number from 0 to 100
 *                      A bad part name, a repeated part or a condition outside
 *                      0-100 stops the build. On a ship the bar color is judged
 *                      against the sample ship's own required level (fighter 70%,
 *                      freighter 60%; the type comes from the generated ship and
 *                      cannot be set). In the inventory panel the colors are:
 *                      below 60% red, 60-69% green, 70% and more blue.
 *                      Example: "sampleConditions": [
 *                        { "part": "Engine", "condition": 35 },
 *                        { "part": "HullPlate", "condition": 65 },
 *                        { "part": "ShieldGenerator", "condition": 95 } ]
 *
 * BAYS: ONE SHIP VIEW IN EACH OF THE THREE BAYS (scene.bays)
 *   "bays" is an array with one entry per bay the spec places. It needs refWidth,
 *   refHeight and contentHeight (and may use offsetY) in the scene block. Each
 *   entry:
 *       bay            bay number, 1 to 3 (required, each number once)
 *       rectX, rectY, rectW, rectH   the bay's rectangle in reference pixels, top
 *                      left is 0,0 (rectW and rectH more than 0). Same placement
 *                      rule as option A: scaled to contentHeight, content center on
 *                      the rectangle's center.
 *       stationField   optional. StationView field to connect. Empty means
 *                      "bay1View", "bay2View" or "bay3View" from the bay number.
 *   Instance names in the scene: "<root name>_Bay1", "_Bay2", "_Bay3". Running the
 *   spec again replaces them. The measure tool reads the same entries and fails a
 *   bay that is not listed.
 *   Example:
 *     "scene": { "path": "Assets/Scenes/Main.unity",
 *       "refWidth": 1280, "refHeight": 720, "contentHeight": 3.4, "offsetY": -0.65,
 *       "bays": [
 *         { "bay": 1, "rectX": 20,  "rectY": 20, "rectW": 260, "rectH": 150 },
 *         { "bay": 2, "rectX": 296, "rectY": 20, "rectW": 260, "rectH": 150 },
 *         { "bay": 3, "rectX": 572, "rectY": 20, "rectW": 260, "rectH": 150 } ] }
 *
 * DRAG HANDLES ON A SHIP (ShipView)
 *   ShipView.slotAreas is an array of five SpriteRenderers, in part order (engine,
 *   hull plate, shield generator, thruster, power core). Each one is the drag
 *   handle of that part slot and also a drop target. Its sprite bounds are the hit
 *   area, and it is the sprite that moves with the pointer. Link them like the
 *   bar fills: { "owner": "ShipInBay", "component": "ShipView", "field": "slotAreas",
 *   "index": 0, "target": "SlotArea_Engine", "targetType": "SpriteRenderer" }.
 *   A ship view with no slotAreas still works, it just has no drag.
 *
 * BUY AND RELEASE BUTTONS ON A SHIP (ShipView)
 *   ShipView.buyButtons is an array of five SpriteRenderers in part order, one buy
 *   button next to each slot. The sprite bounds are the click area. The view tints
 *   the sprite with buyActiveColor when the slot is empty or weak (the buy preview
 *   says it can be bought) and with buyInactiveColor otherwise, and writes the price
 *   on it. ShipView.releaseButton is one SpriteRenderer, the release button.
 *   Link them like the bar fills:
 *   { "owner": "ShipInBay", "component": "ShipView", "field": "buyButtons",
 *     "index": 0, "target": "BuyButton_Engine", "targetType": "SpriteRenderer" }
 *   { "owner": "ShipInBay", "component": "ShipView", "field": "releaseButton",
 *     "index": -1, "target": "ReleaseButton", "targetType": "SpriteRenderer" }
 *   Without these links the ship view still works, it just has no buttons.
 *
 * WARNING DIALOG (WarningView)
 *   Put "WarningView" on the root of its own prefab. One dialog serves all bays.
 *   Fields to link, all with "index": -1:
 *     dialogRoot      GameObject: a child that holds all the dialog sprites. It is
 *                     hidden when the game starts and shown while a warning is open.
 *                     The WarningView must not be on this object.
 *     panel           SpriteRenderer: dialog background
 *     textArea        SpriteRenderer, optional: where the message is written (the
 *                     panel is used when empty)
 *     confirmButton   SpriteRenderer: click area of Confirm
 *     cancelButton    SpriteRenderer: click area of Cancel
 *   Example link: { "owner": "WarningDialog", "component": "WarningView",
 *     "field": "dialogRoot", "index": -1, "target": "DialogRoot",
 *     "targetType": "GameObject" }
 *   Connect it to the station with "stationField": "warningView".
 *   Give the sprites a high sortingOrder so the dialog is above the ships.
 *
 * INVENTORY PANEL (InventoryView)
 *   Put "InventoryView" on the root. Fields to link:
 *     slots   array of 10 records, slot 1 is index 0. Link each field with
 *             "field": "slots", "index": <slot>, "subField": <name below>:
 *               area          SpriteRenderer: drag handle and drop target of the slot
 *               dragRoot      Transform, optional: what moves when the part is
 *                             dragged (for example the slot group with its bar).
 *                             Empty means the area sprite.
 *               fill          SpriteRenderer: condition bar fill. Sprite pivot on the
 *                             left edge, 1 world unit wide at scale 1. Its start
 *                             X scale is the full bar (100%).
 *               freighterMark Transform, optional: mark at the freighter level (60%)
 *               fighterMark   Transform, optional: mark at the fighter level (70%)
 *                             Marks are put in the same parent as the fill, at the
 *                             right X. Their Y and Z stay as placed. The levels come
 *                             from balance.json.
 *     binArea    SpriteRenderer, "index": -1: dropping a part here bins it at once
 *     panelArea  SpriteRenderer, "index": -1, optional: dropping a part on it (not on
 *                a slot) puts the part in the first free slot
 *   Example link: { "owner": "InventoryPanel", "component": "InventoryView",
 *     "field": "slots", "index": 0, "subField": "fill",
 *     "target": "Slot1_Fill", "targetType": "SpriteRenderer" }
 *   A refused drop (wrong part type, full inventory, same slot) puts the part back.
 *
 * FULL EXAMPLE (the ship in a bay: body, five condition bars, ShipView, bay 1)
 *
 * {
 *   "prefabPath": "Assets/Prefabs/UX/ShipInBay.prefab",
 *   "sprites": [
 *     { "key": "box", "path": "Assets/Prefabs/UX/ShipInBayArt/PlaceholderBox.png",
 *       "pivotX": 0.5, "pivotY": 0.5, "pixelsPerUnit": 4 },
 *     { "key": "barLeft", "path": "Assets/Prefabs/UX/ShipInBayArt/PlaceholderBarLeft.png",
 *       "pivotX": 0, "pivotY": 0.5, "pixelsPerUnit": 4 }
 *   ],
 *   "root": { "name": "ShipInBay", "components": [ "ShipView" ] },
 *   "objects": [
 *     { "name": "Body", "sprite": "box", "color": "#73849A", "sortingOrder": 0,
 *       "posY": 0.9, "scaleX": 3, "scaleY": 1.4 },
 *     { "name": "BarTrack_Engine", "sprite": "barLeft", "color": "track", "sortingOrder": 1,
 *       "posX": -1.2, "posY": -0.1, "scaleX": 2.4, "scaleY": 0.2 },
 *     { "name": "BarFill_Engine", "sprite": "barLeft", "color": "passes", "sortingOrder": 2,
 *       "posX": -1.2, "posY": -0.1, "scaleX": 2.4, "scaleY": 0.12 }
 *   ],
 *   "links": [
 *     { "owner": "ShipInBay", "component": "ShipView", "field": "body", "index": -1,
 *       "target": "Body", "targetType": "SpriteRenderer" },
 *     { "owner": "ShipInBay", "component": "ShipView", "field": "barFills", "index": 0,
 *       "target": "BarFill_Engine", "targetType": "SpriteRenderer" }
 *   ],
 *   "scene": {
 *     "path": "Assets/Scenes/Main.unity",
 *     "stationField": "bay1View",
 *     "refWidth": 1280, "refHeight": 720,
 *     "rectX": 20, "rectY": 20, "rectW": 260, "rectH": 150,
 *     "contentHeight": 3.4, "offsetY": -0.65,
 *     "screenshot": "ship_in_bay.png"
 *   }
 * }
 * (shortened here: a real ship has all five bars and five barFills links)
 *
 * The bar fill order in barFills is the game's part order: engine, hull plate,
 * shield generator, thruster, power core.
 */

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GoodEnoughRepairs.EditorTools
{
    [Serializable]
    public class SpecSprite
    {
        public string key;
        public string path;
        public float pivotX;
        public float pivotY;
        public float pixelsPerUnit;
    }

    [Serializable]
    public class SpecObject
    {
        public string name;
        public string parent;
        public string sprite;
        public string color;
        public int sortingOrder;
        public float posX;
        public float posY;
        public float posZ;
        public float scaleX;
        public float scaleY;
        public float scaleZ;
        public string[] components;
    }

    [Serializable]
    public class SpecLink
    {
        public string owner;
        public string component;
        public string field;
        public int index;
        public string subField;
        public string target;
        public string targetType;
    }

    [Serializable]
    public class SpecPartCondition
    {
        public string part;
        public int condition;
    }

    [Serializable]
    public class SpecBay
    {
        public int bay;
        public string stationField;
        public float rectX;
        public float rectY;
        public float rectW;
        public float rectH;
    }

    [Serializable]
    public class SpecScene
    {
        public string path;
        public string stationField;
        public float refWidth;
        public float refHeight;
        public float rectX;
        public float rectY;
        public float rectW;
        public float rectH;
        public float contentHeight;
        public float offsetY;
        public float worldX;
        public float worldY;
        public float worldScale;
        public string screenshot;
        public string sampleDialog;
        public SpecPartCondition[] sampleConditions;
        public SpecBay[] bays;
    }

    [Serializable]
    public class PrefabSpec
    {
        public string prefabPath;
        public SpecSprite[] sprites;
        public SpecObject root;
        public SpecObject[] objects;
        public SpecLink[] links;
        public SpecScene scene;
    }

    /// <summary>
    /// Builds prefabs from JSON specs. The format is documented in the comment at
    /// the top of this file. No reflection and no type names as text: components
    /// are attached through direct references in the switches below.
    /// </summary>
    public static class PrefabSpecBuilder
    {
        private const string SpecSuffix = ".spec.json";
        private const string BaySuffix = "_Bay";

        /// <summary>Name of the scene instance for a bay entry of a spec.</summary>
        public static string InstanceName(string rootName, int bay)
        {
            return rootName + BaySuffix + bay;
        }

        /// <summary>Builds every *.spec.json under Assets/.</summary>
        public static void BuildAll()
        {
            string[] files = Directory.GetFiles("Assets", "*" + SpecSuffix, SearchOption.AllDirectories);
            if (files.Length == 0) throw new InvalidOperationException("No " + SpecSuffix + " files found under Assets.");
            Array.Sort(files, StringComparer.Ordinal);
            for (int i = 0; i < files.Length; i++)
            {
                Build(files[i].Replace('\\', '/'));
            }
        }

        /// <summary>Builds one spec file.</summary>
        public static void Build(string specPath)
        {
            if (string.IsNullOrEmpty(specPath) || !File.Exists(specPath))
            {
                throw new InvalidOperationException("Spec file not found: " + specPath);
            }

            PrefabSpec spec = JsonUtility.FromJson<PrefabSpec>(File.ReadAllText(specPath));
            if (spec == null) throw Fail(specPath, "the file could not be read as a spec");
            Validate(spec, specPath);

            Dictionary<string, Sprite> sprites = MakeSprites(spec, specPath);
            GameObject root = MakePrefabContent(spec, sprites, specPath);
            try
            {
                string folder = Path.GetDirectoryName(spec.prefabPath);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                PrefabUtility.SaveAsPrefabAsset(root, spec.prefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
            Debug.Log("MADE: " + spec.prefabPath);

            if (spec.scene != null && !string.IsNullOrEmpty(spec.scene.path))
            {
                PlaceInScene(spec, specPath);
            }
        }

        private static InvalidOperationException Fail(string specPath, string message)
        {
            return new InvalidOperationException("Spec error in " + specPath + ": " + message);
        }

        private static void Validate(PrefabSpec spec, string specPath)
        {
            if (string.IsNullOrEmpty(spec.prefabPath)) throw Fail(specPath, "prefabPath is missing");
            if (!spec.prefabPath.EndsWith(".prefab", StringComparison.Ordinal))
            {
                throw Fail(specPath, "prefabPath must end with .prefab");
            }
            if (spec.root == null || string.IsNullOrEmpty(spec.root.name)) throw Fail(specPath, "root.name is missing");

            HashSet<string> keys = new HashSet<string>();
            if (spec.sprites != null)
            {
                for (int i = 0; i < spec.sprites.Length; i++)
                {
                    SpecSprite s = spec.sprites[i];
                    if (string.IsNullOrEmpty(s.key)) throw Fail(specPath, "sprites[" + i + "].key is missing");
                    if (string.IsNullOrEmpty(s.path)) throw Fail(specPath, "sprites[" + i + "].path is missing");
                    if (s.pixelsPerUnit <= 0f) throw Fail(specPath, "sprites[" + i + "].pixelsPerUnit must be more than 0");
                    if (!keys.Add(s.key)) throw Fail(specPath, "duplicate sprite key " + s.key);
                }
            }

            if (spec.scene != null)
            {
                SampleOverrides(spec.scene, specPath);
                string d = spec.scene.sampleDialog;
                if (!string.IsNullOrEmpty(d) && d != "buy" && d != "release")
                {
                    throw Fail(specPath, "scene.sampleDialog must be \"buy\" or \"release\"");
                }
                ValidateBays(spec.scene, specPath);
            }
        }

        private static void ValidateBays(SpecScene sc, string specPath)
        {
            if (sc.bays == null || sc.bays.Length == 0) return;
            HashSet<int> seen = new HashSet<int>();
            for (int i = 0; i < sc.bays.Length; i++)
            {
                SpecBay b = sc.bays[i];
                string where = "scene.bays[" + i + "]";
                if (b == null) throw Fail(specPath, where + " is empty");
                if (b.bay < 1 || b.bay > GameRules.DockingBays)
                {
                    throw Fail(specPath, where + ".bay must be from 1 to " + GameRules.DockingBays);
                }
                if (!seen.Add(b.bay)) throw Fail(specPath, where + ": bay " + b.bay + " is listed twice");
                if (b.rectW <= 0f || b.rectH <= 0f)
                {
                    throw Fail(specPath, where + ": rectW and rectH must be more than 0");
                }
            }
        }

        // Index of a part name in the game's part order (PartTypes.All), or -1.
        private static int PartIndex(string name)
        {
            PartType type;
            switch (name)
            {
                case "Engine": type = PartType.Engine; break;
                case "HullPlate": type = PartType.HullPlate; break;
                case "ShieldGenerator": type = PartType.ShieldGenerator; break;
                case "Thruster": type = PartType.Thruster; break;
                case "PowerCore": type = PartType.PowerCore; break;
                default: return -1;
            }
            PartType[] all = PartTypes.All;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == type) return i;
            }
            return -1;
        }

        // One value per part slot: the condition from the spec, or -1 for "keep the
        // generated one". Stops the build on a bad entry.
        private static int[] SampleOverrides(SpecScene sc, string specPath)
        {
            int[] result = new int[PartTypes.All.Length];
            for (int i = 0; i < result.Length; i++) result[i] = -1;
            if (sc.sampleConditions == null) return result;

            for (int i = 0; i < sc.sampleConditions.Length; i++)
            {
                SpecPartCondition c = sc.sampleConditions[i];
                string where = "scene.sampleConditions[" + i + "]";
                if (c == null || string.IsNullOrEmpty(c.part)) throw Fail(specPath, where + ".part is missing");
                int index = PartIndex(c.part);
                if (index < 0) throw Fail(specPath, where + ": unknown part " + c.part);
                if (result[index] >= 0) throw Fail(specPath, where + ": part " + c.part + " is listed twice");
                if (c.condition < Part.MinCondition || c.condition > Part.MaxCondition)
                {
                    throw Fail(specPath, where + ": condition must be from " + Part.MinCondition + " to " +
                        Part.MaxCondition);
                }
                result[index] = c.condition;
            }
            return result;
        }

        private static Dictionary<string, Sprite> MakeSprites(PrefabSpec spec, string specPath)
        {
            Dictionary<string, Sprite> result = new Dictionary<string, Sprite>();
            if (spec.sprites == null) return result;
            for (int i = 0; i < spec.sprites.Length; i++)
            {
                SpecSprite s = spec.sprites[i];
                Sprite sprite = MakeSprite(s, specPath);
                result[s.key] = sprite;
                Debug.Log("MADE: " + s.path);
            }
            return result;
        }

        private static Sprite MakeSprite(SpecSprite s, string specPath)
        {
            string folder = Path.GetDirectoryName(s.path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

            if (!File.Exists(s.path))
            {
                Texture2D tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                Color32[] pixels = new Color32[16];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
                tex.SetPixels32(pixels);
                tex.Apply();
                File.WriteAllBytes(s.path, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(s.path, ImportAssetOptions.ForceSynchronousImport);
            }

            TextureImporter importer = AssetImporter.GetAtPath(s.path) as TextureImporter;
            if (importer == null) throw Fail(specPath, "sprite file is not a texture: " + s.path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = s.pixelsPerUnit;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;

            TextureImporterSettings settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = new Vector2(s.pivotX, s.pivotY);
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();

            Sprite loaded = AssetDatabase.LoadAssetAtPath<Sprite>(s.path);
            if (loaded == null) throw Fail(specPath, "sprite could not be loaded: " + s.path);
            return loaded;
        }

        private static Color ParseColor(string text, string specPath, string where)
        {
            if (string.IsNullOrEmpty(text)) return Color.white;
            switch (text)
            {
                case "below": return BarColors.Below;
                case "passes": return BarColors.Passes;
                case "surplus": return BarColors.Surplus;
                case "track": return BarColors.Track;
                case "marker": return BarColors.Marker;
                case "panel": return BarColors.Panel;
            }
            Color color;
            if (text.StartsWith("#", StringComparison.Ordinal) && ColorUtility.TryParseHtmlString(text, out color))
            {
                return color;
            }
            throw Fail(specPath, where + ": bad color " + text);
        }

        private static float OneIfZero(float v)
        {
            return v == 0f ? 1f : v;
        }

        private static GameObject MakeObject(SpecObject o, Transform parent, Dictionary<string, Sprite> sprites,
            string specPath)
        {
            GameObject go = new GameObject(o.name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(o.posX, o.posY, o.posZ);
            go.transform.localScale = new Vector3(OneIfZero(o.scaleX), OneIfZero(o.scaleY), OneIfZero(o.scaleZ));

            if (!string.IsNullOrEmpty(o.sprite))
            {
                Sprite sprite;
                if (!sprites.TryGetValue(o.sprite, out sprite))
                {
                    throw Fail(specPath, o.name + ": unknown sprite key " + o.sprite);
                }
                SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.color = ParseColor(o.color, specPath, o.name);
                sr.sortingOrder = o.sortingOrder;
            }

            if (o.components != null)
            {
                for (int i = 0; i < o.components.Length; i++)
                {
                    AddComponentByName(go, o.components[i], specPath);
                }
            }
            return go;
        }

        // The fixed list of components a spec may attach. Each name is one direct,
        // compile-time reference. To allow a new type, add one case here.
        private static void AddComponentByName(GameObject go, string name, string specPath)
        {
            switch (name)
            {
                case "ShipView":
                    go.AddComponent<ShipView>();
                    return;
                case "InventoryView":
                    go.AddComponent<InventoryView>();
                    return;
                case "WarningView":
                    go.AddComponent<WarningView>();
                    return;
                case "SpriteRenderer":
                    if (go.GetComponent<SpriteRenderer>() == null) go.AddComponent<SpriteRenderer>();
                    return;
                default:
                    throw Fail(specPath, go.name + ": unknown component " + name);
            }
        }

        private static Component GetComponentByName(GameObject go, string name, string specPath)
        {
            Component c;
            switch (name)
            {
                case "ShipView":
                    c = go.GetComponent<ShipView>();
                    break;
                case "InventoryView":
                    c = go.GetComponent<InventoryView>();
                    break;
                case "WarningView":
                    c = go.GetComponent<WarningView>();
                    break;
                case "SpriteRenderer":
                    c = go.GetComponent<SpriteRenderer>();
                    break;
                case "Transform":
                    c = go.transform;
                    break;
                default:
                    throw Fail(specPath, "unknown component " + name);
            }
            if (c == null) throw Fail(specPath, go.name + " has no " + name);
            return c;
        }

        private static UnityEngine.Object GetTargetByType(GameObject go, string type, string specPath)
        {
            switch (type)
            {
                case "GameObject":
                    return go;
                case "Transform":
                    return go.transform;
                case "SpriteRenderer":
                    SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
                    if (sr == null) throw Fail(specPath, go.name + " has no SpriteRenderer");
                    return sr;
                default:
                    throw Fail(specPath, "unknown targetType " + type);
            }
        }

        private static GameObject MakePrefabContent(PrefabSpec spec, Dictionary<string, Sprite> sprites,
            string specPath)
        {
            Dictionary<string, GameObject> map = new Dictionary<string, GameObject>();
            GameObject root = MakeObject(spec.root, null, sprites, specPath);
            try
            {
                map[spec.root.name] = root;

                if (spec.objects != null)
                {
                    for (int i = 0; i < spec.objects.Length; i++)
                    {
                        SpecObject o = spec.objects[i];
                        if (string.IsNullOrEmpty(o.name)) throw Fail(specPath, "objects[" + i + "].name is missing");
                        if (map.ContainsKey(o.name)) throw Fail(specPath, "duplicate object name " + o.name);

                        Transform parent = root.transform;
                        if (!string.IsNullOrEmpty(o.parent))
                        {
                            GameObject p;
                            if (!map.TryGetValue(o.parent, out p))
                            {
                                throw Fail(specPath, o.name + ": parent " + o.parent + " is unknown or comes later");
                            }
                            parent = p.transform;
                        }
                        map[o.name] = MakeObject(o, parent, sprites, specPath);
                    }
                }

                if (spec.links != null)
                {
                    for (int i = 0; i < spec.links.Length; i++)
                    {
                        ApplyLink(spec.links[i], map, specPath);
                    }
                }
                return root;
            }
            catch (Exception)
            {
                UnityEngine.Object.DestroyImmediate(root);
                throw;
            }
        }

        private static void ApplyLink(SpecLink link, Dictionary<string, GameObject> map, string specPath)
        {
            GameObject owner;
            GameObject target;
            if (string.IsNullOrEmpty(link.owner) || !map.TryGetValue(link.owner, out owner))
            {
                throw Fail(specPath, "link owner not found: " + link.owner);
            }
            if (string.IsNullOrEmpty(link.target) || !map.TryGetValue(link.target, out target))
            {
                throw Fail(specPath, "link target not found: " + link.target);
            }
            if (string.IsNullOrEmpty(link.field)) throw Fail(specPath, "a link has no field");

            Component component = GetComponentByName(owner, link.component, specPath);
            UnityEngine.Object value = GetTargetByType(target, link.targetType, specPath);

            SerializedObject so = new SerializedObject(component);
            SerializedProperty prop = so.FindProperty(link.field);
            if (prop == null) throw Fail(specPath, link.component + " has no field " + link.field);

            if (link.index >= 0)
            {
                if (!prop.isArray) throw Fail(specPath, link.field + " is not an array");
                if (prop.arraySize < link.index + 1) prop.arraySize = link.index + 1;
                SerializedProperty element = prop.GetArrayElementAtIndex(link.index);
                if (!string.IsNullOrEmpty(link.subField))
                {
                    element = element.FindPropertyRelative(link.subField);
                    if (element == null)
                    {
                        throw Fail(specPath, link.field + " elements have no field " + link.subField);
                    }
                }
                element.objectReferenceValue = value;
            }
            else
            {
                if (!string.IsNullOrEmpty(link.subField))
                {
                    throw Fail(specPath, "subField needs an array element (index 0 or more) on " + link.field);
                }
                prop.objectReferenceValue = value;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // Puts one instance of the prefab in the open scene. With screen set, the
        // rectangle (reference pixels) decides the position and scale, and the
        // ship's content is centered with the same ShipView code StationView uses.
        private static GameObject PlaceOne(GameObject prefab, string name, Camera cam, SpecScene sc,
            float rx, float ry, float rw, float rh, bool screen)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.name = name;
            ShipView view = instance.GetComponent<ShipView>();

            Vector3 pos;
            float scale;
            if (screen)
            {
                float depth = Mathf.Abs(cam.transform.position.z);
                float vx = (rx + rw / 2f) / sc.refWidth;
                float vy = 1f - (ry + rh / 2f) / sc.refHeight;
                pos = cam.ViewportToWorldPoint(new Vector3(vx, vy, depth));
                pos.z = 0f;
                float unitsPerPixel = 2f * cam.orthographicSize / sc.refHeight;
                scale = rh * unitsPerPixel / sc.contentHeight;
                pos += new Vector3(0f, sc.offsetY * scale, 0f);
            }
            else
            {
                pos = new Vector3(sc.worldX, sc.worldY, 0f);
                scale = OneIfZero(sc.worldScale);
            }
            instance.transform.position = pos;
            instance.transform.localScale = new Vector3(scale, scale, 1f);

            // Same centering rule as StationView: the center of the body and bars.
            if (screen && view != null) view.CenterOn(pos);
            return instance;
        }

        private static StationView GetStation()
        {
            StationView station = UnityEngine.Object.FindFirstObjectByType<StationView>();
            if (station == null)
            {
                GameObject st = new GameObject("Station");
                station = st.AddComponent<StationView>();
            }
            return station;
        }

        private static void ConnectStation(StationView station, string field, GameObject instance, string specPath)
        {
            SerializedObject so = new SerializedObject(station);
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null) throw Fail(specPath, "StationView has no field " + field);

            string propType = prop.type ?? "";
            UnityEngine.Object component;
            string needed;
            if (propType.Contains("WarningView"))
            {
                component = instance.GetComponent<WarningView>();
                needed = "WarningView";
            }
            else if (propType.Contains("InventoryView"))
            {
                component = instance.GetComponent<InventoryView>();
                needed = "InventoryView";
            }
            else
            {
                component = instance.GetComponent<ShipView>();
                needed = "ShipView";
            }
            if (component == null)
            {
                throw Fail(specPath, "the prefab root has no " + needed + " to connect to " + field);
            }
            prop.objectReferenceValue = component;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(station);
        }

        private static void PlaceInScene(PrefabSpec spec, string specPath)
        {
            SpecScene sc = spec.scene;
            if (!File.Exists(sc.path)) throw Fail(specPath, "scene not found: " + sc.path);

            EditorSceneManager.OpenScene(sc.path, OpenSceneMode.Single);

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

            bool hasBays = sc.bays != null && sc.bays.Length > 0;
            bool screenPlacement = hasBays || sc.rectW > 0f;
            if (screenPlacement)
            {
                if (sc.refWidth <= 0f || sc.refHeight <= 0f || sc.contentHeight <= 0f)
                {
                    throw Fail(specPath, "scene: refWidth, refHeight and contentHeight must be more than 0");
                }
                // The edit-mode camera has the aspect of the editor window. Use the
                // reference screen's shape so the placement matches the game view.
                cam.aspect = sc.refWidth / sc.refHeight;
            }

            // Remove older instances so the builder can be run again.
            string bayPrefix = spec.root.name + BaySuffix;
            GameObject[] all = UnityEngine.Object.FindObjectsByType<GameObject>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                GameObject g = all[i];
                if (g == null || g.transform.parent != null) continue;
                if (g.name == spec.root.name || g.name.StartsWith(bayPrefix, StringComparison.Ordinal))
                {
                    UnityEngine.Object.DestroyImmediate(g);
                }
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(spec.prefabPath);
            if (prefab == null) throw Fail(specPath, "prefab could not be loaded: " + spec.prefabPath);

            List<GameObject> instances = new List<GameObject>();
            List<string> fields = new List<string>();
            if (hasBays)
            {
                for (int i = 0; i < sc.bays.Length; i++)
                {
                    SpecBay b = sc.bays[i];
                    instances.Add(PlaceOne(prefab, InstanceName(spec.root.name, b.bay), cam, sc,
                        b.rectX, b.rectY, b.rectW, b.rectH, true));
                    fields.Add(string.IsNullOrEmpty(b.stationField) ? "bay" + b.bay + "View" : b.stationField);
                }
            }
            else
            {
                instances.Add(PlaceOne(prefab, spec.root.name, cam, sc,
                    sc.rectX, sc.rectY, sc.rectW, sc.rectH, sc.rectW > 0f));
                fields.Add(sc.stationField);
            }

            StationView station = null;
            for (int i = 0; i < instances.Count; i++)
            {
                if (string.IsNullOrEmpty(fields[i])) continue;
                if (station == null) station = GetStation();
                ConnectStation(station, fields[i], instances[i], specPath);
            }

            GameObject first = instances[0];
            ShipView view = first.GetComponent<ShipView>();
            InventoryView invView = first.GetComponent<InventoryView>();
            WarningView warnView = first.GetComponent<WarningView>();

            EditorSceneManager.MarkSceneDirty(first.scene);
            EditorSceneManager.SaveScene(first.scene);
            Debug.Log("MADE: " + sc.path);

            if (!string.IsNullOrEmpty(sc.screenshot))
            {
                if (view == null && invView == null && warnView == null)
                {
                    throw Fail(specPath,
                        "a screenshot needs a ShipView, an InventoryView or a WarningView on the prefab root");
                }
                List<ShipView> others = new List<ShipView>();
                for (int i = 1; i < instances.Count; i++)
                {
                    ShipView v = instances[i].GetComponent<ShipView>();
                    if (v != null) others.Add(v);
                }
                int w = screenPlacement ? Mathf.RoundToInt(sc.refWidth) : 1280;
                int h = screenPlacement ? Mathf.RoundToInt(sc.refHeight) : 720;
                TakeScreenshot(view, others, invView, warnView, sc.sampleDialog, cam, sc.screenshot,
                    SampleOverrides(sc, specPath), w, h, specPath);
            }

            if (screenPlacement) cam.ResetAspect();
        }

        // Done after the scene is saved: showing the sample changes bar scales and
        // colors, and that must not end up in the scene file.
        private static void TakeScreenshot(ShipView view, List<ShipView> others, InventoryView invView,
            WarningView warnView, string dialog, Camera cam, string fileName, int[] conditions, int w, int h,
            string specPath)
        {
            BalanceData balance = BalanceLoader.Load();
            if (balance == null) throw Fail(specPath, "the balance could not be loaded for the sample ship");

            int surplusMargin = BarBand.SurplusMargin(balance.requiredFighterPercent, balance.requiredFreighterPercent);
            Ship ship = ShipFactory.Create(balance, balance.days[0], new System.Random(1));
            DockingBays bays = new DockingBays();
            bays.TryDock(ship);
            Inventory inventory = new Inventory();

            if (view != null)
            {
                view.Bind(new BuyRelease(bays, inventory, balance), null);
                view.Show(ship, surplusMargin, conditions);

                // Other bays get their own sample ship.
                for (int i = 0; i < others.Count; i++)
                {
                    Ship extra = ShipFactory.Create(balance, balance.days[0], new System.Random(i + 2));
                    DockingBays extraBays = new DockingBays();
                    extraBays.TryDock(extra);
                    others[i].Bind(new BuyRelease(extraBays, new Inventory(), balance), null);
                    others[i].Show(extra, surplusMargin, conditions);
                }
            }
            else if (warnView != null)
            {
                BuyRelease commands = new BuyRelease(bays, inventory, balance);
                warnView.Bind(commands, ContentLoader.Load());
                if (dialog == "buy")
                {
                    bool opened = false;
                    PartType[] types = PartTypes.All;
                    for (int i = 0; i < types.Length && !opened; i++)
                    {
                        opened = warnView.OpenBuy(ship, types[i]);
                    }
                    if (!opened) throw Fail(specPath, "the sample ship has no slot that can be bought");
                }
                else
                {
                    warnView.OpenRelease(ship);
                }
            }
            else
            {
                // The sample ship's five parts go into inventory slots 1-5.
                PartType[] types = PartTypes.All;
                for (int i = 0; i < types.Length; i++)
                {
                    Part real = ship.GetPart(types[i]);
                    int condition = conditions[i] >= 0 ? conditions[i] : (real == null ? 0 : real.Condition);
                    int slot;
                    inventory.TryAdd(new Part(types[i], condition), out slot);
                }
                invView.Bind(new PartMoves(inventory, balance), balance);
            }

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
            string file = Path.GetFullPath(Path.Combine(dir, Path.GetFileName(fileName)));
            File.WriteAllBytes(file, tex.EncodeToPNG());

            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);

            Debug.Log("SCREENSHOT: " + file);
        }
    }
}

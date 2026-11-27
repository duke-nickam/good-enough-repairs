/*
 * PREFAB SPEC BUILDER
 *
 * Turns a prefab spec (a JSON file) into a prefab. It can also place instances
 * in a scene, connect them to StationView fields, show a docked sample ship (or a
 * sample inventory, a sample warning dialog, a sample credits counter or a sample
 * day summary) in edit mode and save a screenshot outside the project.
 *
 * HOW TO RUN
 *   PrefabSpecBuilder.BuildAll()          builds every *.spec.json under Assets/
 *                                         (Unity batch mode:
 *                                         -executeMethod GoodEnoughRepairs.EditorTools.PrefabSpecBuilder.BuildAll)
 *   PrefabSpecBuilder.BuildSpecAt(path)   builds one spec file from the given path,
 *                                         for example "Assets/Prefabs/UX/ship_in_bay.spec.json".
 *                                         The path may be a changed copy of a spec.
 *   PrefabSpecBuilder.Build(specPath)     the same as BuildSpecAt (kept for old callers)
 *
 * The sample ship and the mechanic's lines in screenshots come from the JSON files
 * in Assets/GameData (balance.json and content/), read as plain files and parsed.
 *
 * The builder prints "MADE: <path>" for every sprite PNG, prefab and scene it
 * handled and "SCREENSHOT: <absolute path>" for every screenshot. A prefab or scene
 * that needed no change is also reported with "UNCHANGED: <path>" and its file is
 * not written. A bad spec stops the build with an error that names the spec file.
 * It never saves a half prefab. Every build reads the spec file from disk and
 * refreshes the asset database first, so a shot always comes from the current specs.
 *
 * ONE LINE PER SPEC, AND SCREENSHOTS ONLY FOR CHANGED SPECS
 *   After each spec the builder prints one line:
 *     "BUILT: <spec path> (screenshot taken)"
 *     "BUILT: <spec path> (no screenshot)"      changed, but no screenshot asked for
 *     "SKIPPED (unchanged): <spec path>"        nothing changed, no screenshot taken
 *   A spec counts as changed when its prefab was made new, the in-place patch
 *   changed the prefab (a property, an object added or removed, a link), a sprite
 *   file or its import settings were made or fixed, or the scene changed (an
 *   instance added, removed, moved or scaled, or a StationView link set).
 *   A spec with a screenshot is photographed when it changed, or when no earlier
 *   shot of it exists in the temp folder ger_shots (outside the project). A spec
 *   that did not change and already has its shot is not photographed. Nothing is
 *   stored in the project for this, so an unchanged build leaves no Git diff.
 *   To photograph everything again, delete the ger_shots folder in the system temp
 *   folder. The decision is ScreenshotDecision.ShouldPhotograph.
 *
 * UNCHANGED BUILD WRITES NOTHING
 *   A build with a spec that has not changed leaves the prefab files, the scene
 *   file and the sprite import settings (.meta) as they are, byte for byte:
 *   - A prefab is saved only when the patch really changed it (the content is
 *     compared before and after the patch).
 *   - The scene is saved only when an instance was added, removed, moved or
 *     scaled, or a StationView link, the camera or the event system changed.
 *   - A sprite is re-imported only when an import setting differs.
 *   When a screenshot is taken, it shows sample data in the open scene and
 *   then loads the scene again from disk without saving.
 *
 * UPDATE, NOT REBUILD
 *   The builder works like a person editing the prefab:
 *   - If the prefab does not exist yet, it is built from scratch.
 *   - If it exists, it is opened and compared with the spec. Objects are matched
 *     by their name path from the root, for example "Slot1/Slot1_Fill".
 *     Only properties that differ are set (position, scale, sprite, color,
 *     sorting order, text settings, components, links, hidden flag). Objects that
 *     stay keep their identity: the same object and the same components, never
 *     recreated.
 *   - An object that is new in the spec is added alone. An object in the prefab
 *     that the spec does not list (gone from the spec, renamed, moved to another
 *     parent, or added by hand) is removed alone. A rename or a move counts as
 *     remove and add.
 *   - Child order follows the spec order.
 *   - Components: ShipView, InventoryView, WarningView, CreditsView, DaySummaryView,
 *     SpriteRenderer and text (TextMesh and TextKeyView) are removed when the spec
 *     no longer asks for them. Canvas, Image and Button are added when missing but
 *     not removed, so remove the object instead.
 *   - Links are set when they differ. A link dropped from the spec is not cleared
 *     unless its target object is removed.
 *   - A spec error stops the build before the prefab is saved.
 *   In the scene, the instance with the right name that is an instance of the
 *   prefab is kept: only its position and scale are set when they differ, and the
 *   StationView connection is set. A missing instance is added. An instance that
 *   the spec no longer lists (for example a bay that is gone) is removed alone.
 *   The prefab or a scene instance is never deleted and rebuilt as a whole when it
 *   exists. The scene file itself must exist (the builder never creates scenes).
 *
 * SPEC FILE RULES
 *   - File name ends with ".spec.json", anywhere under Assets/.
 *   - JSON that Unity's JsonUtility can read: objects and arrays, no dictionaries.
 *   - Leave a field out to get its default. Text defaults to "", numbers to 0,
 *     flags to false.
 *   - Object names must not contain "/" (it is the path separator).
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
 *       hidden         true saves the object inactive (hidden) in the prefab. Its
 *                      children are hidden with it. Default false (active). A code
 *                      view shows it later, for example WarningView shows its
 *                      dialogRoot. Links and text still work on a hidden object.
 *       sprite         a sprite key from "sprites"; adds a SpriteRenderer with it
 *       color          SpriteRenderer color (only when "sprite" is set)
 *       sortingOrder   SpriteRenderer sorting order (only when "sprite" is set);
 *                      also the sorting order of a text element and of a "Canvas"
 *       posX, posY, posZ     local position
 *       scaleX, scaleY, scaleZ   local scale; 0 or left out means 1
 *       components[]   extra components to attach, from this fixed list:
 *                        "ShipView"        the ship view (UX assembly)
 *                        "InventoryView"   the inventory panel with drag and drop
 *                        "WarningView"     the buy and release warning dialog
 *                        "CreditsView"     the credits counter
 *                        "DaySummaryView"  the end-of-day summary panel
 *                        "TextKeyView"     a text element component (not needed
 *                                          when "textKey" is set)
 *                        "SpriteRenderer"  an empty SpriteRenderer (not needed
 *                                          when "sprite" is set)
 *                        "Canvas"          a world-space UI canvas (with a
 *                                          raycaster) at the object's sortingOrder.
 *                                          UI objects below it are placed in prefab
 *                                          units, like any other object.
 *                        "Image"           a see-through image that blocks clicks.
 *                                          Its size is 1 unit times the object's
 *                                          scale (use scaleX and scaleY). It must
 *                                          be under an object with "Canvas".
 *                        "Button"          a see-through UI button (it has an
 *                                          Image too). Same size rule as "Image".
 *                                          Under an object with "Canvas".
 *                      An unknown name stops the build. A new type needs one line
 *                      in the builder's code.
 *       textKey        key in ui_text.json. Makes the object a text element: the
 *                      builder adds a TextMesh with a built-in font and a
 *                      TextKeyView that fills it from the key at start and when
 *                      the numbers change. Leave it out for no text. Keep the
 *                      object's scale at 1 (use textSize to size the text).
 *                      The player's words never go in the spec, only the key.
 *       textSize       text height in prefab units, about; 0 or left out means 0.3
 *       textColor      color of the text (same color rules as "color"); empty
 *                      means white
 *       textAlign      "left", "center" or "right": which point of the text sits
 *                      at the object's position. Empty means "center".
 *       Example: { "name": "PaymentText", "textKey": "payment", "sortingOrder": 6,
 *                  "posX": 0, "posY": 1.8, "textSize": 0.3, "textAlign": "center" }
 *       Which keys exist and what {0}, {1} mean is in
 *       Assets/GameData/content/ui_text.json. A ShipView fills the numbers
 *       of its own text elements once they are linked (see TEXT ON A SHIP).
 *   links[]       sets a reference field on a component to another object:
 *       owner          name of the object that holds the component
 *       component      "ShipView", "InventoryView", "WarningView", "CreditsView",
 *                      "DaySummaryView", "TextKeyView", "SpriteRenderer" or "Transform"
 *       field          the serialized field name, for example "body" or "barFills"
 *       index          -1 for a single field (default is 0, so write -1); 0 or more
 *                      for an element of an array field (the array grows if needed)
 *       subField       optional. Only with index 0 or more, when the array holds
 *                      small records (InventoryView.slots): the name of the field
 *                      inside that record, for example "fill". Leave it out for
 *                      arrays of plain references (barFills, slotAreas, buyButtons).
 *       target         name of the object to point at
 *       targetType     what to point at on the target: "GameObject",
 *                      "SpriteRenderer", "Transform", "TextKeyView" or "Button"
 *   scene         optional. Leave it out to only build the prefab.
 *       path           scene file, for example "Assets/Scenes/Main.unity". It must
 *                      exist (the builder never creates scenes).
 *       stationField   name of the StationView field that gets the instance's
 *                      component, for example "bay1View" (gets the ShipView),
 *                      "inventoryView" (gets the InventoryView), "warningView"
 *                      (gets the WarningView), "creditsView" (gets the CreditsView)
 *                      or "summaryView" (gets the DaySummaryView). Empty means do
 *                      not connect. Not used when "bays" is given.
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
 *                      is placed there. Use this for the inventory panel, the
 *                      warning dialog, the credits counter and the day summary.
 *       screenshot     file name of the screenshot, for example "ship_in_bay.png".
 *                      Empty means no screenshot. It is saved in the system temp
 *                      folder under ger_shots, outside the project. It is taken
 *                      only for a changed spec or one with no earlier shot (see
 *                      ONE LINE PER SPEC above). Before the shot
 *                      the builder shows a docked sample ship (day 1, seed 1) on the
 *                      instance, or, for an InventoryView, the sample ship's five
 *                      parts in inventory slots 1-5, or, for a WarningView, the
 *                      warning dialog open on the sample ship with its message
 *                      lines filled, or, for a CreditsView, the credits of a sample
 *                      run (the starting credits from balance.json), or, for a
 *                      DaySummaryView, the summary open after a sample day (see
 *                      SAMPLES FOR CREDITS AND DAY SUMMARY below). With "bays", the
 *                      first instance gets the seed 1
 *                      ship and every other instance its own sample ship (seed 2,
 *                      3). For the inventory, warning, credits and summary shots,
 *                      every ShipView in the scene gets its own sample ship too, so
 *                      no '{' text shows.
 *                      Every shot except the warning shot is taken with the warning
 *                      dialog hidden.
 *                      All of this is done after the scene is saved, so the shown
 *                      bar sizes, colors and the open dialog do not end up in the
 *                      scene file. After the shot the scene is loaded again from
 *                      disk, which drops the shown state. With option A the shot has
 *                      the reference width and height. Text elements (textKey) are
 *                      in the screenshot.
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
 * SAMPLES FOR CREDITS AND DAY SUMMARY
 *   Both samples use a real Run made by RunFactory (balance.json, content, seed 1).
 *   - CreditsView: the view is bound to the run and shows its credits, which are
 *     the starting credits from balance.json.
 *   - DaySummaryView: the view is bound to the run, then a sample day is played
 *     through the run's own StartDay on a copy of the balance with no ships on any
 *     day. The day starts, upkeep from balance.json is charged and the day ends
 *     at once, so the summary opens with the real rules: Earned 0, Spent 0, Upkeep
 *     from balance.json, Balance after upkeep. The panel is open with the title,
 *     all four number lines, the note and the Continue button filled from
 *     ui_text.json. The run-over state (Play again) has no sample.
 *   The copy of the balance is only used for the sample. Nothing is saved.
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
 *   spec again updates them in place. A bay that is no longer listed has its
 *   instance removed. The measure tool reads the same entries and fails a
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
 * TEXT ON A SHIP (ShipView)
 *   Add text elements (objects with "textKey") and link them with targetType
 *   "TextKeyView". Fields, all optional:
 *     buyTexts       array of five, part order, index 0 to 4: key "buy_button",
 *                    the view fills {0} with the price. Without one, the label is
 *                    drawn with OnGUI.
 *     releaseText    index -1: key "release_button". Without it, drawn with OnGUI.
 *     typeText       index -1: the view sets the key (type_fighter or type_freighter).
 *                    Give it any textKey, for example "type_fighter".
 *     paymentText    index -1: key "payment", the view fills {0}.
 *     requiredText   index -1: key "needs", the view fills {0}.
 *   Example link: { "owner": "ShipInBay", "component": "ShipView",
 *     "field": "paymentText", "index": -1, "target": "PaymentText",
 *     "targetType": "TextKeyView" }
 *
 * CREDITS COUNTER (CreditsView)
 *   Put "CreditsView" on the root of its own prefab. Link, with "index": -1:
 *     creditsText     TextKeyView: a text element with key "credits". The view
 *                     fills {0} with the run's credits.
 *   Connect it to the station with "stationField": "creditsView".
 *
 * DAY SUMMARY (DaySummaryView)
 *   Put "DaySummaryView" on the root of its own prefab (not on the hidden panel
 *   object). Fields to link, all with "index": -1:
 *     panelRoot       GameObject: a child that holds all the panel objects. Give it
 *                     "hidden": true so the prefab is saved closed.
 *     clickBlocker    GameObject, optional: an "Image" object under a "Canvas",
 *                     shown and hidden with the panel.
 *     titleText       TextKeyView: the view sets its key (summary_title,
 *                     run_end_title_win or run_end_title_bankrupt).
 *     earnedText      TextKeyView, key summary_earned, the view fills {0}
 *     spentText       TextKeyView, key summary_spent, the view fills {0}
 *     upkeepText      TextKeyView, key summary_upkeep, the view fills {0}
 *     balanceText     TextKeyView, key summary_balance, the view fills {0}
 *     noteText        TextKeyView, key summary_note, hidden when the run is over
 *     continueButton  Button: shown while the run goes on. Its label is a text
 *                     element with key summary_continue.
 *     playAgainButton Button: shown when the run is over. Its label is a text
 *                     element with key run_end_play_again.
 *   Connect it to the station with "stationField": "summaryView". The builder also
 *   adds an EventSystem (Input System UI module) if the scene has none.
 *
 * WARNING DIALOG (WarningView)
 *   Put "WarningView" on the root of its own prefab. One dialog serves all bays.
 *   Fields to link, all with "index": -1:
 *     dialogRoot      GameObject: a child that holds all the dialog objects. It is
 *                     hidden when the game starts and shown while a warning is open.
 *                     The WarningView must not be on this object. Give it
 *                     "hidden": true so the prefab is saved closed.
 *     panel           SpriteRenderer: dialog background
 *     textArea        SpriteRenderer, optional
 *     confirmButton   SpriteRenderer: the Confirm button sprite (the look)
 *     cancelButton    SpriteRenderer: the Cancel button sprite (the look)
 *     confirmUiButton Button: a "Button" object over the Confirm sprite
 *     cancelUiButton  Button: a "Button" object over the Cancel sprite
 *     clickBlocker    GameObject: an "Image" object, big enough to cover the
 *                     screen, below the buttons in the same "Canvas". It is shown
 *                     and hidden with the dialog and blocks UI clicks behind it.
 *     titleText       TextKeyView, optional: the title. The view sets its key.
 *     confirmText     TextKeyView, optional: the Confirm label. The view sets its key.
 *     cancelText      TextKeyView, optional: the Cancel label. The view sets its key.
 *     Message lines, TextKeyView, each optional (a line with no link is not shown).
 *     The view shows only the lines that apply and fills their {0}, {1}:
 *     mechanicText        buy and release: the mechanic's line, taken from
 *                         mechanic_lines.json (buy_warning on buy, release_warning
 *                         on release). Give it any textKey: the view overwrites
 *                         the text.
 *     buyPriceText        buy: key buy_price
 *     buyBinnedText       buy, when the old part is binned: key buy_binned
 *     releasePaymentText  release: key release_payment
 *     releasePenaltyText  release, when parts are fitted: key release_penalty
 *     releaseNetText      release: key release_net
 *   Example link: { "owner": "WarningDialog", "component": "WarningView",
 *     "field": "dialogRoot", "index": -1, "target": "DialogRoot",
 *     "targetType": "GameObject" }
 *   Connect it to the station with "stationField": "warningView". When a scene
 *   gets a WarningView, the builder also adds an EventSystem (Input System UI
 *   module) if the scene has none, so the UI buttons work.
 *   Give the sprites and the "Canvas" a high sortingOrder so the dialog is above
 *   the ships.
 *
 * INVENTORY PANEL (InventoryView)
 *   Put "InventoryView" on the root. Its labels (keys "inventory_title",
 *   "bin_label", "bin_hint") are plain text elements with "textKey" and need no
 *   link. Fields to link:
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
using System.Globalization;
using System.IO;
using System.Text;
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
        public bool hidden;
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
        public string textKey;
        public float textSize;
        public string textColor;
        public string textAlign;
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
    /// The decision "take a screenshot of this spec or not". Plain logic, no scene
    /// and no files, so it can be tested alone.
    /// </summary>
    public static class ScreenshotDecision
    {
        /// <summary>
        /// True when the spec asks for a screenshot and it is changed (anything: a
        /// new or changed prefab, a made or fixed sprite, a changed scene) or has no
        /// earlier shot to look at.
        /// </summary>
        public static bool ShouldPhotograph(bool screenshotRequested, bool changed, bool earlierShotExists)
        {
            if (!screenshotRequested) return false;
            return changed || !earlierShotExists;
        }

        /// <summary>The one plain line printed for a spec after it was handled.</summary>
        public static string BuildLine(string specPath, bool changed, bool photographed)
        {
            if (photographed) return "BUILT: " + specPath + " (screenshot taken)";
            if (changed) return "BUILT: " + specPath + " (no screenshot)";
            return "SKIPPED (unchanged): " + specPath;
        }
    }

    /// <summary>
    /// Builds prefabs from JSON specs. The format is documented in the comment at
    /// the top of this file. No reflection and no type names as text: components
    /// are attached through direct references in the switches below.
    /// An existing prefab or scene instance is updated in place, never rebuilt.
    /// A build that changes nothing writes no prefab and no scene, and takes no
    /// screenshot of a spec that already has one.
    /// </summary>
    public static class PrefabSpecBuilder
    {
        private const string SpecSuffix = ".spec.json";
        private const string BaySuffix = "_Bay";
        private const string ShotFolder = "ger_shots";
        private const float DefaultTextSize = 0.3f;

        // TextMesh: line height in world units is about fontSize * characterSize * 0.1.
        private const int TextFontSize = 64;
        private const float TextSizeDivisor = 6.4f;

        /// <summary>Name of the scene instance for a bay entry of a spec.</summary>
        public static string InstanceName(string rootName, int bay)
        {
            return rootName + BaySuffix + bay;
        }

        private static string GameDataFile(string relative)
        {
            return Path.Combine(Application.dataPath, "GameData/" + relative);
        }

        /// <summary>
        /// Reads Assets/GameData/balance.json as a plain file and parses it.
        /// Logs an error and returns null when it is missing or bad.
        /// </summary>
        public static BalanceData LoadBalance()
        {
            string path = GameDataFile("balance.json");
            if (!File.Exists(path))
            {
                Debug.LogError("[Builder] File not found: " + path);
                return null;
            }
            string error;
            BalanceData data = BalanceLoader.Parse(File.ReadAllText(path), out error);
            if (data == null) Debug.LogError("[Builder] balance.json is not usable: " + error);
            return data;
        }

        /// <summary>
        /// Reads the content JSON files from Assets/GameData/content as plain files
        /// and parses them. A missing or bad file is logged and left out (empty text).
        /// </summary>
        public static ContentData LoadContent()
        {
            string error;
            MechanicLinesFile lines = null;
            DayBriefsFile briefs = null;
            PartCatalogFile parts = null;
            UiTextFile ui = null;

            string p = GameDataFile("content/mechanic_lines.json");
            if (File.Exists(p))
            {
                lines = ContentLoader.ParseLines(File.ReadAllText(p), out error);
                if (lines == null) Debug.LogError("[Builder] mechanic_lines.json is not usable: " + error);
            }
            p = GameDataFile("content/day_briefs.json");
            if (File.Exists(p))
            {
                briefs = ContentLoader.ParseBriefs(File.ReadAllText(p), out error);
                if (briefs == null) Debug.LogError("[Builder] day_briefs.json is not usable: " + error);
            }
            p = GameDataFile("content/part_catalog.json");
            if (File.Exists(p))
            {
                parts = ContentLoader.ParseParts(File.ReadAllText(p), out error);
                if (parts == null) Debug.LogError("[Builder] part_catalog.json is not usable: " + error);
            }
            p = GameDataFile("content/ui_text.json");
            if (File.Exists(p))
            {
                ui = ContentLoader.ParseUiText(File.ReadAllText(p), out error);
                if (ui == null) Debug.LogError("[Builder] ui_text.json is not usable: " + error);
            }
            return new ContentData(lines, briefs, parts, null, ui);
        }

        /// <summary>Builds every *.spec.json under Assets/.</summary>
        public static void BuildAll()
        {
            AssetDatabase.Refresh();
            string[] files = Directory.GetFiles("Assets", "*" + SpecSuffix, SearchOption.AllDirectories);
            if (files.Length == 0) throw new InvalidOperationException("No " + SpecSuffix + " files found under Assets.");
            Array.Sort(files, StringComparer.Ordinal);
            for (int i = 0; i < files.Length; i++)
            {
                BuildSpecAt(files[i].Replace('\\', '/'));
            }
        }

        /// <summary>Builds one spec file. Same as BuildSpecAt.</summary>
        public static void Build(string specPath)
        {
            BuildSpecAt(specPath);
        }

        /// <summary>
        /// Builds one spec from the given spec path (any path, for example a changed
        /// copy of a spec). An existing prefab and scene instances are updated in place.
        /// </summary>
        public static void BuildSpecAt(string specPath)
        {
            if (string.IsNullOrEmpty(specPath) || !File.Exists(specPath))
            {
                throw new InvalidOperationException("Spec file not found: " + specPath);
            }

            // Always read the spec from disk, and pick up changed assets first.
            AssetDatabase.Refresh();
            PrefabSpec spec = JsonUtility.FromJson<PrefabSpec>(File.ReadAllText(specPath));
            if (spec == null) throw Fail(specPath, "the file could not be read as a spec");
            Validate(spec, specPath);

            bool changed = false;
            Dictionary<string, Sprite> sprites = MakeSprites(spec, specPath, ref changed);

            string folder = Path.GetDirectoryName(spec.prefabPath);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

            bool exists = File.Exists(spec.prefabPath) &&
                AssetDatabase.LoadAssetAtPath<GameObject>(spec.prefabPath) != null;
            if (!exists)
            {
                GameObject root = MakePrefabContent(spec, sprites, specPath);
                try
                {
                    PrefabUtility.SaveAsPrefabAsset(root, spec.prefabPath);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
                changed = true;
            }
            else
            {
                if (PatchPrefab(spec, sprites, specPath)) changed = true;
            }
            Debug.Log("MADE: " + spec.prefabPath);

            bool photographed = false;
            if (spec.scene != null && !string.IsNullOrEmpty(spec.scene.path))
            {
                PlaceInScene(spec, specPath, ref changed, out photographed);
            }
            Debug.Log(ScreenshotDecision.BuildLine(specPath, changed, photographed));
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
            ObjectPaths(spec, specPath);

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

        // Name path of every object in the spec, from the root: the root is "", a
        // child of the root is "Name", a grandchild is "Parent/Name". Stops the build
        // on a missing or repeated name, or a parent that is unknown or comes later.
        private static Dictionary<string, string> ObjectPaths(PrefabSpec spec, string specPath)
        {
            Dictionary<string, string> paths = new Dictionary<string, string>();
            paths[spec.root.name] = "";
            if (spec.objects == null) return paths;
            for (int i = 0; i < spec.objects.Length; i++)
            {
                SpecObject o = spec.objects[i];
                if (o == null || string.IsNullOrEmpty(o.name)) throw Fail(specPath, "objects[" + i + "].name is missing");
                if (o.name.IndexOf('/') >= 0) throw Fail(specPath, o.name + ": the name must not contain /");
                if (paths.ContainsKey(o.name)) throw Fail(specPath, "duplicate object name " + o.name);

                string parentPath = "";
                if (!string.IsNullOrEmpty(o.parent))
                {
                    if (!paths.TryGetValue(o.parent, out parentPath))
                    {
                        throw Fail(specPath, o.name + ": parent " + o.parent + " is unknown or comes later");
                    }
                }
                paths[o.name] = parentPath.Length == 0 ? o.name : parentPath + "/" + o.name;
            }
            return paths;
        }

        private static string ParentOfPath(string path)
        {
            int k = path.LastIndexOf('/');
            return k < 0 ? "" : path.Substring(0, k);
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

        private static Dictionary<string, Sprite> MakeSprites(PrefabSpec spec, string specPath, ref bool changed)
        {
            Dictionary<string, Sprite> result = new Dictionary<string, Sprite>();
            if (spec.sprites == null) return result;
            for (int i = 0; i < spec.sprites.Length; i++)
            {
                SpecSprite s = spec.sprites[i];
                Sprite sprite = MakeSprite(s, specPath, ref changed);
                result[s.key] = sprite;
                Debug.Log("MADE: " + s.path);
            }
            return result;
        }

        // changed is set to true when the PNG was made or an import setting was fixed.
        private static Sprite MakeSprite(SpecSprite s, string specPath, ref bool changed)
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
                changed = true;
            }

            TextureImporter importer = AssetImporter.GetAtPath(s.path) as TextureImporter;
            if (importer == null) throw Fail(specPath, "sprite file is not a texture: " + s.path);

            // Only touch the importer (and so the .meta file) when a setting differs.
            bool dirty = false;
            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                dirty = true;
            }
            if (importer.spriteImportMode != SpriteImportMode.Single)
            {
                importer.spriteImportMode = SpriteImportMode.Single;
                dirty = true;
            }
            if (!Mathf.Approximately(importer.spritePixelsPerUnit, s.pixelsPerUnit))
            {
                importer.spritePixelsPerUnit = s.pixelsPerUnit;
                dirty = true;
            }
            if (importer.filterMode != FilterMode.Point)
            {
                importer.filterMode = FilterMode.Point;
                dirty = true;
            }
            if (importer.mipmapEnabled)
            {
                importer.mipmapEnabled = false;
                dirty = true;
            }
            if (!importer.alphaIsTransparency)
            {
                importer.alphaIsTransparency = true;
                dirty = true;
            }

            TextureImporterSettings settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            if (settings.spriteAlignment != (int)SpriteAlignment.Custom ||
                !Mathf.Approximately(settings.spritePivot.x, s.pivotX) ||
                !Mathf.Approximately(settings.spritePivot.y, s.pivotY))
            {
                settings.spriteAlignment = (int)SpriteAlignment.Custom;
                settings.spritePivot = new Vector2(s.pivotX, s.pivotY);
                importer.SetTextureSettings(settings);
                dirty = true;
            }
            if (dirty)
            {
                importer.SaveAndReimport();
                changed = true;
            }

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

        private static bool Listed(SpecObject o, string name)
        {
            if (o.components == null) return false;
            for (int i = 0; i < o.components.Length; i++)
            {
                if (o.components[i] == name) return true;
            }
            return false;
        }

        private static void DestroyIfPresent<T>(GameObject go) where T : Component
        {
            T c = go.GetComponent<T>();
            if (c != null) UnityEngine.Object.DestroyImmediate(c);
        }

        // Sets local position and scale from the spec, only when they differ.
        private static void ApplyTransform(Transform t, SpecObject o)
        {
            Vector3 pos = new Vector3(o.posX, o.posY, o.posZ);
            if (t.localPosition != pos) t.localPosition = pos;
            Vector3 scale = new Vector3(OneIfZero(o.scaleX), OneIfZero(o.scaleY), OneIfZero(o.scaleZ));
            if (t.localScale != scale) t.localScale = scale;
        }

        // Makes a new object from the spec.
        private static GameObject MakeObject(SpecObject o, Transform parent, Dictionary<string, Sprite> sprites,
            string specPath)
        {
            GameObject go = new GameObject(o.name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(o.posX, o.posY, o.posZ);
            go.transform.localScale = new Vector3(OneIfZero(o.scaleX), OneIfZero(o.scaleY), OneIfZero(o.scaleZ));
            ApplyContent(go, o, sprites, specPath);
            return go;
        }

        // Brings the sprite, text and components of an object in line with the spec.
        // Works on a new object and on an existing one: it only sets what differs.
        private static void ApplyContent(GameObject go, SpecObject o, Dictionary<string, Sprite> sprites,
            string specPath)
        {
            ApplySprite(go, o, sprites, specPath);
            ApplyText(go, o, specPath);

            if (o.components != null)
            {
                for (int i = 0; i < o.components.Length; i++)
                {
                    AddComponentByName(go, o.components[i], o, specPath);
                }
            }

            // Components the spec no longer asks for.
            if (!Listed(o, "ShipView")) DestroyIfPresent<ShipView>(go);
            if (!Listed(o, "InventoryView")) DestroyIfPresent<InventoryView>(go);
            if (!Listed(o, "WarningView")) DestroyIfPresent<WarningView>(go);
            if (!Listed(o, "CreditsView")) DestroyIfPresent<CreditsView>(go);
            if (!Listed(o, "DaySummaryView")) DestroyIfPresent<DaySummaryView>(go);
            if (string.IsNullOrEmpty(o.sprite) && !Listed(o, "SpriteRenderer")) DestroyIfPresent<SpriteRenderer>(go);

            // Hidden flag: an object with "hidden": true is saved inactive.
            bool wantActive = !o.hidden;
            if (go.activeSelf != wantActive) go.SetActive(wantActive);
        }

        private static void ApplySprite(GameObject go, SpecObject o, Dictionary<string, Sprite> sprites,
            string specPath)
        {
            if (string.IsNullOrEmpty(o.sprite)) return;

            Sprite sprite;
            if (!sprites.TryGetValue(o.sprite, out sprite))
            {
                throw Fail(specPath, o.name + ": unknown sprite key " + o.sprite);
            }
            SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
            if (sr == null) sr = go.AddComponent<SpriteRenderer>();
            if (sr.sprite != sprite) sr.sprite = sprite;
            Color color = ParseColor(o.color, specPath, o.name);
            if (sr.color != color) sr.color = color;
            if (sr.sortingOrder != o.sortingOrder) sr.sortingOrder = o.sortingOrder;
        }

        // A text element: a TextMesh with a built-in font, and a TextKeyView that
        // fills it from the text key. Sets only what differs. With no textKey, the
        // text parts are removed.
        private static void ApplyText(GameObject go, SpecObject o, string specPath)
        {
            if (string.IsNullOrEmpty(o.textKey))
            {
                if (!Listed(o, "TextKeyView")) DestroyIfPresent<TextKeyView>(go);
                TextMesh old = go.GetComponent<TextMesh>();
                if (old != null)
                {
                    UnityEngine.Object.DestroyImmediate(old);
                    DestroyIfPresent<MeshRenderer>(go);
                }
                return;
            }

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) throw Fail(specPath, o.name + ": the built-in font could not be loaded");

            TextAnchor anchor;
            TextAlignment alignment;
            switch (string.IsNullOrEmpty(o.textAlign) ? "center" : o.textAlign)
            {
                case "left":
                    anchor = TextAnchor.MiddleLeft;
                    alignment = TextAlignment.Left;
                    break;
                case "center":
                    anchor = TextAnchor.MiddleCenter;
                    alignment = TextAlignment.Center;
                    break;
                case "right":
                    anchor = TextAnchor.MiddleRight;
                    alignment = TextAlignment.Right;
                    break;
                default:
                    throw Fail(specPath, o.name + ": textAlign must be left, center or right");
            }
            if (o.textSize < 0f) throw Fail(specPath, o.name + ": textSize must not be negative");

            TextMesh tm = go.GetComponent<TextMesh>();
            if (tm == null) tm = go.AddComponent<TextMesh>();
            if (tm.font != font) tm.font = font;
            if (tm.fontSize != TextFontSize) tm.fontSize = TextFontSize;
            float characterSize = (o.textSize > 0f ? o.textSize : DefaultTextSize) / TextSizeDivisor;
            if (!Mathf.Approximately(tm.characterSize, characterSize)) tm.characterSize = characterSize;
            if (tm.anchor != anchor) tm.anchor = anchor;
            if (tm.alignment != alignment) tm.alignment = alignment;
            Color color = ParseColor(o.textColor, specPath, o.name);
            if (tm.color != color) tm.color = color;

            MeshRenderer mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                if (mr.sharedMaterial != font.material) mr.sharedMaterial = font.material;
                if (mr.sortingOrder != o.sortingOrder) mr.sortingOrder = o.sortingOrder;
            }

            bool refresh = false;
            TextKeyView view = go.GetComponent<TextKeyView>();
            if (view == null)
            {
                view = go.AddComponent<TextKeyView>();
                refresh = true;
            }
            if (view.textKey != o.textKey)
            {
                view.textKey = o.textKey;
                refresh = true;
            }
            if (view.textMesh != tm)
            {
                view.textMesh = tm;
                refresh = true;
            }
            if (refresh) view.Refresh();
        }

        // The fixed list of components a spec may attach. Each name is one direct,
        // compile-time reference. To allow a new type, add one case here. Every case
        // does nothing when the component is already there.
        private static void AddComponentByName(GameObject go, string name, SpecObject o, string specPath)
        {
            switch (name)
            {
                case "ShipView":
                    if (go.GetComponent<ShipView>() == null) go.AddComponent<ShipView>();
                    return;
                case "InventoryView":
                    if (go.GetComponent<InventoryView>() == null) go.AddComponent<InventoryView>();
                    return;
                case "WarningView":
                    if (go.GetComponent<WarningView>() == null) go.AddComponent<WarningView>();
                    return;
                case "CreditsView":
                    if (go.GetComponent<CreditsView>() == null) go.AddComponent<CreditsView>();
                    return;
                case "DaySummaryView":
                    if (go.GetComponent<DaySummaryView>() == null) go.AddComponent<DaySummaryView>();
                    return;
                case "TextKeyView":
                    if (go.GetComponent<TextKeyView>() == null) go.AddComponent<TextKeyView>();
                    return;
                case "SpriteRenderer":
                    if (go.GetComponent<SpriteRenderer>() == null) go.AddComponent<SpriteRenderer>();
                    return;
                case "Canvas":
                    Canvas canvas = go.GetComponent<Canvas>();
                    if (canvas == null) UiParts.MakeCanvas(go, o.sortingOrder);
                    else if (canvas.sortingOrder != o.sortingOrder) canvas.sortingOrder = o.sortingOrder;
                    return;
                case "Image":
                    if (go.GetComponent<CanvasRenderer>() == null) UiParts.MakeImage(go);
                    return;
                case "Button":
                    Component existingButton = UiParts.GetButton(go);
                    if (existingButton == null) UiParts.MakeButton(go);
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
                case "CreditsView":
                    c = go.GetComponent<CreditsView>();
                    break;
                case "DaySummaryView":
                    c = go.GetComponent<DaySummaryView>();
                    break;
                case "TextKeyView":
                    c = go.GetComponent<TextKeyView>();
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
                case "TextKeyView":
                    TextKeyView tv = go.GetComponent<TextKeyView>();
                    if (tv == null) throw Fail(specPath, go.name + " has no TextKeyView (give it a textKey)");
                    return tv;
                case "Button":
                    Component button = UiParts.GetButton(go);
                    if (button == null) throw Fail(specPath, go.name + " has no Button (add the \"Button\" component)");
                    return button;
                default:
                    throw Fail(specPath, "unknown targetType " + type);
            }
        }

        // Builds a whole prefab content from scratch (the prefab does not exist yet).
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

        // Opens the existing prefab, patches it to match the spec and saves it back
        // to the same asset, but only when the patch really changed something. On an
        // error nothing is saved. Returns true when the prefab was changed and saved.
        private static bool PatchPrefab(PrefabSpec spec, Dictionary<string, Sprite> sprites, string specPath)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(spec.prefabPath);
            try
            {
                string before = Signature(root);
                PatchContent(spec, root, sprites, specPath);
                string after = Signature(root);
                if (before == after)
                {
                    Debug.Log("UNCHANGED: " + spec.prefabPath);
                    return false;
                }
                PrefabUtility.SaveAsPrefabAsset(root, spec.prefabPath);
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ---- content signature: a text that changes when anything saved in the
        // prefab changes (objects, order, active flag, components, every property).

        private static string Signature(GameObject root)
        {
            StringBuilder sb = new StringBuilder();
            SignatureWalk(root.transform, root.transform, sb);
            return sb.ToString();
        }

        private static string PathIn(Transform t, Transform root)
        {
            if (t == root) return "<root>";
            string path = t.name;
            Transform p = t.parent;
            while (p != null && p != root)
            {
                path = p.name + "/" + path;
                p = p.parent;
            }
            return path;
        }

        private static void SignatureWalk(Transform t, Transform root, StringBuilder sb)
        {
            GameObject go = t.gameObject;
            sb.Append('[').Append(PathIn(t, root)).Append('|').Append(go.activeSelf ? '1' : '0').Append('\n');
            Component[] comps = go.GetComponents<Component>();
            for (int i = 0; i < comps.Length; i++)
            {
                Component c = comps[i];
                if (c == null) continue;
                sb.Append('<').Append(c.GetType().FullName).Append('\n');
                SerializedObject so = new SerializedObject(c);
                SerializedProperty p = so.GetIterator();
                bool enter = true;
                while (p.Next(enter))
                {
                    enter = true;
                    if (p.propertyType == SerializedPropertyType.Generic) continue;
                    sb.Append(p.propertyPath).Append('=').Append(ValueText(p, root)).Append('\n');
                }
            }
            for (int i = 0; i < t.childCount; i++) SignatureWalk(t.GetChild(i), root, sb);
        }

        private static string Num(float v)
        {
            return v.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string ValueText(SerializedProperty p, Transform root)
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
                    return Num(p.floatValue);
                case SerializedPropertyType.String:
                    return p.stringValue;
                case SerializedPropertyType.Color:
                    Color col = p.colorValue;
                    return Num(col.r) + "," + Num(col.g) + "," + Num(col.b) + "," + Num(col.a);
                case SerializedPropertyType.Vector2:
                    Vector2 v2 = p.vector2Value;
                    return Num(v2.x) + "," + Num(v2.y);
                case SerializedPropertyType.Vector3:
                    Vector3 v3 = p.vector3Value;
                    return Num(v3.x) + "," + Num(v3.y) + "," + Num(v3.z);
                case SerializedPropertyType.Vector4:
                    Vector4 v4 = p.vector4Value;
                    return Num(v4.x) + "," + Num(v4.y) + "," + Num(v4.z) + "," + Num(v4.w);
                case SerializedPropertyType.Quaternion:
                    Quaternion q = p.quaternionValue;
                    return Num(q.x) + "," + Num(q.y) + "," + Num(q.z) + "," + Num(q.w);
                case SerializedPropertyType.Rect:
                    Rect r = p.rectValue;
                    return Num(r.x) + "," + Num(r.y) + "," + Num(r.width) + "," + Num(r.height);
                case SerializedPropertyType.Bounds:
                    Bounds bd = p.boundsValue;
                    return Num(bd.center.x) + "," + Num(bd.center.y) + "," + Num(bd.center.z) + "," +
                        Num(bd.size.x) + "," + Num(bd.size.y) + "," + Num(bd.size.z);
                case SerializedPropertyType.ObjectReference:
                    return RefText(p.objectReferenceValue, root);
                default:
                    return p.propertyType.ToString();
            }
        }

        private static string RefText(UnityEngine.Object o, Transform root)
        {
            if (o == null) return "null";
            GameObject go = o as GameObject;
            Component comp = o as Component;
            if (comp != null) go = comp.gameObject;
            if (go != null && go.transform.root == root)
            {
                return "in:" + PathIn(go.transform, root) + ":" + o.GetType().Name;
            }
            return "asset:" + AssetDatabase.GetAssetPath(o) + ":" + o.name;
        }

        private static void CollectStale(Transform parent, string parentPath, HashSet<string> wanted,
            HashSet<string> seen, List<GameObject> stale)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform c = parent.GetChild(i);
                string path = parentPath.Length == 0 ? c.name : parentPath + "/" + c.name;
                if (wanted.Contains(path) && seen.Add(path))
                {
                    CollectStale(c, path, wanted, seen, stale);
                }
                else
                {
                    stale.Add(c.gameObject);
                }
            }
        }

        private static void PatchContent(PrefabSpec spec, GameObject root, Dictionary<string, Sprite> sprites,
            string specPath)
        {
            Dictionary<string, string> paths = ObjectPaths(spec, specPath);

            // Remove objects the spec does not list (matched by name path).
            HashSet<string> wanted = new HashSet<string>();
            foreach (KeyValuePair<string, string> pair in paths)
            {
                if (pair.Value.Length > 0) wanted.Add(pair.Value);
            }
            List<GameObject> stale = new List<GameObject>();
            CollectStale(root.transform, "", wanted, new HashSet<string>(), stale);
            for (int i = 0; i < stale.Count; i++)
            {
                if (stale[i] != null) UnityEngine.Object.DestroyImmediate(stale[i]);
            }

            if (root.name != spec.root.name) root.name = spec.root.name;
            Dictionary<string, GameObject> map = new Dictionary<string, GameObject>();
            map[spec.root.name] = root;
            ApplyContent(root, spec.root, sprites, specPath);
            ApplyTransform(root.transform, spec.root);

            Dictionary<string, int> nextIndex = new Dictionary<string, int>();
            if (spec.objects != null)
            {
                for (int i = 0; i < spec.objects.Length; i++)
                {
                    SpecObject o = spec.objects[i];
                    string path = paths[o.name];
                    string parentPath = ParentOfPath(path);
                    Transform parent = parentPath.Length == 0 ? root.transform : root.transform.Find(parentPath);
                    if (parent == null) throw Fail(specPath, o.name + ": parent object could not be found");

                    Transform existing = root.transform.Find(path);
                    GameObject go;
                    if (existing == null)
                    {
                        go = MakeObject(o, parent, sprites, specPath);
                    }
                    else
                    {
                        go = existing.gameObject;
                        ApplyContent(go, o, sprites, specPath);
                        ApplyTransform(go.transform, o);
                    }
                    map[o.name] = go;

                    // Child order follows the spec order.
                    int index;
                    nextIndex.TryGetValue(parentPath, out index);
                    if (go.transform.GetSiblingIndex() != index) go.transform.SetSiblingIndex(index);
                    nextIndex[parentPath] = index + 1;
                }
            }

            if (spec.links != null)
            {
                for (int i = 0; i < spec.links.Length; i++)
                {
                    ApplyLink(spec.links[i], map, specPath);
                }
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
                if (element.objectReferenceValue != value) element.objectReferenceValue = value;
            }
            else
            {
                if (!string.IsNullOrEmpty(link.subField))
                {
                    throw Fail(specPath, "subField needs an array element (index 0 or more) on " + link.field);
                }
                if (prop.objectReferenceValue != value) prop.objectReferenceValue = value;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static bool SameExact(Vector3 a, Vector3 b)
        {
            return a.x == b.x && a.y == b.y && a.z == b.z;
        }

        // Puts one instance of the prefab in the open scene, or updates the existing
        // one. With screen set, the rectangle (reference pixels) decides the position
        // and scale, and the ship's content is centered with the same ShipView code
        // StationView uses. An existing instance is kept: only position and scale
        // are set, and only when they differ. changed tells if the scene was changed.
        private static GameObject PlaceOne(GameObject prefab, GameObject existing, string name, Camera cam,
            SpecScene sc, float rx, float ry, float rw, float rh, bool screen, out bool changed)
        {
            GameObject instance = existing;
            if (instance == null)
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                instance.name = name;
            }
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

            Vector3 oldPos = instance.transform.position;
            Vector3 oldScale = instance.transform.localScale;
            Vector3 newScale = new Vector3(scale, scale, 1f);
            if (existing == null || instance.transform.localScale != newScale)
            {
                instance.transform.localScale = newScale;
            }
            instance.transform.position = pos;

            // Same centering rule as StationView: the center of the body and bars.
            if (screen && view != null) view.CenterOn(pos);

            // Keep the exact old position when nothing really changed.
            if (existing != null && instance.transform.position == oldPos)
            {
                instance.transform.position = oldPos;
            }

            changed = existing == null ||
                !SameExact(instance.transform.position, oldPos) ||
                !SameExact(instance.transform.localScale, oldScale);
            return instance;
        }

        // Finds the scene instances this spec owns (root-level objects named like the
        // spec's root or its bay instances). Keeps the first prefab instance for every
        // wanted name and removes every other matching object (a bay that is gone, a
        // repeated name, or an object that is not an instance of this prefab).
        private static Dictionary<string, GameObject> SelectInstances(string rootName, string prefabPath,
            HashSet<string> desired, out bool removed)
        {
            removed = false;
            string bayPrefix = rootName + BaySuffix;
            GameObject[] all = UnityEngine.Object.FindObjectsByType<GameObject>(
                FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);

            Dictionary<string, GameObject> kept = new Dictionary<string, GameObject>();
            List<GameObject> matching = new List<GameObject>();
            for (int i = 0; i < all.Length; i++)
            {
                GameObject g = all[i];
                if (g == null || g.transform.parent != null) continue;
                if (!(g.name == rootName || g.name.StartsWith(bayPrefix, StringComparison.Ordinal))) continue;
                matching.Add(g);
                if (desired.Contains(g.name) && !kept.ContainsKey(g.name) &&
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(g) == prefabPath)
                {
                    kept[g.name] = g;
                }
            }
            for (int i = 0; i < matching.Count; i++)
            {
                if (!kept.ContainsValue(matching[i]))
                {
                    UnityEngine.Object.DestroyImmediate(matching[i]);
                    removed = true;
                }
            }
            return kept;
        }

        private static StationView GetStation(out bool created)
        {
            created = false;
            StationView station = UnityEngine.Object.FindFirstObjectByType<StationView>();
            if (station == null)
            {
                GameObject st = new GameObject("Station");
                station = st.AddComponent<StationView>();
                created = true;
            }
            return station;
        }

        // Returns true when the StationView field was changed.
        private static bool ConnectStation(StationView station, string field, GameObject instance, string specPath)
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
            else if (propType.Contains("CreditsView"))
            {
                component = instance.GetComponent<CreditsView>();
                needed = "CreditsView";
            }
            else if (propType.Contains("DaySummaryView"))
            {
                component = instance.GetComponent<DaySummaryView>();
                needed = "DaySummaryView";
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
            if (prop.objectReferenceValue != component)
            {
                prop.objectReferenceValue = component;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(station);
                return true;
            }
            return false;
        }

        private static int CountSceneObjects()
        {
            return UnityEngine.Object.FindObjectsByType<GameObject>(
                FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
        }

        // Full path of the screenshot file in the temp folder outside the project.
        private static string ShotPath(string fileName)
        {
            string dir = Path.Combine(Path.GetTempPath(), ShotFolder);
            return Path.GetFullPath(Path.Combine(dir, fileName));
        }

        // changed comes in as "the prefab or a sprite changed" and goes out as
        // "anything in this spec changed" (the scene included).
        private static void PlaceInScene(PrefabSpec spec, string specPath, ref bool changed, out bool photographed)
        {
            photographed = false;
            SpecScene sc = spec.scene;
            if (!File.Exists(sc.path)) throw Fail(specPath, "scene not found: " + sc.path);

            EditorSceneManager.OpenScene(sc.path, OpenSceneMode.Single);
            bool sceneChanged = false;

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
                sceneChanged = true;
            }
            if (!cam.orthographic)
            {
                cam.orthographic = true;
                cam.orthographicSize = 5f;
                sceneChanged = true;
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

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(spec.prefabPath);
            if (prefab == null) throw Fail(specPath, "prefab could not be loaded: " + spec.prefabPath);

            // Keep the instances that are already there, remove the ones the spec no
            // longer lists, add the missing ones.
            HashSet<string> desired = new HashSet<string>();
            if (hasBays)
            {
                for (int i = 0; i < sc.bays.Length; i++) desired.Add(InstanceName(spec.root.name, sc.bays[i].bay));
            }
            else
            {
                desired.Add(spec.root.name);
            }
            bool removedAny;
            Dictionary<string, GameObject> kept = SelectInstances(spec.root.name, spec.prefabPath, desired,
                out removedAny);
            if (removedAny) sceneChanged = true;

            List<GameObject> instances = new List<GameObject>();
            List<string> fields = new List<string>();
            bool placedChanged;
            if (hasBays)
            {
                for (int i = 0; i < sc.bays.Length; i++)
                {
                    SpecBay b = sc.bays[i];
                    string instanceName = InstanceName(spec.root.name, b.bay);
                    GameObject existing;
                    kept.TryGetValue(instanceName, out existing);
                    instances.Add(PlaceOne(prefab, existing, instanceName, cam, sc,
                        b.rectX, b.rectY, b.rectW, b.rectH, true, out placedChanged));
                    if (placedChanged) sceneChanged = true;
                    fields.Add(string.IsNullOrEmpty(b.stationField) ? "bay" + b.bay + "View" : b.stationField);
                }
            }
            else
            {
                GameObject existing;
                kept.TryGetValue(spec.root.name, out existing);
                instances.Add(PlaceOne(prefab, existing, spec.root.name, cam, sc,
                    sc.rectX, sc.rectY, sc.rectW, sc.rectH, sc.rectW > 0f, out placedChanged));
                if (placedChanged) sceneChanged = true;
                fields.Add(sc.stationField);
            }

            StationView station = null;
            for (int i = 0; i < instances.Count; i++)
            {
                if (string.IsNullOrEmpty(fields[i])) continue;
                if (station == null)
                {
                    bool created;
                    station = GetStation(out created);
                    if (created) sceneChanged = true;
                }
                if (ConnectStation(station, fields[i], instances[i], specPath)) sceneChanged = true;
            }

            GameObject first = instances[0];
            ShipView view = first.GetComponent<ShipView>();
            InventoryView invView = first.GetComponent<InventoryView>();
            WarningView warnView = first.GetComponent<WarningView>();
            CreditsView creditsView = first.GetComponent<CreditsView>();
            DaySummaryView summaryView = first.GetComponent<DaySummaryView>();

            // The warning's and the summary's UI buttons need an event system in the scene.
            if (warnView != null || summaryView != null)
            {
                int objectsBefore = CountSceneObjects();
                UiParts.EnsureEventSystem();
                if (CountSceneObjects() != objectsBefore) sceneChanged = true;
            }

            if (sceneChanged)
            {
                EditorSceneManager.MarkSceneDirty(first.scene);
                EditorSceneManager.SaveScene(first.scene);
                changed = true;
            }
            else
            {
                Debug.Log("UNCHANGED: " + sc.path);
            }
            Debug.Log("MADE: " + sc.path);

            bool requested = !string.IsNullOrEmpty(sc.screenshot);
            bool shotExists = requested && File.Exists(ShotPath(sc.screenshot));
            if (ScreenshotDecision.ShouldPhotograph(requested, changed, shotExists))
            {
                if (view == null && invView == null && warnView == null && creditsView == null &&
                    summaryView == null)
                {
                    throw Fail(specPath,
                        "a screenshot needs a ShipView, an InventoryView, a WarningView, a CreditsView or a DaySummaryView on the prefab root");
                }
                List<ShipView> others = new List<ShipView>();
                for (int i = 1; i < instances.Count; i++)
                {
                    ShipView v = instances[i].GetComponent<ShipView>();
                    if (v != null) others.Add(v);
                }
                int w = screenPlacement ? Mathf.RoundToInt(sc.refWidth) : 1280;
                int h = screenPlacement ? Mathf.RoundToInt(sc.refHeight) : 720;
                TakeScreenshot(view, others, invView, warnView, creditsView, summaryView, sc.sampleDialog, cam,
                    sc.screenshot, SampleOverrides(sc, specPath), w, h, specPath);
                photographed = true;
            }

            if (screenPlacement) cam.ResetAspect();

            // Load the saved scene again, so the shown sample state (bars, text, the
            // open dialog) is dropped and never carried into the next build.
            EditorSceneManager.OpenScene(sc.path, OpenSceneMode.Single);
        }

        // Gives every ShipView in the open scene its own sample ship (seeds from
        // firstSeed), so no '{' text shows. Not saved: done after the scene is saved.
        private static void FillSampleShips(BalanceData balance, int surplusMargin, int firstSeed)
        {
            ShipView[] views = UnityEngine.Object.FindObjectsByType<ShipView>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            Array.Sort(views, (a, b) => string.CompareOrdinal(a.name, b.name));
            for (int i = 0; i < views.Length; i++)
            {
                Ship s = ShipFactory.Create(balance, balance.days[0], new System.Random(firstSeed + i));
                DockingBays db = new DockingBays();
                db.TryDock(s);
                views[i].Bind(new BuyRelease(db, new Inventory(), balance), null);
                views[i].Show(s, surplusMargin, null);
            }
        }

        // A copy of the balance in which no day has ships. A run made from it ends
        // its first day as soon as it starts: the real rules charge the upkeep and
        // build the day summary. Used only for the day summary sample.
        private static BalanceData EmptyDaysCopy(BalanceData balance)
        {
            BalanceData copy = JsonUtility.FromJson<BalanceData>(JsonUtility.ToJson(balance));
            if (copy.days != null)
            {
                for (int i = 0; i < copy.days.Length; i++)
                {
                    if (copy.days[i] != null) copy.days[i].ships = 0;
                }
            }
            return copy;
        }

        // Done after the scene is saved: showing the sample changes bar scales and
        // colors, and that must not end up in the scene file.
        private static void TakeScreenshot(ShipView view, List<ShipView> others, InventoryView invView,
            WarningView warnView, CreditsView creditsView, DaySummaryView summaryView, string dialog, Camera cam,
            string fileName, int[] conditions, int w, int h, string specPath)
        {
            BalanceData balance = LoadBalance();
            if (balance == null) throw Fail(specPath, "the balance could not be loaded for the sample ship");

            // Text elements read the shared content; in batch mode nothing else sets it.
            TextKeyView.SetShared(LoadContent());

            // Every shot except the warning shot is taken with the warning dialog
            // closed. In edit mode WarningView.Awake does not run, so the dialog
            // would stay open (with raw text) in the scene.
            if (warnView == null)
            {
                WarningView sceneWarning = UnityEngine.Object.FindFirstObjectByType<WarningView>(
                    FindObjectsInactive.Include);
                if (sceneWarning != null) sceneWarning.Close();
            }

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
                // Every bay ship view in the scene gets its own sample ship.
                FillSampleShips(balance, surplusMargin, 2);

                BuyRelease commands = new BuyRelease(bays, inventory, balance);
                warnView.Bind(commands, LoadContent());
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
                    // Opens the release warning: the mechanic's line, payment,
                    // penalty and net lines are filled from the sample ship.
                    if (!warnView.OpenRelease(ship)) throw Fail(specPath, "the sample ship cannot be released");
                }
            }
            else if (creditsView != null)
            {
                // Every bay ship view in the scene gets its own sample ship.
                FillSampleShips(balance, surplusMargin, 2);

                // The credits of a sample run: the starting credits from balance.json.
                Run run = RunFactory.Create(balance, LoadContent(), 1);
                creditsView.Bind(run);
            }
            else if (summaryView != null)
            {
                // Every bay ship view in the scene gets its own sample ship.
                FillSampleShips(balance, surplusMargin, 2);

                // Bind first, then play a sample day through the run's own rules: the
                // day ends at once (no ships), and the view opens with the summary.
                Run run = RunFactory.Create(EmptyDaysCopy(balance), LoadContent(), 1);
                summaryView.Bind(run, null, null);
                RunResult started = run.StartDay();
                if (!started.Success) throw Fail(specPath, "the sample run could not start its day");
                if (!summaryView.IsOpen) throw Fail(specPath, "the day summary did not open for the sample day");
            }
            else
            {
                // Every bay ship view in the scene gets its own sample ship.
                FillSampleShips(balance, surplusMargin, 2);

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
            UnityEngine.Object.DestroyImmediate(rt);

            string full = ShotPath(fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllBytes(full, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            Debug.Log("SCREENSHOT: " + full);
        }
    }
}

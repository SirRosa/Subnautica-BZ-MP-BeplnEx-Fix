using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Sirro.BZ.MP.ConstrutorZero;
using UnityEngine;
using HarmonyLib;

public static class Program
{
    private static int count;
    private static void Assert(bool ok, string name)
    {
        if (!ok) throw new Exception("FAIL " + name);
        count++;
        Console.WriteLine("PASS " + name);
    }
    private static void Eq(float actual, float expected, string name) =>
        Assert(Math.Abs(actual - expected) < 0.001f, name + " (got " + actual + ")");

    public static void Main()
    {
        AbiTests.Run(Assert);
        var plugin = new MainPlugin();
        Assert(plugin.PluginType.ToString() == "Client", "Native plugin type");
        Assert(plugin.Name.StartsWith("Construtor Zero"), "One combined plugin");

        var box = new GameObject();
        var constructable = new Constructable();
        box.AddComponent(constructable);
        Builder.prefab = box;
        Builder.ghostModel = new GameObject();

        // Install BOT Benson-like prefixes BEFORE the native Construtor Zero
        // plugin, preserving the actual multiplayer startup order.
        var mpHarmony = new Harmony("bot-benson-simulation");
        var builderTryPlace = typeof(Builder).GetMethod("TryPlace");
        var identityPatch = new HarmonyMethod(typeof(FakeMultiplayerConstruction)
            .GetMethod("IdentityPrefix")) { priority = Priority.First };
        var packetPatch = new HarmonyMethod(typeof(FakeMultiplayerConstruction)
            .GetMethod("ServerPacketPrefix")) { priority = Priority.Normal };
        mpHarmony.Patch(builderTryPlace, prefix: identityPatch);
        mpHarmony.Patch(builderTryPlace, prefix: packetPatch);

        plugin.OnEnabled();
        var patches = Harmony.GetAllPatchedMethods().ToList();
        Assert(patches.Any(m => m.Name == "SetPlaceOnSurface"), "Harmony Snap hook installed");
        Assert(patches.Any(m => m.Name == "Update"), "Harmony Builder Update hook installed");
        Assert(patches.Any(m => m.Name == "UpdateAllowed"), "Harmony validation hook installed");
        Assert(patches.Any(m => m.Name == "GetObstacles"), "Harmony obstacle List<GameObject> hook installed");
        Assert(patches.Any(m => m.Name == "IsPowered"), "Harmony power hook installed");
        Assert(patches.Any(m => m.DeclaringType == typeof(StorageContainer)),
            "CraftFromContainers storage registration hooks installed");
        Assert(patches.Any(m => m.DeclaringType == typeof(Inventory)),
            "CraftFromContainers inventory observation hooks installed");
        var cfcType = plugin.GetType().Assembly.GetType(
            "Sirro.BZ.MP.ConstrutorZero.CraftFromContainersBridge", true);
        var storages = cfcType.GetProperty("RegisteredStorages",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        var cfcReady = cfcType.GetProperty("Ready",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert((bool)cfcReady.GetValue(null),
            "All essential CFC Harmony hooks must exist before activation");
        Subnautica.API.Features.Network.Identifier = new FakeIdentifier();
        Player.main.transform.position = new Vector3(0, 0, 0);
        var locker = new StorageContainer();
        locker.storageRoot.position = new Vector3(2, 0, 0);
        locker.storageRoot.gameObject.name = "locker-a";
        locker.storageRoot.gameObject.AddComponent(new Constructable {
            techType = TechType.Locker, gameObject = locker.storageRoot.gameObject
        });
        locker.container.Add(new InventoryItem {
            item = new Pickupable { techType = TechType.Titanium,
                gameObject = new GameObject { name = "item-a" } }
        });
        locker.Awake();
        Assert((int)storages.GetValue(null) == 1, "Storage registered on Awake");
        var requestOne = cfcType.GetMethod("TryRequestOne",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        locker.storageRoot.position = new Vector3(250, 0, 0);
        Assert(!(bool)requestOne.Invoke(null, new object[] { TechType.Titanium }),
            "Out-of-range locker cannot be used for crafting");
        locker.storageRoot.position = new Vector3(2, 0, 0);
        var confType = plugin.GetType().Assembly.GetType(
            "Sirro.BZ.MP.ConstrutorZero.ContainerConfig", true);
        var allowedField = confType.GetField("Allowed", BindingFlags.Static |
            BindingFlags.Public | BindingFlags.NonPublic);
        var allowed = (IDictionary<string, bool>)allowedField.GetValue(null);
        allowed["Locker"] = false;
        Assert(!(bool)requestOne.Invoke(null, new object[] { TechType.Titanium }),
            "Disabled locker type is excluded");
        allowed["Locker"] = true;
        Assert((bool)requestOne.Invoke(null, new object[] { TechType.Titanium }),
            "Server transfer requested using storage event");
        Assert(Subnautica.Events.Handlers.Storage.NetworkRequests == 1 &&
            Subnautica.Events.Handlers.Storage.ServerAccepted == 1,
            "Server accepted transfer for concrete unique item ID");
        Assert(!(bool)requestOne.Invoke(null, new object[] { TechType.Titanium }),
            "Duplicate in-flight request prevented");
        Assert(Subnautica.Events.Handlers.Storage.NetworkRequests == 1,
            "No duplicate storage packet submitted");
        Assert(locker.container.Count == 1,
            "Server transfer does not silently delete local items before ack");
        Assert(Inventory.main.GetPickupCount(TechType.Titanium) == 0,
            "Craft ingredients are NOT forged without server approval");
        Assert(!Inventory.main.DestroyItem(TechType.Titanium, false),
            "Craft resource removal is NOT forged on client");
        locker.ResetContainer();
        Assert((int)storages.GetValue(null) == 1,
            "Registry refreshed on actual StorageContainer.ResetContainer");
        // Multi-ingredient, multi-packet prefetch: the recipe is observed but does
        // not cause automatic consumption, and a fake client inventory delivery
        // must carry the SAME server-authorized item ID before the next request.
        var manual = confType.GetField("ManualPrefetchEnabled",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        manual.SetValue(null, true);
        var recipe = new uGUI_RecipeEntry { techType = TechType.TestRecipe };
        recipe.UpdateIngredients(null, false);
        var tracker = plugin.GetType().Assembly.GetType(
            "Sirro.BZ.MP.ConstrutorZero.RecipePrefetch", true);
        var start = tracker.GetMethod("BeginLastObserved",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        var pump = tracker.GetMethod("Pump",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        var active = tracker.GetProperty("IsActive",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        var inFlight = tracker.GetProperty("InFlightId",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        Action<string> log = msg => Console.WriteLine("CFC: " + msg);
        var second = new InventoryItem { item = new Pickupable {
            techType = TechType.Titanium, gameObject = new GameObject { name = "item-b" } } };
        var third = new InventoryItem { item = new Pickupable {
            techType = TechType.Titanium, gameObject = new GameObject { name = "item-c" } } };
        locker.container.Add(second);
        locker.container.Add(third);
        Assert((bool)start.Invoke(null, new object[] { log }),
            "Complete recipe prepared from TechData without mutating inventory");
        Assert(Subnautica.Events.Handlers.Storage.NetworkRequests == 1,
            "UI recipe preview did not send unsolicited requests");
        pump.Invoke(null, new object[] { log });
        Assert((string)inFlight.GetValue(null) == "item-b",
            "First missing ingredient enters server-only in-flight state");
        Assert(Subnautica.Events.Handlers.Storage.NetworkRequests == 2,
            "Only one new server transfer requested");
        Inventory.main.OnAddItem(new InventoryItem { item = new Pickupable {
            techType = TechType.Copper, gameObject = new GameObject { name = "intruder" } } });
        Assert((string)inFlight.GetValue(null) == "item-b",
            "Unrelated inventory delivery does not acknowledge pending transfer");
        var acknowledge = tracker.GetMethod("AcknowledgeInventoryItem",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert(!(bool)acknowledge.Invoke(null, new object[] {
                "item-b", TechType.Copper, log }) &&
            (string)inFlight.GetValue(null) == "item-b",
            "Matching ID with wrong TechType must not acknowledge transfer");
        pump.Invoke(null, new object[] { log });
        Assert(Subnautica.Events.Handlers.Storage.NetworkRequests == 2,
            "Second transfer blocked until matching inventory acknowledgement");
        Inventory.main.OnAddItem(second);
        Assert((string)inFlight.GetValue(null) == null,
            "Matching Inventory.OnAddItem confirms delivery");
        locker.container.Remove(second);
        pump.Invoke(null, new object[] { log });
        Assert((string)inFlight.GetValue(null) == "item-c",
            "Second ingredient can be fetched only after first acknowledgement");
        Assert(Subnautica.Events.Handlers.Storage.NetworkRequests == 3,
            "Server receives the second ingredient request");
        Inventory.main.OnAddItem(third);
        Assert(!(bool)active.GetValue(null), "Recipe transfer queue completed safely");
        Assert(Inventory.main.GetPickupCount(TechType.Titanium) == 2,
            "Ingredient counts change ONLY on actual Inventory.OnAddItem acknowledgements");
        Assert(Inventory.main.GetPickupCount(TechType.Copper) == 1,
            "Unrelated item delivery is tracked by vanilla inventory");
        // Simulate consuming the delivered ingredients with vanilla crafting.
        Inventory.main.ActualItems.Clear();
        var currentContext = tracker.GetMethod("BeginCurrentContext",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        var cancel = tracker.GetMethod("Cancel",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Builder.constructableTechType = TechType.TestRecipe;
        Assert((bool)currentContext.Invoke(null, new object[] { log }),
            "Building recipe ingredients can be obtained from live Builder context");
        Assert((bool)active.GetValue(null), "Building prefetch is an explicit queued operation");
        Assert(Subnautica.Events.Handlers.Storage.NetworkRequests == 3,
            "Merely selecting a building recipe sends no storage packet");
        cancel.Invoke(null, new object[] { "cancel builder", log });
        Assert(!(bool)active.GetValue(null), "Builder prefetch can be cancelled without item mutations");
        Builder.constructableTechType = TechType.None;
        recipe.UpdateIngredients(null, false);
        Assert((bool)start.Invoke(null, new object[] { log }),
            "Another prefetch can be prepared after prior completion");
        Subnautica.API.Features.Network.IsMultiplayerActive = false;
        pump.Invoke(null, new object[] { log });
        Assert(!(bool)active.GetValue(null),
            "World disconnect cancels all outstanding ingredient requests");
        Subnautica.API.Features.Network.IsMultiplayerActive = true;
        manual.SetValue(null, false);

        // Transparent auto craft: the real uGUI_CraftingMenu.Action(Craft) is
        // deferred until two server-approved item transfers were received.
        var autoField = confType.GetField("AutoCraftEnabled",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        autoField.SetValue(null, true);
        locker.container.Clear(); // stale locker no longer offers any items
        var autoLocker = new StorageContainer();
        autoLocker.storageRoot.position = new Vector3(3, 0, 0);
        autoLocker.storageRoot.gameObject.name = "locker-a";
        autoLocker.storageRoot.gameObject.AddComponent(new Constructable {
            techType = TechType.Locker, gameObject = autoLocker.storageRoot.gameObject
        });
        var fourth = new InventoryItem { item = new Pickupable {
            techType = TechType.Titanium, gameObject = new GameObject { name = "item-d" } } };
        var fifth = new InventoryItem { item = new Pickupable {
            techType = TechType.Titanium, gameObject = new GameObject { name = "item-e" } } };
        autoLocker.container.Add(fourth);
        autoLocker.container.Add(fifth);
        autoLocker.Awake();
        var gui = new uGUI_CraftingMenu();
        var craftNode = new uGUI_CraftingMenu.Node {
            action = TreeAction.Craft, techType = TechType.TestRecipe };
        // UI-only: two real locker units can advertise a craftable recipe
        // without forging ANY local inventory resources or network request.
        int beforeUi = Subnautica.Events.Handlers.Storage.NetworkRequests;
        Assert(Inventory.main.GetPickupCount(TechType.Titanium) == 0,
            "Fabricator has zero local recipe materials before UI override");
        Assert(gui.ActionAvailable(craftNode),
            "Fabricator UI advertises complete nearby locker stock");
        Assert(Inventory.main.GetPickupCount(TechType.Titanium) == 0 &&
            Subnautica.Events.Handlers.Storage.NetworkRequests == beforeUi,
            "UI-only availability cannot mutate inventory or send a packet");
        Assert(gui.ActionAvailable(new uGUI_CraftingMenu.Node {
                action = TreeAction.Expand, techType = TechType.TestRecipe }),
            "Fabricator category navigation stays available");
        autoLocker.container.Remove(fifth);
        Time.frameCount++;  // invalidate per-frame advisory availability cache
        Assert(!gui.ActionAvailable(craftNode),
            "Insufficient locker ingredients do not unlock fabricator recipe");
        autoLocker.container.Add(fifth);
        Time.frameCount++;
        Assert(gui.ActionAvailable(craftNode),
            "Fabricator availability refreshes on newly stocked locker");
        gui.Action(new uGUI_CraftingMenu.Node {
            action = TreeAction.Expand, techType = TechType.TestRecipe });
        Assert(gui.ExpandActions == 1,
            "Category expansion unaffected by automatic crafting interception");
        CrafterLogic.IsUnlocked = false;
        Assert(!gui.ActionAvailable(craftNode),
            "Locker stock never unlocks a locked blueprint");
        int packetCountBeforeLockedRecipe = Subnautica.Events.Handlers.Storage.NetworkRequests;
        gui.Action(craftNode);
        Assert(Subnautica.Events.Handlers.Storage.NetworkRequests ==
            packetCountBeforeLockedRecipe,
            "Locked blueprint never triggers container transfer packets");
        var autoTypeForLock = plugin.GetType().Assembly.GetType(
            "Sirro.BZ.MP.ConstrutorZero.AutoCraftCoordinator", true);
        var waitingForLock = autoTypeForLock.GetProperty("IsWaiting",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        Assert(!(bool)waitingForLock.GetValue(null),
            "Locked blueprint cannot start deferred crafting");
        CrafterLogic.IsUnlocked = true;
        GameModeManager.CraftingRequiresResources = false;
        Assert(gui.ActionAvailable(craftNode),
            "Creative mode remains available via vanilla fabricator logic");
        int packetsBeforeCreative = Subnautica.Events.Handlers.Storage.NetworkRequests;
        gui.Action(craftNode);
        Assert(!(bool)waitingForLock.GetValue(null) &&
            Subnautica.Events.Handlers.Storage.NetworkRequests == packetsBeforeCreative,
            "Creative mode does not transfer any container ingredients");
        GameModeManager.CraftingRequiresResources = true;
        gui.CraftActionCalls = 0; // fake vanilla action has no blueprint validation
        gui.Action(craftNode);
        Assert(gui.CraftActionCalls == 0,
            "Craft action postponed rather than attempting free client-side crafting");
        var autoType = plugin.GetType().Assembly.GetType(
            "Sirro.BZ.MP.ConstrutorZero.AutoCraftCoordinator", true);
        var isWaiting = autoType.GetProperty("IsWaiting",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        Assert((bool)isWaiting.GetValue(null), "Craft waits for server ingredient delivery");
        Assert(!gui.ActionAvailable(craftNode),
            "UI cannot advertise another deferred craft while one is active");
        Time.frameCount++;
        Player.main.Update();
        Assert(Subnautica.Events.Handlers.Storage.NetworkRequests == 4,
            "Auto craft requested first ingredient through MP storage processor");
        Inventory.main.OnAddItem(fourth);
        autoLocker.container.Remove(fourth);
        Time.frameCount++;
        Player.main.Update();
        Assert(Subnautica.Events.Handlers.Storage.NetworkRequests == 5,
            "Auto craft requested second ingredient only after first server ACK");
        Assert(gui.CraftActionCalls == 0,
            "Vanilla craft still blocked while one ingredient remains outstanding");
        Inventory.main.OnAddItem(fifth);
        Time.frameCount++;
        Player.main.Update();
        Assert(gui.CraftActionCalls == 1,
            "Original craft action replayed exactly once after valid inventory delivery");
        Assert(!(bool)isWaiting.GetValue(null), "Auto craft state reset after replay");
        Assert(Inventory.main.GetPickupCount(TechType.Titanium) == 2,
            "Auto craft uses real inventory rather than synthesizing materials");
        Assert(gui.ActionAvailable(craftNode),
            "Fabricator availability returns to vanilla after real inventory delivery");
        // The inventory ACK is not proof that the locker UI has caught up
        // with the packet. A stale locker object must not re-request that ID.
        Assert(!(bool)requestOne.Invoke(null, new object[] { TechType.Titanium }),
            "ACKed item IDs cannot be requested again from stale locker state");

        // Closing the menu must cancel deferred actions without crafting anything.
        Inventory.main.ActualItems.Clear();
        gui.Action(craftNode);
        Assert((bool)isWaiting.GetValue(null), "Second deferred craft is pending");
        int priorExpansions = gui.ExpandActions;
        gui.Action(new uGUI_CraftingMenu.Node {
            action = TreeAction.Expand, techType = TechType.TestRecipe });
        Assert(gui.ExpandActions == priorExpansions + 1,
            "Category navigation remains functional during pending craft");
        Assert(!(bool)isWaiting.GetValue(null),
            "Navigating away cancels delayed craft before it can replay");
        gui.Action(craftNode);
        Assert((bool)isWaiting.GetValue(null),
            "A new deliberate craft click can stage a separate request");
        gui.Close();
        Assert(!(bool)isWaiting.GetValue(null),
            "Menu close cancels staged craft and ingredient queue");

        var selectedMenu = new uGUI_CraftingMenu();
        selectedMenu.Action(craftNode);
        Assert((bool)isWaiting.GetValue(null),
            "Craft prefetch staged before the UI loses selection");
        int beforeDeselect = Subnautica.Events.Handlers.Storage.NetworkRequests;
        selectedMenu.OnDeselect();
        Assert(!(bool)isWaiting.GetValue(null) && !(bool)active.GetValue(null) &&
            Subnautica.Events.Handlers.Storage.NetworkRequests == beforeDeselect,
            "Fabricator OnDeselect cancels queued transfers without further packets");

        // Loss of blueprint unlock or no-cost mode must cancel BEFORE another
        // pending locker item is requested, not merely before UI replay.
        var resyncMenu = new uGUI_CraftingMenu();
        resyncMenu.Action(craftNode);
        Assert((bool)isWaiting.GetValue(null),
            "Craft request staged for game-mode switch scenario");
        int packetsBeforeModeChange = Subnautica.Events.Handlers.Storage.NetworkRequests;
        GameModeManager.CraftingRequiresResources = false;
        Time.frameCount++;
        Player.main.Update();
        Assert(!(bool)isWaiting.GetValue(null) && !(bool)active.GetValue(null) &&
            Subnautica.Events.Handlers.Storage.NetworkRequests == packetsBeforeModeChange,
            "No extra locker request or UI replay after switch to free crafting");
        GameModeManager.CraftingRequiresResources = true;

        var relockedMenu = new uGUI_CraftingMenu();
        relockedMenu.Action(craftNode);
        Assert((bool)isWaiting.GetValue(null), "Craft staged before recipe relock");
        int packetsBeforeRelock = Subnautica.Events.Handlers.Storage.NetworkRequests;
        CrafterLogic.IsUnlocked = false;
        Time.frameCount++;
        Player.main.Update();
        Assert(!(bool)isWaiting.GetValue(null) && !(bool)active.GetValue(null) &&
            Subnautica.Events.Handlers.Storage.NetworkRequests == packetsBeforeRelock,
            "No extra locker request or UI replay after blueprint is relocked");
        CrafterLogic.IsUnlocked = true;
        Time.frameCount++;
        Player.main.Update();
        Assert(gui.CraftActionCalls == 1,
            "Cancelled deferred craft cannot replay after closing UI");
        autoField.SetValue(null, false);

        autoLocker.container.Clear();
        autoLocker.container.Add(new InventoryItem { item = new Pickupable {
            techType = TechType.Titanium,
            gameObject = new GameObject { name = "item-f" } } });
        int beforeUnavailable = Subnautica.Events.Handlers.Storage.NetworkRequests;
        Subnautica.Events.Handlers.Storage.HasStorageHandler = false;
        Assert(!(bool)requestOne.Invoke(null, new object[] { TechType.Titanium }),
            "Missing multiplayer storage processor does not count as a transfer request");
        Assert(Subnautica.Events.Handlers.Storage.NetworkRequests == beforeUnavailable,
            "No fake network packet when storage handler is unavailable");
        Subnautica.Events.Handlers.Storage.HasStorageHandler = true;

        // Missing building materials use the SAME authoritative transfer queue,
        // but never call Builder.TryPlace until the exact selected ghost pose is
        // unchanged and all materials exist in the actual local inventory.
        Inventory.main.ActualItems.Clear();
        autoLocker.container.Clear();
        var buildFirst = new InventoryItem { item = new Pickupable {
            techType = TechType.Titanium, gameObject = new GameObject { name = "item-g" } } };
        var buildSecond = new InventoryItem { item = new Pickupable {
            techType = TechType.Titanium, gameObject = new GameObject { name = "item-h" } } };
        autoLocker.container.Add(buildFirst);
        autoLocker.container.Add(buildSecond);
        var autoBuildField = confType.GetField("AutoBuildEnabled",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        var autoPlaceField = confType.GetField("AutoPlaceAfterPrefetch",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        autoBuildField.SetValue(null, true);
        autoPlaceField.SetValue(null, true);
        Builder.constructableTechType = TechType.TestRecipe;
        Builder.placePosition = new Vector3(5, 6, 7);
        Builder.placeRotation = Quaternion.Euler(new Vector3(0, 90, 0));
        Builder.CanPlace = true;
        Builder.PlacementCalls = 0;
        Assert(!Builder.TryPlace(), "Initial building action deferred before any resource consumption");
        Assert(Builder.PlacementCalls == 0,
            "Vanilla Builder.TryPlace never runs before all server receipts");
        Assert(FakeMultiplayerConstruction.PacketsSent == 0,
            "No BOT Benson construction packet before missing resources are delivered");
        var patchOrder = Harmony.GetPatchInfo(builderTryPlace).Prefixes
            .OrderByDescending(p => p.priority).ToList();
        Assert(patchOrder[0].owner == "sirro.bz.mp.construtor-zero.building" &&
            patchOrder[0].priority > patchOrder[1].priority,
            "Construtor ingredient guard runs before MP identity and packet prefixes");
        var autoBuildType = plugin.GetType().Assembly.GetType(
            "Sirro.BZ.MP.ConstrutorZero.AutoBuildCoordinator", true);
        var buildWaiting = autoBuildType.GetProperty("IsWaiting",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert((bool)buildWaiting.GetValue(null),
            "Building prefetch waits with original construction pose recorded");
        Time.frameCount++;
        Player.main.Update();
        Assert(Builder.PlacementCalls == 0 &&
            FakeMultiplayerConstruction.PacketsSent == 0,
            "No original placement OR multiplayer packet before ingredient acknowledgement");
        Inventory.main.OnAddItem(buildFirst);
        autoLocker.container.Remove(buildFirst);
        Time.frameCount++;
        Player.main.Update();
        Assert(Builder.PlacementCalls == 0 &&
            FakeMultiplayerConstruction.PacketsSent == 0,
            "Partial server delivery cannot send construction packet");
        // Pending placement must stay blocked even if the game mode changes
        // or the ghost temporarily becomes invalid before the next frame.
        GameModeManager.CraftingRequiresResources = false;
        Assert(!Builder.TryPlace() && FakeMultiplayerConstruction.PacketsSent == 0,
            "Pending building still blocked if Creative mode toggles");
        GameModeManager.CraftingRequiresResources = true;
        Builder.CanPlace = false;
        Assert(!Builder.TryPlace() && FakeMultiplayerConstruction.PacketsSent == 0,
            "Pending building still blocked if placement becomes invalid");
        Builder.CanPlace = true;
        Inventory.main.OnAddItem(buildSecond);
        // Race: the second click happens AFTER the last item entered inventory
        // but BEFORE the coordinator's next scheduled replay frame.
        Assert(!Builder.TryPlace(),
            "Second building click stays deferred after ingredients arrive but before replay");
        Assert(FakeMultiplayerConstruction.PacketsSent == 0,
            "No premature multiplayer construction packet in final-ACK race window");
        Time.frameCount++;
        Player.main.Update();
        Assert(FakeMultiplayerConstruction.PacketsSent == 1 &&
            Builder.PlacementCalls == 0,
            "One BOT Benson construction request only after two server receipts; vanilla suppressed");
        Assert(!(bool)buildWaiting.GetValue(null), "Auto build state reset after placement");
        Time.frameCount++;
        Player.main.Update();
        Assert(FakeMultiplayerConstruction.PacketsSent == 1 &&
            Builder.PlacementCalls == 0, "Auto build cannot emit duplicate multiplayer packets");

        Inventory.main.ActualItems.Clear();
        Builder.placePosition = new Vector3(5, 6, 7);
        Builder.placeRotation = Quaternion.Euler(new Vector3(0, 90, 0));
        Assert(!Builder.TryPlace(), "Second construction attempt staged");
        Builder.placePosition = new Vector3(6, 6, 7);
        int countBeforePoseChange = Subnautica.Events.Handlers.Storage.NetworkRequests;
        Time.frameCount++;
        Player.main.Update();
        Assert(!(bool)buildWaiting.GetValue(null),
            "Moving the construction ghost cancels delayed placement");
        Assert(FakeMultiplayerConstruction.PacketsSent == 1 &&
            Builder.PlacementCalls == 0 &&
            Subnautica.Events.Handlers.Storage.NetworkRequests == countBeforePoseChange,
            "Changed construction pose sends no further MP packet or placement");
        Builder.CanPlace = false;
        var buildPrefix = autoBuildType.GetMethod("Prefix",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        int beforeInvalid = Subnautica.Events.Handlers.Storage.NetworkRequests;
        Assert((bool)buildPrefix.Invoke(null, new object[] { false }),
            "Invalid ghost placement passes through original game's rejection path");
        Assert(!(bool)buildWaiting.GetValue(null) &&
            Subnautica.Events.Handlers.Storage.NetworkRequests == beforeInvalid,
            "No container transfers or deferred construction for invalid placement");
        Builder.CanPlace = true;
        GameModeManager.CraftingRequiresResources = false;
        int beforeCreativeBuilding = Subnautica.Events.Handlers.Storage.NetworkRequests;
        Assert((bool)buildPrefix.Invoke(null, new object[] { false }),
            "Creative mode building follows the original no-cost placement path");
        Assert(!(bool)buildWaiting.GetValue(null) &&
            Subnautica.Events.Handlers.Storage.NetworkRequests == beforeCreativeBuilding,
            "Creative construction does not request locker ingredients");
        GameModeManager.CraftingRequiresResources = true;
        autoBuildField.SetValue(null, false);
        autoPlaceField.SetValue(null, false);
        Builder.constructableTechType = TechType.None;

        var hit = new RaycastHit { normal = new Vector3(0,1,0), transform = new Transform() };
        var pos = new Vector3(1.37f, 0f, 2.74f);
        var rot = Quaternion.Euler(new Vector3(0,43f,0));
        Builder.SetPlaceOnSurface(hit, ref pos, ref rot);
        Eq(pos.x,1.5f,"Native snap x");
        Eq(pos.z,2.5f,"Native snap z");
        Eq(rot.eulerAngles.y,45f,"Rotation grid 45 degrees");

        var wallHit = new RaycastHit { normal = new Vector3(1,0,0), transform = null };
        var wallPosition = new Vector3(0f,1.37f,2.74f);
        var wallRotation = Quaternion.Euler(new Vector3(0,43f,0));
        Builder.SetPlaceOnSurface(wallHit, ref wallPosition, ref wallRotation);
        Eq(wallRotation.eulerAngles.y,43f,
            "Wall ghost is not incorrectly rotated by floor snap");
        var ceilingHit = new RaycastHit {
            normal = new Vector3(0,-1f,0), transform = null
        };
        var ceilingPosition = new Vector3(1.37f,0f,2.74f);
        var ceilingRotation = Quaternion.Euler(new Vector3(0,43f,0));
        Builder.SetPlaceOnSurface(ceilingHit,
            ref ceilingPosition, ref ceilingRotation);
        Eq(ceilingRotation.eulerAngles.y,43f,
            "Ceiling ghost is not incorrectly rotated by floor snap");

        // Regression: Builder.prefab is a field in the real game; it may be
        // a property in the real BZ game; base modules must never be snapped.
        var basePrefab = new GameObject();
        basePrefab.AddComponent(new ConstructableBase());
        Builder.prefab = basePrefab;
        var basePosition = new Vector3(1.37f, 0f, 2.74f);
        var baseRotation = Quaternion.Euler(new Vector3(0,43f,0));
        Builder.SetPlaceOnSurface(hit, ref basePosition, ref baseRotation);
        Eq(basePosition.x,1.37f,
            "Base-piece prefab field bypasses position snap");
        Eq(basePosition.z,2.74f,
            "Base-piece position remains original network ghost position");
        Eq(baseRotation.eulerAngles.y,43f,
            "Base-piece rotation remains original network orientation");
        Builder.prefab = box;

        // Local/world transforms are not identity: the grid must be defined
        // relative to the hit object rather than the scene origin.
        var translatedHit = new RaycastHit {
            normal = new Vector3(0,1,0),
            transform = new Transform { position = new Vector3(1.3f,2f,0.5f) }
        };
        var translatedPosition = new Vector3(2.66f,2f,3.24f);
        var translatedRotation = Quaternion.Euler(new Vector3(0,90f,0));
        Builder.SetPlaceOnSurface(translatedHit, ref translatedPosition, ref translatedRotation);
        Eq(translatedPosition.x,2.8f,"Local snap x respects moving origin");
        Eq(translatedPosition.z,3.0f,"Local snap z respects moving origin");

        // Force a failure AFTER the position has been rounded. Without a true
        // boxed Vector3 copy the Harmony postfix leaks an invalid partial pose.
        var failureHit = new RaycastHit {
            normal = new Vector3(0f,1f,0f), transform = null
        };
        var failurePosition = new Vector3(1.37f,0f,2.74f);
        var failureRotation = new Quaternion(0f,43f,0f,1f);
        Quaternion.ThrowOnEuler = true;
        Builder.SetPlaceOnSurface(failureHit, ref failurePosition, ref failureRotation);
        Eq(failurePosition.x,1.37f,
            "Snap failure restores original boxed X instead of leaking rounded pose");
        Eq(failurePosition.z,2.74f,
            "Snap failure restores original boxed Z instead of leaking rounded pose");
        Eq(failureRotation.eulerAngles.y,43f,
            "Snap failure restores original rotation");
        Quaternion.ThrowOnEuler = false;
        var optionsType = plugin.GetType().Assembly.GetType(
            "Sirro.BZ.MP.ConstrutorZero.Options", true);
        var enabledField = optionsType.GetField("Enabled",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert(!(bool)enabledField.GetValue(null),
            "Snap is disabled for session after reflection failure");
        enabledField.SetValue(null, true);

        Input.Held.Add(KeyCode.LeftControl);
        Input.Held.Add(KeyCode.LeftAlt);
        Time.frameCount++;
        pos = new Vector3(1.37f,0,2.74f);
        rot = Quaternion.Euler(new Vector3(0,43.3f,0));
        Builder.SetPlaceOnSurface(hit,ref pos,ref rot);
        Eq(pos.x,1.4f,"Fine snap x");
        Eq(pos.z,2.7f,"Fine snap z");
        Eq(rot.eulerAngles.y,45f,"Fine rotation 5 degrees");
        Input.Clear();

        Time.frameCount++;
        Input.Down.Add(KeyCode.G);
        Builder.Update();
        Input.Clear();
        // Even an explicit unsafe setting must remain blocked while connected to MP.
        var cfg = plugin.GetType().Assembly.GetType("Sirro.BZ.MP.ConstrutorZero.Cfg", true);
        var unsafeField = cfg.GetField("AllowUnsafeWorldChanges",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        unsafeField.SetValue(null, true);
        Assert(!Builder.UpdateAllowed(),"Unsafe Full Override blocked even when configured true");
        Assert(Builder.allowedInBase && Builder.allowedOutside &&
            Builder.allowedOnConstructables && Builder.allowedInSub,
            "Expanded furniture placement flags applied while ghost is active");
        Assert(Builder.allowedSurfaceTypes.Contains(SurfaceType.Ceiling),
            "Expanded ceiling surface available while building furniture");

        // Switching ghosts without Builder.End must not leak permissive
        // furniture flags onto native base modules or a missing prefab.
        var changingBasePrefab = new GameObject();
        changingBasePrefab.AddComponent(new ConstructableBase());
        Builder.prefab = changingBasePrefab;
        Builder.UpdateAllowed();
        Assert(!Builder.allowedInBase && !Builder.allowedOutside &&
            !Builder.allowedSurfaceTypes.Contains(SurfaceType.Ceiling),
            "Base ghost immediately restores vanilla global Builder permissions");
        Builder.prefab = null;
        Builder.UpdateAllowed();
        Assert(!Builder.allowedInBase && !Builder.allowedOutside,
            "Missing prefab cannot gain expanded furniture permissions");
        Builder.prefab = box;
        Builder.UpdateAllowed();
        Assert(Builder.allowedInBase && Builder.allowedOutside &&
            Builder.allowedSurfaceTypes.Contains(SurfaceType.Ceiling),
            "Furniture permissions reapply only after valid furniture ghost resumes");

        var expandedField = cfg.GetField("ExpandedSurfaces",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        expandedField.SetValue(null, false);
        Builder.UpdateAllowed();
        Assert(!Builder.allowedInBase && !Builder.allowedOutside &&
            !Builder.allowedSurfaceTypes.Contains(SurfaceType.Ceiling),
            "Turning off expanded surfaces restores original Builder flags");
        expandedField.SetValue(null, true);
        Builder.UpdateAllowed();
        Assert(Builder.allowedInBase && Builder.allowedOutside,
            "Re-enabling expanded surfaces takes effect only for valid furniture");
        Assert(!Builder.ValidateOutdoor(new GameObject()),"Unsafe outdoor override blocked");
        unsafeField.SetValue(null, false);
        string deconstructReason = null;
        Assert(!constructable.DeconstructionAllowed(ref deconstructReason),
            "Unsafe deconstruction blocked with real ref string signature");
        var o = new List<GameObject> { new GameObject() };
        Builder.GetObstacles(new Vector3(0,0,0), Quaternion.Euler(new Vector3(0,0,0)),
            new List<object>(), item => true, o);
        Assert(o.Count == 1, "Obstacles preserved in safe mode");

        Time.frameCount++;
        Input.Down.Add(KeyCode.B);
        Builder.Update();
        Input.Clear();
        Eq(Builder.placeDefaultDistance,4f,"Extended build default distance");
        Eq(Builder.placeMaxDistance,6f,"Extended build max distance");
        Eq(constructable.placeMaxDistance,6f,"Constructable max distance updated");
        Assert(Harmony.GetAllPatchedMethods().Any(m => m.DeclaringType == typeof(Targeting) &&
            m.Name == "GetTarget"), "Real Targeting.GetTarget extension hook installed");
        GameObject target;
        float distance;
        Targeting.GetTarget(2f, out target, out distance);
        Eq(Targeting.LastMaxDistance,4f,"Targeting raycast extended while building");
        Builder.End();
        Eq(Builder.placeMaxDistance,3f,"Builder range restored on End");
        Eq(constructable.placeMaxDistance,3f,"Constructable range restored on End");
        Assert(!Builder.allowedInBase && !Builder.allowedOutside &&
            !Builder.allowedOnConstructables && !Builder.allowedInSub,
            "Builder.End restores vanilla placement restrictions for next structure");
        Assert(!Builder.allowedSurfaceTypes.Contains(SurfaceType.Ceiling),
            "Builder.End removes only the ceiling surface inserted by Construtor Zero");
        Builder.ghostModel = null;
        Targeting.GetTarget(2f, out target, out distance);
        Eq(Targeting.LastMaxDistance,2f,"Normal non-building target distance unchanged");
        Builder.ghostModel = new GameObject();

        Time.frameCount++;
        Input.Down.Add(KeyCode.KeypadDivide);
        Builder.placePosition = new Vector3(1,2,3);
        Builder.Update();
        Input.Clear();
        var ghostPosition = new Vector3(-99, -99, -99);
        var ghostRotation = Quaternion.Euler(new Vector3(0, 30, 0));
        Builder.SetDefaultPlaceTransform(ref ghostPosition, ref ghostRotation);
        Eq(ghostPosition.x, 0f,
            "Ref-argument placement remains vanilla to avoid Harmony argument corruption");
        Eq(Builder.placePosition.x, 1f,
            "Manual ghost position is managed by Builder.Update instead");
        Input.Held.Add(KeyCode.Keypad4);
        Time.frameCount++;
        Builder.Update();
        Eq(Builder.placePosition.x, 1f,
            "MP free placement cannot move ghost after stale collision check");
        Input.Clear();

        // Preserve the original free placement tool in offline mode only.
        Subnautica.API.Features.Network.IsMultiplayerActive = false;
        Time.frameCount++;
        Input.Down.Add(KeyCode.KeypadDivide);
        Builder.Update();
        Input.Clear();
        Time.frameCount++;
        Input.Held.Add(KeyCode.Keypad4);
        Builder.Update();
        Assert(Builder.placePosition.x < 1f,
            "Offline free placement still moves ghost");
        Input.Clear();
        Subnautica.API.Features.Network.IsMultiplayerActive = true;
        Builder.End();
        ghostPosition = new Vector3(-99, -99, -99);
        Builder.SetDefaultPlaceTransform(ref ghostPosition, ref ghostRotation);
        Eq(ghostPosition.x, 0f, "Default placement ref args restored after End");

        Assert((new PowerConsumer{powerRelay = new FakePowerRelay()}).IsPowered(),
            "Detached power relay fallback");

        var placeTool = new PlaceTool();
        placeTool.LateUpdate();
        Assert(Inventory.main.quickSlots.HotkeysIgnored,
            "PlaceTool reserves quick-slot hotkeys while snapping rotation is active");
        placeTool.rotationEnabled = false;
        placeTool.LateUpdate();
        Assert(!Inventory.main.quickSlots.HotkeysIgnored,
            "PlaceTool without rotation leaves normal quick-slot hotkeys enabled");
        placeTool.rotationEnabled = true;
        placeTool.LateUpdate();
        placeTool.OnHolster();
        Assert(!Inventory.main.quickSlots.HotkeysIgnored,
            "Holstering PlaceTool always restores quick-slot hotkeys");
        placeTool.LateUpdate();
        placeTool.OnPlace();
        Assert(!Inventory.main.quickSlots.HotkeysIgnored,
            "Placing a held item restores quick-slot hotkeys");
        placeTool.LateUpdate();

        // Regression: two ingredient entries using the same TechType must not
        // independently subtract the current inventory twice.
        var fullRecipe = tracker.GetMethod("InventoryHasCompleteRecipe",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        var beginRecipe = tracker.GetMethod("Begin",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        var remaining = tracker.GetProperty("Remaining",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        Inventory.main.ActualItems[TechType.Titanium] = 1;
        manual.SetValue(null, true);
        Assert(!(bool)fullRecipe.Invoke(null,
                new object[] { TechType.DuplicateIngredientRecipe }),
            "Duplicate ingredient rows require all three units, not two");
        Assert((bool)beginRecipe.Invoke(null, new object[] {
                TechType.DuplicateIngredientRecipe, log }),
            "Duplicate ingredient recipe plans missing materials");
        Assert((int)remaining.GetValue(null) == 2,
            "Three required minus one owned schedules exactly two transfers");
        cancel.Invoke(null, new object[] { "end duplicate test", log });
        Inventory.main.ActualItems[TechType.Titanium] = 2;
        Assert(!(bool)fullRecipe.Invoke(null,
                new object[] { TechType.DuplicateIngredientRecipe }),
            "Duplicate ingredient recipe still incomplete at two of three units");
        Inventory.main.ActualItems[TechType.Titanium] = 3;
        Assert((bool)fullRecipe.Invoke(null,
                new object[] { TechType.DuplicateIngredientRecipe }),
            "Duplicate ingredient recipe succeeds only at three actual units");
        manual.SetValue(null, false);

        // Rejected server requests must not be optimistically acknowledged,
        // and a timed-out item ID must never be sent twice in one session.
        locker.container.Clear();
        autoLocker.container.Clear();
        Inventory.main.ActualItems.Clear();
        var deniedLocker = new StorageContainer();
        deniedLocker.storageRoot.position = new Vector3(2, 0, 0);
        deniedLocker.storageRoot.gameObject.name = "locker-denied";
        deniedLocker.storageRoot.gameObject.AddComponent(new Constructable {
            techType = TechType.Locker, gameObject = deniedLocker.storageRoot.gameObject
        });
        deniedLocker.container.Add(new InventoryItem { item = new Pickupable {
            techType = TechType.Titanium,
            gameObject = new GameObject { name = "denied-item" } } });
        deniedLocker.Awake();
        manual.SetValue(null, true);
        Assert((bool)beginRecipe.Invoke(null, new object[] { TechType.TestRecipe, log }),
            "Denied-server scenario prepares a real ingredient queue");
        int acceptedBeforeDenial = Subnautica.Events.Handlers.Storage.ServerAccepted;
        int sentBeforeDenial = Subnautica.Events.Handlers.Storage.NetworkRequests;
        pump.Invoke(null, new object[] { log });
        Assert(Subnautica.Events.Handlers.Storage.NetworkRequests == sentBeforeDenial + 1 &&
            Subnautica.Events.Handlers.Storage.ServerAccepted == acceptedBeforeDenial,
            "A storage event dispatch is not a server acceptance");
        var sentAt = tracker.GetField("sentAtUtc",
            BindingFlags.Static | BindingFlags.NonPublic);
        sentAt.SetValue(null, DateTime.UtcNow.AddSeconds(-30));
        pump.Invoke(null, new object[] { log });
        Assert(!(bool)active.GetValue(null),
            "Server denial times out without replaying the recipe");
        int packetsAfterTimeout = Subnautica.Events.Handlers.Storage.NetworkRequests;
        Assert(!(bool)requestOne.Invoke(null, new object[] { TechType.Titanium }) &&
            Subnautica.Events.Handlers.Storage.NetworkRequests == packetsAfterTimeout,
            "Timed-out item stays quarantined until ACK or session reset");

        // Real Player.Update must cancel the two coordinators before returning
        // on disconnect, not just the underlying prefetch queue.
        manual.SetValue(null, false);
        autoField.SetValue(null, true);
        var interruptedMenu = new uGUI_CraftingMenu();
        interruptedMenu.Action(craftNode);
        Assert((bool)isWaiting.GetValue(null), "Deferred craft staged before disconnect");
        Subnautica.API.Features.Network.IsMultiplayerActive = false;
        Time.frameCount++;
        Player.main.Update();
        Assert(!(bool)isWaiting.GetValue(null) && !(bool)active.GetValue(null),
            "Disconnect cancels deferred crafting coordinator and queue");
        Subnautica.API.Features.Network.IsMultiplayerActive = true;
        autoField.SetValue(null, false);

        autoBuildField.SetValue(null, true);
        Builder.constructableTechType = TechType.TestRecipe;
        Builder.CanPlace = true;
        Builder.ghostModel = new GameObject();
        Assert(!Builder.TryPlace() && (bool)buildWaiting.GetValue(null),
            "Deferred building staged before disconnect");
        Subnautica.API.Features.Network.IsMultiplayerActive = false;
        Time.frameCount++;
        Player.main.Update();
        Assert(!(bool)buildWaiting.GetValue(null) && !(bool)active.GetValue(null),
            "Disconnect cancels deferred building coordinator and queue");
        Subnautica.API.Features.Network.IsMultiplayerActive = true;
        autoBuildField.SetValue(null, false);
        Builder.constructableTechType = TechType.None;
        Assert((bool)requestOne.Invoke(null, new object[] { TechType.Titanium }),
            "Session reset releases prior quarantined item IDs");
        manual.SetValue(null, false);

        plugin.OnDisabled();
        Assert(!Inventory.main.quickSlots.HotkeysIgnored,
            "Plugin unload restores quick-slot hotkeys");
        Assert((int)storages.GetValue(null) == 0, "Storage registry cleaned on plugin shutdown");
        foreach (var method in Harmony.GetAllPatchedMethods().Where(m => m.DeclaringType == typeof(Builder)))
        {
            var patchInfo = Harmony.GetPatchInfo(method);
            if (patchInfo != null)
                Console.WriteLine("POST_UNLOAD " + method.Name + ": " +
                    string.Join(",", patchInfo.Prefixes.Concat(patchInfo.Postfixes)
                        .Concat(patchInfo.Transpilers).Concat(patchInfo.Finalizers)
                        .Select(p => p.owner + "/" + p.PatchMethod.Name)));
        }
        Assert(!Harmony.GetAllPatchedMethods()
            .Where(m => m.DeclaringType == typeof(Builder))
            .Any(m =>
            {
                var info = Harmony.GetPatchInfo(m);
                return info != null &&
                    info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers)
                        .Concat(info.Finalizers)
                        .Any(p => p.owner.StartsWith("sirro.bz.mp.construtor-zero",
                            StringComparison.Ordinal));
            }), "All Construtor Zero Harmony hooks removed on disable");
        Assert(Harmony.GetPatchInfo(builderTryPlace).Prefixes
            .Any(p => p.owner == "bot-benson-simulation"),
            "Multiplayer Harmony patches survive Construtor Zero unloading");
        Console.WriteLine("ALL " + count + " CONTRACT/SIMULATION CHECKS PASSED");
    }
}

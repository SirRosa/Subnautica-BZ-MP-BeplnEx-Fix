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
            techType = TechType.Titanium, gameObject = new GameObject { name = "intruder" } } });
        Assert((string)inFlight.GetValue(null) == "item-b",
            "Unrelated inventory delivery does not acknowledge pending transfer");
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
        Assert(Inventory.main.GetPickupCount(TechType.Titanium) == 0,
            "Plugin never falsifies client ingredient counts after delivery");
        recipe.UpdateIngredients(null, false);
        Assert((bool)start.Invoke(null, new object[] { log }),
            "Another prefetch can be prepared after prior completion");
        Subnautica.API.Features.Network.IsMultiplayerActive = false;
        pump.Invoke(null, new object[] { log });
        Assert(!(bool)active.GetValue(null),
            "World disconnect cancels all outstanding ingredient requests");
        Subnautica.API.Features.Network.IsMultiplayerActive = true;
        manual.SetValue(null, false);

        var hit = new RaycastHit { normal = new Vector3(0,1,0), transform = new Transform() };
        var pos = new Vector3(1.37f, 0f, 2.74f);
        var rot = Quaternion.Euler(new Vector3(0,43f,0));
        Builder.SetPlaceOnSurface(hit, ref pos, ref rot);
        Eq(pos.x,1.5f,"Native snap x");
        Eq(pos.z,2.5f,"Native snap z");
        Eq(rot.eulerAngles.y,45f,"Rotation grid 45 degrees");

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
        Assert(Builder.placePosition.x < 1,"Free placement moves ghost");
        Input.Clear();
        Builder.End();
        ghostPosition = new Vector3(-99, -99, -99);
        Builder.SetDefaultPlaceTransform(ref ghostPosition, ref ghostRotation);
        Eq(ghostPosition.x, 0f, "Default placement ref args restored after End");

        Assert((new PowerConsumer{powerRelay = new FakePowerRelay()}).IsPowered(),
            "Detached power relay fallback");

        plugin.OnDisabled();
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
                return info != null && (info.Prefixes.Count + info.Postfixes.Count +
                    info.Transpilers.Count + info.Finalizers.Count) != 0;
            }), "Harmony hooks removed on disable");
        Console.WriteLine("ALL " + count + " CONTRACT/SIMULATION CHECKS PASSED");
    }
}

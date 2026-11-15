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
        Assert(patches.Any(m => m.Name == "GetObstacles"), "Harmony obstacle by-ref hook installed");
        Assert(patches.Any(m => m.Name == "IsPowered"), "Harmony power hook installed");
        Assert(patches.Any(m => m.DeclaringType == typeof(StorageContainer)),
            "CraftFromContainers storage registration hooks installed");
        Assert(patches.Any(m => m.DeclaringType == typeof(Inventory)),
            "CraftFromContainers inventory observation hooks installed");
        var cfcType = plugin.GetType().Assembly.GetType(
            "Sirro.BZ.MP.ConstrutorZero.CraftFromContainersBridge", true);
        var storages = cfcType.GetProperty("RegisteredStorages",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        var locker = new StorageContainer();
        locker.Awake();
        Assert((int)storages.GetValue(null) == 1, "Storage registered on Awake");
        Assert(Inventory.main.GetPickupCount(TechType.Titanium) == 0,
            "Craft ingredients are NOT forged without server approval");
        Assert(!Inventory.main.DestroyItem(TechType.Titanium),
            "Craft resource removal is NOT forged on client");
        locker.OnDestroy();
        Assert((int)storages.GetValue(null) == 0, "Storage registry cleaned on Destroy");

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
        Assert(!Builder.ValidateOutdoor(),"Unsafe outdoor override blocked");
        unsafeField.SetValue(null, false);
        Assert(!constructable.DeconstructionAllowed(),"Unsafe deconstruction blocked");
        var o = new List<GameObject> { new GameObject() };
        Builder.GetObstacles(ref o);
        Assert(o.Count == 1, "Obstacles preserved in safe mode");

        Time.frameCount++;
        Input.Down.Add(KeyCode.B);
        Builder.Update();
        Input.Clear();
        Eq(Builder.placeDefaultDistance,4f,"Extended build default distance");
        Eq(Builder.placeMaxDistance,6f,"Extended build max distance");
        Eq(constructable.placeMaxDistance,6f,"Constructable max distance updated");
        Builder.End();
        Eq(Builder.placeMaxDistance,3f,"Builder range restored on End");
        Eq(constructable.placeMaxDistance,3f,"Constructable range restored on End");

        Time.frameCount++;
        Input.Down.Add(KeyCode.KeypadDivide);
        Builder.placePosition = new Vector3(1,2,3);
        Builder.Update();
        Input.Clear();
        Assert(!Builder.SetDefaultPlaceTransform(),"Freeze disables default placement");
        Input.Held.Add(KeyCode.Keypad4);
        Time.frameCount++;
        Builder.Update();
        Assert(Builder.placePosition.x < 1,"Free placement moves ghost");
        Input.Clear();
        Builder.End();
        Assert(Builder.SetDefaultPlaceTransform(),"Free placement released after End");

        Assert((new PowerConsumer{powerRelay = new FakePowerRelay()}).IsPowered(),
            "Detached power relay fallback");

        plugin.OnDisabled();
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

using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ABI fixture checks. Every expected signature was extracted from the
/// user-supplied Subnautica Below Zero Assembly-CSharp.dll / Events.dll
/// (see ABI-AUDIT-301.md). These assertions check the CI fake, not game runtime.
/// </summary>
public static class AbiTests
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic |
        BindingFlags.Instance | BindingFlags.Static;

    public static void Run(Action<bool, string> assert)
    {
        var setSurface = typeof(Builder).GetMethod("SetPlaceOnSurface", All);
        assert(setSurface != null && setSurface.ReturnType == typeof(void) &&
            setSurface.GetParameters().Length == 3 &&
            setSurface.GetParameters()[1].ParameterType == typeof(Vector3).MakeByRefType() &&
            setSurface.GetParameters()[2].ParameterType == typeof(Quaternion).MakeByRefType(),
            "Actual Builder.SetPlaceOnSurface ABI reflected in test fixture");

        var defaultPlace = typeof(Builder).GetMethod("SetDefaultPlaceTransform", All);
        assert(defaultPlace != null && defaultPlace.ReturnType == typeof(void) &&
            defaultPlace.GetParameters().Length == 2 &&
            defaultPlace.GetParameters()[0].ParameterType == typeof(Vector3).MakeByRefType() &&
            defaultPlace.GetParameters()[1].ParameterType == typeof(Quaternion).MakeByRefType(),
            "Actual Builder.SetDefaultPlaceTransform(ref,ref) ABI reflected in fixture");

        assert(typeof(Builder).GetMethod("CreateGhost", All)?.ReturnType == typeof(bool),
            "Builder.CreateGhost returns bool in real BZ");

        var obstacles = typeof(Builder).GetMethod("GetObstacles", All);
        assert(obstacles != null && obstacles.ReturnType == typeof(void) &&
            obstacles.GetParameters().Length == 5 &&
            obstacles.GetParameters()[4].ParameterType == typeof(List<GameObject>),
            "Builder.GetObstacles has five arguments and results list is not byref");

        assert(typeof(StorageContainer).GetMethod("OnDestroy", All) == null &&
            typeof(StorageContainer).GetMethod("ResetContainer", All) != null,
            "No nonexistent StorageContainer.OnDestroy; ResetContainer is available");

        var destroy = typeof(Inventory).GetMethod("DestroyItem", All);
        assert(destroy != null && destroy.ReturnType == typeof(bool) &&
            destroy.GetParameters().Length == 2 &&
            destroy.GetParameters()[0].ParameterType == typeof(TechType) &&
            destroy.GetParameters()[1].ParameterType == typeof(bool),
            "Inventory.DestroyItem(TechType,bool) matches BZ");

        var allowed = typeof(Constructable).GetMethod("DeconstructionAllowed", All);
        assert(allowed != null && allowed.ReturnType == typeof(bool) &&
            allowed.GetParameters().Length == 1 &&
            allowed.GetParameters()[0].ParameterType == typeof(string).MakeByRefType(),
            "Constructable.DeconstructionAllowed(ref string) matches BZ");

        var eventMethod = typeof(Subnautica.Events.Handlers.Storage)
            .GetMethod("OnItemRemoving", All);
        assert(eventMethod != null && eventMethod.IsStatic &&
            eventMethod.GetParameters().Length == 1 &&
            eventMethod.GetParameters()[0].ParameterType ==
                typeof(Subnautica.Events.EventArgs.StorageItemRemovingEventArgs),
            "Events.Storage.OnItemRemoving one-argument static ABI confirmed");
    }
}

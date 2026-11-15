using System;
using System.Reflection;

namespace Sirro.BZ.MP.ConstrutorZero
{
    /// <summary>
    /// Builder.TryPlace creates an UNBUILT Constructable blueprint, not a
    /// completed craft. Resources are consumed later by BuilderTool/
    /// Constructable.Construct. Never suppress this action because the
    /// player lacks ingredients: BOT Benson must send the ordinary blueprint
    /// placement packet even if the inventory AND every locker are empty.
    ///
    /// When auto_build_enabled=true, we may begin a separate, optional
    /// server-authoritative ingredient prefetch at the deliberate placement
    /// click. That transfer must NEVER delay, replay or place a blueprint.
    /// The player still uses the normal construction tool to finish it.
    /// </summary>
    internal static class AutoBuildCoordinator
    {
        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static;

        // Retained for backwards-compatible integration diagnostics; no
        // blueprint placement transaction is EVER pending in this bridge.
        internal static bool IsWaiting => false;

        internal static bool Prefix(ref bool __result)
        {
            // IMPORTANT: always return true. Even a network error, missing
            // materials, an active fabricator craft or a CFC disconnect must
            // leave normal multiplayer blueprint placement untouched.
            if (!CraftFromContainersBridge.Ready || !ContainerConfig.Enabled ||
                !ContainerConfig.AutoBuildEnabled || !CraftFromContainersBridge.NetworkActive)
                return true;
            try
            {
                var builder = Reflect.Type("Builder");
                if (builder == null ||
                    !(CraftFromContainersBridge.Read(builder, "isPlacing") is bool placing) ||
                    !placing ||
                    !(CraftFromContainersBridge.Read(builder, "canPlace") is bool valid) ||
                    !valid || !RecipePrefetch.RequiresIngredientCosts())
                    return true;

                var techType = CraftFromContainersBridge.Read(builder, "constructableTechType");
                var prefab = CraftFromContainersBridge.Read(builder, "prefab");
                if (techType == null || techType.ToString() == "None" ||
                    prefab == null || IsBasePiece(prefab) ||
                    RecipePrefetch.IsActive || AutoCraftCoordinator.IsWaiting ||
                    RecipePrefetch.InventoryHasCompleteRecipe(techType))
                    return true;

                CraftFromContainersBridge.RefreshAvailabilityBeforePlacement();
                if (RecipePrefetch.CanSourceMissingIngredients(techType))
                {
                    if (RecipePrefetch.Begin(techType, CraftFromContainersBridge.Report))
                        Trace.Info("CFC_BLUEPRINT_PREFETCH: materials requested separately; " +
                            "normal multiplayer blueprint placement is NOT deferred");
                }
                else
                {
                    CraftFromContainersBridge.DiagnoseMissingBlueprintSources(techType);
                }
                // No remote items? Still place the empty blueprint. Never
                // create/consume materials, suppress a packet or replay click.
            }
            catch (Exception e)
            {
                Trace.Info("CFC_BLUEPRINT_PREFETCH_SKIPPED: " +
                    (e.InnerException ?? e).Message);
            }
            return true;
        }

        internal static void IngredientsDelivered()
        {
            // Inventory receives confirmed resources; this is NOT an ACK
            // for blueprint placement and must never call Builder.TryPlace.
        }

        internal static void BuilderEnded()
        {
            // After placing the blueprint the construction tool normally
            // closes. Continue a server-approved prefetch independently.
        }

        internal static void Tick() { } // No ghost-pose/replay state.

        internal static void Cancel(string reason)
        {
            // The caller cancels RecipePrefetch on MP disconnect. Never
            // cancel unrelated fabricator transfers from this class.
        }

        internal static void Reset() { }

        private static bool IsBasePiece(object prefab)
        {
            // Base-piece construction has a distinct packet protocol.
            var type = Reflect.Type("ConstructableBase");
            if (type == null) return true;
            var get = prefab.GetType().GetMethod("GetComponent", All, null,
                new[] { typeof(Type) }, null);
            return get == null || get.Invoke(prefab, new object[] { type }) != null;
        }
    }
}

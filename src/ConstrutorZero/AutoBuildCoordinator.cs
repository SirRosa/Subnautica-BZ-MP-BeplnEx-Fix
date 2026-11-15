using System;
using System.Reflection;

namespace Sirro.BZ.MP.ConstrutorZero
{
    /// <summary>
    /// Defers only the vanilla Builder.TryPlace() call until the server has
    /// transferred the required ingredients to the player's real inventory.
    ///
    /// Critical invariant: the construction ghost prefab, position, rotation
    /// and TechType must be the SAME as at the original click; otherwise do not
    /// replay anything. This prevents unexpected placement after moving away.
    /// The actual construction packet remains entirely owned by BOT Benson.
    /// </summary>
    internal static class AutoBuildCoordinator
    {
        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static;
        private static bool waiting;
        private static bool ready;
        private static bool bypass;
        private static DateTime startedUtc;
        private static object sourcePrefab;
        private static object sourceTechType;
        private static float[] initialPosition;
        private static float[] initialRotation;

        internal static bool IsWaiting => waiting;

        internal static bool Prefix(ref bool __result)
        {
            if (bypass) return true;
            // An already deferred click must remain blocked even if the game
            // temporarily changes affordability, placement validity, or mode
            // before the next Player.Update processes the final ACK.
            if (waiting)
            {
                __result = false;
                return false;
            }
            if (!CraftFromContainersBridge.Ready || !ContainerConfig.Enabled ||
                !ContainerConfig.AutoBuildEnabled || !CraftFromContainersBridge.NetworkActive)
                return true;
            try
            {
                var builder = Reflect.Type("Builder");
                if (builder == null || !(CraftFromContainersBridge.Read(builder, "isPlacing") is bool placing) ||
                    !placing) return true;

                if (!RecipePrefetch.RequiresIngredientCosts())
                    return true; // Creative mode uses vanilla free building.

                var techType = CraftFromContainersBridge.Read(builder, "constructableTechType");
                if (techType == null || string.Equals(techType.ToString(), "None",
                    StringComparison.OrdinalIgnoreCase))
                    return true;

                // Don't ask the server to move resources for a placement already
                // rejected by the game's normal collision/surface validation.
                var validPlacement = CraftFromContainersBridge.Read(builder, "canPlace");
                if (!(validPlacement is bool) || !(bool)validPlacement)
                    return true;
                // A pending construction owns this click, even if another inventory
                // event has already made the recipe temporarily affordable. Check
                // the transaction BEFORE the fast path or a second click could
                // emit an extra BOT Benson construction packet.
                if (RecipePrefetch.IsActive || AutoCraftCoordinator.IsWaiting)
                {
                    __result = false;
                    return false;
                }
                if (RecipePrefetch.InventoryHasCompleteRecipe(techType)) return true;

                var prefab = CraftFromContainersBridge.Read(builder, "prefab");
                if (prefab == null || IsBasePiece(prefab))
                    return true; // Base piece packets have separate construction semantics.
                // Never start a multi-item staged build if the client can
                // already prove some required ingredient is unavailable.
                // This avoids predictable half-delivered recipes; concurrent
                // server races can still reject an individually requested item.
                if (!RecipePrefetch.CanSourceMissingIngredients(techType))
                    return true;

                var position = Snapshot(CraftFromContainersBridge.Read(builder, "placePosition"),
                    new[] { "x", "y", "z" });
                var rotation = Snapshot(CraftFromContainersBridge.Read(builder, "placeRotation"),
                    new[] { "x", "y", "z", "w" });
                if (position == null || rotation == null ||
                    !RecipePrefetch.Begin(techType, CraftFromContainersBridge.Report))
                    return true;

                sourcePrefab = prefab;
                sourceTechType = techType;
                initialPosition = position;
                initialRotation = rotation;
                waiting = true;
                ready = false;
                startedUtc = DateTime.UtcNow;
                __result = false;
                CraftFromContainersBridge.Report("Building paused pending multiplayer " +
                    "ingredient delivery; don't move the construction ghost");
                return false;
            }
            catch (Exception e)
            {
                CraftFromContainersBridge.Report("Building prefetch skipped: " + e.Message);
                return true; // vanilla remains authoritative
            }
        }

        internal static void IngredientsDelivered()
        {
            if (waiting) ready = true;
        }

        internal static void BuilderEnded()
        {
            if (waiting && !bypass)
                Cancel("Construction tool closed before resources were delivered");
        }

        internal static void Tick()
        {
            if (!waiting) return;
            if (!CraftFromContainersBridge.NetworkActive ||
                DateTime.UtcNow - startedUtc > TimeSpan.FromSeconds(45))
            {
                Cancel("Building transaction disconnected or timed out");
                return;
            }
            var builder = Reflect.Type("Builder");
            if (builder == null || !RecipePrefetch.RequiresIngredientCosts() ||
                !Equals(sourceTechType, CraftFromContainersBridge.Read(builder, "constructableTechType")) ||
                !ReferenceEquals(sourcePrefab, CraftFromContainersBridge.Read(builder, "prefab")) ||
                !(CraftFromContainersBridge.Read(builder, "isPlacing") is bool placing) || !placing ||
                !SamePose(initialPosition,
                    Snapshot(CraftFromContainersBridge.Read(builder, "placePosition"),
                        new[] { "x", "y", "z" }), 0.02f) ||
                !SamePose(initialRotation,
                    Snapshot(CraftFromContainersBridge.Read(builder, "placeRotation"),
                        new[] { "x", "y", "z", "w" }), 0.001f))
            {
                Cancel("Construction pose changed; delayed placement was cancelled");
                return;
            }

            if (!ready)
            {
                if (!RecipePrefetch.IsActive)
                    Cancel("Ingredient transfer failed or was cancelled");
                return;
            }
            if (!RecipePrefetch.InventoryHasCompleteRecipe(sourceTechType))
            {
                Cancel("Required construction ingredients are not in inventory");
                return;
            }
            if (!ContainerConfig.AutoPlaceAfterPrefetch)
            {
                CraftFromContainersBridge.Report("Ingredients delivered. Click again " +
                    "to place the construction yourself.");
                Reset();
                return;
            }
            if (!(CraftFromContainersBridge.Read(builder, "canPlace") is bool valid) || !valid)
            {
                // Leave the transaction pending while vanilla evaluates valid
                // placement, but never disable collision or validation rules.
                return;
            }

            try
            {
                var place = builder.GetMethod("TryPlace", All, null,
                    Type.EmptyTypes, null);
                if (place == null) throw new MissingMethodException("Builder.TryPlace()");
                bypass = true;
                // BOT Benson's Builder.TryPlace prefix sends a network request,
                // then returns false to prevent original singleplayer placement.
                // Therefore the return value is NOT a server-acceptance signal.
                place.Invoke(null, null);
                CraftFromContainersBridge.Report("Vanilla Builder.TryPlace interceptor " +
                    "invoked; the multiplayer server will determine placement");
            }
            catch (Exception e)
            {
                CraftFromContainersBridge.Report("Deferred placement rejected: " +
                    (e.InnerException ?? e).Message);
            }
            finally
            {
                Reset();
            }
        }

        internal static void Cancel(string reason)
        {
            if (!waiting) return;
            Reset();
            RecipePrefetch.Cancel(reason, CraftFromContainersBridge.Report);
        }

        internal static void Reset()
        {
            waiting = ready = bypass = false;
            sourcePrefab = sourceTechType = null;
            initialPosition = initialRotation = null;
            startedUtc = default(DateTime);
        }

        private static bool IsBasePiece(object prefab)
        {
            var baseType = Reflect.Type("ConstructableBase");
            if (baseType == null) return true; // fail closed
            var method = prefab.GetType().GetMethod("GetComponent", All,
                null, new[] { typeof(Type) }, null);
            return method == null || method.Invoke(prefab, new[] { baseType }) != null;
        }

        private static float[] Snapshot(object vector, string[] fields)
        {
            if (vector == null) return null;
            try
            {
                var values = new float[fields.Length];
                for (int i = 0; i < values.Length; i++)
                    values[i] = Reflect.Component(vector, fields[i]);
                return values;
            }
            catch { return null; }
        }

        private static bool SamePose(float[] original, float[] current, float maxDifference)
        {
            if (original == null || current == null ||
                original.Length != current.Length) return false;
            for (int i = 0; i < original.Length; i++)
                if (float.IsNaN(current[i]) || float.IsInfinity(current[i]) ||
                    Math.Abs(original[i] - current[i]) > maxDifference)
                    return false;
            return true;
        }
    }
}

using System;
using System.Linq;
using System.Reflection;

namespace Sirro.BZ.MP.ConstrutorZero
{
    /// <summary>
    /// Defers ONLY uGUI_CraftingMenu.Action(Node{ action = Craft }) while the
    /// multiplayer server moves the required ingredients into the real inventory.
    /// No local locker deletion, no synthetic inventory counts, and no direct item
    /// creation. Replays the original UI action at most once after verification.
    /// </summary>
    internal static class AutoCraftCoordinator
    {
        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static;
        private static WeakReference menuRef;
        private static object node;
        private static object recipeType;
        private static DateTime startedUtc;
        private static bool waiting;
        private static bool ready;
        private static bool bypass;

        internal static bool IsWaiting => waiting;
        internal static bool ReadyToReplay => ready;

        internal static bool CraftActionPrefix(object __instance, object[] __args)
        {
            if (bypass || !ContainerConfig.AutoCraftEnabled ||
                !ContainerConfig.Enabled || !CraftFromContainersBridge.NetworkActive ||
                __instance == null || __args == null || __args.Length != 1)
                return true;

            var requestedNode = __args[0];
            if (requestedNode == null) return true;
            var action = CraftFromContainersBridge.Read(requestedNode, "action");
            if (action == null || !string.Equals(action.ToString(), "Craft",
                StringComparison.OrdinalIgnoreCase))
            {
                // Navigating to a different category invalidates the previously
                // staged action. The ordinary menu operation still executes.
                if (waiting)
                    Cancel("Crafting category changed; deferred recipe cancelled");
                return true;
            }

            if (waiting)
            {
                Cancel("Crafting selection changed while fetching ingredients");
                return false; // the second click must not craft unexpected recipe
            }

            var wanted = CraftFromContainersBridge.Read(requestedNode, "techType");
            if (wanted == null || string.Equals(wanted.ToString(), "None",
                StringComparison.OrdinalIgnoreCase))
                return true;

            // Never queue ingredients for a blueprint that the game has not
            // unlocked. The original Craft UI checks CrafterLogic permissions;
            // our prefix runs first, so it must preserve that restriction.
            if (!CraftingRecipeUnlocked(wanted))
                return true;

            // Creative and other no-cost modes must remain vanilla. The prefetch
            // layer must never move player resources when crafting is free.
            if (!RecipePrefetch.RequiresIngredientCosts())
                return true;

            // When everything is already in the actual local inventory, vanilla
            // code should execute unchanged, with no new server requests.
            if (RecipePrefetch.InventoryHasCompleteRecipe(wanted))
                return true;

            // Missing resources may be in a locker. Abort the first action only
            // when the validated, explicit prefetch queue was actually created.
            if (!RecipePrefetch.Begin(wanted, CraftFromContainersBridge.Report))
            {
                // The advertised locker stock may have changed after UI
                // rendering. Vanilla cannot craft with missing inventory
                // items; do not dispatch an unvalidated network action.
                CraftFromContainersBridge.Report(
                    "Crafting materials unavailable or transfer already active");
                return false;
            }

            menuRef = new WeakReference(__instance);
            node = requestedNode;
            recipeType = wanted;
            waiting = true;
            ready = false;
            startedUtc = DateTime.UtcNow;
            CraftFromContainersBridge.Report("Craft postponed until all ingredients " +
                "arrive through the multiplayer server");
            return false;
        }

        /// <summary>
        /// UI-only affordability extension for the real game's
        /// uGUI_CraftingMenu.ActionAvailable(Node). Do not change vanilla
        /// inventory counts or crafting resource consumption.
        /// </summary>
        internal static bool CanAdvertise(object requestedNode)
        {
            if (requestedNode == null || waiting ||
                !ContainerConfig.Enabled || !ContainerConfig.AutoCraftEnabled ||
                !CraftFromContainersBridge.Ready ||
                !CraftFromContainersBridge.NetworkActive)
                return false;
            try
            {
                var action = CraftFromContainersBridge.Read(requestedNode, "action");
                if (action == null || !string.Equals(action.ToString(),
                    "Craft", StringComparison.OrdinalIgnoreCase)) return false;
                var wanted = CraftFromContainersBridge.Read(requestedNode, "techType");
                return wanted != null &&
                    !string.Equals(wanted.ToString(), "None",
                        StringComparison.OrdinalIgnoreCase) &&
                    CraftingRecipeUnlocked(wanted) &&
                    RecipePrefetch.CanSourceMissingIngredients(wanted);
            }
            catch { return false; }
        }

        private static bool CraftingRecipeUnlocked(object recipe)
        {
            try
            {
                var crafterLogic = Reflect.Type("CrafterLogic");
                var check = crafterLogic?.GetMethods(All).FirstOrDefault(m =>
                    m.Name == "IsCraftRecipeUnlocked" && m.IsStatic &&
                    m.GetParameters().Length == 1 &&
                    m.GetParameters()[0].ParameterType.IsInstanceOfType(recipe) &&
                    m.ReturnType == typeof(bool));
                // Fail closed for automation when runtime API does not match:
                // vanilla still gets the original action without any CFC transfer.
                return check != null && (bool)check.Invoke(null, new[] { recipe });
            }
            catch { return false; }
        }

        internal static void IngredientsDelivered()
        {
            if (waiting) ready = true;
        }

        internal static void CraftingMenuClosing(object __instance)
        {
            if (waiting && menuRef != null &&
                ReferenceEquals(menuRef.Target, __instance))
                Cancel("Crafting menu closed before server delivery completed");
        }

        internal static void Tick()
        {
            if (!waiting) return;
            if (!CraftFromContainersBridge.NetworkActive)
            {
                Cancel("Multiplayer disconnected while crafting");
                return;
            }
            if (DateTime.UtcNow - startedUtc > TimeSpan.FromSeconds(45))
            {
                Cancel("Timed out waiting for server-confirmed ingredients");
                return;
            }
            // Check the context *before* requesting another ingredient. The
            // player may change game mode, lose recipe unlock, or switch
            // selected node during a long asynchronous transfer.
            var menu = menuRef?.Target;
            var currentRecipe = node == null ? null :
                CraftFromContainersBridge.Read(node, "techType");
            var currentAction = node == null ? null :
                CraftFromContainersBridge.Read(node, "action");
            if (menu == null || node == null || recipeType == null ||
                IsDestroyedUnityObject(menu) ||
                !Equals(currentRecipe, recipeType) ||
                currentAction == null || !string.Equals(currentAction.ToString(),
                    "Craft", StringComparison.OrdinalIgnoreCase) ||
                !RecipePrefetch.RequiresIngredientCosts() ||
                !CraftingRecipeUnlocked(recipeType))
            {
                // No silent replay when the UI has reused its recipe node, the
                // blueprint was revoked, or game mode changed while waiting.
                // Already delivered materials remain in vanilla inventory.
                Cancel("Deferred craft no longer matches the original valid recipe");
                return;
            }
            if (!ready)
            {
                if (!RecipePrefetch.IsActive)
                    Cancel("Ingredient prefetch was cancelled or failed");
                return;
            }
            if (!RecipePrefetch.InventoryHasCompleteRecipe(recipeType))
            {
                Cancel("Craft was not resumed: inventory changed after delivery");
                return;
            }

            // Replay after Inventory.OnAddItem completed, on a later Player.Update
            // rather than recursively inside the inventory callback.
            try
            {
                var action = menu.GetType().GetMethods(All)
                    .FirstOrDefault(m => m.Name == "Action" &&
                        m.GetParameters().Length == 1 &&
                        m.GetParameters()[0].ParameterType.IsInstanceOfType(node));
                if (action == null)
                {
                    Cancel("Craft replay method is missing");
                    return;
                }
                bypass = true;
                action.Invoke(menu, new[] { node });
                CraftFromContainersBridge.Report("Original crafting action resumed after " +
                    "server-confirmed ingredient delivery");
            }
            catch (Exception ex)
            {
                CraftFromContainersBridge.Report("Craft replay failed: " +
                    (ex.InnerException ?? ex).Message);
            }
            finally
            {
                bypass = false;
                Reset();
            }
        }

        internal static void Cancel(string reason)
        {
            if (!waiting) return;
            Reset();
            RecipePrefetch.Cancel(reason, CraftFromContainersBridge.Report);
            CraftFromContainersBridge.Report(reason);
        }

        internal static void Reset()
        {
            waiting = ready = bypass = false;
            menuRef = null;
            node = recipeType = null;
            startedUtc = default(DateTime);
        }

        private static bool IsDestroyedUnityObject(object item)
        {
            try
            {
                var unity = Reflect.Type("UnityEngine.Object");
                if (unity == null || !unity.IsInstanceOfType(item)) return false;
                var equality = unity.GetMethod("op_Equality", All, null,
                    new[] { unity, unity }, null);
                return equality != null && (bool)equality.Invoke(null,
                    new object[] { item, null });
            }
            catch { return true; } // fail closed
        }
    }
}

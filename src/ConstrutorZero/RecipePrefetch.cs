using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace Sirro.BZ.MP.ConstrutorZero
{
    /// <summary>
    /// Explicit, non-automatic recipe ingredient prefetch.
    ///
    /// The original CraftFromContainers reports Inventory.DestroyItem success after
    /// locally deleting locker contents. That cannot be used with this MP server.
    /// Here, ingredients are transferred to the player's REAL inventory one at a
    /// time and the original crafting pipeline consumes them afterwards.
    /// A recipe's craft action itself is NEVER faked or called by this class.
    ///
    /// A positive transfer request is not an acknowledgement. Only the actual
    /// Inventory.OnAddItem for the requested unique item completes a transfer.
    /// </summary>
    internal static class RecipePrefetch
    {
        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static;
        private static readonly Queue<object> needed = new Queue<object>();
        private static string inFlightItemId;
        private static DateTime sentAtUtc;
        private static object observedRecipe;
        private static string activeRecipeName;
        private static int completed;
        private static readonly TimeSpan TransferTimeout = TimeSpan.FromSeconds(20);
        private const int MaxIngredientTransfers = 80;

        internal static int Remaining => needed.Count + (inFlightItemId == null ? 0 : 1);
        internal static bool IsActive => inFlightItemId != null || needed.Count > 0;
        internal static string InFlightId => inFlightItemId;
        internal static string ActiveRecipeName => activeRecipeName;

        internal static void ObserveRecipe(object recipe)
        {
            // Pinned-recipe updates are not always the selected fabricator recipe.
            // Never transfer resources merely because the UI updated.
            if (recipe != null) observedRecipe = recipe;
        }

        internal static bool Begin(object recipeType, Action<string> report)
        {
            if (IsActive || recipeType == null || !RequiresIngredientCosts())
                return false;
            if (!CraftFromContainersBridge.NetworkActive || !ContainerConfig.Enabled ||
                !(ContainerConfig.ManualPrefetchEnabled || ContainerConfig.AutoCraftEnabled ||
                  ContainerConfig.AutoBuildEnabled))
                return false;
            try
            {
                var techData = Reflect.Type("TechData");
                var get = techData?.GetMethods(All).FirstOrDefault(m =>
                    m.Name == "GetIngredients" && m.GetParameters().Length == 1 &&
                    m.GetParameters()[0].ParameterType.IsInstanceOfType(recipeType));
                var data = get?.Invoke(null, new[] { recipeType }) as IEnumerable;
                if (data == null) return false;
                var inventory = Reflect.Type("Inventory");
                var instance = CraftFromContainersBridge.Read(inventory, "main");
                if (instance == null) return false;
                var count = instance.GetType().GetMethods(All).FirstOrDefault(m =>
                    m.Name == "GetPickupCount" && m.GetParameters().Length == 1 &&
                    m.GetParameters()[0].ParameterType == recipeType.GetType());
                if (count == null) return false;

                var planned = new List<object>();
                foreach (var ingredient in data)
                {
                    if (ingredient == null) continue;
                    object techType = CraftFromContainersBridge.Read(ingredient, "techType");
                    object requiredValue = CraftFromContainersBridge.Read(ingredient, "amount");
                    if (techType == null || requiredValue == null) continue;
                    int required = Math.Max(0, Convert.ToInt32(requiredValue,
                        CultureInfo.InvariantCulture));
                    int owned = Math.Max(0, Convert.ToInt32(count.Invoke(instance,
                        new[] { techType }), CultureInfo.InvariantCulture));
                    int missing = Math.Max(0, required - owned);
                    if (missing > MaxIngredientTransfers ||
                        planned.Count + missing > MaxIngredientTransfers)
                    {
                        report?.Invoke("Recipe exceeds safe ingredient transfer limit");
                        return false;
                    }
                    for (int n = 0; n < missing; n++) planned.Add(techType);
                }
                if (planned.Count == 0)
                {
                    report?.Invoke("Required ingredients are already in your inventory");
                    return false;
                }
                foreach (var item in planned) needed.Enqueue(item);
                activeRecipeName = recipeType.ToString();
                completed = 0;
                report?.Invoke("Prefetch requested: " + planned.Count + " missing ingredient(s) for " +
                    activeRecipeName + " (craft manually after server delivery)");
                return true;
            }
            catch (Exception ex)
            {
                Cancel("Recipe lookup failed: " + ex.Message, report);
                return false;
            }
        }

        internal static bool RequiresIngredientCosts()
        {
            // Verified from the supplied Below Zero Assembly-CSharp.dll:
            // GameModeManager.GetOption<T>(GameOption.CraftingRequiresResources).
            // A failed lookup disables our resource-moving automation, leaving
            // Creative/no-cost mode and unknown modes to the untouched vanilla game.
            try
            {
                var mode = Reflect.Type("GameModeManager");
                var option = Reflect.Type("GameOption");
                if (mode == null || option == null) return false;
                var key = Enum.Parse(option, "CraftingRequiresResources");
                var getOption = mode.GetMethods(All).FirstOrDefault(m =>
                    m.Name == "GetOption" && m.IsStatic &&
                    m.IsGenericMethodDefinition && m.GetParameters().Length == 1 &&
                    m.GetParameters()[0].ParameterType == option);
                if (getOption == null) return false;
                return (bool)getOption.MakeGenericMethod(typeof(bool))
                    .Invoke(null, new[] { key });
            }
            catch { return false; }
        }

        internal static bool InventoryHasCompleteRecipe(object recipeType)
        {
            if (recipeType == null) return false;
            try
            {
                var techData = Reflect.Type("TechData");
                var method = techData?.GetMethods(All).FirstOrDefault(m =>
                    m.Name == "GetIngredients" && m.GetParameters().Length == 1 &&
                    m.GetParameters()[0].ParameterType.IsInstanceOfType(recipeType));
                var ingredients = method?.Invoke(null, new[] { recipeType }) as IEnumerable;
                if (ingredients == null) return false;
                var inv = CraftFromContainersBridge.Read(Reflect.Type("Inventory"), "main");
                if (inv == null) return false;
                var count = inv.GetType().GetMethods(All).FirstOrDefault(m =>
                    m.Name == "GetPickupCount" && m.GetParameters().Length == 1 &&
                    m.GetParameters()[0].ParameterType == recipeType.GetType());
                if (count == null) return false;
                foreach (var ingredient in ingredients)
                {
                    var type = CraftFromContainersBridge.Read(ingredient, "techType");
                    var amount = CraftFromContainersBridge.Read(ingredient, "amount");
                    if (type == null || amount == null) return false;
                    int required = Math.Max(0, Convert.ToInt32(amount,
                        CultureInfo.InvariantCulture));
                    int available = Convert.ToInt32(count.Invoke(inv, new[] { type }),
                        CultureInfo.InvariantCulture);
                    if (available < required) return false;
                }
                return true;
            }
            catch { return false; }
        }

        internal static bool BeginCurrentContext(Action<string> report)
        {
            // Building always takes precedence over pinned recipe notifications.
            // Never infer a building recipe from a random inventory-count query.
            var builder = Reflect.Type("Builder");
            var isPlacing = CraftFromContainersBridge.Read(builder, "isPlacing");
            if (isPlacing is bool && (bool)isPlacing)
            {
                var techType = CraftFromContainersBridge.Read(builder, "constructableTechType");
                if (techType != null && !string.Equals(techType.ToString(), "None",
                    StringComparison.OrdinalIgnoreCase))
                    return Begin(techType, report);
            }
            return BeginLastObserved(report);
        }

        internal static bool BeginLastObserved(Action<string> report)
        {
            object type = CraftFromContainersBridge.Read(observedRecipe, "techType");
            if (type == null)
            {
                report?.Invoke("No recipe selected. Open a pinned recipe first.");
                return false;
            }
            return Begin(type, report);
        }

        internal static void Pump(Action<string> report)
        {
            if (!IsActive) return;
            // World disconnect invalidates target locker identities and client session state.
            if (!CraftFromContainersBridge.NetworkActive)
            {
                Cancel("Multiplayer disconnected; prefetch cancelled", report);
                return;
            }
            if (inFlightItemId != null)
            {
                if (DateTime.UtcNow - sentAtUtc > TransferTimeout)
                    Cancel("Server did not deliver the requested item; prefetch aborted", report);
                return;
            }
            if (needed.Count == 0) return;
            var desired = needed.Peek();
            if (!CraftFromContainersBridge.TryRequestOne(desired))
            {
                Cancel("No eligible source item or server transfer API unavailable", report);
                return;
            }
            inFlightItemId = CraftFromContainersBridge.LastRequestedUniqueId;
            sentAtUtc = DateTime.UtcNow;
            needed.Dequeue();
            if (string.IsNullOrWhiteSpace(inFlightItemId))
                Cancel("Server request did not provide an item identity", report);
        }

        internal static bool AcknowledgeInventoryItem(string itemId, Action<string> report)
        {
            if (inFlightItemId == null || !string.Equals(inFlightItemId, itemId,
                StringComparison.Ordinal)) return false;
            inFlightItemId = null;
            CraftFromContainersBridge.AcknowledgeTransfer(itemId);
            completed++;
            if (needed.Count == 0)
            {
                var name = activeRecipeName;
                int delivered = completed;
                Reset();
                AutoCraftCoordinator.IngredientsDelivered();
                AutoBuildCoordinator.IngredientsDelivered();
                report?.Invoke("Server delivered " + delivered +
                    " ingredient(s) for " + name + ". You can now craft normally.");
            }
            return true;
        }

        internal static void Cancel(string reason, Action<string> report)
        {
            var wasActive = IsActive;
            Reset();
            if (wasActive) report?.Invoke(reason);
        }

        internal static void Reset()
        {
            needed.Clear();
            inFlightItemId = null;
            sentAtUtc = default(DateTime);
            observedRecipe = null;
            activeRecipeName = null;
            completed = 0;
        }
    }
}

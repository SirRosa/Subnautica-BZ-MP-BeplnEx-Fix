using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Globalization;

namespace Sirro.BZ.MP.ConstrutorZero
{
    /// <summary>
    /// Network-safe foundation for Craft from Containers BZ.
    ///
    /// IMPORTANT: The original mod calls ItemsContainer.DestroyItem and reports a
    /// successful Inventory.DestroyItem synchronously. That is unsafe with BOT Benson's
    /// authoritative, asynchronous server storage/inventory protocol.
    ///
    /// This bridge never directly destroys shared locker contents. The only permitted
    /// mutation is a server-validated storage->inventory transfer via the SAME event
    /// used by the multiplayer's ordinary locker drag-and-drop UI.
    ///
    /// No fabricated success is returned to the crafting system.
    /// </summary>
    internal static class CraftFromContainersBridge
    {
        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Static | BindingFlags.Instance;
        private static readonly Dictionary<object, object> Storage = new Dictionary<object, object>();
        private static readonly HashSet<string> Warned = new HashSet<string>();
        // In MP the bridge must never defer a user action when its essential
        // hooks are missing. Harmony can report success for unrelated hooks.
        private static readonly HashSet<string> ActiveHooks =
            new HashSet<string>(StringComparer.Ordinal);
        // Prevent repeated transfer requests for the same unique item while the
        // authoritative server is processing the previous request.
        private static readonly Dictionary<string, DateTime> PendingItemIds =
            new Dictionary<string, DateTime>(StringComparer.Ordinal);
        // UI stock snapshots skip already-requested item wrappers cheaply.
        // Network transfers remain guarded by persistent unique item IDs.
        private static readonly HashSet<object> PendingPickups = new HashSet<object>();
        // UI affordability is advisory only. It must never inflate inventory
        // counts or bypass vanilla resource consumption.
        private static readonly Dictionary<object, int> UiAvailableCache =
            new Dictionary<object, int>();
        private static DateTime uiCacheCreatedUtc;
        private static readonly TimeSpan UiCacheLifetime = TimeSpan.FromMilliseconds(850);
        private static object harmony;
        private static MethodInfo unpatch;
        private static MethodInfo patch;
        private static Type harmonyMethod;
        private static object lastRequestedTechType;
        private static string lastSentItemId;
        internal static string LastRequestedUniqueId => lastSentItemId;
        internal static bool NetworkActive => IsNetworkActive();
        internal static bool Ready => enabled;
        internal static object Read(object instance, string name) => Get(instance, name);
        internal static void AcknowledgeTransfer(string itemId)
        {
            // Do NOT release the same ID immediately on ACK: the local
            // locker representation can remain stale until a later server
            // packet updates it, and would otherwise be requested twice.
            // Keep this identity quarantined for the current session,
            // even after ACK; a stale locker object must never submit it again.
            InvalidateUiCache();
        }
        private static int lastTransferFrame = -1;
        private static int lastPruneFrame = -1;
        private static bool configHintShown;
        private static int installedHooks;
        private static bool enabled;

        internal static int RegisteredStorages => Storage.Count;
        internal static int HookCount => installedHooks;

        internal static void Install()
        {
            try
            {
                ContainerConfig.Load();
                var harmonyType = Reflect.Type("HarmonyLib.Harmony");
                harmonyMethod = Reflect.Type("HarmonyLib.HarmonyMethod");
                if (harmonyType == null || harmonyMethod == null)
                    throw new MissingMemberException("Harmony is not available");
                harmony = Activator.CreateInstance(harmonyType, "sirro.bz.mp.construtor-zero.containers");
                unpatch = harmonyType.GetMethod("UnpatchAll", All, null, new[] { typeof(string) }, null);
                patch = harmonyType.GetMethods(All).FirstOrDefault(m => m.Name == "Patch" &&
                    m.GetParameters().Length >= 3 && m.GetParameters()[0].ParameterType == typeof(MethodBase));
                if (patch == null) throw new MissingMethodException("Harmony.Patch");

                Hook("StorageContainer", "Awake", null, nameof(StorageAwake));
                Hook("StorageContainer", "ResetContainer", null, nameof(StorageAwake));
                Hook("SeamothStorageContainer", "Awake", null, nameof(StorageAwake));
                // Observes TechType queried by vanilla inventory. Does NOT inflate
                // crafting counts until secure synchronous consumption is possible.
                Hook("Inventory", "GetPickupCount", null, nameof(InventoryCountObserved));
                Hook("Inventory", "DestroyItem", null, nameof(InventoryDestroyObserved));
                Hook("Inventory", "OnAddItem", null, nameof(InventoryAdded));
                Hook("uGUI_RecipeEntry", "UpdateIngredients", null, nameof(RecipeObserved));
                // UI-only visuals. Never alter CrafterLogic fulfilment or
                // Inventory.GetPickupCount; the server owns real ingredients.
                Hook("TooltipFactory", "CraftRecipe", null,
                    nameof(CraftRecipeDisplayPostfix));
                Hook("uGUI_CraftingMenu", "Action", nameof(CraftActionPrefix), null);
                Hook("uGUI_CraftingMenu", "ActionAvailable", null,
                    nameof(CraftAvailablePostfix));
                Hook("uGUI_CraftingMenu", "Close", nameof(CraftMenuClosePrefix), null);
                Hook("uGUI_CraftingMenu", "OnDeselect", nameof(CraftMenuClosePrefix), null);
                Hook("Player", "Update", null, nameof(PlayerUpdate));
                // Required hooks are the transaction safety boundary:
                // a missing inventory acknowledgement or update loop would
                // otherwise leave a pending craft/build in limbo indefinitely.
                foreach (var required in new[] {
                    "StorageContainer.Awake",
                    "Inventory.OnAddItem",
                    "uGUI_CraftingMenu.Action",
                    "uGUI_CraftingMenu.ActionAvailable",
                    "uGUI_CraftingMenu.Close",
                    "uGUI_CraftingMenu.OnDeselect",
                    "Player.Update"
                })
                {
                    if (!ActiveHooks.Contains(required))
                        throw new MissingMethodException(
                            "CFC_REQUIRED_HOOK_MISSING: " + required);
                }
                enabled = true;
                Trace.Info("CFC_READY: hooks=" + installedHooks +
                    ", range=" + ContainerConfig.RangeMeters + "m, server_transfer_only=true");
                Trace.Info("CFC_SETTINGS: enabled=" + ContainerConfig.Enabled +
                    ", auto_craft=" + ContainerConfig.AutoCraftEnabled +
                    ", auto_build=" + ContainerConfig.AutoBuildEnabled +
                    ", manual_prefetch=" + ContainerConfig.ManualPrefetchEnabled);
                if (!ContainerConfig.AutoCraftEnabled && !ContainerConfig.AutoBuildEnabled &&
                    !ContainerConfig.ManualPrefetchEnabled)
                    Trace.Info("CFC_OFF_BY_CONFIG: enable auto_craft_enabled=true in " +
                        "ConstrutorZero.Containers.ini for fabricator crafting");
                Trace.Info("CFC_PROTOCOL: IsAllowed=false only means the MP event handler dispatched; " +
                    "it is NOT a server-accepted transaction");
                Trace.Info("CFC_LIMITATION: no server recipe-atomicity or rollback; " +
                    "ingredients remain in inventory if the operation is cancelled");
            }
            catch (Exception e)
            {
                Trace.Info("CFC_INIT_FAILED: " + e);
                Uninstall(); // also unpatch any hooks installed before failure
            }
        }

        private static void Hook(string className, string name, string prefix, string postfix)
        {
            var type = Reflect.Type(className);
            if (type == null) return; // SeamothStorageContainer absent in BZ is expected
            foreach (var method in type.GetMethods(All).Where(m => m.Name == name))
            {
                try
                {
                    var args = new object[patch.GetParameters().Length];
                    args[0] = method;
                    if (prefix != null)
                        args[1] = Activator.CreateInstance(harmonyMethod, typeof(CraftFromContainersBridge)
                            .GetMethod(prefix, All));
                    if (postfix != null)
                        args[2] = Activator.CreateInstance(harmonyMethod, typeof(CraftFromContainersBridge)
                            .GetMethod(postfix, All));
                    patch.Invoke(harmony, args);
                    installedHooks++;
                    ActiveHooks.Add(className + "." + name);
                }
                catch (Exception e)
                {
                    Once("CFC_HOOK_FAIL " + className + "." + name + ": " +
                        (e.InnerException ?? e).Message);
                }
            }
        }

        internal static void Uninstall()
        {
            try
            {
                enabled = false;
                if (unpatch != null)
                    unpatch.Invoke(harmony, new object[] { "sirro.bz.mp.construtor-zero.containers" });
            }
            catch (Exception e) { Trace.Info("CFC_UNPATCH_FAILED: " + e.Message); }
            AutoCraftCoordinator.Reset();
            AutoBuildCoordinator.Reset();
            RecipePrefetch.Reset();
            Storage.Clear();
            PendingItemIds.Clear();
            PendingPickups.Clear();
            InvalidateUiCache();
            uiCacheCreatedUtc = default(DateTime);
            lastSentItemId = null;
            lastRequestedTechType = null;
            lastPruneFrame = -1;
            configHintShown = false;
            installedHooks = 0;
            ActiveHooks.Clear();
        }

        private static void StorageAwake(object __instance)
        {
            if (__instance == null) return;
            try
            {
                var root = Get(__instance, "storageRoot") ?? Get(__instance, "transform");
                var contents = Get(__instance, "container");
                if (root != null)
                {
                    if (contents == null)
                    {
                        if (Storage.Remove(root)) InvalidateUiCache();
                    }
                    else if (!Storage.TryGetValue(root, out var existing) ||
                             !ReferenceEquals(existing, contents))
                    {
                        Storage[root] = contents;
                        InvalidateUiCache();
                    }
                }
            }
            catch (Exception e) { Once("CFC_REGISTER: " + e.Message); }
        }

        private static void DiscoverSceneStorages()
        {
            // Most placed lockers already exist before a client-side plugin
            // enables. Awake-only registration permanently misses them.
            try
            {
                var unity = Reflect.Type("UnityEngine.Object");
                var containerType = Reflect.Type("StorageContainer");
                if (unity == null || containerType == null) return;
                var find = unity.GetMethods(All).FirstOrDefault(m =>
                    m.Name == "FindObjectsOfType" && m.IsStatic &&
                    !m.IsGenericMethod && m.GetParameters().Length == 1 &&
                    m.GetParameters()[0].ParameterType == typeof(Type));
                if (find == null)
                {
                    Once("CFC_SCENE_DISCOVERY_UNAVAILABLE: UnityEngine.Object.FindObjectsOfType(Type)");
                    return;
                }
                var found = find.Invoke(null, new object[] { containerType }) as IEnumerable;
                if (found == null) return;
                int count = 0;
                foreach (var component in found)
                {
                    if (component == null) continue;
                    StorageAwake(component);
                    count++;
                }
                var network = typeof(Subnautica.API.Features.SubnauticaPlugin)
                    .Assembly.GetType("Subnautica.API.Features.Network");
                var identifier = Get(network, "Identifier");
                int supported = 0;
                foreach (var source in Storage.ToArray())
                {
                    if (ResolveLocker(source.Key, source.Value, identifier,
                        out _, out _)) supported++;
                }
                Trace.Info("CFC_STORAGE_SCAN: scene_containers=" + count +
                    ", registered=" + Storage.Count +
                    ", supported_lockers=" + supported +
                    " (only Locker/SmallLocker use this server protocol)");
            }
            catch (Exception ex)
            {
                Once("CFC_STORAGE_SCAN_FAILED: " +
                    (ex.InnerException ?? ex).Message);
            }
        }

        /// <summary>
        /// BOT Benson resolves locker ownership via ItemsContainer.tr.parent,
        /// not StorageContainer.storageRoot.gameObject. The latter is often
        /// a separate child whose Constructable belongs to a parent object.
        /// </summary>
        private static bool ResolveLocker(object origin, object container,
            object identifier, out object lockerType, out string containerId)
        {
            lockerType = null;
            containerId = null;
            if (origin == null || container == null || identifier == null)
                return false;

            var owner = Get(Get(Get(container, "tr"), "parent"), "gameObject");
            var constructable = Component(owner, "Constructable", false) ??
                Component(owner, "Constructable", true);
            if (constructable == null)
            {
                var legacyRoot = Get(origin, "gameObject");
                constructable = Component(legacyRoot, "Constructable", true);
            }
            if (constructable == null) return false;
            lockerType = Get(constructable, "techType");
            var name = lockerType?.ToString() ?? "";
            if ((name != "Locker" && name != "SmallLocker") ||
                !ContainerConfig.IsAllowed(name)) return false;

            containerId = GetIdentity(identifier, Get(constructable, "gameObject"));
            return !string.IsNullOrWhiteSpace(containerId);
        }

        /// <summary>
        /// The supplied BZ game DLL exposes GetItems(TechType,
        /// List&lt;InventoryItem&gt;) AND IEnumerable&lt;InventoryItem&gt;.
        /// Prefer the type-filtered query to keep the request tied to the
        /// exact TechType that the server receives.
        /// </summary>
        private static IEnumerable ItemsOfType(object container, object techType)
        {
            if (container == null || techType == null) return new object[0];
            var itemType = Reflect.Type("InventoryItem");
            if (itemType == null) return new object[0];
            var listType = typeof(List<>).MakeGenericType(itemType);
            var list = (IList)Activator.CreateInstance(listType);
            var method = container.GetType().GetMethods(All).FirstOrDefault(m =>
            {
                if (m.Name != "GetItems" || m.GetParameters().Length != 2)
                    return false;
                var parameters = m.GetParameters();
                return parameters[0].ParameterType.IsInstanceOfType(techType) &&
                    parameters[1].ParameterType.IsAssignableFrom(listType);
            });
            if (method == null)
            {
                Once("CFC_ITEMS_API_MISSING: " + container.GetType().FullName +
                    ".GetItems(TechType,List<InventoryItem>)");
                return new object[0];
            }
            method.Invoke(container, new object[] { techType, list });
            return list;
        }

        private static void PruneDeadStorages()
        {
            // Unity native objects can be destroyed while the managed wrapper
            // remains strongly referenced. Compare using UnityEngine.Object.op_Equality,
            // not ReferenceEquals, and prune on every use.
            try
            {
                var unityObject = Reflect.Type("UnityEngine.Object");
                var unityEquals = unityObject?.GetMethod("op_Equality",
                    BindingFlags.Public | BindingFlags.Static,
                    null, new[] { unityObject, unityObject }, null);
                if (unityEquals == null) return;
                foreach (var root in Storage.Keys.ToArray())
                {
                    bool dead = root == null || (bool)unityEquals.Invoke(null,
                        new[] { root, null });
                    if (dead) Storage.Remove(root);
                }
            }
            catch (Exception e) { Once("CFC_PRUNE_FAILED: " + e.Message); }
        }

        private static void InventoryCountObserved(object __instance, object[] __args)
        {
            if (!enabled || !ContainerConfig.Enabled || __args == null || __args.Length == 0)
                return;
            if (__args[0] != null) lastRequestedTechType = __args[0];
        }

        private static void InventoryDestroyObserved(object __instance, object[] __args, bool __result)
        {
            // Never mark false->true here: that would yield free crafting in multiplayer.
            if (!enabled || !ContainerConfig.Enabled || __result) return;
            if (__args == null || __args.Length == 0 || __args[0] == null) return;
            lastRequestedTechType = __args[0];
        }

        internal static void Report(string message) => Info(message);

        private static bool CraftActionPrefix(object __instance, object[] __args)
        {
            if (!Ready) return true;
            try { return AutoCraftCoordinator.CraftActionPrefix(__instance, __args); }
            catch (Exception e)
            {
                FailClosed("crafting action", e);
                return true; // never crash vanilla crafting because of our prefix
            }
        }

        private static void CraftMenuClosePrefix(object __instance)
        {
            if (!Ready) return;
            try { AutoCraftCoordinator.CraftingMenuClosing(__instance); }
            catch (Exception e) { FailClosed("crafting menu close", e); }
        }

        private static void FailClosed(string stage, Exception e)
        {
            // A reflection/engine error must never escape a Harmony hook or
            // leave the queue running on subsequent Player.Update frames.
            enabled = false;
            Once("CFC_RUNTIME_DISABLED (" + stage + "): " +
                (e.InnerException ?? e));
            try { AutoCraftCoordinator.Reset(); } catch { }
            try { AutoBuildCoordinator.Reset(); } catch { }
            try { RecipePrefetch.Reset(); } catch { }
            InvalidateUiCache();
            lastSentItemId = null;
            // Do not clear PendingItemIds. A packet may still be in flight:
            // clearing would allow a second request against stale storage.
        }

        private static void CraftAvailablePostfix(object[] __args, ref bool __result)
        {
            if (__result || !Ready || !ContainerConfig.AutoCraftEnabled ||
                __args == null || __args.Length != 1) return;
            // Only the fabricator UI's availability may be extended. The
            // original CrafterLogic / Inventory APIs remain untouched and
            // consume REAL items only after explicit server delivery.
            __result = AutoCraftCoordinator.CanAdvertise(__args[0]);
        }

        private static void RecipeObserved(object __instance, object[] __args)
        {
            if (!enabled || !ContainerConfig.Enabled) return;
            if (ContainerConfig.ManualPrefetchEnabled)
                RecipePrefetch.ObserveRecipe(__instance);
            if (!ContainerConfig.AutoCraftEnabled) return;
            try { RefreshPinnedRecipeVisuals(__instance, __args); }
            catch (Exception ex)
            {
                Once("CFC_RECIPE_UI_FAILED: " +
                    (ex.InnerException ?? ex).Message);
            }
        }

        private static int LocalCount(object techType)
        {
            var inv = Get(Reflect.Type("Inventory"), "main");
            var method = inv?.GetType().GetMethods(All).FirstOrDefault(m =>
                m.Name == "GetPickupCount" && m.GetParameters().Length == 1 &&
                m.GetParameters()[0].ParameterType.IsInstanceOfType(techType));
            return method == null ? 0 : Math.Max(0, Convert.ToInt32(
                method.Invoke(inv, new[] { techType }), CultureInfo.InvariantCulture));
        }

        private static bool WriteMember(object obj, string name, object value)
        {
            if (obj == null) return false;
            const BindingFlags flags = BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.Instance;
            var field = obj.GetType().GetField(name, flags);
            if (field != null) { field.SetValue(obj, value); return true; }
            var property = obj.GetType().GetProperty(name, flags);
            if (property != null && property.CanWrite)
            {
                property.SetValue(obj, value, null);
                return true;
            }
            return false;
        }

        private static void RefreshPinnedRecipeVisuals(object entry, object[] args)
        {
            if (entry == null || args == null || args.Length < 1) return;
            var items = Get(entry, "items") as IList;
            var techType = Get(entry, "techType");
            var manager = Get(entry, "manager");
            if (items == null || techType == null || manager == null) return;
            var ingredientsMethod = Reflect.Type("TechData")?.GetMethods(All)
                .FirstOrDefault(m => m.Name == "GetIngredients" &&
                    m.IsStatic && m.GetParameters().Length == 1 &&
                    m.GetParameters()[0].ParameterType.IsInstanceOfType(techType));
            var ingredients = ingredientsMethod?.Invoke(null, new[] { techType })
                as IEnumerable;
            if (ingredients == null) return;
            int index = 0;
            foreach (var ingredient in ingredients)
            {
                if (index >= items.Count) break;
                var kind = Get(ingredient, "techType");
                var needed = Get(ingredient, "amount");
                var recipeItem = items[index++];
                if (kind == null || needed == null || recipeItem == null) continue;
                int required = Convert.ToInt32(needed, CultureInfo.InvariantCulture);
                if (required <= 0) continue;
                var current = Math.Min(9999,
                    LocalCount(kind) + CountAvailable(kind));
                var color = Get(manager, current >= required ?
                    "colorGreen" : "colorRed");
                var label = Get(recipeItem, "text");
                if (label != null && color != null)
                    WriteMember(label, "color", color);
                var set = recipeItem.GetType().GetMethods(All).FirstOrDefault(m =>
                    m.Name == "Set" && m.GetParameters().Length == 4 &&
                    m.GetParameters()[0].ParameterType.IsInstanceOfType(kind));
                // Do not trigger the original item-acquisition ping for an
                // advisory locker preview.
                set?.Invoke(recipeItem, new object[] { kind, current, required, false });
            }
        }

        private static void CraftRecipeDisplayPostfix(object[] __args)
        {
            if (!Ready || !ContainerConfig.Enabled ||
                !ContainerConfig.AutoCraftEnabled ||
                !IsNetworkActive() || __args == null || __args.Length != 3 ||
                !(__args[1] is bool locked) || locked) return;
            try
            {
                // TooltipFactory.CraftRecipe(TechType,bool,TooltipData)
                // builds the RED ingredient icons using only the inventory.
                // Rebuild those *display icons only* from the combined advisory
                // stock, leaving vanilla ingredient validation untouched.
                var recipe = __args[0];
                var icons = Get(__args[2], "icons") as IList;
                if (recipe == null || icons == null) return;
                var ingredients = Reflect.Type("TechData")?.GetMethods(All)
                    .FirstOrDefault(m => m.Name == "GetIngredients" && m.IsStatic &&
                        m.GetParameters().Length == 1 &&
                        m.GetParameters()[0].ParameterType.IsInstanceOfType(recipe))
                    ?.Invoke(null, new[] { recipe }) as IEnumerable;
                if (ingredients == null) return;
                var spriteManager = Reflect.Type("SpriteManager");
                var iconType = Reflect.Type("TooltipIcon");
                var language = Get(Reflect.Type("Language"), "main");
                var getLanguage = language?.GetType().GetMethod("Get", All,
                    null, new[] { typeof(string) }, null);
                var spriteGet = spriteManager?.GetMethods(All).FirstOrDefault(m =>
                    m.Name == "Get" && m.IsStatic &&
                    m.GetParameters().Length == 1 &&
                    m.GetParameters()[0].ParameterType == recipe.GetType());
                var iconCtor = iconType?.GetConstructors(All)
                    .FirstOrDefault(c => c.GetParameters().Length == 2 &&
                        c.GetParameters()[1].ParameterType == typeof(string));
                if (spriteGet == null || iconCtor == null) return;
                var candidate = new List<object>();
                foreach (var ingredient in ingredients)
                {
                    var kind = Get(ingredient, "techType");
                    var amount = Get(ingredient, "amount");
                    if (kind == null || amount == null) return;
                    var required = Convert.ToInt32(amount, CultureInfo.InvariantCulture);
                    if (required <= 0) return;
                    var available = LocalCount(kind) + CountAvailable(kind);
                    var name = getLanguage?.Invoke(language, new object[] {
                        kind.ToString() }) as string ?? kind.ToString();
                    var text = "<color=" + (available >= required ?
                        "#94DE00FF" : "#FF7777FF") + ">" + name +
                        (required > 1 ? " x" + required : "") + "</color>";
                    var sprite = spriteGet.Invoke(null, new[] { kind });
                    candidate.Add(iconCtor.Invoke(new[] { sprite, (object)text }));
                }
                // No change on any failed lookup. Avoid replacing icons when
                // the game has different or modded tooltip ABI.
                icons.Clear();
                foreach (var icon in candidate) icons.Add(icon);
            }
            catch (Exception ex)
            {
                Once("CFC_TOOLTIP_UI_FAILED: " +
                    (ex.InnerException ?? ex).Message);
            }
        }

        private static void InventoryAdded(object __instance, object[] __args)
        {
            if (!Ready || !ContainerConfig.Enabled || !RecipePrefetch.IsActive)
                return;
            try
            {
                var inventory = Reflect.Type("Inventory");
                if (__instance == null || !ReferenceEquals(__instance, Get(inventory, "main")))
                    return;
                if (__args == null || __args.Length == 0 || __args[0] == null) return;
                var pickup = Get(__args[0], "item");
                var gameObject = Get(pickup, "gameObject");
                var net = typeof(Subnautica.API.Features.SubnauticaPlugin).Assembly
                    .GetType("Subnautica.API.Features.Network");
                var id = GetIdentity(Get(net, "Identifier"), gameObject);
                // Identity alone is not sufficient. A later packet can reuse
                // an object wrapper or a mod can replace its TechType.
                // Confirm that the delivered item is still the requested type.
                RecipePrefetch.AcknowledgeInventoryItem(
                    id, GetTechType(pickup), Info);
            }
            catch (Exception e) { Once("CFC_INVENTORY_ACK_FAILED: " + e.Message); }
        }

        private static void PlayerUpdate()
        {
            if (!Ready || !ContainerConfig.Enabled) return;
            try
            {
            var unityTimeType = Reflect.Type("UnityEngine.Time");
            var currentFrame = Convert.ToInt32(Get(unityTimeType, "frameCount") ?? 0);
            if (lastPruneFrame < 0 || currentFrame < lastPruneFrame ||
                currentFrame - lastPruneFrame >= 900)
            {
                lastPruneFrame = currentFrame;
                DiscoverSceneStorages();
                PruneDeadStorages();
            }
            if (!IsNetworkActive())
            {
                // A disconnect ends the entire deferred operation, not just its
                // ingredient queue. Otherwise the crafting/building coordinators
                // can retain a stale replay across the next session.
                AutoBuildCoordinator.Cancel("Multiplayer disconnected; building cancelled");
                AutoCraftCoordinator.Cancel("Multiplayer disconnected; crafting cancelled");
                RecipePrefetch.Cancel("Multiplayer disconnected; requests cancelled", Info);
                // An item ID may be requested again only in a new session.
                PendingItemIds.Clear();
                PendingPickups.Clear();
                InvalidateUiCache();
                uiCacheCreatedUtc = default(DateTime);
                lastSentItemId = null;
                return;
            }
            if (!configHintShown && !ContainerConfig.AutoCraftEnabled &&
                !ContainerConfig.AutoBuildEnabled &&
                !ContainerConfig.ManualPrefetchEnabled)
            {
                configHintShown = true;
                Info("CFC is installed but all transfers are OFF in " +
                    "ConstrutorZero.Containers.ini. Set auto_craft_enabled=true " +
                    "and restart the game.");
            }
            // Validate the pending ghost/recipe first. Only then dispatch
            // another locker request; otherwise a cancelled operation can
            // still move one extra item in the same update frame.
            AutoBuildCoordinator.Tick();
            AutoCraftCoordinator.Tick();
            RecipePrefetch.Pump(Info);
            if (!ContainerConfig.ManualPrefetchEnabled)
                return;
            try
            {
                var input = Reflect.Type("UnityEngine.Input");
                var keyCode = Reflect.Type("UnityEngine.KeyCode");
                var time = Reflect.Type("UnityEngine.Time");
                var frame = Convert.ToInt32(Get(time, "frameCount") ?? 0);
                if (frame == lastTransferFrame) return;
                var down = input?.GetMethod("GetKeyDown", All, null, new[] { keyCode }, null);
                if (keyCode == null || down == null) return;
                var hotkey = Enum.Parse(keyCode, ContainerConfig.TransferKey);
                if (!(bool)down.Invoke(null, new[] { hotkey })) return;
                lastTransferFrame = frame;
                if (RecipePrefetch.IsActive)
                {
                    RecipePrefetch.Cancel("Recipe prefetch cancelled by user", Info);
                    return;
                }
                if (RecipePrefetch.BeginCurrentContext(Info))
                    RecipePrefetch.Pump(Info);
                else if (lastRequestedTechType != null)
                    Trace.Info("CFC: last vanilla inventory query was " + lastRequestedTechType);
            }
            catch (Exception e) { Once("CFC_HOTKEY: " + e.Message); }
            }
            catch (Exception e) { FailClosed("Player.Update", e); }
        }

        /// <summary>
        /// Request a server-authoritative SINGLE item transfer. The server validates
        /// the item's unique ID, removes it from locker, and sends a normal
        /// inventory addition packet. The client MUST NOT mutate local storage here.
        /// </summary>
        internal static bool TryRequestOne(object wantedTechType)
        {
            if (!Ready || wantedTechType == null || !ContainerConfig.Enabled)
                return false;
            // Server validation is mandatory even for the host. Never bypass it.
            if (!IsNetworkActive()) return false;
            try
            {
                PruneDeadStorages();
                var player = Reflect.Type("Player");
                var local = Get(player, "main");
                var pos = Get(Get(local, "transform"), "position");
                if (pos == null) return false;
                var network = typeof(Subnautica.API.Features.SubnauticaPlugin).Assembly
                    .GetType("Subnautica.API.Features.Network");
                var identifier = Get(network, "Identifier");
                var argsType = Reflect.Type("Subnautica.Events.EventArgs.StorageItemRemovingEventArgs");
                var handlerType = Reflect.Type("Subnautica.Events.Handlers.Storage");
                var dispatch = handlerType?.GetMethod("OnItemRemoving", All);
                if (argsType == null || identifier == null || dispatch == null)
                {
                    Once("CFC_NETWORK_UNAVAILABLE: no Storage.ItemRemoving event");
                    return false;
                }

                foreach (var kv in Storage.ToArray())
                {
                    var origin = kv.Key;
                    var container = kv.Value;
                    if (origin == null || container == null ||
                        Distance(pos, Get(Get(origin, "transform") ?? origin, "position")) > ContainerConfig.RangeMeters)
                        continue;
                    if (!ResolveLocker(origin, container, identifier,
                        out var lockerType, out var containerId)) continue;

                    foreach (var item in ItemsOfType(container, wantedTechType))
                    {
                        var pickup = Get(item, "item");
                        if (pickup == null || !Equals(GetTechType(pickup), wantedTechType)) continue;
                        var gameObject = Get(pickup, "gameObject");
                        var itemId = GetIdentity(identifier, gameObject);
                        if (string.IsNullOrWhiteSpace(itemId)) continue;
                        var now = DateTime.UtcNow;
                        // A slow or rejected server request is NOT proof that
                        // its item can be requested again. Keep the ID quarantined
                        // until exact inventory acknowledgement or a disconnect.
                        // The old five-second expiry allowed duplicate requests
                        // while the prefetch timeout was still 20 seconds.
                        if (PendingItemIds.ContainsKey(itemId))
                            continue;
                        // Explicit, unique identity guards are required before issuing server requests.
                        var constructor = argsType.GetConstructors(All)
                            .FirstOrDefault(c => c.GetParameters().Length == 5);
                        if (constructor == null) return false;
                        var values = new object[] { containerId, lockerType, itemId, pickup, true };
                        var request = constructor.Invoke(values);
                        dispatch.Invoke(null, new[] { request });
                        // In BOT Benson the storage processor sets IsAllowed=false
                        // and emits a server packet. Leaving it true means the
                        // event was not handled, so no network request took place.
                        var isAllowed = Get(request, "IsAllowed");
                        if (!(isAllowed is bool) || (bool)isAllowed)
                        {
                            Once("CFC_TRANSFER_BLOCKED: no multiplayer storage handler " +
                                "accepted the transfer event");
                            return false;
                        }
                        PendingItemIds[itemId] = now;
                        PendingPickups.Add(pickup);
                        InvalidateUiCache();
                        lastSentItemId = itemId;
                        Trace.Info("CFC_SERVER_TRANSFER_REQUEST: " + wantedTechType +
                            ", item=" + itemId + ", locker=" + containerId);
                        return true;
                    }
                }
            }
            catch (Exception e) { Once("CFC_TRANSFER_FAILED: " + (e.InnerException ?? e).Message); }
            return false;
        }

        /// <summary>
        /// UI-only advisory count. Never modifies vanilla inventory totals or
        /// grants crafting success, and counts distinct, unrequested server IDs.
        /// The server must still validate every later actual transfer.
        /// </summary>
        internal static int CountAvailable(object wantedTechType)
        {
            if (!Ready || !ContainerConfig.Enabled || !IsNetworkActive() ||
                wantedTechType == null) return 0;
            try
            {
                // Fabricator probes ActionAvailable for many recipes per
                // selection. Count all types in ONE snapshot, not by walking
                // every locker separately for every candidate ingredient.
                if (uiCacheCreatedUtc == default(DateTime) ||
                    DateTime.UtcNow - uiCacheCreatedUtc >= UiCacheLifetime)
                    RefreshUiStockSnapshot();
                return UiAvailableCache.TryGetValue(wantedTechType, out var n) ? n : 0;
            }
            catch (Exception ex)
            {
                Once("CFC_UI_AVAILABILITY_FAILED: " +
                    (ex.InnerException ?? ex).Message);
                return 0;
            }
        }

        private static void InvalidateUiCache()
        {
            UiAvailableCache.Clear();
            uiCacheCreatedUtc = default(DateTime);
        }

        private static void RefreshUiStockSnapshot()
        {
            UiAvailableCache.Clear();
            uiCacheCreatedUtc = DateTime.UtcNow;
            var player = Reflect.Type("Player");
            var origin = Get(Get(player, "main"), "transform");
            var position = Get(origin, "position");
            var network = typeof(Subnautica.API.Features.SubnauticaPlugin)
                .Assembly.GetType("Subnautica.API.Features.Network");
            var identifier = Get(network, "Identifier");
            if (position == null || identifier == null) return;

            foreach (var entry in Storage)
            {
                var root = entry.Key;
                var container = entry.Value;
                if (root == null || container == null ||
                    Distance(position, Get(Get(root, "transform") ?? root, "position")) >
                        ContainerConfig.RangeMeters) continue;
                if (!ResolveLocker(root, container, identifier, out _, out _))
                    continue;

                // BZ ItemsContainer implements IEnumerable<InventoryItem>.
                // Enumerate ONCE for every material type without per-item
                // network identity reflection (only actual transfers need it).
                if (!(container is IEnumerable inventoryItems)) continue;
                foreach (var item in inventoryItems)
                {
                    var pickup = Get(item, "item");
                    if (pickup == null || PendingPickups.Contains(pickup)) continue;
                    var techType = GetTechType(pickup);
                    if (techType == null) continue;
                    UiAvailableCache.TryGetValue(techType, out var current);
                    if (current < 80) UiAvailableCache[techType] = current + 1;
                }
            }
        }

        private static object GetTechType(object pickupable)
        {
            var method = pickupable.GetType().GetMethod("GetTechType", All,
                null, Type.EmptyTypes, null);
            return method?.Invoke(pickupable, null);
        }

        private static string GetIdentity(object identifier, object gameObject)
        {
            if (gameObject == null) return null;
            var methods = identifier.GetType().GetMethods(All).Where(m => m.Name == "GetIdentityId");
            foreach (var method in methods)
            {
                var p = method.GetParameters();
                if (p.Length < 1 || !p[0].ParameterType.IsInstanceOfType(gameObject)) continue;
                try
                {
                    var args = p.Length == 1 ? new[] { gameObject } :
                        new object[] { gameObject, false };
                    return method.Invoke(identifier, args) as string;
                }
                catch { }
            }
            return null;
        }

        private static object Component(object gameObject, string name, bool parent)
        {
            var componentType = Reflect.Type(name);
            if (componentType == null) return null;
            var method = gameObject.GetType().GetMethod(
                parent ? "GetComponentInParent" : "GetComponent",
                All, null, new[] { typeof(Type) }, null);
            return method?.Invoke(gameObject, new object[] { componentType });
        }

        private static float Distance(object positionA, object positionB)
        {
            if (positionA == null || positionB == null) return float.MaxValue;
            var dx = Reflect.Component(positionA, "x") - Reflect.Component(positionB, "x");
            var dy = Reflect.Component(positionA, "y") - Reflect.Component(positionB, "y");
            var dz = Reflect.Component(positionA, "z") - Reflect.Component(positionB, "z");
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private static bool IsNetworkActive()
        {
            try
            {
                var network = typeof(Subnautica.API.Features.SubnauticaPlugin).Assembly
                    .GetType("Subnautica.API.Features.Network");
                return (bool)(Get(network, "IsMultiplayerActive") ?? false);
            }
            catch { return false; }
        }

        private static object Get(object obj, string name)
        {
            if (obj == null) return null;
            var t = obj as Type ?? obj.GetType();
            var instance = obj is Type ? null : obj;
            var field = t.GetField(name, All);
            if (field != null) return field.GetValue(instance);
            return t.GetProperty(name, All)?.GetValue(instance, null);
        }

        private static void Info(string message)
        {
            // Normal transfers should NEVER flood the HUD with mod logs.
            // Player-facing messages are opt-in; diagnostic messages remain
            // available in the plugin/client log when investigating failures.
            Trace.Info("CFC: " + message);
            if (!ContainerConfig.ShowMessages) return;
            try
            {
                var error = Reflect.Type("ErrorMessage");
                error?.GetMethod("AddMessage", All, null, new[] { typeof(string) }, null)
                    ?.Invoke(null, new object[] { "Construtor Zero: " + message });
            }
            catch { }
        }

        private static void Once(string message)
        {
            if (Warned.Add(message)) Trace.Info(message);
        }
    }

    internal static class ContainerConfig
    {
        internal static float RangeMeters = 100f;
        internal static bool Enabled = true;
        internal static bool ManualPrefetchEnabled = false;
        internal static bool AutoCraftEnabled = false;
        internal static bool AutoBuildEnabled = false;
        internal static bool AutoPlaceAfterPrefetch = false;
        internal static bool ShowMessages = false;
        internal static string TransferKey = "F6";
        internal static readonly Dictionary<string, bool> Allowed = new Dictionary<string, bool>(
            StringComparer.OrdinalIgnoreCase);

        internal static bool IsAllowed(string name) =>
            !Allowed.TryGetValue(name, out var allowed) || allowed;

        internal static void Load()
        {
            try
            {
                string folder = Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData),
                    ".botbenson", "Subnautica Below Zero", "Game", "Config");
                Directory.CreateDirectory(folder);
                var filename = Path.Combine(folder, "ConstrutorZero.Containers.ini");
                if (!File.Exists(filename))
                    File.WriteAllLines(filename, new[] {
                        "# Construtor Zero CFC (BOT Benson 3.0.1): server-mediated inventory staging.",
                        "# Each ingredient is requested individually; only vanilla crafting consumes items.",
                        "# No atomic cross-recipe server transaction, rollback, or local locker deletion.",
                        "# Manual F6 prefetch uses most recently observed pinned recipe.",
                        "# Auto craft/build and auto placement remain disabled until explicit opt-in.",
                        "enabled=true",
                        "range_meters=100",
                        "manual_prefetch_enabled=false",
                        "# Auto craft can advertise confirmed local locker stock to the fabricator UI.",
                        "# A UI availability flag is NOT a server authorization or a free item.",
                        "# Enable only after two-client tests on a backed-up world.",
                        "auto_craft_enabled=false",
                        "# For furniture only: prefetch resources without changing server building packets.",
                        "# Ghost pose must remain unchanged; base module prefetch is not supported.",
                        "auto_build_enabled=false",
                        "auto_place_after_prefetch=false",
                        "# Screen messages are verbose and can cause HUD stutter.",
                        "show_cfc_messages=false",
                        "transfer_key=F6",
                        "Locker=true",
                        "SmallLocker=true"
                    });
                foreach (var raw in File.ReadAllLines(filename))
                {
                    var parts = raw.Split(new[] { '=' }, 2);
                    if (parts.Length != 2 || raw.TrimStart().StartsWith("#")) continue;
                    string name = parts[0].Trim();
                    string value = parts[1].Trim();
                    if (name.Equals("enabled", StringComparison.OrdinalIgnoreCase))
                        Enabled = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    else if (name.Equals("manual_prefetch_enabled", StringComparison.OrdinalIgnoreCase))
                        ManualPrefetchEnabled = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    else if (name.Equals("auto_craft_enabled", StringComparison.OrdinalIgnoreCase))
                        AutoCraftEnabled = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    else if (name.Equals("auto_build_enabled", StringComparison.OrdinalIgnoreCase))
                        AutoBuildEnabled = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    else if (name.Equals("auto_place_after_prefetch", StringComparison.OrdinalIgnoreCase))
                        AutoPlaceAfterPrefetch = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    else if (name.Equals("show_cfc_messages", StringComparison.OrdinalIgnoreCase))
                        ShowMessages = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    else if (name.Equals("range_meters", StringComparison.OrdinalIgnoreCase) &&
                        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var range))
                        RangeMeters = Math.Max(1f, Math.Min(300f, range));
                    else if (name.Equals("transfer_key", StringComparison.OrdinalIgnoreCase))
                        TransferKey = value;
                    else if (name.Equals("Locker", StringComparison.OrdinalIgnoreCase) ||
                             name.Equals("SmallLocker", StringComparison.OrdinalIgnoreCase))
                        Allowed[name] = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception e) { Trace.Info("CFC_CONFIG_WARNING: " + e.Message); }
        }
    }
}

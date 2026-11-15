using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace Sirro.BZ.MP.ConstrutorZero
{
    /// <summary>
    /// Multiplayer-aware reimplementation of the non-Nautilus Building Tweaks controls.
    /// This never changes parent transforms or serializes network entities.
    /// World-mutating features are intentionally gated pending real two-client verification.
    /// </summary>
    internal static class BuildingTweaksBridge
    {
        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static;
        private static object harmony;
        private static MethodInfo unpatch;
        private static Type harmonyMethod;
        private static MethodInfo patchMethod;
        private static int patched;
        private static int lastKeyFrame = -1;
        private static bool attachToTarget;
        private static bool fullOverride;
        private static bool extendedRange;
        private static bool frozen;
        private static bool freezeInitialized;
        private static bool hasLoggedWorldGuard;
        private static float? originalDefaultRange, originalMaxRange;
        private static readonly Dictionary<object, Tuple<float, float>> ConstructableRanges =
            new Dictionary<object, Tuple<float, float>>();
        private static bool[] originalPlacementFlags;
        private static IList expandedSurfaceList;
        private static object addedSurfaceType;
        private static readonly string[] PlacementFlagNames = {
            "allowedOnConstructables", "allowedInBase", "allowedInSub", "allowedOutside"
        };
        private static float activeRange = 2f;
        private static object frozenPlacePosition;
        private static Type inputType, keyCodeType, unityTimeType;

        public static int PatchCount => patched;
        public static bool FullOverrideEnabled => IsUnsafePlacementEnabled();
        public static bool AttachToTargetRequested => attachToTarget;

        public static void Install()
        {
            try
            {
                Cfg.Load();
                var ht = Reflect.Type("HarmonyLib.Harmony");
                harmonyMethod = Reflect.Type("HarmonyLib.HarmonyMethod");
                if (ht == null || harmonyMethod == null)
                    throw new MissingMemberException("Harmony not available");
                harmony = Activator.CreateInstance(ht, "sirro.bz.mp.construtor-zero.building");
                patchMethod = ht.GetMethods(All).FirstOrDefault(m => m.Name == "Patch" &&
                    m.GetParameters().Length >= 3 &&
                    m.GetParameters()[0].ParameterType == typeof(MethodBase));
                unpatch = ht.GetMethod("UnpatchSelf", All);
                if (unpatch == null)
                    unpatch = ht.GetMethod("UnpatchAll", All,
                        null, new[] { typeof(string) }, null);
                if (patchMethod == null) throw new MissingMethodException("Harmony.Patch");

                // User input and ghost changes are applied to the LOCAL vanilla Builder only.
                Hook("Builder", "Update", null, nameof(UpdatePostfix));
                Hook("Builder", "End", null, nameof(EndPostfix));
                Hook("Builder", "CreateGhost", null, nameof(CreateGhostPostfix));
                Hook("Targeting", "GetTarget", nameof(TargetingRangePrefix), null);
                Hook("Builder", "UpdateAllowed", nameof(UpdateAllowedPrefix), nameof(UpdateAllowedPostfix));
                Hook("Builder", "ValidateOutdoor", null, nameof(AllowedPostfix));
                Hook("Builder", "GetObstacles", null, nameof(GetObstaclesPostfix));
                Hook("Builder", "CheckSpace", null, nameof(CheckSpacePostfix));
                Hook("Builder", "TryPlace", nameof(TryPlacePrefix), nameof(TryPlacePostfix));
                Hook("Constructable", "DeconstructionAllowed", nameof(DeconstructionPrefix), null);
                Hook("BaseDeconstructable", "DeconstructionAllowed", nameof(DeconstructionPrefix), null);
                Hook("Builder", "CanDestroyObject", null, nameof(CanDestroyObjectPostfix));
                Hook("PowerConsumer", "IsPowered", nameof(PowerConsumerPrefix), null);
                // Original Tobey SnapBuilder prevents PlaceTool rotation input
                // from simultaneously triggering quick-slot hotkeys.
                Hook("PlaceTool", "LateUpdate", null, nameof(PlaceToolLateUpdatePostfix));
                Hook("PlaceTool", "OnHolster", null, nameof(PlaceToolExitPostfix));
                Hook("PlaceTool", "OnPlace", null, nameof(PlaceToolExitPostfix));
                Trace.Info("BUILDING_PATCHES_INSTALLED=" + patched +
                    " | multiplayer=" + IsMultiplayer() +
                    " | unsafeWorldChanges=" + Cfg.AllowUnsafeWorldChanges);
                if (IsMultiplayer())
                    Trace.Info("SYNC_GUARD: mobile attachments, server-side geometry and unsafe deconstruction are disabled");
            }
            catch (Exception e) { Trace.Info("BUILDING_PATCH_ERROR: " + e); }
        }

        private static void Hook(string typeName, string methodName, string prefixName, string postfixName)
        {
            var type = Reflect.Type(typeName);
            if (type == null) { Trace.Info("BUILDING_SKIP: no " + typeName); return; }
            var methods = type.GetMethods(All).Where(m => m.Name == methodName).ToArray();
            if (methods.Length == 0) { Trace.Info("BUILDING_SKIP: " + typeName + "." + methodName + " missing"); return; }
            foreach (var original in methods)
            {
                try
                {
                    // Limit overloaded methods to expected supported shapes.
                    if (methodName == "CheckSpace" && original.GetParameters().Length < 5) continue;
                    var args = new object[patchMethod.GetParameters().Length];
                    args[0] = original;
                    if (prefixName != null)
                        args[1] = Activator.CreateInstance(harmonyMethod, typeof(BuildingTweaksBridge)
                            .GetMethod(prefixName, All));
                    if (postfixName != null)
                        args[2] = Activator.CreateInstance(harmonyMethod, typeof(BuildingTweaksBridge)
                            .GetMethod(postfixName, All));
                    // Run local ghost transformations before multiplayer's default-priority
                    // Builder.Update postfix serializes ghost data to the network.
                    if (methodName == "Update" && args[2] != null)
                    {
                        var hmtype = args[2].GetType();
                        var priority = hmtype.GetField("priority", All);
                        if (priority != null) priority.SetValue(args[2], 800);
                    }
                    // BOT Benson patches Builder.TryPlace with a Harmony Prefix
                    // that SENDS THE CONSTRUCTION PACKET and returns false.
                    // Our missing-ingredient guard MUST execute before that
                    // prefix or a deferred build would still reach the server.
                    // Priority.First (800) is used by MP's identity prefix.
                    if (methodName == "TryPlace" && args[1] != null)
                    {
                        var priority = args[1].GetType().GetField("priority", All);
                        if (priority != null) priority.SetValue(args[1], 1000);
                    }
                    patchMethod.Invoke(harmony, args);
                    patched++;
                    Trace.Info("BUILDING_HOOK: " + typeName + "." + methodName +
                        "(" + original.GetParameters().Length + ")");
                }
                catch (Exception e)
                {
                    Trace.Info("BUILDING_HOOK_FAILED: " + typeName + "." + methodName + ": " +
                        (e.InnerException ?? e).Message);
                }
            }
        }

        public static void Uninstall()
        {
            try
            {
                SetQuickSlotHotkeysIgnored(false);
                RestoreRanges();
                RestorePlacementFlags();
                if (unpatch == null) Trace.Info("BUILDING_UNPATCH_WARNING: no Harmony unpatch API");
                else if (unpatch.Name == "UnpatchAll")
                    unpatch.Invoke(harmony, new object[] { "sirro.bz.mp.construtor-zero.building" });
                else unpatch.Invoke(harmony, null);
            }
            catch (Exception e) { Trace.Info("BUILDING_UNPATCH: " + e.Message); }
            frozen = attachToTarget = fullOverride = extendedRange = false;
            freezeInitialized = false;
            patched = 0;
        }

        private static bool IsMultiplayer()
        {
            try
            {
                var network = typeof(Subnautica.API.Features.SubnauticaPlugin).Assembly
                    .GetType("Subnautica.API.Features.Network", false);
                return (bool)(network?.GetProperty("IsMultiplayerActive", All)?.GetValue(null, null)
                    ?? network?.GetField("IsMultiplayerActive", All)?.GetValue(null) ?? true);
            }
            catch { return true; }
        }

        // MP safety: even if an opt-in override is enabled, do not pretend dynamic parenting
        // can synchronize. No packet handler is registered for parent changes.
        public static bool IsUnsafePlacementEnabled() =>
            Cfg.AllowUnsafeWorldChanges && fullOverride && !IsMultiplayer();

        private static void UpdatePostfix()
        {
            try
            {
                var builder = Reflect.Type("Builder");
                if (builder == null || Value(builder, "ghostModel") == null)
                {
                    ResetControlsIfNotBuilding();
                    return;
                }
                if (keyCodeType == null)
                {
                    keyCodeType = Reflect.Type("UnityEngine.KeyCode");
                    inputType = Reflect.Type("UnityEngine.Input");
                    unityTimeType = Reflect.Type("UnityEngine.Time");
                }
                int frame = Convert.ToInt32(Value(unityTimeType, "frameCount") ?? 0);
                if (frame != lastKeyFrame)
                {
                    lastKeyFrame = frame;
                    if (Down("G")) { fullOverride = !fullOverride; Announce("Full Override " +
                        (fullOverride ? (Cfg.AllowUnsafeWorldChanges ? "ON" : "BLOCKED (safe MP mode)") : "OFF")); }
                    if (Down("T")) { attachToTarget = !attachToTarget; Announce(
                        "Attach to Target " + (attachToTarget ? "requested; disabled for MP sync safety" : "OFF")); }
                    if (Down("B")) { extendedRange = !extendedRange; Announce(
                        "Extended build range " + (extendedRange ? "ON" : "OFF")); }
                    if (Down("KeypadDivide"))
                    {
                        if (IsMultiplayer())
                        {
                            // Builder.Update has already calculated collision
                            // and canPlace. Mutating placePosition in a postfix
                            // would send a ghost pose that was NOT validated by
                            // the game, and could desync the server/client.
                            frozen = false;
                            freezeInitialized = false;
                            Announce("Free placement disabled in multiplayer: " +
                                "ghost collision validation would be stale");
                        }
                        else
                        {
                            frozen = !frozen;
                            freezeInitialized = false;
                            Announce("Free placement " + (frozen ? "ON" : "OFF"));
                        }
                    }
                }
                if (extendedRange)
                {
                    if (Held("Mouse4")) activeRange = Math.Min(10f, activeRange + .02f);
                    if (Held("Mouse3")) activeRange = Math.Max(1f, activeRange - .02f);
                }
                UpdateRanges();
                if (frozen && IsMultiplayer())
                {
                    frozen = false;
                    freezeInitialized = false;
                }
                if (frozen) UpdateFreePlacement(builder);
            }
            catch (Exception e) { LogOnce("BUILDER_UPDATE", e); }
        }

        private static void ResetControlsIfNotBuilding()
        {
            RestoreRanges();
            if (frozen) { frozen = false; freezeInitialized = false; }
        }

        private static void EndPostfix()
        {
            AutoBuildCoordinator.BuilderEnded();
            frozen = false; freezeInitialized = false;
            attachToTarget = false; fullOverride = false;
            RestoreRanges();
            RestorePlacementFlags();
        }

        private static void CreateGhostPostfix()
        {
            // Never reset captured base distances here: this game method may execute
            // repeatedly during a placement. Recapturing multiplied distances would
            // make the range grow exponentially each frame.
            UpdateRanges();
        }

        private static void TargetingRangePrefix(object[] __args)
        {
            // BZ Targeting.GetTarget(float, out GameObject, out float):
            // This changes only a local targeting ray distance, not a network
            // construction ID or authoritative object transform.
            if (!extendedRange || __args == null || __args.Length != 3 ||
                !(__args[0] is float distance)) return;
            try
            {
                var builder = Reflect.Type("Builder");
                var placing = Value(builder, "isPlacing");
                if (!(placing is bool) || !(bool)placing) return;
                __args[0] = Math.Max(0f, distance) * activeRange;
            }
            catch (Exception e) { LogOnce("TARGETING_RANGE", e); }
        }

        private static void UpdateRanges()
        {
            var builder = Reflect.Type("Builder");
            if (builder == null) return;
            float? defaultRange = FloatValue(builder, "placeDefaultDistance");
            float? maxRange = FloatValue(builder, "placeMaxDistance");
            if (!defaultRange.HasValue || !maxRange.HasValue) return;
            if (!originalDefaultRange.HasValue) originalDefaultRange = defaultRange;
            if (!originalMaxRange.HasValue) originalMaxRange = maxRange;
            var multiplier = extendedRange ? activeRange : 1f;
            SetValue(builder, "placeDefaultDistance", originalDefaultRange.Value * multiplier);
            SetValue(builder, "placeMaxDistance", originalMaxRange.Value * multiplier);
            try
            {
                var prefab = Value(builder, "prefab");
                var constructableType = Reflect.Type("Constructable");
                var get = prefab?.GetType().GetMethod("GetComponent", All,
                    null, new[] { typeof(Type) }, null);
                var constructable = constructableType == null ? null
                    : get?.Invoke(prefab, new object[] { constructableType });
                if (constructable != null)
                {
                    if (!ConstructableRanges.TryGetValue(constructable, out var distances))
                    {
                        var originalMin = FloatValue(constructable, "placeDefaultDistance");
                        var originalMax = FloatValue(constructable, "placeMaxDistance");
                        if (originalMin.HasValue && originalMax.HasValue)
                        {
                            distances = Tuple.Create(originalMin.Value, originalMax.Value);
                            ConstructableRanges[constructable] = distances;
                        }
                    }
                    if (distances != null)
                    {
                        SetValue(constructable, "placeDefaultDistance", distances.Item1 * multiplier);
                        SetValue(constructable, "placeMaxDistance", distances.Item2 * multiplier);
                    }
                }
            }
            catch (Exception e) { LogOnce("RANGE_CONSTRUCTABLE", e); }
        }

        private static void RestoreRanges()
        {
            var builder = Reflect.Type("Builder");
            if (builder != null)
            {
                if (originalDefaultRange.HasValue)
                    SetValue(builder, "placeDefaultDistance", originalDefaultRange.Value);
                if (originalMaxRange.HasValue)
                    SetValue(builder, "placeMaxDistance", originalMaxRange.Value);
            }
            foreach (var entry in ConstructableRanges)
            {
                try
                {
                    SetValue(entry.Key, "placeDefaultDistance", entry.Value.Item1);
                    SetValue(entry.Key, "placeMaxDistance", entry.Value.Item2);
                }
                catch { /* destroyed Unity objects may no longer be accessible */ }
            }
            ConstructableRanges.Clear();
            originalDefaultRange = originalMaxRange = null;
        }

        private static void UpdateFreePlacement(Type builder)
        {
            // Modifies ghost preview position, not placed entity or its network identity.
            var position = Value(builder, "placePosition");
            if (position == null) return;
            if (!freezeInitialized)
            {
                frozenPlacePosition = position;
                freezeInitialized = true;
            }
            float delta = Convert.ToSingle(Value(unityTimeType, "deltaTime") ?? .016f) * Cfg.FreeMoveSpeed;
            float x = Reflect.Component(frozenPlacePosition, "x");
            float y = Reflect.Component(frozenPlacePosition, "y");
            float z = Reflect.Component(frozenPlacePosition, "z");
            if (Held("Keypad4")) x -= delta;
            if (Held("Keypad6")) x += delta;
            if (Held("Keypad2")) z -= delta;
            if (Held("Keypad8")) z += delta;
            if (Held("KeypadMinus")) y -= delta;
            if (Held("KeypadPlus")) y += delta;
            Reflect.Set(frozenPlacePosition, "x", x);
            Reflect.Set(frozenPlacePosition, "y", y);
            Reflect.Set(frozenPlacePosition, "z", z);
            SetValue(builder, "placePosition", frozenPlacePosition);
        }

        private static void RestorePlacementFlags()
        {
            if (originalPlacementFlags != null)
            {
                var builder = Reflect.Type("Builder");
                if (builder != null)
                    for (int i = 0; i < PlacementFlagNames.Length; i++)
                        SetValue(builder, PlacementFlagNames[i], originalPlacementFlags[i]);
                originalPlacementFlags = null;
            }
            if (expandedSurfaceList != null && addedSurfaceType != null)
            {
                try { expandedSurfaceList.Remove(addedSurfaceType); }
                catch (Exception e) { LogOnce("RESTORE_SURFACES", e); }
            }
            expandedSurfaceList = null;
            addedSurfaceType = null;
        }

        private static void UpdateAllowedPrefix()
        {
            if (!Cfg.ExpandedSurfaces) return;
            var builder = Reflect.Type("Builder");
            if (builder == null) return;
            try
            {
                // Only furniture ghost: restrictions for base pieces are controlled by MP.
                var prefab = Value(builder, "prefab");
                if (HasComponent(prefab, "ConstructableBase")) return;
                if (originalPlacementFlags == null)
                {
                    var values = new bool[PlacementFlagNames.Length];
                    for (int i = 0; i < values.Length; i++)
                    {
                        var original = Value(builder, PlacementFlagNames[i]);
                        if (!(original is bool)) return; // incompatible ABI, do not mutate.
                        values[i] = (bool)original;
                    }
                    originalPlacementFlags = values;
                }
                SetValue(builder, "allowedOnConstructables", true);
                SetValue(builder, "allowedInBase", true);
                SetValue(builder, "allowedInSub", true);
                SetValue(builder, "allowedOutside", true);
                var surfaces = Value(builder, "allowedSurfaceTypes") as IList;
                var surfaceType = Reflect.Type("SurfaceType");
                if (surfaces != null && surfaceType != null)
                {
                    var wall = Enum.Parse(surfaceType, "Wall");
                    var ceiling = Enum.Parse(surfaceType, "Ceiling");
                    if (surfaces.Contains(wall) && !surfaces.Contains(ceiling))
                    {
                        surfaces.Add(ceiling);
                        expandedSurfaceList = surfaces;
                        addedSurfaceType = ceiling;
                    }
                }
            }
            catch (Exception e) { LogOnce("UPDATE_ALLOWED_SURFACE", e); }
        }

        private static void UpdateAllowedPostfix(ref bool __result)
        {
            if (IsUnsafePlacementEnabled()) __result = true;
        }

        private static void AllowedPostfix(ref bool __result)
        {
            if (IsUnsafePlacementEnabled()) __result = true;
        }

        private static void GetObstaclesPostfix(object[] __args)
        {
            // Original BZ Builder.GetObstacles has a by-ref "results" parameter,
            // not a return value. Using __result prevents Harmony from installing
            // the patch on a void-returning method.
            if (!IsUnsafePlacementEnabled() || __args == null) return;
            try
            {
                foreach (var value in __args)
                    if (value is IList list && value.GetType().IsGenericType &&
                        value.GetType().GetGenericArguments()[0].Name == "GameObject")
                        list.Clear();
            }
            catch (Exception e) { LogOnce("OBSTACLES", e); }
        }

        private static void CheckSpacePostfix(object[] __args)
        {
            if (!IsUnsafePlacementEnabled() || __args == null) return;
            try
            {
                foreach (var obj in __args)
                    if (obj is IList list && obj.GetType().IsGenericType &&
                        obj.GetType().GenericTypeArguments[0].Name == "GameObject")
                        list.Clear();
            }
            catch (Exception e) { LogOnce("CHECK_SPACE", e); }
        }

        private static bool DeconstructionPrefix(ref bool __result)
        {
            if (!IsUnsafePlacementEnabled()) return true;
            __result = true;
            return false;
        }

        private static bool TryPlacePrefix(ref bool __result) =>
            AutoBuildCoordinator.Prefix(ref __result);

        private static void TryPlacePostfix(bool __result)
        {
            if (__result)
            {
                frozen = false;
                freezeInitialized = false;
                // No implicit parenting; MP serializes position + rotation + base ghost data,
                // not arbitrary transform parent hierarchy.
                if (attachToTarget && !hasLoggedWorldGuard)
                {
                    hasLoggedWorldGuard = true;
                    Trace.Info("ATTACH_BLOCKED: cannot synchronize arbitrary parent transforms with MP 3.0.1");
                }
                attachToTarget = fullOverride = false;
            }
        }

        private static void CanDestroyObjectPostfix(object[] __args, ref bool __result)
        {
            if (!__result || __args == null || __args.Length == 0) return;
            try
            {
                var target = __args[0];
                if (target == null) return;
                foreach (var type in new[] { "Creature", "BaseGhost", "Constructable", "LifepodDrop" })
                    if (HasComponentInParent(target, type)) { __result = false; return; }
                var name = (Value(target, "name") as string ?? "").ToLowerInvariant();
                if (new[] { "override", "aurora", "crashedship", "starship" }
                    .Any(term => name.Contains(term))) __result = false;
            }
            catch (Exception e) { LogOnce("CAN_DESTROY", e); }
        }

        private static void PlaceToolLateUpdatePostfix(object __instance)
        {
            if (__instance == null) return;
            try
            {
                var isUsing = Value(__instance, "usingPlayer") != null;
                var isRotating = Value(__instance, "rotationEnabled");
                bool reserveHotkeys = isUsing && Options.Enabled &&
                    Options.RotationEnabled && isRotating is bool && (bool)isRotating;
                SetQuickSlotHotkeysIgnored(reserveHotkeys);
            }
            catch (Exception e) { LogOnce("PLACETOOL_HOTKEYS", e); }
        }

        private static void PlaceToolExitPostfix() => SetQuickSlotHotkeysIgnored(false);

        private static void SetQuickSlotHotkeysIgnored(bool ignored)
        {
            try
            {
                var inv = Value(Reflect.Type("Inventory"), "main");
                var quickSlots = Value(inv, "quickSlots");
                if (quickSlots == null) return;
                var method = quickSlots.GetType().GetMethod("SetIgnoreHotkeyInput",
                    All, null, new[] { typeof(bool) }, null);
                method?.Invoke(quickSlots, new object[] { ignored });
            }
            catch (Exception e) { LogOnce("QUICKSLOTS", e); }
        }

        private static bool PowerConsumerPrefix(object __instance, ref bool __result)
        {
            if (!Cfg.FixDetachedPowerConsumer || __instance == null) return true;
            try
            {
                if (Value(__instance, "baseComp") != null) return true;
                var relay = Value(__instance, "powerRelay");
                if (relay == null) return true;
                var check = relay.GetType().GetMethod("IsPowered", All,
                    null, Type.EmptyTypes, null);
                if (check == null) return true;
                __result = (bool)check.Invoke(relay, null);
                return false;
            }
            catch (Exception e) { LogOnce("POWER_CONSUMER", e); return true; }
        }

        private static bool HasComponent(object go, string typeName) =>
            TestComponent(go, typeName, "GetComponent");
        private static bool HasComponentInParent(object go, string typeName) =>
            TestComponent(go, typeName, "GetComponentInParent");
        private static bool TestComponent(object go, string typeName, string method)
        {
            var t = Reflect.Type(typeName);
            if (go == null || t == null) return false;
            var m = go.GetType().GetMethod(method, All, null, new[] { typeof(Type) }, null);
            return m != null && m.Invoke(go, new object[] { t }) != null;
        }

        private static object Value(object source, string field)
        {
            if (source == null) return null;
            var type = source as Type ?? source.GetType();
            var instance = source is Type ? null : source;
            var f = type.GetField(field, All);
            if (f != null) return f.GetValue(instance);
            return type.GetProperty(field, All)?.GetValue(instance, null);
        }

        private static void SetValue(object source, string field, object value)
        {
            if (source == null) return;
            var type = source as Type ?? source.GetType();
            var instance = source is Type ? null : source;
            var f = type.GetField(field, All);
            if (f != null) { f.SetValue(instance, value); return; }
            type.GetProperty(field, All)?.SetValue(instance, value, null);
        }

        private static float? FloatValue(object source, string key)
        {
            var value = Value(source, key);
            return value == null ? (float?)null : Convert.ToSingle(value, CultureInfo.InvariantCulture);
        }

        private static bool Down(string key) => Input(key, true);
        private static bool Held(string key) => Input(key, false);
        private static bool Input(string key, bool down)
        {
            if (inputType == null || keyCodeType == null) return false;
            var method = inputType.GetMethod(down ? "GetKeyDown" : "GetKey",
                All, null, new[] { keyCodeType }, null);
            if (method == null) return false;
            return (bool)method.Invoke(null, new[] { Enum.Parse(keyCodeType, key) });
        }

        private static void Announce(string message)
        {
            Trace.Info("CONTROL: " + message);
            try
            {
                var error = Reflect.Type("ErrorMessage");
                error?.GetMethod("AddMessage", All, null,
                    new[] { typeof(string) }, null)?.Invoke(null,
                    new object[] { "Construtor Zero: " + message });
            }
            catch { }
        }

        private static readonly HashSet<string> Logged = new HashSet<string>();
        private static void LogOnce(string scope, Exception e)
        {
            if (Logged.Add(scope)) Trace.Info("BUILDING_WARNING " + scope + ": " + e);
        }
    }

    internal static class Cfg
    {
        internal static bool ExpandedSurfaces = true;
        internal static bool FixDetachedPowerConsumer = true;
        internal static bool AllowUnsafeWorldChanges = false;
        internal static float FreeMoveSpeed = 2f;

        internal static void Load()
        {
            try
            {
                string root = Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData),
                    ".botbenson", "Subnautica Below Zero", "Game", "Config");
                Directory.CreateDirectory(root);
                string path = Path.Combine(root, "ConstrutorZero.Building.ini");
                if (!File.Exists(path))
                    File.WriteAllLines(path, new[] {
                        "# Construtor Zero — native multiplayer building controls.",
                        "# Expanded furniture surfaces and detached power consumer checks are enabled.",
                        "# Dynamic parenting and unsafe world edits are NOT synchronized in MP 3.0.1.",
                        "expanded_surfaces=true",
                        "fix_detached_power_consumer=true",
                        "allow_unsafe_world_changes=false",
                        "free_move_speed=2.0"
                    });
                foreach (var line in File.ReadAllLines(path))
                {
                    var pair = line.Split(new[] { '=' }, 2);
                    if (pair.Length < 2) continue;
                    string key = pair[0].Trim().ToLowerInvariant();
                    string value = pair[1].Trim();
                    if (key == "expanded_surfaces") ExpandedSurfaces = Bool(value);
                    if (key == "fix_detached_power_consumer") FixDetachedPowerConsumer = Bool(value);
                    if (key == "allow_unsafe_world_changes") AllowUnsafeWorldChanges = Bool(value);
                    if (key == "free_move_speed" && float.TryParse(value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out float speed))
                        FreeMoveSpeed = Math.Max(.1f, Math.Min(10f, speed));
                }
            }
            catch (Exception e) { Trace.Info("BUILDING_CONFIG: " + e.Message); }
        }

        private static bool Bool(string value) =>
            string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }
}

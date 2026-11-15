using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Globalization;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Subnautica.API.Enums;
using Subnautica.API.Features;

namespace Sirro.BZ.MP.ConstrutorZero
{
    // EXPERIMENTAL: first working native gameplay prototype, not the full Tobey SnapBuilder.
    // Uses no BepInEx, Nautilus, or redistribution of the game's proprietary DLLs.
    public sealed class MainPlugin : SubnauticaPlugin
    {
        private object harmony;
        private MethodInfo unpatch;
        public override string Name { get; } = "Construtor Zero [experimental]";
        public override SubnauticaPluginType PluginType { get; } = SubnauticaPluginType.Client;

        public override void OnEnabled()
        {
            try
            {
                Options.Load();
                var builder = Reflect.Type("Builder");
                var hm = Reflect.Type("HarmonyLib.HarmonyMethod");
                var ht = Reflect.Type("HarmonyLib.Harmony");
                if (builder == null || hm == null || ht == null)
                    throw new MissingMemberException("Builder/Harmony missing");

                var original = builder.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "SetPlaceOnSurface" &&
                        m.GetParameters().Length >= 3 &&
                        m.GetParameters()[0].ParameterType.FullName == "UnityEngine.RaycastHit" &&
                        m.GetParameters()[1].ParameterType.IsByRef &&
                        m.GetParameters()[1].ParameterType.GetElementType().FullName == "UnityEngine.Vector3");
                if (original == null)
                    throw new MissingMethodException("Builder.SetPlaceOnSurface(RaycastHit, ref Vector3, ref Quaternion)");

                harmony = Activator.CreateInstance(ht, "sirro.bz.mp.construtor-zero.snap");
                var postfix = Activator.CreateInstance(hm,
                    typeof(SnapHook).GetMethod("Postfix", BindingFlags.Public | BindingFlags.Static));
                var patch = ht.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "Patch" &&
                        m.GetParameters().Length >= 3 &&
                        m.GetParameters()[0].ParameterType == typeof(MethodBase));
                if (patch == null) throw new MissingMethodException("Harmony.Patch");
                var arguments = new object[patch.GetParameters().Length];
                arguments[0] = original;
                arguments[2] = postfix;
                patch.Invoke(harmony, arguments);
                unpatch = ht.GetMethod("UnpatchSelf", BindingFlags.Public | BindingFlags.Instance);
                if (unpatch == null)
                    unpatch = ht.GetMethod("UnpatchAll", BindingFlags.Public | BindingFlags.Instance,
                        null, new[] { typeof(string) }, null);
                Trace.Info("ENABLED: " + original + " | grid=" + Options.Grid + ", fine=" + Options.Fine);
            }
            catch (Exception ex)
            {
                Trace.Info("SNAP_DISABLED: hook installation failed: " + ex);
            }
            finally
            {
                BuildingTweaksBridge.Install();
                CraftFromContainersBridge.Install();
            }
        }

        public override void OnDisabled()
        {
            CraftFromContainersBridge.Uninstall();
            BuildingTweaksBridge.Uninstall();
            try
            {
                if (unpatch == null) Trace.Info("UNPATCH_WARNING: no supported Harmony unpatch API");
                else if (unpatch.Name == "UnpatchAll")
                    unpatch.Invoke(harmony, new object[] { "sirro.bz.mp.construtor-zero.snap" });
                else unpatch.Invoke(harmony, null);
            }
            catch (Exception ex) { Trace.Info("Unpatch warning: " + ex.Message); }
        }
    }

    public static class SnapMath
    {
        public static float RotationAngle(float degrees, float step)
        {
            if (step <= 0 || float.IsNaN(degrees) || float.IsInfinity(degrees)) return degrees;
            return (float)(Math.Round(degrees / step, MidpointRounding.AwayFromZero) * step);
        }

        // Position of a prefab pivot whose lowest bounds corner must sit
        // exactly on the physical floor hit. This is independent of the
        // camera-ray height and of grid rounding.
        public static float GroundPivotHeight(float surfaceY, float lowestCornerY) =>
            surfaceY - lowestCornerY;

        /// <summary>
        /// World-anchored wall grid. Tangential horizontal distance and
        /// absolute Y use a FIXED WORLD origin, not a wall collider's local
        /// transform. Moving the player towards/away from the same wall must
        /// not shift the snapped slots as different colliders are hit.
        /// The wall-normal distance is preserved; this never pushes the
        /// ghost into or away from the wall.
        /// </summary>
        public static float[] WorldWallPosition(float x, float y, float z,
            float nx, float ny, float nz, float grid)
        {
            var result = new[] { x, y, z };
            if (grid <= 0f || float.IsInfinity(grid) || float.IsNaN(grid))
                return result;
            foreach (var value in new[] { x, y, z, nx, ny, nz })
                if (float.IsNaN(value) || float.IsInfinity(value))
                    return result;
            double lengthSquared = nx*nx + nz*nz;
            if (lengthSquared < .01d) return result;
            double len = Math.Sqrt(lengthSquared);
            var tx = -nz / len;
            var tz = nx / len;
            var worldAlongWall = x*tx + z*tz;
            var snappedAlongWall = Math.Round(worldAlongWall / grid,
                MidpointRounding.AwayFromZero) * grid;
            var delta = snappedAlongWall - worldAlongWall;
            var snappedY = Math.Round(y / grid,
                MidpointRounding.AwayFromZero) * grid;
            var dy = snappedY - y;
            // compensate for a slightly inclined wall normal
            result[0] = (float)(x + tx*delta - nx*ny/lengthSquared*dy);
            result[1] = (float)snappedY;
            result[2] = (float)(z + tz*delta - nz*ny/lengthSquared*dy);
            return result;
        }

        public static float PlaneAxis(float value, float normal, float grid)
        {
            if (grid <= 0 || float.IsInfinity(value) || float.IsNaN(value) ||
                Math.Abs(normal) >= .999f) return value;
            return (float)(Math.Round(value / grid, MidpointRounding.AwayFromZero) * grid);
        }

        /// <summary>
        /// Grid on the TANGENT plane, not on all world axes. In particular,
        /// never quantize ground height (Y): that lifted placed lockers above
        /// terrain or buried them underneath it. For sloped surfaces, project
        /// the horizontal grid displacement back onto the hit tangent plane.
        /// All values are in the hit object's local coordinate system.
        /// </summary>
        public static float[] SurfacePosition(float x, float y, float z,
            float nx, float ny, float nz, float grid)
        {
            var original = new[] { x, y, z };
            if (grid <= 0f || float.IsNaN(grid) || float.IsInfinity(grid))
                return original;
            var n = new[] { nx, ny, nz };
            for (int i = 0; i < 3; i++)
                if (float.IsNaN(original[i]) || float.IsInfinity(original[i]) ||
                    float.IsNaN(n[i]) || float.IsInfinity(n[i]))
                    return original;

            int normalAxis = 0;
            if (Math.Abs(n[1]) > Math.Abs(n[normalAxis])) normalAxis = 1;
            if (Math.Abs(n[2]) > Math.Abs(n[normalAxis])) normalAxis = 2;
            if (Math.Abs(n[normalAxis]) < .01f) return original;
            var result = (float[])original.Clone();
            double tangentProjection = 0;
            for (int i = 0; i < 3; i++)
            {
                if (i == normalAxis) continue;
                result[i] = (float)(Math.Round(original[i] / grid,
                    MidpointRounding.AwayFromZero) * grid);
                tangentProjection += n[i] * (result[i] - original[i]);
            }
            result[normalAxis] = (float)(
                original[normalAxis] - tangentProjection / n[normalAxis]);
            return result;
        }
    }

    internal static class Options
    {
        internal static bool Enabled = true;
        internal static float Grid = .5f, Fine = .1f;
        internal static bool RotationEnabled = true;
        internal static bool GroundAlignEnabled = true;
        internal static float RotationStep = 45f, FineRotationStep = 5f;

        internal static void Load()
        {
            try
            {
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    ".botbenson", "Subnautica Below Zero", "Game", "Config", "ConstrutorZero.ini");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                if (!File.Exists(path))
                    File.WriteAllLines(path, new[] {
                        "# Mouse wheel click toggles; LeftCtrl uses the fine grid.",
                        "enabled=true", "grid=0.5", "fine_grid=0.1",
                        "# Align ground furniture pivot using actual Builder bounds and ray hit.",
                        "ground_align=true",
                        "rotation_enabled=true", "rotation_step=45", "fine_rotation_step=5" });
                foreach (var line in File.ReadAllLines(path))
                {
                    var pair = line.Split(new[] { '=' }, 2);
                    if (pair.Length < 2) continue;
                    var key = pair[0].Trim().ToLowerInvariant();
                    var value = pair[1].Trim();
                    if (key == "enabled") Enabled = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    else if (key == "ground_align") GroundAlignEnabled =
                        value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    else if (key == "rotation_enabled") RotationEnabled = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    else if ((key == "rotation_step" || key == "fine_rotation_step") &&
                             float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var rot) &&
                             rot >= 1f && rot <= 90f)
                    {
                        if (key == "rotation_step") RotationStep = rot;
                        else FineRotationStep = rot;
                    }
                    else if ((key == "grid" || key == "fine_grid") &&
                             float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var size) &&
                             size >= .01f && size <= 5f)
                    {
                        if (key == "grid") Grid = size;
                        else Fine = size;
                    }
                }
            }
            catch (Exception ex) { Trace.Info("Using default settings: " + ex.Message); }
        }
    }

    internal static class Reflect
    {
        private static readonly Dictionary<string, Type> TypeCache =
            new Dictionary<string, Type>(StringComparer.Ordinal);

        internal static Type Type(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName)) return null;
            lock (TypeCache)
                if (TypeCache.TryGetValue(fullName, out var known))
                    return known;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = asm.GetType(fullName, false);
                    if (t == null) continue;
                    lock (TypeCache) TypeCache[fullName] = t;
                    return t;
                }
                catch { }
            }
            // Do not cache missing types; other native mods may still be
            // loading their assemblies when Construtor Zero enables.
            return null;
        }
        internal static float Component(object v, string axis) =>
            (float)v.GetType().GetField(axis, BindingFlags.Public | BindingFlags.Instance).GetValue(v);
        internal static void Set(object v, string axis, float n) =>
            v.GetType().GetField(axis, BindingFlags.Public | BindingFlags.Instance).SetValue(v, n);
        internal static object Transform(object tr, string method, Type vecType, object vector) =>
            tr.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.Instance,
                null, new[] { vecType }, null).Invoke(tr, new[] { vector });
    }

    public static class SnapHook
    {
        private static bool alreadyWarned;
        private static bool firstInvocationLogged;
        private static bool firstSnapLogged;
        private static Type input, keyCode;
        private static MethodInfo getKeyDown, getKey;
        private static int lastToggleFrame = -1;

        // Harmony __args supplies the actual method parameters; index 1 is the by-ref Vector3.
        public static void Postfix(object[] __args)
        {
            if (__args == null || __args.Length < 3 || __args[0] == null || __args[1] == null)
                return;
            object originalPosition = null, originalRotation = null;
            try
            {
                // Reflection mutates boxed Vector3 by reference. Preserve true
                // value-type copies so any mid-hook exception rolls back both
                // geometry arguments before the multiplayer reads the pose.
                originalPosition = RuntimeHelpers.GetObjectValue(__args[1]);
                originalRotation = __args[2] == null ? null :
                    RuntimeHelpers.GetObjectValue(__args[2]);
                if (!firstInvocationLogged)
                {
                    firstInvocationLogged = true;
                    Trace.Info("POSTFIX_CALLED: Builder.SetPlaceOnSurface");
                }
                if (Key("Mouse2", true))
                {
                    // A builder may call placement several times in the same Unity frame.
                    // Toggle at most once per frame to avoid flipping back immediately.
                    var unityTime = Reflect.Type("UnityEngine.Time");
                    var frameProperty = unityTime?.GetProperty("frameCount",
                        BindingFlags.Public | BindingFlags.Static);
                    int frame = frameProperty != null ? (int)frameProperty.GetValue(null, null) : 0;
                    if (frame != lastToggleFrame)
                    {
                        lastToggleFrame = frame;
                        Options.Enabled = !Options.Enabled;
                        Trace.Info("Snapping " + (Options.Enabled ? "ON" : "OFF"));
                    }
                }
                if (!Options.Enabled || IsWholeBasePiece()) return;
                var hit = __args[0];
                var position = __args[1];
                // Vector3 is a value type. Reflection SetValue mutates the same
                // boxed instance; saving 'before = position' would alias it.
                float beforeX = Reflect.Component(position, "x");
                float beforeY = Reflect.Component(position, "y");
                float beforeZ = Reflect.Component(position, "z");
                var vectorType = position.GetType();
                var target = hit.GetType().GetProperty("transform")?.GetValue(hit, null);
                var normal = hit.GetType().GetProperty("normal")?.GetValue(hit, null);
                if (normal == null) return;

                // Wall placement uses a fixed world-space grid: NEVER
                // rebase to hit.transform or to the camera. Vanilla wall
                // colliders often live under distinct child transforms, so
                // local snapping jumped when a moving player aimed at the
                // same physical wall via a neighboring collider.
                bool wall = Math.Abs(Reflect.Component(normal, "y")) < .65f;
                if (!wall && target != null)
                {
                    position = Reflect.Transform(target, "InverseTransformPoint", vectorType, position);
                    normal = Reflect.Transform(target, "InverseTransformDirection", vectorType, normal);
                }
                float grid = (Key("LeftControl", false) || Key("RightControl", false))
                    ? Options.Fine : Options.Grid;
                var tangent = wall
                    ? SnapMath.WorldWallPosition(
                        Reflect.Component(position, "x"),
                        Reflect.Component(position, "y"),
                        Reflect.Component(position, "z"),
                        Reflect.Component(normal, "x"),
                        Reflect.Component(normal, "y"),
                        Reflect.Component(normal, "z"), grid)
                    : SnapMath.SurfacePosition(
                        Reflect.Component(position, "x"),
                        Reflect.Component(position, "y"),
                        Reflect.Component(position, "z"),
                        Reflect.Component(normal, "x"),
                        Reflect.Component(normal, "y"),
                        Reflect.Component(normal, "z"), grid);
                Reflect.Set(position, "x", tangent[0]);
                Reflect.Set(position, "y", tangent[1]);
                Reflect.Set(position, "z", tangent[2]);

                // The old tangent grid retained the PRE-EXISTING vertical
                // offset. For floor furniture this could be the camera/wall
                // placement depth, leaving lockers floating despite a valid
                // floor raycast. Anchor the prefab's lowest collision corner
                // to the actual hit.point (not the camera cursor elevation).
                // Never apply to wall, ceiling or arbitrary base pieces.
                if (Options.GroundAlignEnabled &&
                    Reflect.Component(normal, "y") >= .985f &&
                    TryLowestPrefabCornerY(out var bottom))
                {
                    var hitPoint = hit.GetType().GetProperty("point",
                        BindingFlags.Public | BindingFlags.Instance)
                        ?.GetValue(hit, null);
                    if (hitPoint != null)
                    {
                        if (target != null)
                            hitPoint = Reflect.Transform(target,
                                "InverseTransformPoint", vectorType, hitPoint);
                        var height = SnapMath.GroundPivotHeight(
                            Reflect.Component(hitPoint, "y"), bottom);
                        if (!float.IsNaN(height) && !float.IsInfinity(height))
                            Reflect.Set(position, "y", height);
                    }
                }
                if (!wall && target != null)
                    position = Reflect.Transform(target, "TransformPoint", vectorType, position);
                __args[1] = position;
                if (Options.RotationEnabled && IsRotationEnabled())
                    SnapFloorRotation(__args, vectorType, hit);
                if (!firstSnapLogged)
                {
                    var deltaX = Math.Abs(beforeX - Reflect.Component(position, "x"));
                    var deltaY = Math.Abs(beforeY - Reflect.Component(position, "y"));
                    var deltaZ = Math.Abs(beforeZ - Reflect.Component(position, "z"));
                    if (deltaX + deltaY + deltaZ > .001f)
                    {
                        firstSnapLogged = true;
                        Trace.Info("POSITION_CHANGED: snapping applied to placement");
                    }
                }
            }
            catch (Exception ex)
            {
                if (originalPosition != null) __args[1] = originalPosition;
                if (originalRotation != null) __args[2] = originalRotation;
                if (!alreadyWarned)
                {
                    alreadyWarned = true;
                    Trace.Info("ERROR: snapping disabled for this session: " + ex);
                }
                Options.Enabled = false;
            }
        }

        private static bool TryLowestPrefabCornerY(out float bottom)
        {
            bottom = 0f;
            try
            {
                var builder = Reflect.Type("Builder");
                const BindingFlags flags = BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.Static;
                var bounds = builder?.GetField("bounds", flags)?.GetValue(null) ??
                    builder?.GetProperty("bounds", flags)?.GetValue(null, null);
                if (!(bounds is IEnumerable list)) return false;
                bool found = false;
                float lowest = float.PositiveInfinity;
                foreach (var obb in list)
                {
                    if (obb == null) continue;
                    // Important: GetCorners() from Tobey SnapBuilder is an
                    // EXTENSION method, not an instance method on the native
                    // BZ OrientedBounds type. The previous reflection code
                    // never obtained real corners and silently skipped the
                    // floor alignment. The source extension computes:
                    // bottom = bounds.position.y - bounds.extents.y.
                    var type = obb.GetType();
                    var position = type.GetField("position",
                        BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Instance)?.GetValue(obb);
                    var extents = type.GetField("extents",
                        BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Instance)?.GetValue(obb);
                    if (position == null || extents == null) continue;
                    float cy = Reflect.Component(position, "y");
                    float ey = Math.Abs(Reflect.Component(extents, "y"));
                    if (float.IsNaN(cy) || float.IsInfinity(cy) ||
                        float.IsNaN(ey) || float.IsInfinity(ey)) continue;
                    lowest = Math.Min(lowest, cy - ey);
                    found = true;
                }
                if (!found || Math.Abs(lowest) > 10f) return false;
                bottom = lowest;
                return true;
            }
            catch
            {
                // On unknown geometry never invent a vertical offset.
                return false;
            }
        }

        private static bool IsRotationEnabled()
        {
            try
            {
                var builder = Reflect.Type("Builder");
                return (bool)(builder?.GetField("rotationEnabled",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    ?.GetValue(null) ?? false);
            }
            catch { return false; }
        }

        private static void SnapFloorRotation(object[] args, Type vectorType, object hit)
        {
            if (args.Length < 3 || args[2] == null) return;
            var worldNormal = hit.GetType().GetProperty("normal")?.GetValue(hit, null);
            if (worldNormal == null || Reflect.Component(worldNormal, "y") < .85f)
                return; // Do not distort wall or ceiling placement rotation.

            var rotation = args[2];
            var quaternionType = rotation.GetType();
            var euler = quaternionType.GetProperty("eulerAngles",
                BindingFlags.Public | BindingFlags.Instance)?.GetValue(rotation, null);
            if (euler == null) return;
            float step = Key("LeftAlt", false) || Key("RightAlt", false)
                ? Options.FineRotationStep : Options.RotationStep;
            float y = Reflect.Component(euler, "y");
            float rounded = SnapMath.RotationAngle(y, step);
            if (Math.Abs(y - rounded) < .001f) return;
            Reflect.Set(euler, "y", rounded);
            var eulerMethod = quaternionType.GetMethod("Euler",
                BindingFlags.Public | BindingFlags.Static, null, new[] { vectorType }, null);
            if (eulerMethod != null)
                args[2] = eulerMethod.Invoke(null, new[] { euler });
        }

        private static bool IsWholeBasePiece()
        {
            // BZ Builder.prefab is a PROPERTY in supported builds, not
            // necessarily a field. Using GetField alone silently snapped
            // ConstructableBase modules (and could invalidate MP ghost poses).
            // If we cannot identify the prefab safely, leave placement vanilla.
            try
            {
                var builder = Reflect.Type("Builder");
                if (builder == null) return true;
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Static;
                var prefab = builder.GetField("prefab", flags)?.GetValue(null) ??
                    builder.GetProperty("prefab", flags)?.GetValue(null, null);
                var baseType = Reflect.Type("ConstructableBase");
                if (prefab == null || baseType == null) return true;
                var getComponent = prefab.GetType().GetMethod("GetComponent",
                    new[] { typeof(Type) });
                if (getComponent == null) return true;
                return getComponent.Invoke(prefab, new object[] { baseType }) != null;
            }
            catch (Exception ex)
            {
                if (!alreadyWarned)
                {
                    alreadyWarned = true;
                    Trace.Info("SNAP_BASE_PREFAB_GUARD: " + ex.Message);
                }
                return true;
            }
        }

        private static bool Key(string key, bool down)
        {
            if (input == null)
            {
                input = Reflect.Type("UnityEngine.Input");
                keyCode = Reflect.Type("UnityEngine.KeyCode");
                if (input == null || keyCode == null) return false;
                getKeyDown = input.GetMethod("GetKeyDown", new[] { keyCode });
                getKey = input.GetMethod("GetKey", new[] { keyCode });
            }
            var method = down ? getKeyDown : getKey;
            return method != null && (bool)method.Invoke(null, new[] { Enum.Parse(keyCode, key) });
        }
    }

    internal static class Trace
    {
        internal static void Info(string text)
        {
            try
            {
                var type = typeof(SubnauticaPlugin).Assembly.GetType("Subnautica.API.Features.Log");
                var method = type?.GetMethod("Info", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(string) }, null);
                method?.Invoke(null, new object[] { "[ConstrutorZero] " + text });
            }
            catch { }
        }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Globalization;
using Subnautica.API.Enums;
using Subnautica.API.Features;

namespace Sirro.BZ.MP.NativeSnapBuilder
{
    // EXPERIMENTAL: first working native gameplay prototype, not the full Tobey SnapBuilder.
    // Uses no BepInEx, Nautilus, or redistribution of the game's proprietary DLLs.
    public sealed class MainPlugin : SubnauticaPlugin
    {
        private object harmony;
        private MethodInfo unpatch;
        public override string Name { get; } = "Sirro Native SnapBuilder [experimental]";
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

                harmony = Activator.CreateInstance(ht, "sirro.bz.mp.native-snapbuilder");
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
                Trace.Info("ENABLED: " + original + " | grid=" + Options.Grid + ", fine=" + Options.Fine);
            }
            catch (Exception ex)
            {
                Trace.Info("DISABLED: hook installation failed: " + ex);
            }
        }

        public override void OnDisabled()
        {
            try { unpatch?.Invoke(harmony, null); }
            catch (Exception ex) { Trace.Info("Unpatch warning: " + ex.Message); }
        }
    }

    public static class SnapMath
    {
        public static float PlaneAxis(float value, float normal, float grid)
        {
            if (grid <= 0 || float.IsInfinity(value) || float.IsNaN(value) ||
                Math.Abs(normal) >= .999f) return value;
            return (float)(Math.Round(value / grid, MidpointRounding.AwayFromZero) * grid);
        }
    }

    internal static class Options
    {
        internal static bool Enabled = true;
        internal static float Grid = .5f, Fine = .1f;

        internal static void Load()
        {
            try
            {
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    ".botbenson", "Subnautica Below Zero", "Game", "Config", "Sirro.NativeSnapBuilder.ini");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                if (!File.Exists(path))
                    File.WriteAllLines(path, new[] {
                        "# Mouse wheel click toggles; LeftCtrl uses the fine grid.",
                        "enabled=true", "grid=0.5", "fine_grid=0.1" });
                foreach (var line in File.ReadAllLines(path))
                {
                    var pair = line.Split(new[] { '=' }, 2);
                    if (pair.Length < 2) continue;
                    var key = pair[0].Trim().ToLowerInvariant();
                    var value = pair[1].Trim();
                    if (key == "enabled") Enabled = value.Equals("true", StringComparison.OrdinalIgnoreCase);
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
        internal static Type Type(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { var t = asm.GetType(fullName, false); if (t != null) return t; }
                catch { }
            }
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
            try
            {
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
                var before = position;
                var vectorType = position.GetType();
                var target = hit.GetType().GetProperty("transform")?.GetValue(hit, null);
                var normal = hit.GetType().GetProperty("normal")?.GetValue(hit, null);
                if (normal == null) return;

                // Snap in the hit object's LOCAL coordinate system.
                if (target != null)
                {
                    position = Reflect.Transform(target, "InverseTransformPoint", vectorType, position);
                    normal = Reflect.Transform(target, "InverseTransformDirection", vectorType, normal);
                }
                float grid = (Key("LeftControl", false) || Key("RightControl", false))
                    ? Options.Fine : Options.Grid;
                foreach (var axis in new[] { "x", "y", "z" })
                    Reflect.Set(position, axis, SnapMath.PlaneAxis(
                        Reflect.Component(position, axis), Reflect.Component(normal, axis), grid));
                if (target != null)
                    position = Reflect.Transform(target, "TransformPoint", vectorType, position);
                __args[1] = position;
                if (!firstSnapLogged)
                {
                    var deltaX = Math.Abs(Reflect.Component(before, "x") - Reflect.Component(position, "x"));
                    var deltaY = Math.Abs(Reflect.Component(before, "y") - Reflect.Component(position, "y"));
                    var deltaZ = Math.Abs(Reflect.Component(before, "z") - Reflect.Component(position, "z"));
                    if (deltaX + deltaY + deltaZ > .001f)
                    {
                        firstSnapLogged = true;
                        Trace.Info("POSITION_CHANGED: snapping applied to placement");
                    }
                }
            }
            catch (Exception ex)
            {
                if (!alreadyWarned)
                {
                    alreadyWarned = true;
                    Trace.Info("ERROR: snapping disabled for this session: " + ex);
                }
                Options.Enabled = false;
            }
        }

        private static bool IsWholeBasePiece()
        {
            var builder = Reflect.Type("Builder");
            var prefab = builder?.GetField("prefab",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
            var baseType = Reflect.Type("ConstructableBase");
            if (prefab == null || baseType == null) return false;
            return prefab.GetType().GetMethod("GetComponent", new[] { typeof(Type) })
                ?.Invoke(prefab, new object[] { baseType }) != null;
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
                method?.Invoke(null, new object[] { "[Sirro.NativeSnapBuilder] " + text });
            }
            catch { }
        }
    }
}

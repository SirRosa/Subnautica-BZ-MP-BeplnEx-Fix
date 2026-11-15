using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Subnautica.API.Enums;
using Subnautica.API.Features;

namespace Sirro.BelowZero.Multiplayer.PortProbe
{
    /// <summary>
    /// Non-gameplay smoke test for the third-party DLL loading pathway in BOT Benson 3.0.1.
    /// Does not alter game or save state.
    /// </summary>
    public sealed class MainPlugin : SubnauticaPlugin
    {
        public override string Name { get; } = "Sirro MP Native Plugin Probe";
        public override SubnauticaPluginType PluginType { get; } = SubnauticaPluginType.Client;

        public override void OnEnabled() => Record("enabled");
        public override void OnDisabled() => Record("disabled");

        private static void Record(string state)
        {
            var message = "[Sirro.MP.PortProbe] " + state + " @ " + DateTime.UtcNow.ToString("O");
            try
            {
                // The test is considered successful only if this callback is called by the actual launcher.
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "Sirro-BZ-MP-PortProbe.txt"), message);
            }
            catch (Exception) { /* diagnostics must never crash the game */ }

            try
            {
                // Avoid compile-time dependencies on Unity/game assemblies.
                var debug = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType("UnityEngine.Debug", false))
                    .FirstOrDefault(t => t != null);
                var log = debug?.GetMethod("Log", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(object) }, null);
                log?.Invoke(null, new object[] { message });
            }
            catch (Exception) { /* diagnostic only */ }
        }
    }
}

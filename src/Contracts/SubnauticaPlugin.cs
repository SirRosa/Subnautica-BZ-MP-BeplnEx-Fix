// Minimal compile-time type contract inferred from the supplied Subnautica.API.dll 3.0.1.
// This assembly must NEVER be distributed with the plugin: the game supplies its own Subnautica.API.dll.
namespace Subnautica.API.Enums
{
    public enum SubnauticaPluginPriority { First, Medium, Last }
    public enum SubnauticaPluginType { Client, Server, ServerClient }
}

namespace Subnautica.API.Features
{
    // Minimal network state used by the simulated integration harness.
    // Compilation-only substitute; never included in any plugin distribution.
    public class Network
    {
        public static bool IsMultiplayerActive { get; set; } = true;
        public static object Identifier { get; set; }
    }
    public abstract class SubnauticaPlugin
    {
        public virtual string Name { get; } = string.Empty;
        public virtual Subnautica.API.Enums.SubnauticaPluginPriority Priority { get; } =
            Subnautica.API.Enums.SubnauticaPluginPriority.Medium;
        public virtual Subnautica.API.Enums.SubnauticaPluginType PluginType { get; } =
            Subnautica.API.Enums.SubnauticaPluginType.Client;
        public virtual void OnEnabled() { }
        public virtual void OnDisabled() { }
    }
}

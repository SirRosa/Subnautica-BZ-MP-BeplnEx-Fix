using HarmonyLib;

/// <summary>
/// Simulates the TWO BOT Benson Harmony prefixes on Builder.TryPlace:
/// - identity prefix at Priority.First (800), side-effect free from Harmony's
///   control-flow perspective, may run even after a false-returning prefix;
/// - construction network sender Prefix at Priority.Normal (400), returns
///   false after sending a packet to prevent singleplayer placement.
///
/// Tests MUST prove Construtor Zero defers the NETWORK PACKET, not merely the
/// original game's Builder.TryPlace method.
/// </summary>
public static class FakeMultiplayerConstruction
{
    public static int PacketsSent;
    public static int IdentityChecks;

    [HarmonyPriority(Priority.First)]
    public static void IdentityPrefix()
    {
        IdentityChecks++;
    }

    [HarmonyPriority(Priority.Normal)]
    public static bool ServerPacketPrefix()
    {
        PacketsSent++;
        // Real BOT Benson usually ends the Builder ghost after denying the
        // vanilla original action; simulate it here.
        Builder.End();
        return false;
    }
}

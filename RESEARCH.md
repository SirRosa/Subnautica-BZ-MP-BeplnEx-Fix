# Three native ports — technical assessment (MP 3.0.1)

Source of the DLL reference audit: user-supplied binaries. These are not bundled in this public repository.

| Original mod | Direct AssemblyRef dependencies of interest | Risk | Recommended port |
|---|---|---|---|
| Tobey.SnapBuilder.dll | BepInEx 5.4.19, 0Harmony 2.7, game + Unity; **no Nautilus** | Medium: builder ghost position, rotation and colliders | Port math/aim/rotation and controls to client plugin; avoid edits to authoritative placement packets |
| BuildingTweaks.dll | BepInEx 5.4.20, 0Harmony 2.7, **Nautilus 1.0.0.0** | High: build validation and deconstruction patches can conflict with MP sync | Replace Nautilus config/options with simple config; review each Harmony patch for MP-specific state; leave server-authoritative checks unchanged |
| CraftFromContainers_BZ.dll | BepInEx 5.4.23.5, 0Harmony 2.9, Newtonsoft.Json; **no Nautilus** | Very high: item counts and consumption must be atomic, owner authoritative | Design host/server-authoritative reservation and consumption; do **not** only run vanilla inventory removal hooks client-side |

## Sources

- Original SnapBuilder: https://github.com/toebeann/SnapBuilder (LGPL-3.0; preserve attribution and obligations if porting source)
- Original Building Tweaks: https://github.com/MrPurple6411/My-Subnautica-Mods/tree/main/BuildingTweaks
- BZ Craft From Containers description: https://www.nexusmods.com/subnauticabelowzero/mods/695
- MP repository (outdated API examples): https://github.com/ismail0234/Subnautica-Below-Zero-Multiplayer
- BOT Benson 3.0.1 announcement: https://subnauticamultiplayer.com/changelog

## API findings from supplied 3.0.1 DLLs

- `Subnautica.API.Features.SubnauticaPlugin` has virtual `Name`, `Priority`, `PluginType`, `OnEnabled`, and `OnDisabled`.
- `SubnauticaPluginType` names: `Client`, `Server`, `ServerClient`.
- `Subnautica.API.Features.PluginManager` has `IsValidPlugin`, `LoadPlugin`, `EnablePlugin`. These methods alone do **not** prove custom local DLLs are accepted.
- Official `Subnautica.SeatruckFastSpeed.dll` has an entry point deriving from `SubnauticaPlugin` with `Name`, `PluginType`, `OnEnabled`.

## Gate before porting

1. Build the **non-gameplay loader probe** and test whether 3.0.1 will actually execute `OnEnabled` on a custom local DLL. This is *not* established merely by the GitHub Actions compile step.
2. If loader accepts third-party plugins, create individual `SnapBuilder`, `BuildingTweaks`, `CraftFromContainers` plugins, retaining the upstream behavior while removing BepInEx and Nautilus calls.
3. Run on disposable copies of the save with two players: simultaneous construction/deconstruction, connected base parts, shared inventory and crafting, rejoin/resync and server restart.
4. Never enable shared-container item removal without an authoritative transactional consumption path (host/server validates available resources and prevents double spend).

## Safety / intellectual property

No third-party DLLs or original multiplayer binaries should be committed to a public repository. The `src/Contracts` API assembly is a **minimal compile-time-only stub**, not a distributable game/launcher binary. No existing third-party source has been copied into this repository.

## Official plugin policy — critical loader limitation

The official [plugin documentation](https://docs.subnauticamultiplayer.com/overview/plugins) explicitly states that **only selected plugins are officially compatible** and instructs installation through `Launcher -> Plugins`. It does not describe arbitrary user-authored DLL installation.

Therefore **a successful compile against the API contract does not establish that the current launcher accepts third-party DLLs**. There may be a plugin registry, checksum, entitlement, or activation mechanism. The supplied `Subnautica.API.dll` contains `PluginManager.RefreshPlugins`, `IsValidPlugin`, and `LoadPlugin`; metadata inspection alone does not prove their runtime authorization policy.

The probe should be used only as a controlled opt-in experiment on a backed-up installation, not presented as a functioning mod. If loading is blocked, development must first investigate the actual loader and permitted extension points; porting gameplay code before that is premature.

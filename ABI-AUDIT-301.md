# Binary ABI audit: Below Zero + BOT Benson 3.0.1

Source: user-supplied installation DLLs inspected as CLI metadata. **No proprietary DLLs have been committed.** SHA-256 abbreviations below identify the inspected builds, not a release checksum.

| Assembly | SHA-256 prefix | TypeDefs | MethodDefs |
|---|---|---:|---:|
| Assembly-CSharp.dll | `f5a9630cfd3f50c3` | 3,964 | 28,762 |
| Subnautica.API.dll | `386b9e69df15cd19` | 319 | 3,532 |
| Subnautica.Client.dll | `62b8f14287062e63` | 447 | 3,141 |
| Subnautica.Server.dll | `ebfb909990b2950e` | 360 | 1,795 |
| Subnautica.Events.dll | `b0a3abddcf9e4d97` | 800 | 4,393 |
| Subnautica.Network.dll | `e1da83bca889b773` | 345 | 3,561 |

## Confirmed method signatures, extracted from binaries

```csharp
// Assembly-CSharp.dll
void Builder.SetPlaceOnSurface(UnityEngine.RaycastHit hit,
    ref UnityEngine.Vector3 position, ref UnityEngine.Quaternion rotation);
void Builder.SetDefaultPlaceTransform(ref UnityEngine.Vector3 position,
    ref UnityEngine.Quaternion rotation);
void Builder.GetObstacles(UnityEngine.Vector3 position, UnityEngine.Quaternion rotation,
    List<OrientedBounds> bounds, Func<UnityEngine.Collider, bool> predicate,
    List<UnityEngine.GameObject> results);
bool Builder.CheckSpace(UnityEngine.Vector3 position, UnityEngine.Quaternion rotation,
    List<OrientedBounds> bounds, int layerMask, UnityEngine.Collider ignore);
void Builder.CheckSpace(UnityEngine.Vector3 position, UnityEngine.Quaternion rotation,
    List<OrientedBounds> bounds, int layerMask, UnityEngine.Collider ignore,
    List<UnityEngine.GameObject> results);
bool Builder.UpdateAllowed();
bool Builder.CreateGhost();
bool Builder.TryPlace();
bool Builder.ValidateOutdoor(UnityEngine.GameObject go);
bool Builder.CanDestroyObject(UnityEngine.GameObject go);
void Builder.Update();
void Builder.End();

void StorageContainer.Awake();
void StorageContainer.ResetContainer();
// StorageContainer.OnDestroy() DOES NOT EXIST
ItemsContainer StorageContainer.container { get; set; };
UnityEngine.Transform StorageContainer.storageRoot;

void SeamothStorageContainer.Awake();
ItemsContainer SeamothStorageContainer.container { get; set; };
int Inventory.GetPickupCount(TechType);
bool Inventory.DestroyItem(TechType, bool);
bool Constructable.DeconstructionAllowed(ref string reason);
bool BaseDeconstructable.DeconstructionAllowed(ref string reason);
bool PowerConsumer.IsPowered();
void uGUI_RecipeEntry.UpdateIngredients(ItemsContainer, bool);
ReadOnlyCollection<Ingredient> TechData.GetIngredients(TechType);

// Subnautica.Events.dll
void Subnautica.Events.Handlers.Storage.OnItemRemoving(
    Subnautica.Events.EventArgs.StorageItemRemovingEventArgs ev);
StorageItemRemovingEventArgs(string uniqueId, TechType techType,
    string itemId, Pickupable pickupable, bool isAllowed);

// Subnautica.Client.dll
void Subnautica.Client.Synchronizations.Processors.Metadata.StorageProcessor
    .OnStorageItemRemoving(StorageItemRemovingEventArgs ev);
void Subnautica.Client.Synchronizations.Processors.Metadata.StorageProcessor
    .SendPacketToServer(string uniqueId, WorldPickupItem item, bool isAdded);

// Subnautica.API.dll
bool Subnautica.API.Features.Network.IsMultiplayerActive { get; set; };
Subnautica.API.Features.NetworkUtility.Identifier Network.Identifier { get; set; };
string Identifier.GetIdentityId(UnityEngine.GameObject go, bool generateIfMissing);
```

## Defects exposed in the previous tests

1. The earlier game fake incorrectly declared `Builder.SetDefaultPlaceTransform` as returning `bool` with zero arguments. The actual method is `void(ref Vector3, ref Quaternion)`. Replacing the ref arguments via Harmony's generic `__args` silently corrupted original values after disabling the feature. **Fixed:** don't patch this method; apply ghost positioning through `Builder.Update` only.
2. The previous Craft From Containers port patched `StorageContainer.OnDestroy`, which does not exist. **Fixed:** patch `ResetContainer` for registry refresh and periodically remove dead Unity transforms.
3. The original simulator used `void Builder.CreateGhost()`; it returns `bool`. Updated the fake to match.
4. The original simulator used `bool Inventory.DestroyItem(TechType)` and `bool Constructable.DeconstructionAllowed()`; actual signatures include an additional `bool` / `ref string`. Updated simulated call signatures.
5. The prior SnapHook assigned `var before=position` on a boxed `Vector3`; subsequent reflection mutated the same boxed value. **Fixed:** save numeric X/Y/Z before changes.
6. Original BepInEx Craft From Containers returns success immediately after local locker consumption; **prohibited in multiplayer** because the server storage processor authorizes item transfer asynchronously.

## Scope / non-goals

This metadata inspection identifies method names, parameters and fields. It does **not** prove each hook's runtime behavior, patch priority against other 3.0.1 plugins, networking semantics, or full parity with original SnapBuilder/Building Tweaks/Craft From Containers. Don't claim production safety based only on successful compilation and simulation.

Next gate: full two-client gameplay and server persistence tests, and authoritative transactions for every crafting ingredient. All user files remain outside this public repo.

## Real 3.0.1 construction packet analysis

The supplied **Subnautica.Network.dll** contains the
`Subnautica.Network.Models.Server.ConstructionGhostTryPlacingArgs`
backing fields:

`Type, ChannelType, ChannelId, UniqueId, SubrootId, TechType, LastRotation,
Position, Rotation, AimTransform, BaseGhostComponent, IsCanPlace, IsBasePiece, IsError`.

No generic `ParentId` / parent-transform object identifier is present.
`SubrootId` describes a built subroot, not an arbitrary moving vehicle,
creature or inventory parent. Therefore original Building Tweaks
`Attach to Target` cannot be represented by a local `transform.SetParent`
and the unmodified default construction packet. An extension to both
sender and authoritative world-state persistence is required.

The original BepInEx `CraftFromContainers_BZ.dll` was also inspected:
its patch classes include `Inventory_GetPickupCount_Patch`,
`uGUI_RecipeEntry_UpdateIngredients_Patch` and
`Inventory_DestroyItem_Patch`. Its removal patch reports synchronous
success; in the multiplayer 3.0.1 server's locker transfer path this must
not be replicated before authoritative acknowledgement.

**Current gate:** the updated CI harness has 99 passing ABI-fixture and
simulated Harmony + network-prefix tests. It is not a two-client integration test and does
not establish that every original feature is available.

## 2026-11-15: Craft UI and construction prefix ordering

Confirmed in the user's Assembly-CSharp.dll:

```csharp
void uGUI_CraftingMenu.Action(uGUI_CraftingMenu.Node node);
// node has action: TreeAction (None, Expand, Craft), techType: TechType
void Inventory.OnAddItem(InventoryItem item);
void Inventory.ConsumeResourcesForRecipe(TechType type, AnimationDone callback);
```

The shipped Subnautica.Events.dll references Harmony 2.3.3 and contains
`Subnautica.Events.Handlers.Building.OnConstructingGhostTryPlacing`.
The public multiplayer source (which may be older than 3.0.1) uses two
Harmony prefixes for `Builder.TryPlace`:

- Identity assignment: `ConstructingGhostTryPlacing.Prefix`,
  `HarmonyPriority(Priority.First)` = 800
- Network construction packet: `ConstructionGhostTryPlacing.Prefix`,
  normal priority, returns false to suppress vanilla singleplayer placement

**Critical integration hazard:** suppressing original `Builder.TryPlace` does
not itself suppress multiplayer's prefix. The Construtor Zero ingredient
guard now explicitly runs at priority 1000, *ahead of both* prefixes.

The `FakeMultiplayerConstruction` harness installs BOT Benson-like
prefixes BEFORE loading Construtor Zero, then asserts:

1. **0 construction packets** when missing materials cause first-click defer
2. **0 packets** after the first of two required ingredients arrives
3. **exactly 1 packet** after both materials arrive and the original
   `Builder.TryPlace` interception is replayed
4. **0 additional packets** after ghost movement or duplicate frame updates
5. Other multiplayer Harmony patches persist when Construtor Zero unloads

Result: 99/99 simulated checks in GitHub Actions run 43733688439.

**Limitations:** the harness is a controlled imitation of the multiplayer's
event prefix, not a running dedicated server. A real two-client replay,
save/rejoin and simultaneous construction test remains mandatory for release.

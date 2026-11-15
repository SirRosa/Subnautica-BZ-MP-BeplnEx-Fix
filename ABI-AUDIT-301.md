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

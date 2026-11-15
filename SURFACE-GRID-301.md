# Construtor Zero 0.3.5-rc6 — floor-pitch, wall-first attachment, stable wall grid

**Status: experimental candidate for an in-game two-client test.**
CI does not certify correct Subnautica Below Zero BOT Benson 3.0.1 behavior.

## Fixed after testing 0.3.4-rc5

**1. Floor Locker detaches when looking downward.**
The previous `SurfaceAttachmentBridge.CheckAsSubModulePostfix` returned immediately when vanilla `CheckAsSubModule` returned `true`. In practice a downward-looking camera can make vanilla accept a camera-ray pose, bypassing the separate downward floor probe. The new code runs the independent floor ray and full hit/collision validation **regardless of vanilla success**. The downward probe also uses `Physics.RaycastAll` to find the real structural floor **behind other lockers' horizontal tops**; a floor Locker must not anchor to another piece of furniture. The original game's accepted pose is no longer implicitly trusted for floor-only prefab placement. A false/missing floor hit leaves the last grounded ghost as nonplaceable, never authorizes floating furniture.

**2. Wall furniture attaches to unrelated lockers instead of structural walls.**
The old horizontal ray queried only a single hit from `Physics.Raycast`, usually the nearest `Constructable` in front of the wall. The new wall probe uses Unity's `Physics.RaycastAll`, sorts candidates in two tiers (**non-furniture structural surfaces first**, ordinary Constructable furniture second), and validates surface direction, target object, distance, tags and collision. Object-to-object wall attachment remains as a secondary option **only when Builder.allowedOnConstructables permits it**. Structural/base objects are excluded from furniture classification using `ConstructableBase` and `BaseDeconstructable`. No world teleport and no override for base construction.

**3. Wall snapping grid shifts while walking forward/backward.**
The previous `SnapHook.Postfix` snapped coordinates in `hit.transform` local space, which can change if a hit crosses child collider transforms. For wall surfaces only, the new `SnapMath.WorldWallPosition` uses a **fixed world-origin grid in the wall's tangent plane**, snapping vertical height and horizontal tangent without moving along wall normal. This stays independent of both character translation and individual collider transforms. Floor grid logic and Ctrl fine grid remain unchanged.

## Tests added
- Vanilla `CheckAsSubModule` can return true with stale elevated pose when looking down; the corrected floor ray still returns the same ground elevation.
- Pitch sweep from looking up to almost straight down, verifying floor-pivot Y plus snapped X/Z remain stable across vanilla camera-ray success thresholds.
- A nearby Locker hides the real wall; the wall-mounted SmallLocker still targets the farther structural wall.
- A horizontal locker TOP hides the real floor; floor-only Locker selects the structural floor behind it, or refuses placement if no real floor exists.
- Secondary furniture-to-furniture attachment can still work if native flags allow it; otherwise it is rejected.
- Three forward/back camera translations and three different wall child collider transforms do not shift the wall grid.
- Oblique wall grid preserves normal depth and snaps the tangent distance consistently.

## Install
1. Backup the multiplayer server world and **replace the plugin on both clients** with the exact `Sirro.BZ.MP.ConstrutorZero.dll` inside the ZIP.
2. Plugin location: `%APPDATA%\.botbenson\Subnautica Below Zero\Game\Plugins\`.
3. Keep `ground_align=true` in `Game\Config\ConstrutorZero.ini`; Ctrl = fine grid, middle mouse button = toggle; G = furniture clipping.
4. Do not install an older Construtor Zero DLL or NativeSnapBuilder DLL next to this one.
5. The Craft From Containers code and quiet HUD behavior from rc4/rc5 are unchanged.

## Mandatory real-world test
- Locker on a flat base floor. Move camera gradually from looking straight ahead to straight down. Check both snap enabled and disabled, then test the same on floor of a second base compartment.
- Wall-mounted SmallLocker on a bare base wall, and another base wall with an ordinary floor Locker between you and the wall.
- With the wall-mounted object selected, walk **forward/back** while looking at the same wall. Its grid should not jump; Ctrl should switch to 0.1m spacing.
- Test object-to-object attachment only as a secondary behavior.
- With G enabled, verify furniture clipping continues to work while no unsupported/floating placement becomes possible.
- Have the second player inspect placed furniture before and after restart/reconnect. Stop if either client sees a different pose.

## Known limitations
- New selection is classified by native `Constructable.allowedSurfaceTypes`; multi-surface objects are left to vanilla. Locker and SmallLocker have dedicated floor/wall anchor rules. Ceilings remain vanilla.
- Walls whose collision layers or parent components are unusual may not be resolved correctly; multiplayer UI and Unity physics can behave differently from the CI fakes.
- This does not add fully atomic Craft From Containers transactions or unrestricted multiplayer world-changing Building Tweaks.
- These tests are simulations and ABI checks, **not two-client in-game results**. Release from the test branch only; `main` is unchanged.

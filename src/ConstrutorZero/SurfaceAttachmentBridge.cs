using System;
using System.Collections;
using System.Linq;
using System.Reflection;

namespace Sirro.BZ.MP.ConstrutorZero
{
    /// <summary>
    /// The vanilla Builder.SetDefaultPlaceTransform follows camera pitch. In
    /// CheckAsSubModule vanilla next raycasts FROM camera; even when that
    /// ray hits an invalid Wall for floor furniture it applies the hit pose
    /// before rejecting the surface, leaving a floating, camera-driven ghost.
    ///
    /// Fix the SOURCE of the placement inside CheckAsSubModule (before its
    /// caller UpdateAllowed checks obstacle lists and paints the ghost).
    /// Use the prefab's native allowedSurfaceTypes, independent of the
    /// temporary G clipping flags, and feed a REAL Physics.RaycastHit back
    /// through vanilla Builder.SetPlaceOnSurface. Validate all restrictions
    /// before setting the return value; never forge placement success.
    /// </summary>
    internal static class SurfaceAttachmentBridge
    {
        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Static | BindingFlags.Instance;
        private enum Anchor { None, Floor, Wall }
        private static bool warned, reportedFloor, reportedWall, reportedMiss;
        private static float rememberedYawX, rememberedYawZ = 1f;
        private static object lastPrefab, lastPosition, lastRotation;

        internal static void Reset()
        {
            lastPrefab = lastPosition = lastRotation = null;
            rememberedYawX = 0f;
            rememberedYawZ = 1f;
            warned = false;
            reportedFloor = reportedWall = reportedMiss = false;
        }

        internal static void CheckAsSubModulePostfix(
            ref bool __result, object[] __args)
        {
            if (!Options.GroundAlignEnabled) return;
            try
            {
                var builder = Reflect.Type("Builder");
                var prefab = Read(builder, "prefab");
                if (prefab == null || IsBasePiece(prefab)) return;

                var surface = AllowedAnchor(builder, prefab);
                if (surface == Anchor.None) return;

                // Always use the surface-specific anchor, EVEN when vanilla
                // has accepted its camera-ray placement. Looking down often
                // makes the original ray succeed at a different height;
                // the previous "if (__result) return" caused the reported
                // ghost jump as the camera pitched through that threshold.
                // The dedicated ray and full validation run every frame.
                if (!TrySurfaceRay(builder, surface, out var hit))
                {
                    if (!reportedMiss)
                    {
                        reportedMiss = true;
                        Trace.Info("SURFACE_LOCK_NO_RAY_HIT: " + surface +
                            ", furniture=" + Read(builder, "constructableTechType") +
                            ". Invalid placement remains blocked.");
                    }
                    KeepLastSurfaceGhost(builder, prefab);
                    __result = false;
                    return;
                }
                if (!ValidateHit(builder, hit, surface))
                {
                    KeepLastSurfaceGhost(builder, prefab);
                    __result = false;
                    return;
                }

                var setSurface = builder.GetMethods(All).FirstOrDefault(m =>
                    m.Name == "SetPlaceOnSurface" &&
                    m.GetParameters().Length == 3 &&
                    m.GetParameters()[0].ParameterType.IsInstanceOfType(hit) &&
                    m.GetParameters()[1].ParameterType.IsByRef &&
                    m.GetParameters()[2].ParameterType.IsByRef);
                if (setSurface == null) return;

                var position = Read(builder, "placePosition");
                var rotation = Read(builder, "placeRotation");
                if (position == null || rotation == null) return;
                // Fake or real Builder.SetPlaceOnSurface may populate the
                // by-ref position from hit.point; starting there also
                // guarantees our fallback is not tied to the former
                // camera-pitch position if a helper fails to update it.
                var args = new object[] { hit, Read(hit, "point"), rotation };
                setSurface.Invoke(null, args); // existing SnapHook rounds tangent axes
                var collider = Read(hit, "collider");
                var hitObject = Read(collider, "gameObject");
                if (hitObject == null) return;

                // Apply the candidate BEFORE running vanilla CheckSpace:
                // the other clients will get the same verified Builder pose
                // in BOT Benson's ordinary TryPlace construction packet.
                Write(builder, "placePosition", args[1]);
                Write(builder, "placeRotation", args[2]);
                Write(builder, "placementTarget", hitObject);
                var valid = CheckPlacementSpace(builder, collider, args[1], args[2]);
                __result = valid;
                if (valid)
                {
                    // Current BZ has CheckAsSubModule(out Collider). Its
                    // caller uses the collider for subsequent obstacle
                    // filtering. Returning success without this OUT value
                    // could misvalidate the new ground/wall placement.
                    if (__args != null && __args.Length > 0)
                        __args[0] = collider;
                    Remember(builder, prefab);
                    if (surface == Anchor.Floor ? !reportedFloor : !reportedWall)
                    {
                        if (surface == Anchor.Floor) reportedFloor = true;
                        else reportedWall = true;
                        Trace.Info("SURFACE_LOCK_FIRST_CONTACT: " + surface +
                            ", furniture=" + Read(builder, "constructableTechType") +
                            ", hitHeight=" + Float(Read(hit, "point"), "y") +
                            ", pivotHeight=" + Float(args[1], "y") +
                            ". Subsequent placements are silent.");
                    }
                }
            }
            catch (Exception e)
            {
                __result = false;
                if (!warned)
                {
                    warned = true;
                    Trace.Info("SURFACE_LOCK_FAILED (placing denied): " +
                        (e.InnerException ?? e));
                }
            }
        }

        private static Anchor AllowedAnchor(Type builder, object prefab)
        {
            var constructable = Reflect.Type("Constructable");
            if (constructable == null) return Anchor.None;
            var get = prefab.GetType().GetMethod("GetComponent", All,
                null, new[] { typeof(Type) }, null);
            var instance = get?.Invoke(prefab, new object[] { constructable });
            if (instance == null) return Anchor.None;

            var techName = Read(builder, "constructableTechType")?.ToString();
            // Actual Below Zero furniture taxonomy: full Locker stands on
            // floor; SmallLocker is a wall locker. Do not infer attachment
            // from what surface an unrelated camera ray happened to hit.
            if (techName == "Locker") return Anchor.Floor;
            if (techName == "SmallLocker") return Anchor.Wall;

            var surfaces = Read(instance, "allowedSurfaceTypes") as IEnumerable;
            if (surfaces == null) return Anchor.None;
            bool floor = false, wall = false, ceiling = false;
            foreach (var item in surfaces)
            {
                switch (item?.ToString())
                {
                    case "Ground": floor = true; break;
                    case "Wall": wall = true; break;
                    case "Ceiling": ceiling = true; break;
                }
            }
            if (floor && !wall && !ceiling) return Anchor.Floor;
            if (wall && !floor && !ceiling) return Anchor.Wall;
            // Multisurface / ceiling furniture: retain game's selection.
            return Anchor.None;
        }

        private static bool IsBasePiece(object prefab)
        {
            var type = Reflect.Type("ConstructableBase");
            if (type == null) return true; // fail closed
            var get = prefab.GetType().GetMethod("GetComponent", All,
                null, new[] { typeof(Type) }, null);
            return get == null || get.Invoke(prefab, new[] { (object)type }) != null;
        }

        private static bool TrySurfaceRay(Type builder, Anchor surface, out object hit)
        {
            hit = null;
            var camera = Read(Reflect.Type("MainCamera"), "camera");
            var transform = Read(camera, "transform");
            var origin = Read(transform, "position");
            var forward = Read(transform, "forward");
            if (origin == null || forward == null) return false;

            var camX = Float(origin, "x");
            var camY = Float(origin, "y");
            var camZ = Float(origin, "z");
            var dirX = Float(forward, "x");
            var dirZ = Float(forward, "z");
            var len = (float)Math.Sqrt(dirX * dirX + dirZ * dirZ);
            if (len >= 0.08f)
            {
                rememberedYawX = dirX / len;
                rememberedYawZ = dirZ / len;
            }
            var minDistance = Math.Max(.5f, GetFloat(builder, "placeMinDistance", .1f));
            var maxDistance = Math.Max(minDistance + .2f,
                Math.Min(5f, GetFloat(builder, "placeMaxDistance", 3f)));
            var preferred = Math.Max(minDistance + .2f,
                Math.Min(maxDistance - .1f,
                    GetFloat(builder, "placeDefaultDistance", 2f)));

            // Camera pitch no longer affects XZ target or candidate height.
            // Prefer native/default distance; attempt a closer point when
            // a wall/object blocks the original target.
            if (surface == Anchor.Wall)
            {
                // One stable horizontal direction, independent of the
                // camera's forward/back position or vertical pitch.
                var dir = NewVector(origin.GetType(), rememberedYawX, 0f, rememberedYawZ);
                return FindWallSurface(builder, origin, dir, maxDistance, out hit);
            }

            var candidates = new[] { preferred, preferred - .45f,
                preferred + .45f, preferred - .9f };
            foreach (var distance in candidates)
            {
                if (distance < minDistance || distance > maxDistance) continue;
                var start = NewVector(origin.GetType(),
                    camX + rememberedYawX * distance,
                    camY + .2f,
                    camZ + rememberedYawZ * distance);
                var direction = NewVector(origin.GetType(), 0f, -1f, 0f);
                float castDistance = 8f;
                if (FindFloorSurface(builder, start, direction, castDistance,
                    out var floor))
                {
                    hit = floor;
                    return true;
                }
            }
            return false;
        }

        private static bool FindFloorSurface(Type builder, object origin,
            object direction, float distance, out object hit)
        {
            hit = null;
            // In a packed base the downward ray frequently hits a locker TOP
            // first (also a horizontal/upward normal), not the actual floor.
            // RaycastAll lets floor objects reach the structural floor behind
            // surrounding furniture. We do NOT classify another piece of
            // furniture as a "floor", even when G enables clipping.
            var physics = Reflect.Type("UnityEngine.Physics");
            var query = Reflect.Type("UnityEngine.QueryTriggerInteraction");
            if (physics == null || query == null) return false;
            var vector = origin.GetType();
            var raycastAll = physics.GetMethods(All).FirstOrDefault(m =>
            {
                var p = m.GetParameters();
                return m.Name == "RaycastAll" && m.IsStatic &&
                    p.Length == 5 && p[0].ParameterType == vector &&
                    p[1].ParameterType == vector &&
                    p[2].ParameterType == typeof(float) &&
                    p[3].ParameterType == typeof(int) &&
                    p[4].ParameterType == query;
            });
            if (raycastAll == null)
            {
                return Raycast(builder, origin, direction, distance, out hit) &&
                    MatchSurface(builder, hit, Anchor.Floor) &&
                    !IsFurnitureHit(hit) && ValidateHit(builder, hit, Anchor.Floor);
            }
            var mask = Read(builder, "placeLayerMask");
            var rawMask = mask is int ? mask : Read(mask, "value");
            if (rawMask == null) return false;
            var hits = raycastAll.Invoke(null, new object[] {
                origin, direction, distance, Convert.ToInt32(rawMask),
                Enum.Parse(query, "Ignore")
            }) as IEnumerable;
            if (hits == null) return false;
            foreach (var candidate in hits.Cast<object>()
                .Where(h => h != null && MatchSurface(builder, h, Anchor.Floor) &&
                    !IsFurnitureHit(h))
                .OrderBy(h => DistanceSquared(origin, Read(h, "point"))))
            {
                if (!ValidateHit(builder, candidate, Anchor.Floor)) continue;
                hit = candidate;
                return true;
            }
            return false;
        }

        private static bool FindWallSurface(Type builder, object origin,
            object direction, float distance, out object hit)
        {
            hit = null;
            var physics = Reflect.Type("UnityEngine.Physics");
            var query = Reflect.Type("UnityEngine.QueryTriggerInteraction");
            if (physics == null || query == null) return false;
            var vector = origin.GetType();
            var raycastAll = physics.GetMethods(All).FirstOrDefault(m =>
            {
                var p = m.GetParameters();
                return m.Name == "RaycastAll" && m.IsStatic &&
                    p.Length == 5 && p[0].ParameterType == vector &&
                    p[1].ParameterType == vector &&
                    p[2].ParameterType == typeof(float) &&
                    p[3].ParameterType == typeof(int) &&
                    p[4].ParameterType == query;
            });
            if (raycastAll == null)
            {
                // Older Unity build fallback. Never falsely claim a wall
                // behind another collider if only one hit can be retrieved.
                return Raycast(builder, origin, direction, distance, out hit) &&
                    MatchSurface(builder, hit, Anchor.Wall) &&
                    ValidateHit(builder, hit, Anchor.Wall) &&
                    (!IsFurnitureHit(hit) ||
                     Read(builder, "allowedOnConstructables") is bool allowed && allowed);
            }
            var mask = Read(builder, "placeLayerMask");
            var rawMask = mask is int ? mask : Read(mask, "value");
            if (rawMask == null) return false;
            var hits = raycastAll.Invoke(null, new object[] {
                origin, direction, distance, Convert.ToInt32(rawMask),
                Enum.Parse(query, "Ignore")
            }) as IEnumerable;
            if (hits == null) return false;

            // Priority matters: a SmallLocker must prefer the BASE WALL
            // behind nearby regular lockers, not attach to the locker mesh.
            // Keep old object-to-object attachment only as a secondary
            // option if original placement flags explicitly allow it.
            var candidates = hits.Cast<object>()
                .Where(h => h != null && MatchSurface(builder, h, Anchor.Wall))
                .OrderBy(h => IsFurnitureHit(h) ? 1 : 0)
                .ThenBy(h => DistanceSquared(origin, Read(h, "point")));
            foreach (var candidate in candidates)
            {
                if (IsFurnitureHit(candidate) &&
                    !(Read(builder, "allowedOnConstructables") is bool allowed &&
                      allowed)) continue;
                if (!ValidateHit(builder, candidate, Anchor.Wall)) continue;
                hit = candidate;
                return true;
            }
            return false;
        }

        private static bool IsFurnitureHit(object hit)
        {
            var go = Read(Read(hit, "collider"), "gameObject");
            var componentType = Reflect.Type("Constructable");
            var baseType = Reflect.Type("ConstructableBase");
            var baseDecon = Reflect.Type("BaseDeconstructable");
            if (go == null || componentType == null) return false;
            var get = go.GetType().GetMethod("GetComponentInParent", All,
                null, new[] { typeof(Type) }, null);
            if (get == null) return true; // ambiguous: don't prioritize
            if (baseType != null &&
                get.Invoke(go, new object[] { baseType }) != null) return false;
            if (baseDecon != null &&
                get.Invoke(go, new object[] { baseDecon }) != null) return false;
            return get.Invoke(go, new object[] { componentType }) != null;
        }

        private static float DistanceSquared(object a, object b)
        {
            if (a == null || b == null) return float.MaxValue;
            var x = Float(a, "x") - Float(b, "x");
            var y = Float(a, "y") - Float(b, "y");
            var z = Float(a, "z") - Float(b, "z");
            return x*x + y*y + z*z;
        }

        private static bool Raycast(Type builder, object origin,
            object direction, float distance, out object hit)
        {
            hit = null;
            var physics = Reflect.Type("UnityEngine.Physics");
            var raycastType = Reflect.Type("UnityEngine.RaycastHit");
            var query = Reflect.Type("UnityEngine.QueryTriggerInteraction");
            if (physics == null || raycastType == null || query == null)
                return false;
            var method = physics.GetMethods(All).FirstOrDefault(m =>
            {
                var p = m.GetParameters();
                return m.Name == "Raycast" && m.IsStatic &&
                    m.ReturnType == typeof(bool) && p.Length == 6 &&
                    p[0].ParameterType == origin.GetType() &&
                    p[1].ParameterType == direction.GetType() &&
                    p[2].ParameterType == raycastType.MakeByRefType() &&
                    p[3].ParameterType == typeof(float) &&
                    p[4].ParameterType == typeof(int) &&
                    p[5].ParameterType == query;
            });
            if (method == null) return false;
            var mask = Read(builder, "placeLayerMask");
            var rawMask = mask is int ? mask : Read(mask, "value");
            if (rawMask == null) return false;
            var args = new object[] {
                origin, direction, Activator.CreateInstance(raycastType),
                distance, Convert.ToInt32(rawMask),
                Enum.Parse(query, "Ignore")
            };
            if (!(bool)method.Invoke(null, args)) return false;
            hit = args[2];
            return hit != null;
        }

        private static bool MatchSurface(Type builder, object hit, Anchor required)
        {
            var normal = Read(hit, "normal");
            if (normal == null) return false;
            var get = builder.GetMethods(All).FirstOrDefault(m =>
                m.Name == "GetSurfaceType" &&
                m.GetParameters().Length == 1 &&
                m.GetParameters()[0].ParameterType == normal.GetType());
            var type = get?.Invoke(null, new[] { normal })?.ToString();
            if (type == null)
                return required == Anchor.Floor ? Float(normal, "y") > .75f :
                    Math.Abs(Float(normal, "y")) < .35f;
            return type == (required == Anchor.Floor ? "Ground" : "Wall");
        }

        private static bool ValidateHit(Type builder, object hit, Anchor surface)
        {
            if (!MatchSurface(builder, hit, surface)) return false;
            var collider = Read(hit, "collider");
            var gameObject = Read(collider, "gameObject");
            var point = Read(hit, "point");
            if (collider == null || gameObject == null || point == null)
                return false;

            // Our downward raycast can be longer than vanilla's horizontal
            // builder range. Never allow the fallback to reach a surface
            // further than the original camera-to-point placeMaxDistance.
            var camera = Read(Reflect.Type("MainCamera"), "camera");
            var cameraPos = Read(Read(camera, "transform"), "position");
            if (cameraPos == null) return false;
            float dx = Float(point, "x") - Float(cameraPos, "x");
            float dy = Float(point, "y") - Float(cameraPos, "y");
            float dz = Float(point, "z") - Float(cameraPos, "z");
            var range = GetFloat(builder, "placeMaxDistance", 3f);
            if (range <= 0f ||
                dx * dx + dy * dy + dz * dz > (range + .01f) * (range + .01f))
                return false;

            // Current BZ CheckAsSubModule also rejects SeaTruck surfaces
            // and requires Constructable.CheckFlags (which takes extra
            // underwater/hit-point parameters in later BZ builds). A
            // fallback raycast is NOT permission to bypass these rules.
            var seaTruck = Reflect.Type("SeaTruckSegment");
            var parentComponent = gameObject.GetType().GetMethod(
                "GetComponentInParent", All, null, new[] { typeof(Type) }, null);
            if (seaTruck != null &&
                (parentComponent == null ||
                 parentComponent.Invoke(gameObject, new object[] { seaTruck }) != null))
                return false;
            if (!CheckConstructableFlags(builder, point)) return false;

            var checkTag = builder.GetMethods(All).FirstOrDefault(m =>
                m.Name == "CheckTag" && m.GetParameters().Length == 1 &&
                m.GetParameters()[0].ParameterType.IsInstanceOfType(collider));
            var checkDistance = builder.GetMethods(All).FirstOrDefault(m =>
                m.Name == "CheckDistance" && m.GetParameters().Length == 2 &&
                m.GetParameters()[0].ParameterType == point.GetType() &&
                m.GetParameters()[1].ParameterType == typeof(float));
            if (checkTag == null || checkDistance == null) return false;
            if (!(bool)checkTag.Invoke(null, new[] { collider }) ||
                !(bool)checkDistance.Invoke(null, new object[] {
                    point, GetFloat(builder, "placeMinDistance", .1f)
                })) return false;

            var allowedOn = Read(builder, "allowedOnConstructables");
            if (allowedOn is bool && !(bool)allowedOn)
            {
                var type = Reflect.Type("Constructable");
                var parent = gameObject.GetType().GetMethod("GetComponentInParent",
                    All, null, new[] { typeof(Type) }, null);
                if (type == null || parent == null) return false;
                if (parent.Invoke(gameObject, new object[] { type }) != null)
                    return false;
            }

            // As in vanilla CheckAsSubModule, only validate outdoor
            // restrictions when the player is not inside a vehicle/base.
            var player = Read(Reflect.Type("Player"), "main");
            var inside = player?.GetType().GetMethod("IsInSub", All,
                null, Type.EmptyTypes, null);
            if (inside == null) return false;
            if (!(bool)inside.Invoke(player, null))
            {
                var validate = builder.GetMethods(All).FirstOrDefault(m =>
                    m.Name == "ValidateOutdoor" && m.GetParameters().Length == 1 &&
                    m.GetParameters()[0].ParameterType.IsInstanceOfType(gameObject));
                if (validate == null) return false;
                // A base wall collider can belong to a child object.
                var root = gameObject;
                var utils = Reflect.Type("UWE.Utils");
                var entityRoot = utils?.GetMethods(All).FirstOrDefault(m =>
                    m.Name == "GetEntityRoot" && m.IsStatic &&
                    m.GetParameters().Length == 1 &&
                    m.GetParameters()[0].ParameterType.IsInstanceOfType(gameObject));
                if (entityRoot != null)
                    root = entityRoot.Invoke(null, new[] { gameObject }) ?? gameObject;
                if (!(bool)validate.Invoke(null, new[] { root })) return false;
            }
            return true;
        }

        private static bool CheckConstructableFlags(Type builder, object point)
        {
            var constructable = Reflect.Type("Constructable");
            if (constructable == null) return false;
            var permitted = new[] {
                Read(builder, "allowedInBase"),
                Read(builder, "allowedInSub"),
                Read(builder, "allowedOutside")
            };
            if (permitted.Any(v => !(v is bool))) return false;
            var checkFlags = constructable.GetMethods(All).FirstOrDefault(m =>
            {
                var ps = m.GetParameters();
                return m.Name == "CheckFlags" && m.IsStatic &&
                    m.ReturnType == typeof(bool) &&
                    (ps.Length == 5 || ps.Length == 3) &&
                    ps[0].ParameterType == typeof(bool) &&
                    ps[1].ParameterType == typeof(bool) &&
                    ps[2].ParameterType == typeof(bool) &&
                    (ps.Length == 3 ||
                     (ps[3].ParameterType == typeof(bool) &&
                      ps[4].ParameterType.IsInstanceOfType(point)));
            });
            if (checkFlags == null) return false;
            var args = new object[] {
                permitted[0], permitted[1], permitted[2]
            };
            if (checkFlags.GetParameters().Length == 5)
            {
                var underwater = Read(builder, "allowedUnderwater");
                if (!(underwater is bool)) return false;
                args = new object[] {
                    permitted[0], permitted[1], permitted[2], underwater, point
                };
            }
            return (bool)checkFlags.Invoke(null, args);
        }

        private static bool CheckPlacementSpace(Type builder,
            object collider, object position, object rotation)
        {
            var bounds = Read(builder, "bounds");
            if (bounds == null) return false;
            var layerMask = Read(builder, "placeLayerMask");
            var value = layerMask is int ? layerMask : Read(layerMask, "value");
            if (value == null) return false;

            var check = builder.GetMethods(All).FirstOrDefault(m =>
            {
                var p = m.GetParameters();
                return m.Name == "CheckSpace" && m.ReturnType == typeof(bool) &&
                    p.Length == 5 &&
                    p[0].ParameterType == position.GetType() &&
                    p[1].ParameterType == rotation.GetType() &&
                    p[2].ParameterType.IsInstanceOfType(bounds) &&
                    p[3].ParameterType == typeof(int) &&
                    p[4].ParameterType.IsInstanceOfType(collider);
            });
            if (check == null) return false;
            return (bool)check.Invoke(null, new object[] {
                position, rotation, bounds, Convert.ToInt32(value), collider
            });
        }

        private static void Remember(Type builder, object prefab)
        {
            lastPrefab = prefab;
            lastPosition = Read(builder, "placePosition");
            lastRotation = Read(builder, "placeRotation");
        }

        private static void KeepLastSurfaceGhost(Type builder, object prefab)
        {
            // Never allow placement on a missing/invalid ray. Keep the
            // ghost visually at its LAST verified surface for this prefab
            // until another legal hit arrives or Builder.End resets it.
            // A time-based expiry made it detach and follow camera pitch
            // again after 450ms, contrary to floor-only placement rules.
            // __result stays false: stale visual poses can NEVER be placed.
            if (!ReferenceEquals(prefab, lastPrefab) ||
                lastPosition == null || lastRotation == null) return;
            Write(builder, "placePosition", lastPosition);
            Write(builder, "placeRotation", lastRotation);
        }

        private static object Read(object instance, string name)
        {
            if (instance == null) return null;
            var type = instance as Type ?? instance.GetType();
            var target = instance is Type ? null : instance;
            var field = type.GetField(name, All);
            return field != null ? field.GetValue(target) :
                type.GetProperty(name, All)?.GetValue(target, null);
        }

        private static bool Write(Type type, string name, object value)
        {
            var field = type.GetField(name, All);
            if (field != null) { field.SetValue(null, value); return true; }
            var prop = type.GetProperty(name, All);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(null, value, null);
                return true;
            }
            return false;
        }

        private static float GetFloat(Type type, string name, float fallback)
        {
            var value = Read(type, name);
            return value == null ? fallback : Convert.ToSingle(value);
        }

        private static float Float(object vector, string axis)
        {
            if (vector == null) return 0f;
            return Convert.ToSingle(Read(vector, axis));
        }

        private static object NewVector(Type vector,
            float x, float y, float z) =>
            Activator.CreateInstance(vector, new object[] { x, y, z });
    }
}

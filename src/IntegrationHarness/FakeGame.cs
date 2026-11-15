using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

// Fakes only for CI: NOT shipped with the game plugin.
// Names/signatures deliberately imitate calls in the published Subnautica BZ code.
namespace UnityEngine
{
    public enum KeyCode
    {
        Mouse2, Mouse3, Mouse4, LeftControl, RightControl, LeftAlt, RightAlt,
        G, T, B, F6, KeypadDivide, Keypad4, Keypad6, Keypad2, Keypad8, KeypadMinus, KeypadPlus
    }
    public static class Time
    {
        public static int frameCount { get; set; } = 1;
        public static float deltaTime { get; set; } = 0.016f;
    }
    public static class Input
    {
        public static readonly HashSet<KeyCode> Down = new HashSet<KeyCode>();
        public static readonly HashSet<KeyCode> Held = new HashSet<KeyCode>();
        public static bool GetKeyDown(KeyCode key) => Down.Contains(key);
        public static bool GetKey(KeyCode key) => Held.Contains(key);
        public static void Clear() { Down.Clear(); Held.Clear(); }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x=x; this.y=y; this.z=z; }
        public override string ToString() => $"({x},{y},{z})";
    }
    public struct Quaternion
    {
        public float x, y, z, w;
        public Vector3 eulerAngles { get { return new Vector3(x,y,z); } }
        public Quaternion(float x, float y, float z, float w) { this.x=x; this.y=y; this.z=z; this.w=w; }
        public static Quaternion Euler(Vector3 euler) => new Quaternion(euler.x, euler.y, euler.z, 1f);
    }
    public class Transform
    {
        public GameObject gameObject = new GameObject();
        public Vector3 position;
        public Vector3 InverseTransformPoint(Vector3 p) => p;
        public Vector3 InverseTransformDirection(Vector3 p) => p;
        public Vector3 TransformPoint(Vector3 p) => p;
    }
    public struct RaycastHit
    {
        public Vector3 normal { get; set; }
        public Transform transform { get; set; }
    }
    public class GameObject
    {
        private readonly Dictionary<Type,object> components = new Dictionary<Type,object>();
        public string name = "test-object";
        public void AddComponent<T>(T component) => components[typeof(T)] = component;
        public object GetComponent(Type type) => components.TryGetValue(type, out var c) ? c : null;
        public object GetComponentInParent(Type type) => GetComponent(type);
    }
}
public enum TechType { None, Titanium, Locker, SmallLocker, TestRecipe }
public class ItemsContainer {}
public class Ingredient
{
    public TechType techType { get; }
    public int amount { get; }
    public Ingredient(TechType techType, int amount) { this.techType = techType; this.amount = amount; }
}
public static class TechData
{
    public static System.Collections.ObjectModel.ReadOnlyCollection<Ingredient> GetIngredients(TechType recipe) =>
        new System.Collections.ObjectModel.ReadOnlyCollection<Ingredient>(
            recipe == TechType.TestRecipe ? new List<Ingredient> {
                new Ingredient(TechType.Titanium, 2) } : new List<Ingredient>());
}
public class uGUI_RecipeEntry
{
    public TechType techType { get; set; }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void UpdateIngredients(ItemsContainer inventory, bool ping) { }
}
public sealed class Pickupable
{
    public TechType techType = TechType.Titanium;
    public UnityEngine.GameObject gameObject = new UnityEngine.GameObject();
    public TechType GetTechType() => techType;
}
public sealed class InventoryItem
{
    public Pickupable item = new Pickupable();
}
public class StorageContainer
{
    public UnityEngine.Transform storageRoot = new UnityEngine.Transform();
    public List<InventoryItem> container = new List<InventoryItem>();
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Awake() { }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void ResetContainer() { }
}
public class Inventory
{
    public static Inventory main = new Inventory();
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int GetPickupCount(TechType type) => 0;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool DestroyItem(TechType destroyTechType, bool skipEquipment = false) => false;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void OnAddItem(InventoryItem item) { }
}
public class Player
{
    public static Player main = new Player();
    public UnityEngine.Transform transform = new UnityEngine.Transform();
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Update() { }
}
public class ConstructableBase
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool DeconstructionAllowed(ref string reason) => false;
}
public class Constructable
{
    public TechType techType = TechType.Locker;
    public UnityEngine.GameObject gameObject = new UnityEngine.GameObject();
    public float placeDefaultDistance = 2f;
    public float placeMaxDistance = 3f;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool DeconstructionAllowed(ref string reason) => false;
}
public class BaseDeconstructable
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool DeconstructionAllowed(ref string reason) => false;
}
public class PowerConsumer
{
    public object baseComp;
    public FakePowerRelay powerRelay;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool IsPowered() => false;
}
public class FakePowerRelay
{
    public bool IsPowered() => true;
}
public class Creature {}
public class BaseGhost {}
public class LifepodDrop {}
public enum SurfaceType { Wall, Ceiling, Ground }

public static class Builder
{
    public static UnityEngine.GameObject prefab { get; set; }
    public static UnityEngine.GameObject ghostModel { get; set; }
    public static bool rotationEnabled = true;
    public static float placeDefaultDistance = 2f;
    public static float placeMaxDistance = 3f;
    public static UnityEngine.Vector3 placePosition;
    public static UnityEngine.Quaternion placeRotation;
    public static bool allowedOnConstructables = false, allowedInBase = false,
        allowedInSub = false, allowedOutside = false;
    public static List<SurfaceType> allowedSurfaceTypes = new List<SurfaceType> { SurfaceType.Wall };

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void SetPlaceOnSurface(UnityEngine.RaycastHit hit,
        ref UnityEngine.Vector3 position, ref UnityEngine.Quaternion rotation) {}

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Update() {}
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void End() {}
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool CreateGhost() => true;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool UpdateAllowed() => false;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool ValidateOutdoor(UnityEngine.GameObject go) => false;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void GetObstacles(UnityEngine.Vector3 position, UnityEngine.Quaternion rotation,
        List<object> bounds, Func<object,bool> predicate, List<UnityEngine.GameObject> results) {}
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool CheckSpace(UnityEngine.Vector3 position, UnityEngine.Quaternion rot,
        List<object> bounds, int mask, object ignore) => true;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void CheckSpace(UnityEngine.Vector3 position, UnityEngine.Quaternion rot,
        List<object> bounds, int mask, object ignore, List<UnityEngine.GameObject> obstacles) {}
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool TryPlace() => true;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void SetDefaultPlaceTransform(ref UnityEngine.Vector3 pos,
        ref UnityEngine.Quaternion rotation)
    {
        pos = new UnityEngine.Vector3(0, 0, 0);
        rotation = UnityEngine.Quaternion.Euler(new UnityEngine.Vector3(0, 0, 0));
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool CanDestroyObject(UnityEngine.GameObject go) => true;
}

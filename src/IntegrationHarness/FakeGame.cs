using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Linq;

// Fakes only for CI: NOT shipped with the game plugin.
// Names/signatures deliberately imitate calls in the published Subnautica BZ code.
namespace UnityEngine
{
    public class Object
    {
        // Scene scan behaves like Unity's non-generic FindObjectsOfType(Type).
        public static object[] FindObjectsOfType(Type type) =>
            StorageContainer.SceneObjects.Where(x => type.IsInstanceOfType(x))
                .Cast<object>().ToArray();
    }
    public enum KeyCode
    {
        Mouse2, Mouse3, Mouse4, LeftControl, RightControl, LeftAlt, RightAlt,
        G, T, B, F6, KeypadDivide, Keypad4, Keypad6, Keypad2, Keypad8, KeypadMinus, KeypadPlus
    }
    public static class Time
    {
        private static int frame = 1;
        public static bool ThrowOnFrameRead;
        public static int frameCount
        {
            get
            {
                if (ThrowOnFrameRead)
                    throw new InvalidOperationException(
                        "Simulated Unity Time.frameCount getter failure");
                return frame;
            }
            set { frame = value; }
        }
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
        public static bool ThrowOnEuler;
        public static Quaternion Euler(Vector3 euler)
        {
            if (ThrowOnEuler) throw new InvalidOperationException(
                "Simulated Unity Euler conversion failure");
            return new Quaternion(euler.x, euler.y, euler.z, 1f);
        }
    }
    public class Transform
    {
        public GameObject gameObject = new GameObject();
        public Transform parent;
        public Vector3 position;
        // Match Unity's translation part of Transform, rather than the old
        // fake identity transform that hid incorrect local/world snapping.
        public Vector3 InverseTransformPoint(Vector3 p) =>
            new Vector3(p.x-position.x, p.y-position.y, p.z-position.z);
        public Vector3 InverseTransformDirection(Vector3 p) => p;
        public Vector3 TransformPoint(Vector3 p) =>
            new Vector3(p.x+position.x, p.y+position.y, p.z+position.z);
    }
    public struct RaycastHit
    {
        public Vector3 normal { get; set; }
        public Vector3 point { get; set; }
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
public enum TechType { None, Titanium, Copper, Locker, SmallLocker, TestRecipe, DuplicateIngredientRecipe }
public class ItemsContainer : IEnumerable<InventoryItem>
{
    private readonly List<InventoryItem> contents = new List<InventoryItem>();
    public int GetItemsCalls;
    public int Enumerations;
    // Real game has both IEnumerable<InventoryItem> and the two-argument
    // filtered GetItems API. Do not fake away a real supported interface.
    public IEnumerator<InventoryItem> GetEnumerator()
    {
        Enumerations++;
        return contents.GetEnumerator();
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public UnityEngine.Transform tr = new UnityEngine.Transform();
    public int Count => contents.Count;
    public void Add(InventoryItem item) => contents.Add(item);
    public bool Remove(InventoryItem item) => contents.Remove(item);
    public void Clear() => contents.Clear();
    // GetItems(TechType, List<InventoryItem>) is also supported.
    public void GetItems(TechType type, List<InventoryItem> result)
    {
        GetItemsCalls++;
        result.AddRange(contents.Where(i => i.item?.GetTechType() == type));
    }
    public int GetCount(TechType type) => contents.Count(i => i.item?.GetTechType() == type);
}
public class Ingredient
{
    public TechType techType { get; }
    public int amount { get; }
    public Ingredient(TechType techType, int amount) { this.techType = techType; this.amount = amount; }
}
public enum GameOption { CraftingRequiresResources }
public static class GameModeManager
{
    public static bool CraftingRequiresResources = true;
    public static T GetOption<T>(GameOption option)
    {
        if (option != GameOption.CraftingRequiresResources)
            throw new ArgumentException("Unsupported fake GameOption");
        return (T)(object)CraftingRequiresResources;
    }
}
public static class CrafterLogic
{
    public static bool IsUnlocked = true;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool IsCraftRecipeUnlocked(TechType techType) => IsUnlocked;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool IsCraftRecipeFulfilled(TechType recipe)
    {
        if (!GameModeManager.CraftingRequiresResources) return true;
        var required = new Dictionary<TechType,int>();
        foreach (var ingredient in TechData.GetIngredients(recipe))
        {
            required.TryGetValue(ingredient.techType, out int current);
            required[ingredient.techType] = current + ingredient.amount;
        }
        foreach (var part in required)
            if (Inventory.main.GetPickupCount(part.Key) < part.Value)
                return false;
        return true;
    }
}
public static class TechData
{
    public static System.Collections.ObjectModel.ReadOnlyCollection<Ingredient> GetIngredients(TechType recipe) =>
        new System.Collections.ObjectModel.ReadOnlyCollection<Ingredient>(
            recipe == TechType.TestRecipe ? new List<Ingredient> {
                new Ingredient(TechType.Titanium, 2) } :
            recipe == TechType.DuplicateIngredientRecipe ? new List<Ingredient> {
                new Ingredient(TechType.Titanium, 2),
                new Ingredient(TechType.Titanium, 1) } : new List<Ingredient>());
}
public enum TreeAction { None, Expand, Craft }
public class uGUI_CraftingMenu
{
    public class Node
    {
        public TreeAction action;
        public TechType techType;
    }
    public int CraftActionCalls;
    public int ExpandActions;
    public bool Closed;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool ActionAvailable(Node node)
    {
        if (node == null) return false;
        if (node.action == TreeAction.Expand) return true;
        return node.action == TreeAction.Craft &&
            CrafterLogic.IsCraftRecipeUnlocked(node.techType) &&
            CrafterLogic.IsCraftRecipeFulfilled(node.techType);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Action(Node node)
    {
        if (node.action == TreeAction.Craft) CraftActionCalls++;
        if (node.action == TreeAction.Expand) ExpandActions++;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Close() { Closed = true; }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void OnDeselect() { Closed = true; }
}
public class FakeColorText { public string color = "red"; }
public class FakeRecipeManager
{
    public string colorGreen = "green";
    public string colorRed = "red";
}
public class uGUI_RecipeItem
{
    public FakeColorText text = new FakeColorText();
    public int DisplayCount;
    public int DisplayRequired;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Set(TechType type, int count, int amount, bool ping)
    {
        DisplayCount = count;
        DisplayRequired = amount;
    }
}
public class uGUI_RecipeEntry
{
    public TechType techType { get; set; }
    public FakeRecipeManager manager = new FakeRecipeManager();
    public List<uGUI_RecipeItem> items = new List<uGUI_RecipeItem>();
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void UpdateIngredients(ItemsContainer inventory, bool ping)
    {
        items.Clear();
        foreach (var ingredient in TechData.GetIngredients(techType))
        {
            var displayed = new uGUI_RecipeItem();
            displayed.DisplayRequired = ingredient.amount;
            displayed.DisplayCount = Inventory.main.GetPickupCount(ingredient.techType);
            displayed.text.color = displayed.DisplayCount >= displayed.DisplayRequired ?
                "green" : "red";
            items.Add(displayed);
        }
    }
}
public class TooltipIcon
{
    public object sprite;
    public string text;
    public TooltipIcon(object sprite, string text) { this.sprite = sprite; this.text = text; }
}
public class TooltipData
{
    public List<TooltipIcon> icons = new List<TooltipIcon>();
}
public static class SpriteManager
{
    public static object Get(TechType type) => type.ToString() + "-sprite";
}
public class Language
{
    public static Language main = new Language();
    public string Get(string key) => key;
}
public static class TooltipFactory
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void CraftRecipe(TechType type, bool locked, TooltipData data)
    {
        foreach (var ingredient in TechData.GetIngredients(type))
            data.icons.Add(new TooltipIcon(SpriteManager.Get(ingredient.techType),
                "<color=#FF7777FF>" + ingredient.techType + "</color>"));
    }
}
public static class ErrorMessage
{
    public static int HudMessages;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void AddMessage(string message) { HudMessages++; }
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
    public static readonly List<StorageContainer> SceneObjects = new List<StorageContainer>();
    public UnityEngine.Transform storageRoot = new UnityEngine.Transform();
    public ItemsContainer container = new ItemsContainer();
    public StorageContainer()
    {
        container.tr.parent = storageRoot;
        SceneObjects.Add(this);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Awake() { }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void ResetContainer() { }
}
public class QuickSlots
{
    public bool HotkeysIgnored;
    public void SetIgnoreHotkeyInput(bool ignore) => HotkeysIgnored = ignore;
}
public class PlaceTool
{
    public object usingPlayer = new object();
    public bool rotationEnabled = true;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void LateUpdate() { }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void OnPlace() { }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void OnHolster() { }
}
public class Inventory
{
    public static Inventory main = new Inventory();
    public QuickSlots quickSlots = new QuickSlots();
    public readonly Dictionary<TechType, int> ActualItems = new Dictionary<TechType, int>();
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int GetPickupCount(TechType type) =>
        ActualItems.TryGetValue(type, out var count) ? count : 0;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool DestroyItem(TechType destroyTechType, bool skipEquipment = false) => false;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void OnAddItem(InventoryItem item)
    {
        // Simulates the actual client inventory state BEFORE Harmony postfix
        // confirms a server-delivered item.
        var type = item.item.GetTechType();
        ActualItems[type] = GetPickupCount(type) + 1;
    }
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

public static class Targeting
{
    public static float LastMaxDistance;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool GetTarget(float maxDistance, out UnityEngine.GameObject target,
        out float distance)
    {
        LastMaxDistance = maxDistance;
        target = null;
        distance = 0f;
        return false;
    }
}
public class OrientedBounds
{
    public UnityEngine.Vector3[] corners;
    public OrientedBounds(params UnityEngine.Vector3[] points) { corners = points; }
    public UnityEngine.Vector3[] GetCorners() => corners;
}
public static class Builder
{
    public static List<OrientedBounds> bounds = new List<OrientedBounds>();
    public static bool CanPlace = true;
    public static bool CollisionSpaceAvailable = true;
    public static bool canPlace => CanPlace;
    public static int PlacementCalls = 0;
    public static bool isPlacing => ghostModel != null;
    public static TechType constructableTechType = TechType.None;
    // Supplied real BZ Assembly-CSharp.dll defines this as a static FIELD.
    // The previous fake's auto-property gave a misleading ABI signal.
    public static UnityEngine.GameObject prefab;
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
        List<object> bounds, int mask, object ignore) => CollisionSpaceAvailable;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void CheckSpace(UnityEngine.Vector3 position, UnityEngine.Quaternion rot,
        List<object> bounds, int mask, object ignore, List<UnityEngine.GameObject> obstacles) {}
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool TryPlace() { PlacementCalls++; return CanPlace; }
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

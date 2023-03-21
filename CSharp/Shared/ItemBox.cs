using Barotrauma;
using HarmonyLib;
using System.Reflection;
using System.Linq;

namespace ZRG27314
{
	class ItemBoxImpl : ACsMod
	{
		const string harmony_id = "com.sjx.ItemBox";
		const string box_identifier = "ItemBox";
		const float max_condition = 1.0f;
		const int item_count = 1024;
		const float increment = max_condition / item_count;

		private readonly Harmony harmony;

		public ItemBoxImpl() {
			harmony = new Harmony(harmony_id);
			harmony.PatchAll(Assembly.GetExecutingAssembly());
			Barotrauma.DebugConsole.AddWarning("Loaded ItemBox Impl");
		}

		public override void Stop() {
			harmony.UnpatchAll(harmony_id);
		}

		public static int SlotPreserveCount(ItemPrefab prefab) {
			if (prefab.MaxStackSize == 1)
			{
				return 1;
			}
			else {
				return prefab.MaxStackSize - 1;
			}
		}

		[HarmonyPatch(typeof(Inventory))]
		class Patch_PutItem
		{

			static MethodBase TargetMethod() {
				Barotrauma.DebugConsole.AddWarning("Patch_PutItem TargetMethod");
				return AccessTools.Method(typeof(Inventory), "PutItem");
			}
			public static bool Prefix(Inventory __instance, out Inventory __state)
			{
				__state = __instance;
				return true;
			}
			public static void Postfix(Inventory __state)
			{
				if (__state.Owner is Item parentItem && parentItem.Prefab.Identifier == box_identifier)
				{
					if (!parentItem.IsFullCondition && __state.AllItems.Any())
					{

						int preserve = SlotPreserveCount(__state.AllItems.First().Prefab);
						var it = __state.AllItemsMod.GetEnumerator();
						while (it.MoveNext() && !parentItem.IsFullCondition)
						{
							if (preserve > 0)
							{
								preserve--;
							}
							else
							{
								parentItem.Condition += increment;
								// patched RemoveItem will be able to distinguish
								it.Current.ParentInventory = null;
								__state.RemoveItem(it.Current);
								Entity.Spawner.AddItemToRemoveQueue(it.Current);
							}
						}
					}
				}
			}
		}

		[HarmonyPatch(typeof(Inventory), nameof(Inventory.RemoveItem))]
		class Patch_RemoveItem
		{
			public class context
			{
				public Inventory inventory;
				public bool do_dump;
				public float? condition;
				public int? quality;
				public ItemPrefab prefab;
				public context(Inventory inv, Item it)
				{
					inventory = inv;
					// patched putItem use this to say "Just despawned"
					do_dump = it.ParentInventory != null;
					condition = it.Condition;
					quality = it.Quality;
					prefab = it.Prefab;
				}
			};
			public static bool Prefix(Inventory __instance, out context __state, Item item)
			{
				__state = new context(__instance, item);
				return true;
			}
			public static void Postfix(context __state)
			{
				if (__state.do_dump && __state.inventory.Owner is Item parentItem && parentItem.Prefab.Identifier == box_identifier)
				{
					int preserve = SlotPreserveCount(__state.prefab);
					int spawn_count = preserve - __state.inventory.AllItems.Count();
					while (parentItem.Condition >= increment && spawn_count > 0)
					{
						spawn_count--;
						parentItem.Condition -= increment;
						Item.Spawner.AddItemToSpawnQueue(__state.prefab, __state.inventory, __state.condition, __state.quality, spawnIfInventoryFull: false);
					}
				}
			}
		}
	}
	/*
static void Prepare_All_Types()
{
InventoryWrapper.Prepare();
ItemWrapper.Prepare();
ItemEnumerableBinding.Prepare();
ItemEnumeratorBinding.Prepare();
EntitySpawnerBinding.Prepare();
}

class InventoryWrapper
{
public static Type? InventoryType = null;
public static MethodInfo? Inventory_PutItem = null;
public static MethodInfo? Inventory_RemoveItem = null;
public static MethodInfo? Inventory_IsEmpty = null;
public static PropertyInfo? Inventory_AllItemsMod = null;
public static FieldInfo? Inventory_Owner = null;
public static void Prepare()
{
	InventoryType = AccessTools.TypeByName("Barotrauma.Inventory");
	Inventory_PutItem = InventoryType.GetMethod("PutItem");
	Inventory_RemoveItem = InventoryType.GetMethod("RemoveItem");
	Inventory_IsEmpty = InventoryType.GetMethod("IsEmpty");
	Inventory_AllItemsMod = InventoryType.GetProperty("AllItemsMod");
	Inventory_Owner = InventoryType!.GetField("Owner");
}
}

class ItemWrapper
{
public static Type? ItemType = null;
public static PropertyInfo? Item_ContainerIdentifier = null;
public static PropertyInfo? Item_IsFullCondition = null;
public static PropertyInfo? Item_Condition = null;
public static PropertyInfo? Item_Quality = null;
public static PropertyInfo? Item_Prefab = null;
public static PropertyInfo? Item_ParentInventory = null;

public static FieldInfo? Item_Spawner = null;

public static void Prepare()
{
	ItemType = AccessTools.TypeByName("Barotrauma.Item");
	Item_ContainerIdentifier = ItemType.GetProperty("ContainerIdentifier")!;
	Item_IsFullCondition = ItemType.GetProperty("IsFullCondition")!;
	Item_Condition = ItemType.GetProperty("Condition")!;
	Item_Quality = ItemType.GetProperty("Quality")!;
	Item_Prefab = ItemType.GetProperty("Prefab")!;
	Item_ParentInventory = ItemType.GetProperty("ParentInventory")!;
	Item_Spawner = ItemType.GetField("Spawner")!;
}
}

class ItemEnumerableBinding
{
public static Type? ItemEnumerableType = null;
public static MethodInfo? ItemEnumerable_GetEnumerator = null;
public static void Prepare()
{
	ItemEnumerableType = AccessTools.TypeByName("System.Collections.IEnumerable<Barotrauma.Item>");
	ItemEnumerable_GetEnumerator = ItemEnumerableType.GetMethod("GetEnumerator")!;
}
}

class ItemEnumeratorBinding
{
public static Type? ItemEnumeratorType = null;
public static MethodInfo? ItemEnumerator_MoveNext = null;
public static PropertyInfo? ItemEnumerator_Current = null;
public static void Prepare()
{
	ItemEnumeratorType = AccessTools.TypeByName("System.Collections.IEnumerator<Barotrauma.Item>");
	ItemEnumerator_MoveNext = ItemEnumeratorType.GetMethod("MoveNext")!;
	ItemEnumerator_Current = ItemEnumeratorType.GetProperty("Current")!;
}
}

class EntitySpawnerBinding
{
public static Type? EntitySpawnerType = null;
public static MethodInfo? EntitySpawner_AddItemToSpawnQueue_Inventory = null;
public static void Prepare()
{
	EntitySpawnerType = AccessTools.TypeByName("Barotrauma.EntitySpawner");
	EntitySpawner_AddItemToSpawnQueue_Inventory = EntitySpawnerType.GetMethod("AddItemToSpawnQueue",
		new Type[] { AccessTools.TypeByName("Barotrauma.EntitySpawner") , InventoryWrapper.InventoryType!,
			typeof(float?), typeof(int?), AccessTools.TypeByName("System.Action<Barotrauma.Item>"), typeof(bool),
			typeof(bool), typeof(InvSlotType)});
}
}

private static bool EntityIsItem(object entity)
{
return entity.GetType() == ItemWrapper.ItemType;
}
private static Identifier GetParentIdentifier(object item)
{
return (Identifier)ItemWrapper.Item_ContainerIdentifier!.GetValue(item)!;
}

private static void increment_condition(object parentItem, float increment)
{
float current = (float)ItemWrapper.Item_Condition!.GetValue(parentItem)!;
current += increment;
ItemWrapper.Item_Condition!.SetValue(parentItem, current);
}

private static float get_condition(object parentItem)
{
return (float)ItemWrapper.Item_Condition!.GetValue(parentItem)!;
}


[HarmonyPatch]
class Patch_PutItem
{
static void Prepare() {
	ItemBoxImpl.Prepare_All_Types();
}
static MethodBase TargetMethod() {
	return InventoryWrapper.Inventory_PutItem!;
}

public static bool Prefix(object __instance, object __state)
{
	__state = __instance;
	return true;
}

private static bool IsFullCondition(object item) {
	return (bool)ItemWrapper.Item_IsFullCondition!.GetValue(item)!;
}

private static object AllItemsModEnumerator(object inventory) {
	return ItemEnumerableBinding.ItemEnumerable_GetEnumerator!.Invoke(InventoryWrapper.Inventory_AllItemsMod!.GetValue(inventory)!, null)!;
}
private static bool ItemEnumeratorMoveNext(object it) {
	return (bool)ItemEnumeratorBinding.ItemEnumerator_MoveNext!.Invoke(it, null)!;
}

private static object ItemEnumeratorCurrent(object it)
{
	return ItemEnumeratorBinding.ItemEnumerator_Current!.GetValue(it)!;
}

private static void RemoveFromInventory(object inventory, object item) {
	InventoryWrapper.Inventory_RemoveItem!.Invoke(inventory, new object[] { item });
}

public static void Postfix(object __state)
{
	object parentEntity = InventoryWrapper.Inventory_Owner!.GetValue(__state)!;
	if (EntityIsItem(parentEntity) && GetParentIdentifier(parentEntity) == box_identifier)
	{
		if (!IsFullCondition(parentEntity))
		{
			var it = AllItemsModEnumerator(__state);
			// skip first
			if (ItemEnumeratorMoveNext(it))
			{
				while (ItemEnumeratorMoveNext(it) && !IsFullCondition(parentEntity))
				{
					increment_condition(parentEntity, increment);
					RemoveFromInventory(__state, ItemEnumeratorCurrent(it));
				}
			}
		}
	}
}
}

[HarmonyPatch]
class Patch_RemoveItem {
static void Prepare()
{
	ItemBoxImpl.Prepare_All_Types();
}

static MethodBase TargetMethod()
{
	return InventoryWrapper.Inventory_RemoveItem!;
}

public class context
{
	public object inventory;
	public bool do_dump;
	public float? condition;
	public int? quality;
	public object prefab;
	public context(object inv, object it)
	{
		inventory = inv;
		do_dump = ItemWrapper.Item_ParentInventory!.GetValue(it) != null;
		condition = ItemWrapper.Item_Condition!.GetValue(it) as float?;
		quality = ItemWrapper.Item_Quality!.GetValue(it) as int?;
		prefab = ItemWrapper.Item_Prefab!.GetValue(it)!;
	}
};

public static bool Prefix(object __instance, context __state, object item)
{
	__state = new context(__instance, item);
	return true;
}


private static bool InventoryIsEmpty(object inv) {
	return (bool)InventoryWrapper.Inventory_IsEmpty!.Invoke(inv, null)!;
}

private static void spawnItemInInventory(object prefab, object inventory, float? condition, int? quality) {
	object spawner = ItemWrapper.Item_Spawner!.GetValue(null)!;
	EntitySpawnerBinding.EntitySpawner_AddItemToSpawnQueue_Inventory!.Invoke(spawner,
		new object?[] { prefab , inventory, condition, quality, null, false, false, InvSlotType.None});
}

public static void Postfix(context __state) {
	if (__state.do_dump) {
		object parentEntity = InventoryWrapper.Inventory_Owner!.GetValue(__state.inventory)!;
		if (EntityIsItem(parentEntity) && GetParentIdentifier(parentEntity) == box_identifier) {
			if (get_condition(parentEntity)>=increment && InventoryIsEmpty(__state.inventory)) {
				increment_condition(parentEntity, -increment);
				spawnItemInInventory(__state.prefab, __state.inventory, __state.condition, __state.quality);
			}
		}
	}
}
}
*/
}

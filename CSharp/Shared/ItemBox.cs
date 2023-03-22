using Barotrauma;
using HarmonyLib;
using System.Reflection;
using System.Linq;
using Barotrauma.Items.Components;
using System.Collections.Generic;
using System;
using Microsoft.Xna.Framework;
using System.ComponentModel;

namespace BaroMod_sjx
{
	partial class ItemBoxImpl : ACsMod
	{
		const string harmony_id = "com.sjx.ItemIOFramework";
		/*
		const string box_identifier = "ItemBox";
		const float max_condition = 1.0f;
		const int item_count = 1024;
		const float increment = max_condition / item_count;
		*/
		private readonly Harmony harmony;

		public ItemBoxImpl() {
			harmony = new Harmony(harmony_id);
			harmony.PatchAll(Assembly.GetExecutingAssembly());
			Barotrauma.DebugConsole.AddWarning("Loaded ItemBox Impl");
		}

		public override void Stop() {
			harmony.UnpatchAll(harmony_id);
		}

		public static int SlotPreserveCount(ItemPrefab prefab, ItemContainer container, int slot_index) {
			int resolved_stack_size = Math.Min(Math.Min(prefab.MaxStackSize, container.GetMaxStackSize(slot_index)), Inventory.MaxStackSize);
			if (resolved_stack_size <= 1)
			{
				return 1;
			}
			else {
				return resolved_stack_size - 1;
			}
		}

		static Dictionary<Type, ItemComponent> get_componentsByType(Item item) {
			return (AccessTools.Field(typeof(Item),"componentsByType").GetValue(item)! as Dictionary<Type, ItemComponent>)!;
		}

		[HarmonyPatch(typeof(Inventory))]
		class Patch_PutItem
		{

			static MethodBase TargetMethod() {
				Barotrauma.DebugConsole.AddWarning("Patch_PutItem TargetMethod");
				return AccessTools.Method(typeof(Inventory), "PutItem");
			}

			public static bool Prefix(Inventory __instance, out Inventory? __state, int i)
			{
				if (__instance.Owner is Item parentItem && get_componentsByType(parentItem).TryGetValue(typeof(ConditionStorage), out ItemComponent? component))
				{
					if (__instance.AllowSwappingContainedItems) {
						DebugConsole.LogError($"ItemContainer of {(__instance.Owner as Item)!.Prefab.Identifier} with ConditionStorage must have AllowSwappingContainedItems=\"false\"!");
						__state = null;
					}
					else {
						__state = __instance;
					}
				}
				else {
					__state = null;
				}
				return true;
			}
			public static void Postfix(Inventory? __state)
			{
				if (__state != null)
				{
					Item parentItem = (__state.Owner as Item)!;
					ConditionStorage storage_info = parentItem.GetComponent<ConditionStorage>();
					ItemContainer container = parentItem.GetComponent<ItemContainer>();
					Inventory.ItemSlot target_slot;
					{
						Inventory.ItemSlot[] slots = (AccessTools.Field(typeof(Inventory), "slots").GetValue(__state)! as Inventory.ItemSlot[])!;
						if (storage_info.slotIndex >= slots.Length) {
							DebugConsole.LogError($"ConditionStorage of {parentItem.Prefab.Identifier} specified index {storage_info.slotIndex} out of {slots.Length}!");
							return;
						}
						target_slot = slots[storage_info.slotIndex];
					}

					if (!storage_info.isFull() && target_slot.Items.Any())
					{
						//bool edited = false;	
						int preserve = SlotPreserveCount(target_slot.Items.First().Prefab, container, storage_info.slotIndex);
						var it = target_slot.Items.ToArray().AsEnumerable().GetEnumerator();
						while (it.MoveNext() && !storage_info.isFull())
						{
							if (preserve > 0)
							{
								preserve--;
							}
							else
							{
								//edited = true;
								storage_info.currentItemCount++;
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
				public float condition;
				public int quality;
				public ItemPrefab prefab;
				public ConditionStorage info;
				public context(Inventory inv, float cond, int q, ItemPrefab p, ConditionStorage sto)
				{
					inventory = inv;
					condition = cond;
					quality = q;
					prefab = p;
					info = sto;
				}
			};
			public static bool Prefix(Inventory __instance, out context? __state, Item item)
			{
				// do not add items if sub is unloading or if removed for overflow.
				if ((item.ParentInventory != null) && !Submarine.Unloading  && (__instance.Owner is Item parentItem && get_componentsByType(parentItem).TryGetValue(typeof(ConditionStorage), out ItemComponent? comp)))
				{
					if (__instance.AllowSwappingContainedItems)
					{
						DebugConsole.LogError($"ItemContainer of {(__instance.Owner as Item)!.Prefab.Identifier} with ConditionStorage must have AllowSwappingContainedItems=\"false\"!");
						__state = null;
					}
					else
					{
						__state = new context(__instance, item.Condition, item.Quality, item.Prefab, (comp as ConditionStorage)!);
					}
				}
				else {
					__state = null;
				}
				return true;
			}
			public static void Postfix(context? __state)
			{
				if (__state != null)
				{
					ConditionStorage storage_info = __state.info;
					ItemContainer container = (__state.inventory.Owner as Item)!.GetComponent<ItemContainer>();
					Inventory.ItemSlot target_slot;
					{
						Inventory.ItemSlot[] slots = (AccessTools.Field(typeof(Inventory), "slots").GetValue(__state.inventory)! as Inventory.ItemSlot[])!;
						if (storage_info.slotIndex >= slots.Length)
						{
							DebugConsole.LogError($"ConditionStorage of {(__state.inventory.Owner as Item)!.Prefab.Identifier} specified index {storage_info.slotIndex} out of {slots.Length}!");
							return;
						}
						target_slot = slots[storage_info.slotIndex];
					}
					int preserve = SlotPreserveCount(__state.prefab, container, storage_info.slotIndex);
					int spawn_count = preserve - target_slot.Items.Count;
					bool edited = false;
					while (!storage_info.isEmpty() && spawn_count > 0)
					{
						edited = true;
						spawn_count--;
						storage_info.currentItemCount--;
						Item.Spawner.AddItemToSpawnQueue(__state.prefab, __state.inventory, __state.condition, __state.quality, spawnIfInventoryFull: false);
					}

					if (edited && GameMain.NetworkMember != null)
					{
						GameMain.NetworkMember.CreateEntityEvent(__state.inventory.Owner as Item,
							new Item.ChangePropertyEventData(storage_info.SerializableProperties["currentItemCount"], storage_info));
					}
				}
			}
		}
		
		[HarmonyPatch(typeof(Inventory), nameof(Inventory.CreateNetworkEvent))]
		class Patch_CreateNetworkEvent
		{
			public static bool Prefix(Inventory __instance, out Inventory? __state) {
				if (GameMain.NetworkMember != null && 
					__instance.Owner is Item parentItem && get_componentsByType(parentItem).TryGetValue(typeof(ConditionStorage), out ItemComponent? comp))
				{
					__state = __instance;
				}
				else {
					__state = null;
				}
				return true;
			}

			public static void Postfix(Inventory? __state)
			{
				if (__state != null) {
					Item parentItem = (__state.Owner as Item)!;
					ConditionStorage storage = parentItem.GetComponent<ConditionStorage>();
					GameMain.NetworkMember.CreateEntityEvent(__state.Owner as Item, new Item.ChangePropertyEventData(storage.SerializableProperties["currentItemCount"], storage));
				}
			}
		}
	}
	
	class ConditionStorage : ItemComponent {

		[Serialize(0, IsPropertySaveable.No, description: "Index of the stacking slot in same item's ItemContainer component")]
		public int slotIndex { get; private set; }
		[Serialize(1024, IsPropertySaveable.No, description: "Maximum number of items stacked within")]
		public int maxItemCount { get; private set; }

		[Serialize(0.6f, IsPropertySaveable.No, description: "icon scale compared to full")]
		public float iconScale { get; private set; }

		[Serialize(0.0f, IsPropertySaveable.No, description: "shift x of icon")]
		public float iconShiftX { get; private set; }

		[Serialize(0.1f, IsPropertySaveable.No, description: "shift y of icon, down is positive")]
		public float iconShiftY { get; private set; }

		[Editable(minValue:0, maxValue: int.MaxValue), Serialize(0, IsPropertySaveable.Yes, description: "Current item count")]
		public int currentItemCount { get; set; }

		public ConditionStorage(Item item, ContentXElement element) : base(item, element){}

		public bool isFull() {
			
			return currentItemCount >= maxItemCount;
		}

		public bool isEmpty() {
			return currentItemCount <= 0;
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

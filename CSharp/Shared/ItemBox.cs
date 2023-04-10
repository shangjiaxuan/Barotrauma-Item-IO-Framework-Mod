using Barotrauma;
using HarmonyLib;
using System.Reflection;
using System.Linq;
using Barotrauma.Items.Components;
using System.Collections.Generic;
using System;
using Microsoft.Xna.Framework;
using System.ComponentModel;
using Barotrauma.Networking;

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
				if (__instance.Owner is Item parentItem 
					&& get_componentsByType(parentItem).TryGetValue(typeof(ConditionStorage), out ItemComponent? component))
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

					if (target_slot.Items.Any()) {
						storage_info.QualityStacked = target_slot.Items.First().Quality;
						storage_info.ConditionStacked = target_slot.Items.First().Condition;
						storage_info.item_type = target_slot.Items.First().Prefab;
						if (!storage_info.isFull())
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
								else if(Entity.Spawner != null) {
									// client cannot despawn items, single player needs to despawn
									Entity.Spawner.AddItemToRemoveQueue(it.Current);
									++storage_info.currentItemCount;
									storage_info.SetSync();
									storage_info.flag_remove_no_spawn = true;
									__state.RemoveItem(it.Current);
									break;
								}
							}
						}
					}
				}
			}
		}

		[HarmonyPatch(typeof(Inventory), nameof(Inventory.RemoveItem))]
		class Patch_RemoveItem
		{
			public static bool Prefix(Inventory __instance, out ConditionStorage? __state, Item item)
			{

				// do not add items if sub is unloading or if removed for overflow.
				if ((item.ParentInventory != null) && !Submarine.Unloading  
					&& (__instance.Owner is Item parentItem && get_componentsByType(parentItem).TryGetValue(typeof(ConditionStorage), out ItemComponent? comp)))
				{
					ConditionStorage storage_info = (comp as ConditionStorage)!;
					if (storage_info.flag_remove_no_spawn)
					{
						storage_info.flag_remove_no_spawn = false;
						__state = null;
					}
					else {
						storage_info.QualityStacked = item.Quality;
						storage_info.ConditionStacked = item.Condition;
						storage_info.item_type = item.Prefab;
						__state = storage_info;
					}
				}
				else {
					__state = null;
				}
				return true;
			}
			public static void Postfix(ConditionStorage? __state)
			{
				if (__state != null)
				{
					ConditionStorage storage_info = __state!;
					ItemContainer container = (storage_info.parentInventory.Owner as Item)!.GetComponent<ItemContainer>();
					Inventory.ItemSlot target_slot;
					{
						Inventory.ItemSlot[] slots = (AccessTools.Field(typeof(Inventory), "slots").GetValue(storage_info.parentInventory)! as Inventory.ItemSlot[])!;
						if (storage_info.slotIndex >= slots.Length)
						{
							DebugConsole.LogError($"ConditionStorage of {(storage_info.parentInventory.Owner as Item)!.Prefab.Identifier} specified index {storage_info.slotIndex} out of {slots.Length}!");
							return;
						}
						target_slot = slots[storage_info.slotIndex];
					}
				
					int preserve = SlotPreserveCount(storage_info.item_type!, container, storage_info.slotIndex);
					int spawn_count = preserve - target_slot.Items.Count;
					int can_spawn = Math.Min(spawn_count, storage_info.currentItemCount);

					// other may be queued, so spawn only one
					if (can_spawn > 0) {
						if (Entity.Spawner != null) {
							storage_info.SetSync();
							--storage_info.currentItemCount;

							Item.Spawner.AddItemToSpawnQueue(storage_info.item_type, storage_info.parentInventory,
									storage_info.ConditionStacked, storage_info.QualityStacked, spawnIfInventoryFull: true);
						}
					}
				}
			}
		}

		[HarmonyPatch(typeof(Inventory))]
		class Patch_TrySwapping {
			static MethodBase TargetMethod()
			{
				return AccessTools.Method(typeof(Inventory), "TrySwapping");
			}

			public static bool Prefix(Inventory __instance, Item item, ref bool __result)
			{
				if ((__instance.Owner is Item parent && parent.GetComponent<ConditionStorage>() != null) 
					|| (item?.ParentInventory?.Owner is Item other_parent && other_parent.GetComponent<ConditionStorage>() != null))
				{
					__result = false;
					return false;
				}
				return true;
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
					storage.SyncItemCount();
				}
			}
		}
	}
	
	partial class ConditionStorage : ItemComponent
	{

		[Serialize(0, IsPropertySaveable.No, description: "Index of the stacking slot in same item's ItemContainer component")]
		public int slotIndex { get; private set; }

		[Serialize(true, IsPropertySaveable.No, description: "Shows count and percentage of stacking item")]
		public bool showCount { get; private set; }

		[Serialize(1024, IsPropertySaveable.No, description: "Maximum number of items stacked within")]
		public int maxItemCount { get; private set; }

		[Serialize(true, IsPropertySaveable.No, description: "Shows icon of stacking item")]
		public bool showIcon { get; private set; }

		[Serialize(0.6f, IsPropertySaveable.No, description: "icon scale compared to full")]
		public float iconScale { get; private set; }

		[Serialize(0.0f, IsPropertySaveable.No, description: "shift x of icon")]
		public float iconShiftX { get; private set; }

		[Serialize(0.1f, IsPropertySaveable.No, description: "shift y of icon, down is positive")]
		public float iconShiftY { get; private set; }

		[Editable(minValue:0, maxValue: int.MaxValue), Serialize(0, IsPropertySaveable.Yes, description: "Current item count")]
		// camel case needed for save compatibility
		public int currentItemCount { 
			get => _currentItemCount;
			// assume set by 
			set {
				OnCountChanged();
				IsActive = true;
				_currentItemCount = value;
			}
		}

		public int _currentItemCount;

		// replace setting parent container hack, so that harpoon guns work correctly
		public bool flag_remove_no_spawn;
		partial void OnCountChanged();


		[Editable, Serialize("", IsPropertySaveable.Yes, description: "current stacked item")]
		public Identifier ItemIdentifier {
			get { 
				return item_type?.Identifier??"";
			}
			set {
				if (value.IsEmpty)
				{
					item_type = null;
				}
				else {
					item_type = ItemPrefab.Find("", value.ToIdentifier());
				}
			}
		}

		public ItemPrefab? item_type;

		[Editable(MinValueInt = 0, MaxValueInt = Quality.MaxQuality), Serialize(0, IsPropertySaveable.Yes, description: "current stacked item quality")]
		public int QualityStacked { get; set; }

		[Editable, Serialize(float.NaN, IsPropertySaveable.Yes, description: "current stacked item condition")]
		public float ConditionStacked { get; set; }

		public ItemInventory parentInventory { 
			get {
				return Item.OwnInventory;
			}
		}

		public ConditionStorage(Item item, ContentXElement element) : base(item, element) {}

		public bool isFull() {
			return currentItemCount >= maxItemCount;
		}

		public void SetSync() {
			IsActive = true;
		}

		public void SyncItemCount() {
#if SERVER
			Item.CreateServerEvent(this);
#endif
		}

		public override void Update(float deltaTime, Camera cam) {
			base.Update(deltaTime, cam);
			SyncItemCount();
			IsActive = false;
		}

		public bool isEmpty() {
			return currentItemCount <= 0;
		}
	}
}

using Barotrauma;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ItemIOFramework;

/// <summary>
/// 把物品堆叠保存在物品自身的 <see cref="ItemContainer"/> 里，并记录堆叠中的物品数量。
/// 数量以服务端为准并同步给客户端，保证两端显示的数量一致。
/// </summary>
/// <remarks>
/// 该组件通过 <see cref="Barotrauma.Plugins.IItemComponentRegistrar"/> 注册到游戏，
/// 正因如此内容包里才能使用 <c>&lt;ConditionStorage /&gt;</c> 元素。
/// <br/><br/>
/// 数量通过游戏自带的物品组件状态事件同步，客户端与服务端各自实现
/// <see cref="IServerSerializable"/> 的对应方法。
/// </remarks>
public partial class ConditionStorage : ItemComponent, IServerSerializable
{
    [Serialize(0, IsPropertySaveable.No, description: "同一物品的 ItemContainer 组件中用于堆叠的槽位索引")]
    public int slotIndex { get; private set; }

    [Serialize(true, IsPropertySaveable.No, description: "显示堆叠物品的数量和百分比")]
    public bool showCount { get; private set; }

    [Serialize(1024, IsPropertySaveable.No, description: "最多可以堆叠的物品数量")]
    public int maxItemCount { get; private set; }

    [Serialize(true, IsPropertySaveable.No, description: "显示堆叠物品的图标")]
    public bool showIcon { get; private set; }

    [Serialize(0.6f, IsPropertySaveable.No, description: "图标相对于满格的缩放比例")]
    public float iconScale { get; private set; }

    [Serialize(0.0f, IsPropertySaveable.No, description: "图标的水平偏移")]
    public float iconShiftX { get; private set; }

    [Serialize(0.1f, IsPropertySaveable.No, description: "图标的垂直偏移，向下为正")]
    public float iconShiftY { get; private set; }

    [Editable(MinValueInt = 0, MaxValueInt = int.MaxValue), Serialize(0, IsPropertySaveable.Yes, description: "当前的物品数量")]
    // 驼峰命名是为了兼容存档
    public int currentItemCount
    {
        get => _currentItemCount;
        // 读取存档时由游戏赋值
        set => SetItemCount(value, false);
    }

    [Editable, Serialize("", IsPropertySaveable.Yes, description: "当前堆叠的物品")]
    public Identifier ItemIdentifier
    {
        get => itemType?.Identifier ?? Identifier.Empty;
        set
        {
            if (value.IsEmpty)
            {
                itemType = null;
            }
            else
            {
                itemType = ItemPrefab.Find("", value);
            }
        }
    }

    [Editable(MinValueInt = 0, MaxValueInt = Quality.MaxQuality), Serialize(0, IsPropertySaveable.Yes, description: "当前堆叠物品的品质")]
    public int QualityStacked { get; set; }

    [Editable, Serialize(float.NaN, IsPropertySaveable.Yes, description: "当前堆叠物品的磨损度")]
    public float ConditionStacked { get; set; }

    /// <summary>
    /// 当前被堆叠物品的原型。
    /// </summary>
    public ItemPrefab? itemType;

    private int _currentItemCount;

    /// <summary>
    /// 缓存下来的 ItemContainer 组件。
    /// </summary>
    private ItemContainer? _itemContainer;

    /// <summary>
    /// 堆叠槽位索引无效并已报告过时置位，避免同一条错误被反复记录。
    /// </summary>
    private bool invalidSlotIndexReported;

    /// <summary>
    /// 组件正在移除物品时置位，避免移除后立刻又生成一个替代物品。
    /// </summary>
    public bool flag_remove_no_spawn;

    public ConditionStorage(Item item, ContentXElement element) : base(item, element) { }

    public ItemInventory? itemInventory => Item.OwnInventory;

    /// <summary>
    /// 物品自身的 ItemContainer 组件。
    /// </summary>
    /// <remarks>
    /// 物品的组件列表在创建之后就不会再变化，所以这里只查找一次并缓存下来。
    /// <see cref="Item.GetComponent{T}"/> 内部用的是 LINQ，会产生一次额外的内存分配，
    /// 而这个属性在客户端绘制槽位时每帧都会被访问。
    /// </remarks>
    public ItemContainer? itemContainer
    {
        get
        {
            _itemContainer ??= Item.GetComponent<ItemContainer>();
            return _itemContainer;
        }
    }

    public bool IsStackFull => currentItemCount >= maxItemCount;
    public bool IsStackEmpty => currentItemCount <= 0;

    /// <summary>
    /// 把当前物品数量发给客户端。只在服务端有效，客户端上不做任何事。
    /// </summary>
    public void SyncItemCount()
    {
        CreateSyncItemCountEvent();
    }

    partial void CreateSyncItemCountEvent();

    public override void Update(float deltaTime, Camera cam)
    {
        base.Update(deltaTime, cam);
        SyncItemCount();
        IsActive = false;
    }

    private void SetItemCount(int value, bool is_network_event = false)
    {
        if (is_network_event || GameMain.NetworkMember == null || GameMain.NetworkMember.IsServer)
        // 权威数量。如果是服务端，之后还需要把它发给客户端。
        {
            if (value != _currentItemCount)
            {
                OnCountActualChanged();
            }
        }
        // 预测数量，之后会被服务端的值重置。
        else
        {
            if (value != _currentItemCount)
            {
                OnCountPredictionChanged();
            }
        }
        _currentItemCount = value;
        IsActive = true;
    }

    partial void OnCountActualChanged();
    partial void OnCountPredictionChanged();

    /// <summary>
    /// 容器槽位中保持可见的物品数量。其余的物品只计数，不实际显示。
    /// </summary>
    public static int SlotPreserveCount(ItemPrefab prefab, Inventory inventory, ItemContainer container, int slot_index)
    {
        int resolved_stack_size = Math.Min(Math.Min(prefab.GetMaxStackSize(inventory), container.GetMaxStackSize(slot_index)), Inventory.MaxPossibleStackSize);
        if (resolved_stack_size <= 1)
        {
            return 1;
        }
        else
        {
            return resolved_stack_size - 1;
        }
    }

    public static ConditionStorage? GetFromInventory(Inventory inventory)
    {
        if (inventory.Owner is Item parentItem)
        {
            return parentItem.GetComponent<ConditionStorage>();
        }
        else
        {
            return null;
        }
    }

    /// <summary>
    /// 当前存放在堆叠槽位里的物品。
    /// </summary>
    public IReadOnlyList<Item> GetSlotItems()
    {
        ItemInventory? inventory = itemInventory;
        if (inventory == null) { return Array.Empty<Item>(); }

        if (slotIndex < 0 || slotIndex >= inventory.Capacity)
        {
            if (!invalidSlotIndexReported)
            {
                invalidSlotIndexReported = true;
                Plugin.DebugConsole.ThrowError($"ConditionStorage of {Item.Prefab.Identifier} specified index {slotIndex} out of {inventory.Capacity}!");
            }
            return Array.Empty<Item>();
        }

        // 这里拿到的是槽位本身的活动列表，不需要再复制一份
        IEnumerable<Item> slotItems = inventory.GetItemsAt(slotIndex);
        return slotItems as IReadOnlyList<Item> ?? slotItems.ToArray();
    }

    /// <summary>
    /// 当前被堆叠的物品，槽位为空时返回 null。
    /// </summary>
    public Item? GetStackedItem()
    {
        foreach (Item item in GetSlotItems())
        {
            return item;
        }
        return null;
    }

    public bool SlotContains(Item item)
    {
        foreach (Item slotItem in GetSlotItems())
        {
            if (slotItem == item) { return true; }
        }
        return false;
    }

    public void OnPutItemDone(Character? user)
    {
        // 处理过程中槽位会被修改，所以先复制一份再遍历
        IReadOnlyList<Item> targetSlotItems = GetSlotItems().ToArray();
        if (targetSlotItems.Count == 0) { return; }

        Item targetSlotItem = targetSlotItems[0];
        QualityStacked = targetSlotItem.Quality;
        ConditionStacked = targetSlotItem.Condition;
        itemType = targetSlotItem.Prefab;

        if (IsStackFull) { return; }

        ItemInventory? inventory = itemInventory;
        ItemContainer? container = itemContainer;
        ItemPrefab prefab = targetSlotItem.Prefab;
        if (inventory == null || container == null) { return; }

        int preserve = SlotPreserveCount(prefab, inventory, container, slotIndex);
        for (int i = 0; i < targetSlotItems.Count && !IsStackFull; i++)
        {
            if (preserve > 0)
            {
                preserve--;
            }
            else if (Entity.Spawner != null)
            {
                // 客户端无法销毁物品，只有单人游戏需要在这里销毁
                Entity.Spawner.AddItemToRemoveQueue(targetSlotItems[i]);
                SetItemCount(currentItemCount + 1);
                flag_remove_no_spawn = true;
                inventory.RemoveItem(targetSlotItems[i]);
                break;
            }
        }
    }

    public void OnRemoveItemDone()
    {
        IReadOnlyList<Item> targetSlotItems = GetSlotItems();

        ItemInventory? inventory = itemInventory;
        ItemContainer? container = itemContainer;
        ItemPrefab? prefab = itemType;
        if (prefab == null || inventory == null || container == null) { return; }

        int preserve = SlotPreserveCount(prefab, inventory, container, slotIndex);
        int spawn_count = preserve - targetSlotItems.Count;
        int can_spawn = Math.Min(spawn_count, currentItemCount);

        // 其余的可能会进入生成队列，所以这里只生成一个
        if (can_spawn > 0 && Entity.Spawner != null)
        {
            SetItemCount(currentItemCount - 1);

            float? condition = float.IsNaN(ConditionStacked) ? null : ConditionStacked;
            Entity.Spawner.AddItemToSpawnQueue(prefab, inventory, condition, QualityStacked, spawnIfInventoryFull: true);
        }
    }
}

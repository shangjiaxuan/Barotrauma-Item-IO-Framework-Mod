using Barotrauma;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using System.Diagnostics.CodeAnalysis;

namespace ItemIOFramework;

public partial class ConditionStorage
{
    /// <summary>
    /// 发送给客户端的物品数量。
    /// </summary>
    private readonly struct EventData : ItemComponent.IEventData
    {
        public readonly int ItemCount;

        public EventData(int itemCount)
        {
            ItemCount = itemCount;
        }
    }

    /// <summary>
    /// 服务端是唯一知道真实堆叠数量的一方。
    /// </summary>
    public override IEventData ServerGetEventData() => new EventData(currentItemCount);

    /// <summary>
    /// 确认事件数据确实是本组件自己的数据，避免写错格式。
    /// </summary>
    public override bool ValidateEventData(NetEntityEvent.IData data)
        => TryExtractEventData<EventData>(data, out _);

    partial void CreateSyncItemCountEvent()
    {
        if (Item.Removed) { return; }

        Item.CreateServerEvent(this, new EventData(currentItemCount));
    }

    public void ServerEventWrite(IWriteMessage msg, Client c, [AllowNull] NetEntityEvent.IData extraData = null)
    {
        EventData eventData = ExtractEventData<EventData>(extraData);
        msg.WriteRangedInteger(eventData.ItemCount, 0, maxItemCount);
    }
}
using Barotrauma;
using Barotrauma.Networking;

namespace ItemIOFramework;

public partial class ConditionStorage
{
    /// <summary>
    /// 最后一次收到的服务端消息的时间戳，用来丢弃过期的事件。
    /// </summary>
    private float lastUpdateTime;

    /// <summary>
    /// 读取服务端发来的权威数量。
    /// </summary>
    public void ClientEventRead(IReadMessage msg, float sendingTime)
    {
        int count = msg.ReadRangedInteger(0, maxItemCount);

        if (lastUpdateTime > sendingTime)
        {
            // 过期的消息，数量要丢弃，但仍然必须从流里读出来
            return;
        }

        lastUpdateTime = sendingTime;
        SetItemCount(count, true);
    }
}
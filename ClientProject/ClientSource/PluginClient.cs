using Barotrauma;
using Barotrauma.Plugins;

namespace ItemIOFramework;

public partial class Plugin
{
    public partial void InitProjectSpecific()
    {
        // 游戏会在绘制背包槽位和创建提示框时调用这两个钩子
        HookService.RegisterHook<PluginDrawSlotForegroundDelegate>(InventorySlotHooks.OnDrawSlotForeground);
        HookService.RegisterHook<PluginItemSlotGetTooltipDelegate>(InventorySlotHooks.OnItemSlotGetTooltip);
    }
}
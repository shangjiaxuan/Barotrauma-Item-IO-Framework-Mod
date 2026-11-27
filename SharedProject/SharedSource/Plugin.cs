using Barotrauma;
using Barotrauma.Plugins;
using Microsoft.Xna.Framework;

namespace ItemIOFramework;

public partial class Plugin : IBarotraumaPlugin
{
    private static IDebugConsole? debugConsole;
    private static IItemComponentRegistrar? itemComponentRegistrar;
    private static IHarmonyProvider? harmonyProvider;
    private static ISimpleHookService? hookService;

    /// <summary>
    /// 插件的调试控制台。
    /// </summary>
    public static IDebugConsole DebugConsole => debugConsole ??= PluginServiceProvider.GetService<IDebugConsole>();

    /// <summary>
    /// 插件的物品组件注册器。自定义物品组件必须在内容加载之前注册，
    /// 否则游戏无法从内容包里解析它们。
    /// </summary>
    public static IItemComponentRegistrar ItemComponentRegistrar => itemComponentRegistrar ??= PluginServiceProvider.GetService<IItemComponentRegistrar>();

    /// <summary>
    /// 提供插件的 Harmony 实例。通过它打的所有补丁会在插件卸载时自动移除。
    /// </summary>
    public static IHarmonyProvider HarmonyProvider => harmonyProvider ??= PluginServiceProvider.GetService<IHarmonyProvider>();

    /// <summary>
    /// 用于注册游戏在特定时机调用的回调。钩子会在插件卸载时自动移除。
    /// </summary>
    public static ISimpleHookService HookService => hookService ??= PluginServiceProvider.GetService<ISimpleHookService>();

    public void Init()
    {
        // 自定义物品组件必须在内容加载之前注册，
        // 否则游戏无法从内容包里解析它们
        ItemComponentRegistrar.RegisterItemComponent<ConditionStorage>();

        HarmonyProvider.PatchAll();
        DebugConsole.NewMessage("ItemIOFramework loaded!", Color.Lime);
        InitProjectSpecific();
    }

    public partial void InitProjectSpecific();

    public void Dispose()
    {
        // 补丁和钩子会在插件卸载时自动移除
        DebugConsole.NewMessage("ItemIOFramework disposed!", Color.Lime);
    }

    public void OnContentLoaded()
    {
    }
}

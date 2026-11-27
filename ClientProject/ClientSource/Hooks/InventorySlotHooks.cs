using Barotrauma;
using Barotrauma.Items.Components;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace ItemIOFramework;

/// <summary>
/// 在使用 <see cref="ConditionStorage"/> 的物品的背包槽位上绘制堆叠指示器，
/// 并在物品的提示框里显示堆叠的数量。
/// </summary>
/// <remarks>
/// 游戏提供了一个钩子，会在绘制背包槽位时、物品本身画好之后被调用。
/// 用这个钩子代替给 <see cref="Inventory.DrawSlot"/> 打补丁，可以避免游戏更新该方法后插件失效。
/// </remarks>
internal static class InventorySlotHooks
{
    /// <summary>
    /// 原版的指示器绘制方法是私有的，只能通过反射调用。
    /// 委托只在加载时创建一次，避免每帧都做一次反射调用。
    /// </summary>
    private static readonly Action<SpriteBatch, Inventory, Sprite, Sprite, Rectangle, float, bool>? drawItemStateIndicator =
        CreateDrawItemStateIndicatorDelegate();

    /// <summary>
    /// 已经报告过"缺少 ItemContainer"的物品。
    /// </summary>
    private static readonly HashSet<ushort> itemsReportedWithoutContainer = new HashSet<ushort>();

    /// <summary>
    /// 指示器使用的样式。样式对象在内容重新加载后会被替换，
    /// 靠比较引用就能知道缓存是否还有效。
    /// </summary>
    private static GUIComponentStyle? cachedIndicatorStyle;

    private static Sprite? cachedIndicatorSprite;
    private static Sprite? cachedEmptyIndicatorSprite;

    /// <summary>
    /// 提示框里显示堆叠数量时用的文本键。
    /// </summary>
    private static readonly Identifier stackedCountTextKey = "stackbox.stackedcount".ToIdentifier();

    private static Action<SpriteBatch, Inventory, Sprite, Sprite, Rectangle, float, bool>? CreateDrawItemStateIndicatorDelegate()
    {
        MethodInfo? method = AccessTools.Method(typeof(Inventory), "DrawItemStateIndicator",
            new[] { typeof(SpriteBatch), typeof(Inventory), typeof(Sprite), typeof(Sprite), typeof(Rectangle), typeof(float), typeof(bool) });
        if (method == null) { return null; }

        try
        {
            return (Action<SpriteBatch, Inventory, Sprite, Sprite, Rectangle, float, bool>)
                method.CreateDelegate(typeof(Action<SpriteBatch, Inventory, Sprite, Sprite, Rectangle, float, bool>));
        }
        catch (Exception)
        {
            // 绑定失败时退回使用简单的进度条
            return null;
        }
    }

    private static Sprite? GetTargetSprite(ConditionStorage conditionStorage)
    {
        Item? stackedItem = conditionStorage.GetStackedItem();
        if (stackedItem == null) { return null; }

        return stackedItem.Prefab.InventoryIcon ?? stackedItem.Sprite;
    }

    /// <summary>
    /// 绘制背包槽位的前景时被调用。
    /// </summary>
    internal static void OnDrawSlotForeground(SpriteBatch spriteBatch, Inventory inventory, VisualSlot slot,
        Item item, int slotIndex, bool drawItem, InvSlotType type)
    {
        if (inventory == null || item == null) { return; }

        ConditionStorage? conditionStorage = item.GetComponent<ConditionStorage>();
        if (conditionStorage == null || (!conditionStorage.showIcon && !conditionStorage.showCount))
        {
            return;
        }

        Rectangle rect = slot.Rect;
        rect.Location += slot.DrawOffset.ToPoint();

        if (slot.HighlightColor.A > 0)
        {
            float inflateAmount = (slot.HighlightColor.A / 255.0f) * slot.HighlightScaleUpAmount * 0.5f;
            rect.Inflate(rect.Width * inflateAmount, rect.Height * inflateAmount);
        }

        // 直接用存储组件缓存好的 ItemContainer，避免每帧再做一次组件查找
        ItemContainer? itemContainer = conditionStorage.itemContainer;

        Sprite? indicatorSprite = null;
        Sprite? emptyIndicatorSprite = null;
        Rectangle conditionIndicatorArea = new Rectangle();

        if (conditionStorage.showCount)
        {
            if (itemContainer == null)
            {
                // 槽位每帧都会重画，所以每个物品只报告一次错误
                if (itemsReportedWithoutContainer.Add(item.ID))
                {
                    Plugin.DebugConsole.ThrowError($"Item {item.Prefab.Identifier} has ConditionStorage but no ItemContainer!");
                }
                return;
            }

            if (itemContainer.InventoryTopSprite != null || itemContainer.InventoryBottomSprite != null)
            {
                return;
            }

            int dir = slot.SubInventoryDir;

            if (itemContainer.ShowContainedStateIndicator)
            {
                conditionIndicatorArea = new Rectangle(rect.X, rect.Bottom - (int)(10 * GUI.Scale), rect.Width, (int)(10 * GUI.Scale));
            }
            else
            {
                conditionIndicatorArea = new Rectangle(
                    rect.X, dir < 0 ? rect.Bottom + HUDLayoutSettings.Padding / 2 : rect.Y - HUDLayoutSettings.Padding / 2 - Inventory.ContainedIndicatorHeight,
                    rect.Width, Inventory.ContainedIndicatorHeight);
                conditionIndicatorArea.Inflate(-4, 0);
            }

            GUIComponentStyle? indicatorStyle = GUIStyle.GetComponentStyle("ContainedStateIndicator.Default");

            // GetDefaultSprite/GetSprite 内部用了 LINQ，每次调用都会产生一次内存分配，
            // 而这里每个槽位每帧都会调用，所以把结果缓存下来
            if (!ReferenceEquals(indicatorStyle, cachedIndicatorStyle))
            {
                cachedIndicatorStyle = indicatorStyle;
                cachedIndicatorSprite = indicatorStyle?.GetDefaultSprite();
                cachedEmptyIndicatorSprite = indicatorStyle?.GetSprite(GUIComponent.ComponentState.Hover);
            }

            indicatorSprite = cachedIndicatorSprite;
            emptyIndicatorSprite = cachedEmptyIndicatorSprite;
        }

        if (conditionStorage.showIcon)
        {
            DrawStackedItemIcon(spriteBatch, slot, item, conditionStorage, rect);
        }

        if (!conditionStorage.showCount || indicatorSprite == null || emptyIndicatorSprite == null) { return; }

        float state = conditionStorage.maxItemCount > 0
            ? conditionStorage.currentItemCount / (float)conditionStorage.maxItemCount
            : 0.0f;
        DrawItemStateIndicator(spriteBatch, inventory, indicatorSprite, emptyIndicatorSprite, conditionIndicatorArea, state);

        string info_text = $"{conditionStorage.currentItemCount}/{conditionStorage.maxItemCount}";
        float text_scale = 0.75f;
        Vector2 info_size = GUIStyle.SmallFont.MeasureString(info_text) * text_scale;
        GUIStyle.SmallFont.DrawString(spriteBatch, info_text,
            conditionIndicatorArea.Center.ToVector2() - (info_size * 0.5f),
            Color.White, 0.0f, Vector2.Zero, text_scale, SpriteEffects.None, 0.0f);
    }

    /// <summary>
    /// 创建背包槽位中物品的提示框时被调用。
    /// </summary>
    internal static void OnItemSlotGetTooltip(ref LocalizedString tooltip, Item item, IEnumerable<Item> itemsInSlot, Character character)
    {
        if (item == null) { return; }

        ConditionStorage? conditionStorage = item.GetComponent<ConditionStorage>();
        if (conditionStorage == null) { return; }

        LocalizedString countText = TextManager.GetFormatted(stackedCountTextKey,
            conditionStorage.currentItemCount, conditionStorage.maxItemCount);

        tooltip += "\n" + countText;
    }

    /// <summary>
    /// 把当前被堆叠物品的图标画在容器上方。
    /// </summary>
    private static void DrawStackedItemIcon(SpriteBatch spriteBatch, VisualSlot slot, Item item,
        ConditionStorage conditionStorage, Rectangle rect)
    {
        Sprite? itemSprite = GetTargetSprite(conditionStorage);
        if (itemSprite == null) { return; }

        float scale = Math.Min(Math.Min((rect.Width - 10) / itemSprite.size.X, (rect.Height - 10) / itemSprite.size.Y), 2.0f);

        float rotation = 0.0f;
        if (slot.HighlightColor.A > 0)
        {
            rotation = (float)Math.Sin(slot.HighlightTimer * MathHelper.TwoPi) * slot.HighlightTimer * 0.3f;
        }

        Color spriteColor = itemSprite == item.Sprite ? item.GetSpriteColor() : item.GetInventoryIconColor();

        Vector2 center = rect.Center.ToVector2()
            + (new Vector2(conditionStorage.iconShiftX, conditionStorage.iconShiftY)) * slot.Rect.Size.ToVector2() * 0.5f;

        // 槽位位于屏幕底部时，原版会把物品贴图往上移，
        // 堆叠物品的图标也要跟着移动才能保持对齐
        if (center.Y > GameMain.GraphicsHeight)
        {
            center.Y -= Math.Min(
                (center.Y + itemSprite.size.Y / 2 * scale) - GameMain.GraphicsHeight,
                (center.Y - itemSprite.size.Y / 2 * scale) - rect.Y);
        }

        itemSprite.Draw(spriteBatch, center, spriteColor, rotation, scale * conditionStorage.iconScale);
    }

    /// <summary>
    /// 绘制和原版给容器物品一样的状态指示器，只是里面表示的是堆叠数量而不是容器内物品的磨损度。
    /// </summary>
    private static void DrawItemStateIndicator(SpriteBatch spriteBatch, Inventory inventory,
        Sprite indicatorSprite, Sprite emptyIndicatorSprite, Rectangle containedIndicatorArea, float containedState)
    {
        if (drawItemStateIndicator != null)
        {
            drawItemStateIndicator(spriteBatch, inventory, indicatorSprite, emptyIndicatorSprite, containedIndicatorArea, containedState, false);
            return;
        }

        // 找不到原版方法时，退回到画一条简单的进度条
        containedIndicatorArea.Inflate(0, -2);
        GUI.DrawRectangle(spriteBatch, containedIndicatorArea, GUIStyle.ColorInventoryBackground, true);
        GUI.DrawRectangle(spriteBatch,
            new Rectangle(containedIndicatorArea.X, containedIndicatorArea.Y,
                (int)(containedIndicatorArea.Width * containedState), containedIndicatorArea.Height),
            ToolBox.GradientLerp(containedState, GUIStyle.ColorInventoryEmpty, GUIStyle.ColorInventoryHalf, GUIStyle.ColorInventoryFull) * 0.8f, true);
    }
}

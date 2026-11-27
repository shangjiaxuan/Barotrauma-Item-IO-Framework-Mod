using Barotrauma;
using HarmonyLib;
using System.Reflection;

namespace ItemIOFramework;

/// <summary>
/// 让物品的 <see cref="ConditionStorage"/> 与其容器内容保持同步的 Harmony 补丁。
/// 这些补丁在客户端和服务端上都会生效。
/// </summary>
internal static class InventoryPatches
{
    /// <summary>
    /// 通知存储组件：有物品被放进了它的槽位里。
    /// </summary>
    [HarmonyPatch]
    internal static class PutItemPatch
    {
        public sealed class Context
        {
            public readonly ConditionStorage Target;
            public readonly Character? User;

            public Context(ConditionStorage target, Character? user)
            {
                Target = target;
                User = user;
            }
        }

        private static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(Inventory), "PutItem",
                new[] { typeof(Item), typeof(int), typeof(Character), typeof(bool), typeof(bool), typeof(bool) });

        public static bool Prefix(Inventory __instance, Character? user, int i, out Context? __state)
        {
            __state = null;
            ConditionStorage? target = ConditionStorage.GetFromInventory(__instance);
            if (target != null && i == target.slotIndex)
            {
                __state = new Context(target, user);
            }
            return true;
        }

        public static void Postfix(Context? __state)
        {
            if (__state != null)
            {
                __state.Target.OnPutItemDone(__state.User);
            }
        }
    }

    /// <summary>
    /// 通知存储组件：有物品被从它的槽位里取走了。
    /// </summary>
    [HarmonyPatch]
    internal static class RemoveItemPatch
    {
        private static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(Inventory), "RemoveItem", new[] { typeof(Item) });

        public static bool Prefix(Inventory __instance, out ConditionStorage? __state, Item item)
        {
            __state = null;
            // 潜艇正在卸载，或者物品是因为溢出而被移除时，不要生成替代物品。
            if (!Submarine.Unloading)
            {
                ConditionStorage? target = ConditionStorage.GetFromInventory(__instance);
                if (target != null)
                {
                    if (target.SlotContains(item))
                    {
                        if (target.flag_remove_no_spawn)
                        {
                            target.flag_remove_no_spawn = false;
                        }
                        else
                        {
                            target.QualityStacked = item.Quality;
                            target.ConditionStacked = item.Condition;
                            target.itemType = item.Prefab;
                            __state = target;
                        }
                    }
                }
            }
            return true;
        }

        public static void Postfix(ConditionStorage? __state)
        {
            if (__state != null)
            {
                __state.OnRemoveItemDone();
            }
        }
    }

    /// <summary>
    /// 禁止在使用 <see cref="ConditionStorage"/> 的容器里交换物品。
    /// </summary>
    [HarmonyPatch]
    internal static class TrySwappingPatch
    {
        private static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(Inventory), "TrySwapping",
                new[] { typeof(int), typeof(Item), typeof(Character), typeof(bool), typeof(bool), typeof(bool) });

        public static bool Prefix(Inventory __instance, Item? item, ref bool __result)
        {
            if (ConditionStorage.GetFromInventory(__instance) != null ||
                (item != null && item.ParentInventory != null && ConditionStorage.GetFromInventory(item.ParentInventory) != null))
            {
                __result = false;
                return false;
            }
            return true;
        }
    }
}

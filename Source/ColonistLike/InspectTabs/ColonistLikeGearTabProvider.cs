using System;
using RimWorld;

namespace MAP_MechanoidMechanitor
{
    /// <summary>只影响动态补齐的装备页，不改写 Def、共享列表或已有第三方页签。</summary>
    internal static class ColonistLikeGearTabProvider
    {
        internal static Type TabType { get; private set; } = typeof(ITab_Pawn_Gear);

        internal static void Register(Type tabType)
        {
            if (!typeof(ITab_Pawn_Gear).IsAssignableFrom(tabType) || tabType.IsAbstract)
                throw new ArgumentException("装备页类型必须是可实例化的 ITab_Pawn_Gear 子类。", nameof(tabType));
            if (TabType != typeof(ITab_Pawn_Gear) && TabType != tabType)
                throw new InvalidOperationException("已有其他兼容模块注册动态装备页类型。");
            TabType = tabType;
        }

        internal static void Unregister(Type tabType)
        {
            if (TabType == tabType)
                TabType = typeof(ITab_Pawn_Gear);
        }
    }
}

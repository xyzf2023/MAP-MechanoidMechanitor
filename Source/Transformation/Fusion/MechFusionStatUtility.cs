using System;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 窄范围的实例 Stat 提供入口。只有固定合体服装与存在有效活动合体记录
    /// 的人类才会读取快照；其他对象立即走原版路径。
    /// </summary>
    internal static class MechFusionStatUtility
    {
        private const float Epsilon = 0.0001f;

        internal static bool IsArmorStat(StatDef? stat)
        {
            return stat == StatDefOf.ArmorRating_Sharp
                || stat == StatDefOf.ArmorRating_Blunt
                || stat == StatDefOf.ArmorRating_Heat;
        }

        /// <summary>
        /// 合体快照中的专属工作速度由合体外甲作为装备属性提供。
        /// MoveSpeed 虽然同样以 Speed 结尾，但仍由最终值补丁强制覆盖，
        /// 因此必须排除。原版的机械师专用工作速度位于 Mechanitor 分类，
        /// 其余工作速度位于 PawnWork 分类；分类与名称双重限制避免误接管。
        /// </summary>
        internal static bool IsApparelWorkSpeedStat(StatDef? stat)
        {
            if (stat == null
                || stat == StatDefOf.MoveSpeed
                || stat.defName.IndexOf("Speed", StringComparison.Ordinal) < 0)
            {
                return false;
            }

            return stat.category == StatCategoryDefOf.PawnWork
                || stat.category?.defName == "Mechanitor";
        }

        internal static void ApplyToPawn(
            Pawn pawn,
            StatDef stat,
            ref float value)
        {
            if (pawn == null
                || stat == null
                || IsArmorStat(stat)
                || !GameComponent_MechFusionSessionRegistry.TryGetSessionForWearer(
                    pawn,
                    out MechFusionSession? session)
                || session == null
                || !session.IsActive)
            {
                return;
            }

            if (stat == StatDefOf.MoveSpeed || stat == StatDefOf.Mass)
            {
                // MoveSpeed 与 Mass 都在 StatWorker.FinalizeValue 后单独处理。
                // Mass 必须保持为“人类正常最终重量 + 源机械族当前重量”，
                // 不能再次套用源机械族 Hediff 的通用偏移/倍率。
                return;
            }

            // 专属工作速度已经由 StatOffsetFromGear 从合体外甲读取。
            // 此处不得再次添加，否则同一份快照会被结算两次。
            if (!IsApparelWorkSpeedStat(stat)
                && session.TryGetStatOffset(stat, out float offset)
                && Math.Abs(offset) > Epsilon)
            {
                value += offset;
            }

            if (session.TryGetStatFactor(stat, out float factor)
                && Math.Abs(factor - 1f) > Epsilon)
            {
                value *= factor;
            }
        }

        internal static bool TryGetForcedMoveSpeed(
            Pawn pawn,
            out float value)
        {
            value = 0f;
            if (pawn == null
                || !GameComponent_MechFusionSessionRegistry.TryGetSessionForWearer(
                    pawn,
                    out MechFusionSession? session)
                || session == null
                || !session.IsActive)
            {
                return false;
            }

            value = GetForcedMoveSpeed(session);
            return true;
        }

        internal static float GetForcedMoveSpeed(MechFusionSession session)
        {
            float baseSpeed = session.MoveSpeedBase;
            if (baseSpeed <= Epsilon && session.SourceThingDef != null)
            {
                // 旧版活动会话没有可靠的 moveSpeedBase 时，从保存的源 Def
                // 恢复基础速度；新会话仍使用合体开始时写入的快照。
                baseSpeed = session.SourceThingDef.GetStatValueAbstract(
                    StatDefOf.MoveSpeed);
            }

            session.TryGetStatOffset(StatDefOf.MoveSpeed, out float offset);
            float factor = session.TryGetStatFactor(
                StatDefOf.MoveSpeed,
                out float storedFactor)
                    ? storedFactor
                    : 1f;
            return Math.Max(0f, (baseSpeed + offset) * factor);
        }

        /// <summary>
        /// 返回合体状态下应额外加入人类最终 Mass 的源机械族重量。
        /// 源机械族真实 Pawn 在合体期间始终由会话保留，因此直接读取其当前
        /// 最终 Mass；机械族主武器已在合体开始时转移给人类，不会重复计重。
        /// 这里只增加 Pawn 总重量，不接入 GearAndInventoryMass，因此不会把
        /// 机械体自身重量误判成人类携带负重。
        /// </summary>
        internal static bool TryGetFusionMassContribution(
            Pawn pawn,
            out float value)
        {
            value = 0f;
            if (pawn == null
                || !GameComponent_MechFusionSessionRegistry.TryGetSessionForWearer(
                    pawn,
                    out MechFusionSession? session)
                || session == null
                || !session.IsActive)
            {
                return false;
            }

            Pawn? source = session.SourcePawn;
            if (source != null
                && !source.Destroyed
                && !source.Discarded
                && !source.Dead)
            {
                value = Math.Max(0f, source.GetStatValue(StatDefOf.Mass));
                return true;
            }

            if (session.SourceThingDef != null)
            {
                // 异常/旧存档降级路径：真实源 Pawn 不可读取时至少保留 Def 基础重量。
                value = Math.Max(
                    0f,
                    session.SourceThingDef.GetStatValueAbstract(StatDefOf.Mass));
                return true;
            }

            return false;
        }

        internal static void ApplyToApparel(
            Apparel apparel,
            StatDef stat,
            ref float value)
        {
            if (apparel == null || stat == null || !IsArmorStat(stat))
            {
                return;
            }

            CompMechFusionShell? shellComp =
                apparel.TryGetComp<CompMechFusionShell>();
            if (shellComp == null
                || !GameComponent_MechFusionSessionRegistry.TryGetSessionById(
                    shellComp.SessionId,
                    out MechFusionSession? session)
                || session == null
                || !session.IsActive)
            {
                return;
            }

            if (stat == StatDefOf.ArmorRating_Sharp)
            {
                value = session.ArmorSharp;
            }
            else if (stat == StatDefOf.ArmorRating_Blunt)
            {
                value = session.ArmorBlunt;
            }
            else
            {
                value = session.ArmorHeat;
            }
        }
    }
}

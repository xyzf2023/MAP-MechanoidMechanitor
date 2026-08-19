using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// Biotech 相关人口限制：收养、成长槽胚胎、自然生育。
    /// 这些类型（Designator_Adopt / Building_GrowthVat / HumanEmbryo）属于 Biotech DLC，
    /// 本项目编译期不可见，因此统一用字符串 HarmonyPatch + 反射访问成员，
    /// 避免直接依赖 DLC 程序集；DLC 未启用时对应补丁不会应用。
    /// </summary>
    public static class MechanoidMechanitorPurgeDirective_BiotechPopulationPatches
    {
        // HumanEmbryo 的 Mother 属性运行时反射获取。
        private static Pawn? GetEmbryoMother(object embryo)
        {
            if (embryo == null)
            {
                return null;
            }

            PropertyInfo? motherProp = embryo.GetType().GetProperty("Mother");
            return motherProp?.GetValue(embryo) as Pawn;
        }

        /// <summary>
        /// 收养 UI 禁用：会新增禁止的玩家自由血肉人口时，取消 CanDesignateThing 通过。
        /// </summary>
        [HarmonyPatch("RimWorld.Designator_Adopt", "CanDesignateThing")]
        public static class
            MechanoidMechanitorPurgeDirective_DesignatorAdopt_CanDesignateThing_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(
                Thing t,
                ref AcceptanceReport __result)
            {
                if (!__result.Accepted
                    || !(t is Pawn pawn)
                    || !MechanoidMechanitorPurgeDirectivePopulationPolicy
                        .WouldAddForbiddenFreeColonist(
                            pawn,
                            Faction.OfPlayerSilentFail))
                {
                    return;
                }

                __result =
                    MechanoidMechanitorPurgeDirectivePopulationPolicy.BlockReason;
            }
        }

        /// <summary>
        /// 收养硬保险：真正会新增禁止人口时阻止 DesignateThing。只禁止“收养”，不阻止婴儿生成。
        /// </summary>
        [HarmonyPatch("RimWorld.Designator_Adopt", "DesignateThing")]
        public static class
            MechanoidMechanitorPurgeDirective_DesignatorAdopt_DesignateThing_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(Thing t)
            {
                if (t is Pawn pawn
                    && MechanoidMechanitorPurgeDirectivePopulationPolicy
                        .WouldAddForbiddenFreeColonist(
                            pawn,
                            Faction.OfPlayerSilentFail))
                {
                    Messages.Message(
                        MechanoidMechanitorPurgeDirectivePopulationPolicy.BlockReason,
                        pawn,
                        MessageTypeDefOf.RejectInput);
                    return false;
                }

                return true;
            }
        }

        /// <summary>
        /// 成长槽胚胎：阻止玩家殖民地通过成长槽培育新的血肉人口。
        /// 不删除/销毁胚胎，不阻止已有 Pawn 进入成长槽加速成长。
        /// </summary>
        [HarmonyPatch("RimWorld.Building_GrowthVat", "SelectEmbryo")]
        public static class
            MechanoidMechanitorPurgeDirective_GrowthVat_SelectEmbryo_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(object __instance, object embryo)
            {
                if (!ModsConfig.BiotechActive)
                {
                    return true;
                }

                Thing? vat = __instance as Thing;
                if (vat == null || vat.Faction != Faction.OfPlayer)
                {
                    return true;
                }

                Pawn? geneticMother = GetEmbryoMother(embryo);
                if (MechanoidMechanitorPurgeDirectivePopulationPolicy
                        .WouldCreateForbiddenFleshFromEmbryo(geneticMother))
                {
                    Messages.Message(
                        MechanoidMechanitorPurgeDirectivePopulationPolicy.BlockReason,
                        vat,
                        MessageTypeDefOf.RejectInput);
                    return false;
                }

                return true;
            }
        }

        /// <summary>
        /// 自然生育：肃清约束期间，把玩家血肉女性的自然受孕概率设为 0。
        /// 不影响动物繁殖，不移除已有 Pregnancy，不在分娩流程做任何修改。
        /// 已存在 Pregnancy 由现有肃清协议继续处理。
        /// 注意：PregnancyUtility 为核心类型，可直接引用；仓库中已有另一个
        /// Patch_PregnancyUtility_PregnancyChanceForPartners（合成伴侣），二者可并存。
        /// </summary>
        [HarmonyPatch(
            typeof(PregnancyUtility),
            nameof(PregnancyUtility.PregnancyChanceForPartners))]
        public static class
            MechanoidMechanitorPurgeDirective_PregnancyChanceForPartners_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Pawn woman, ref float __result)
            {
                if (__result <= 0f)
                {
                    return;
                }

                if (!MechanoidMechanitorPurgeDirectivePopulationPolicy
                        .RestrictionActive)
                {
                    return;
                }

                Faction? player = Faction.OfPlayerSilentFail;
                if (woman != null
                    && player != null
                    && woman.Faction == player
                    && MechanoidMechanitorPurgeDirectivePopulationPolicy
                        .IsFleshHumanlike(woman))
                {
                    __result = 0f;
                }
            }
        }
    }
}

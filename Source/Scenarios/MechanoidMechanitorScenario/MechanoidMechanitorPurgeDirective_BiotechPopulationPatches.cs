using System.Collections.Generic;
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

        /// <summary>
        /// 胚胎代孕植入（根源入口）：阻止玩家创建新的 ImplantEmbryo 手术 Bill。
        /// 仅当胚胎会生成被禁止的血肉 Humanlike 时，给原版 AcceptanceReport 追加拒绝原因，
        /// 不覆盖原版已有的拒绝（如未满 16 岁、已怀孕、已被预约等）。
        /// </summary>
        [HarmonyPatch("RimWorld.HumanEmbryo", "CanImplantReport")]
        public static class
            MechanoidMechanitorPurgeDirective_HumanEmbryo_CanImplantReport_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(
                object __instance,
                ref AcceptanceReport __result)
            {
                if (!ModsConfig.BiotechActive)
                {
                    return;
                }

                if (!MechanoidMechanitorPurgeDirectivePopulationPolicy
                        .RestrictionActive)
                {
                    return;
                }

                // 原版已拒绝时保留原因为准。
                if (!__result.Accepted)
                {
                    return;
                }

                Pawn? geneticMother = GetEmbryoMother(__instance);
                if (MechanoidMechanitorPurgeDirectivePopulationPolicy
                        .WouldCreateForbiddenFleshFromEmbryo(geneticMother))
                {
                    __result =
                        MechanoidMechanitorPurgeDirectivePopulationPolicy.BlockReason;
                }
            }
        }

        /// <summary>
        /// 已排队 ImplantEmbryo Bill 的底层保险。
        /// 关键点：Toils_Recipe 在 ApplyOnPawn 之前已经 ConsumeIngredients，
        /// 因此若在 ApplyOnPawn 直接 return false，胚胎会被当作手术材料消耗掉（D5 明令禁止）。
        /// 选择在 CompletableEver（医生派工之前、尚未消耗任何材料）返回 false，
        /// 使该植入 Bill 永不可被执行，从而安全保留胚胎。
        /// 仅对 ImplantEmbryo 这一种 recipe 生效，不影响其他手术。
        /// </summary>
        [HarmonyPatch("RimWorld.Recipe_ImplantEmbryo", "CompletableEver")]
        public static class
            MechanoidMechanitorPurgeDirective_RecipeImplantEmbryo_CompletableEver_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(Pawn surgeryTarget, ref bool __result)
            {
                if (!ModsConfig.BiotechActive)
                {
                    return true;
                }

                if (!MechanoidMechanitorPurgeDirectivePopulationPolicy
                        .RestrictionActive)
                {
                    return true;
                }

                if (surgeryTarget?.BillStack == null)
                {
                    return true;
                }

                List<Bill> bills = surgeryTarget.BillStack.Bills;
                for (int i = 0; i < bills.Count; i++)
                {
                    Bill bill = bills[i];
                    if (bill?.recipe == null
                        || bill.recipe.defName != "ImplantEmbryo")
                    {
                        continue;
                    }

                    // uniqueRequiredIngredients 仅存在于 Bill_Medical。
                    if (bill.GetType().FullName != "RimWorld.Bill_Medical")
                    {
                        continue;
                    }

                    FieldInfo? uniqueField = bill.GetType().GetField(
                        "uniqueRequiredIngredients",
                        BindingFlags.Public | BindingFlags.Instance);
                    if (uniqueField?.GetValue(bill) is not System.Collections.IEnumerable uniqueList)
                    {
                        continue;
                    }

                    foreach (object ingredient in uniqueList)
                    {
                        if (ingredient == null
                            || ingredient.GetType().FullName != "RimWorld.HumanEmbryo")
                        {
                            continue;
                        }

                        if (MechanoidMechanitorPurgeDirectivePopulationPolicy
                                .WouldCreateForbiddenFleshFromEmbryo(
                                    GetEmbryoMother(ingredient)))
                        {
                            __result = false;
                            return false;
                        }
                    }
                }

                return true;
            }
        }
    }
}

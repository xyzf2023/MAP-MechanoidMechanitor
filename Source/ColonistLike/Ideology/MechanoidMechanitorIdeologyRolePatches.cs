using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// Full 模式下文化角色候选、分配与失去角色心情安全。
    /// </summary>
    public static class MechanoidMechanitorIdeologyRolePatches
    {
        /// <summary>
        /// 身份放宽辅助：原版自由殖民者继续通过，Full 模式正式机械族机械师也视为通过，其他 Pawn 保持
        /// 原判定。供 Transpiler 替换原版 IsFreeNonSlaveColonist 调用后由原版控制流自然继续。
        /// </summary>
        public static bool IsFreeColonistOrFullMechanitor(Pawn pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            if (pawn.IsFreeNonSlaveColonist)
            {
                return true;
            }

            return ModsConfig.IdeologyActive
                && MechanoidMechanitorIdeologyAdaptationUtility
                    .CanServeAsIdeologyRoleOrRitualParticipant(pawn);
        }

        private static IEnumerable<CodeInstruction> RelaxFirstIsFreeNonSlaveColonist(
            IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo original = AccessTools.PropertyGetter(
                typeof(Pawn),
                nameof(Pawn.IsFreeNonSlaveColonist));
            MethodInfo replacement = AccessTools.Method(
                typeof(MechanoidMechanitorIdeologyRolePatches),
                nameof(IsFreeColonistOrFullMechanitor));

            bool replaced = false;
            foreach (CodeInstruction instruction in instructions)
            {
                if (!replaced && instruction.Calls(original))
                {
                    yield return new CodeInstruction(OpCodes.Call, replacement)
                    {
                        labels = instruction.labels,
                        blocks = instruction.blocks
                    };
                    replaced = true;
                    continue;
                }

                yield return instruction;
            }
        }

        [HarmonyPatch]
        public static class Patch_Precept_Role_ValidatePawn
        {
            private static MethodBase? cachedTarget;

            private static bool Prepare()
            {
                return TargetMethod() != null;
            }

            private static MethodBase? TargetMethod()
            {
                return cachedTarget ??= AccessTools.Method(typeof(Precept_Role), "ValidatePawn");
            }

            [HarmonyPostfix]
            public static void Postfix(Precept_Role __instance, Pawn p, ref bool __result)
            {
                if (__result
                    || !ModsConfig.IdeologyActive
                    || !MechanoidMechanitorIdeologyAdaptationUtility
                        .CanServeAsIdeologyRoleOrRitualParticipant(p))
                {
                    return;
                }

                if (p.Destroyed || p.Dead || p.Faction == null || !p.Faction.IsPlayer)
                {
                    return;
                }

                // 意识形态一致性：原版 ValidatePawn 本身不校验意识形态，靠调用方（界面/分配链）
                // 只列出自身文化的角色来约束。放宽身份检查后必须自行确认 Pawn 当前信仰的意识形态
                // 就是该角色所属意识形态本身，避免把信仰 A 的机械族机械师直接分配到意识形态 B 的角色。
                // 不使用 PrimaryIdeo，也不只判断玩家派系是否拥有该意识形态；不一致则保持原版 false，
                // 不自动改写 Pawn 意识形态、不自动解除/重分配其他角色。
                Ideo? pawnIdeo = p.Ideo;
                Ideo? roleIdeo = __instance.ideo;
                if (pawnIdeo == null || roleIdeo == null || pawnIdeo != roleIdeo)
                {
                    return;
                }

                __result = __instance.RequirementsMet(p);
            }
        }

        /// <summary>
        /// 仅放宽 DrawPawnRoleSelection 最前面的 IsFreeNonSlaveColonist 身份检查，随后交由完整原版
        /// 方法执行，保留不可用角色、未满足条件说明、信徒不足提示、GetTip 工具提示与原版显示顺序。
        /// 追加最小 Postfix，在原版“分配职位……”按钮正下方绘制“切换意识形态”按钮：
        /// Postfix 无论原版是否提前返回都会执行，因此 Partial 模式（无职位分配权限、原版提前返回）
        /// 也能以与 Full 模式一致的位置绘制该按钮；显示条件由社交面板工具自身把关。
        /// </summary>
        [HarmonyPatch(
            typeof(SocialCardUtility),
            nameof(SocialCardUtility.DrawPawnRoleSelection))]
        public static class Patch_DrawPawnRoleSelection_AllowMechanitor
        {
            [HarmonyTranspiler]
            public static IEnumerable<CodeInstruction> Transpiler(
                IEnumerable<CodeInstruction> instructions)
            {
                return RelaxFirstIsFreeNonSlaveColonist(instructions);
            }

            [HarmonyPostfix]
            public static void Postfix(Pawn pawn, Rect rect)
            {
                MechanoidMechanitorIdeologySocialCardUtility.DrawChangeIdeoButton(pawn, rect);
            }
        }

        /// <summary>
        /// RitualRoleIdeoRoleChanger 只放宽 IsFreeNonSlaveColonist 身份检查，随后由原版继续执行
        /// AppliesIfChild、可用角色（AllRolesForPawn/RequirementsMet）与玩家文化（ideos.Has）等检查。
        /// </summary>
        [HarmonyPatch(
            typeof(RitualRoleIdeoRoleChanger),
            nameof(RitualRoleIdeoRoleChanger.AppliesToPawn))]
        public static class Patch_RitualRoleIdeoRoleChanger_AppliesToPawn
        {
            [HarmonyTranspiler]
            public static IEnumerable<CodeInstruction> Transpiler(
                IEnumerable<CodeInstruction> instructions)
            {
                return RelaxFirstIsFreeNonSlaveColonist(instructions);
            }
        }

        [HarmonyPatch(
            typeof(RoleRequirement_SupremeGender),
            nameof(RoleRequirement_SupremeGender.Met))]
        public static class Patch_RoleRequirement_SupremeGender_Met
        {
            [HarmonyPostfix]
            public static void Postfix(Pawn pawn, ref bool __result)
            {
                if (__result
                    || !ModsConfig.IdeologyActive
                    || !MechanoidMechanitorIdeologyAdaptationUtility
                        .CanServeAsIdeologyRoleOrRitualParticipant(pawn))
                {
                    return;
                }

                if (pawn.gender == Gender.None)
                {
                    __result = true;
                }
            }
        }

        [HarmonyPatch(typeof(Precept_RoleSingle), nameof(Precept_RoleSingle.Assign))]
        public static class Patch_Precept_RoleSingle_Assign
        {
            [HarmonyPrefix]
            public static void Prefix(Precept_RoleSingle __instance, Pawn p, ref bool addThoughts)
            {
                if (!ModsConfig.IdeologyActive || !addThoughts)
                {
                    return;
                }

                // 仅当旧角色持有者是 Full 模式正式机械族机械师且确实没有心情 Tracker 时才跳过
                // 原版 addThoughts；任意第三方无心情 Pawn 不改变原版行为。
                Pawn? oldPawn = __instance.ChosenPawnValue;
                if (oldPawn == null
                    || oldPawn.needs?.mood != null
                    || !MechanoidMechanitorIdeologyAdaptationUtility
                        .AllowsIdeologyFullParticipation(oldPawn))
                {
                    return;
                }

                if (p != null)
                {
                    Find.LetterStack.ReceiveLetter(
                        "LetterLabelRoleLost".Translate(
                            oldPawn.Named("PAWN"),
                            __instance.Named("ROLE")),
                        "LetterRoleLostDesc".Translate(
                            oldPawn.Named("PAWN"),
                            __instance.Named("ROLE"))
                        + " "
                        + "LetterRoleLostReasonUnassignedDesc".Translate(
                            oldPawn.Named("PAWN")).CapitalizeFirst(),
                        LetterDefOf.NeutralEvent,
                        oldPawn);
                }

                addThoughts = false;
            }

            [HarmonyPostfix]
            public static void Postfix(Pawn p)
            {
                if (p == null
                    || !MechanoidMechanitorIdeologyAdaptationUtility
                        .AllowsIdeologyFullParticipation(p))
                {
                    return;
                }

                p.abilities ??= new Pawn_AbilityTracker(p);
                p.abilities.Notify_TemporaryAbilitiesChanged();
            }
        }

        // 说明：Precept_RoleMulti.Unassign 原版已使用 needs?.mood?.thoughts?.memories? 的空值安全访问，
        // 无需为其提供额外补丁，故不再 Patch。
    }
}

using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.QuestGen;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 让原版任务系统识别 MOD 的运行时“只对玩家永久敌对/中立/盟友”普通派系关系锁。
    /// 不修改 FactionDef.permanentEnemy，也不修改现有 CanChangeGoodwillFor / GoodwillWith / SetRelation 关系锁补丁。
    /// </summary>
    public static class MechanoidMechanitorQuestFactionPatches
    {
        // ---------------------------------------------------------------------
        // 好感奖励关闭：第一层
        // Faction.CanEverGiveGoodwillRewards 属性 getter
        // 原版结果为 false 时不改回 true；若与玩家属于任意锁定的普通派系关系则改为 false。
        // 影响 RewardsGenerator 与奖励偏好界面。
        // ---------------------------------------------------------------------
        [HarmonyPatch(typeof(Faction), nameof(Faction.CanEverGiveGoodwillRewards), MethodType.Getter)]
        public static class MechanoidMechanitorQuestFaction_CanEverGiveGoodwillRewards_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Faction __instance, ref bool __result)
            {
                if (!__result)
                {
                    return;
                }

                if (MechanoidMechanitorQuestFactionPolicy.IsGoodwillLockedForPlayer(__instance))
                {
                    __result = false;
                }
            }
        }

        // ---------------------------------------------------------------------
        // 好感奖励关闭：第二层
        // QuestGen_Rewards.GiveRewards
        // 在原版解析奖励派系前，把锁定派系的 allowGoodwill 置为 false，
        // 避免敌对派系被错误限制为单一奖励方案（仅依赖 CanEverGiveGoodwillRewards 不足以避免）。
        // RewardsGeneratorParams 为值类型，因此 parms 使用 ref 传入。
        // ---------------------------------------------------------------------
        [HarmonyPatch(typeof(QuestGen_Rewards), nameof(QuestGen_Rewards.GiveRewards))]
        public static class MechanoidMechanitorQuestFaction_GiveRewards_Patch
        {
            [HarmonyPrefix]
            public static void Prefix(ref RewardsGeneratorParams parms, Pawn asker)
            {
                if (!parms.allowGoodwill)
                {
                    return;
                }

                // 与原版一致：优先 giverFaction，否则询问者所属派系。
                Faction? giverFaction = parms.giverFaction ?? asker?.Faction;
                if (giverFaction != null
                    && MechanoidMechanitorQuestFactionPolicy.IsGoodwillLockedForPlayer(giverFaction))
                {
                    parms.allowGoodwill = false;
                }
            }
        }

        // ---------------------------------------------------------------------
        // 任务筛选：QuestNode_GetFaction.IsGoodFaction
        // 仅在原版允许永久敌人判断生效（allowPermanentEnemy 明确具有值且不为 true）时，
        // 额外排除被 MOD 锁定为对玩家永久敌对的普通派系。
        // ---------------------------------------------------------------------
        [HarmonyPatch(typeof(QuestNode_GetFaction), "IsGoodFaction")]
        public static class MechanoidMechanitorQuestFaction_GetFaction_IsGoodFaction_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(
                QuestNode_GetFaction __instance,
                Faction faction,
                Slate slate,
                ref bool __result)
            {
                if (!__result)
                {
                    return;
                }

                bool? allowPermanentEnemy = __instance.allowPermanentEnemy.GetValue(slate);
                if (!allowPermanentEnemy.HasValue || allowPermanentEnemy == true)
                {
                    return;
                }

                if (MechanoidMechanitorQuestFactionPolicy.IsPermanentlyHostileToPlayer(faction))
                {
                    __result = false;
                }
            }
        }

        // ---------------------------------------------------------------------
        // 任务筛选：QuestNode_GetPawn.IsGoodPawn
        // 仅在允许永久敌人判断生效（allowPermanentEnemyFaction 明确具有值且不为 true）时，
        // 额外排除属于被 MOD 锁定为对玩家永久敌对派系的 Pawn。
        // ---------------------------------------------------------------------
        [HarmonyPatch(typeof(QuestNode_GetPawn), "IsGoodPawn")]
        public static class MechanoidMechanitorQuestFaction_GetPawn_IsGoodPawn_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(
                QuestNode_GetPawn __instance,
                Pawn pawn,
                Slate slate,
                ref bool __result)
            {
                if (!__result)
                {
                    return;
                }

                if (pawn == null)
                {
                    return;
                }

                Faction? pawnFaction = pawn.Faction;
                if (pawnFaction == null)
                {
                    return;
                }

                bool? allowPermanentEnemyFaction =
                    __instance.allowPermanentEnemyFaction.GetValue(slate);
                if (!allowPermanentEnemyFaction.HasValue
                    || allowPermanentEnemyFaction == true)
                {
                    return;
                }

                if (MechanoidMechanitorQuestFactionPolicy
                        .IsPermanentlyHostileToPlayer(pawnFaction))
                {
                    __result = false;
                }
            }
        }

        // ---------------------------------------------------------------------
        // 任务筛选：QuestNode_GetPawn.TryFindFactionForPawnGeneration
        // 不允许在随机选中非法派系后再令整个任务生成失败的 Postfix 写法。
        // 当 allowPermanentEnemyFaction 明确不为 true 且当前存档存在锁定的普通派系关系时，
        // 在候选集合形成阶段额外排除被 MOD 锁定为对玩家永久敌对的派系，完整保留原版筛选与权重选择。
        // ---------------------------------------------------------------------
        [HarmonyPatch(typeof(QuestNode_GetPawn), "TryFindFactionForPawnGeneration")]
        public static class
            MechanoidMechanitorQuestFaction_GetPawn_TryFindFactionForPawnGeneration_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(
                QuestNode_GetPawn __instance,
                Slate slate,
                out Faction? faction,
                ref bool __result)
            {
                faction = null;

                // 明确允许永久敌人：让原版正常执行，不添加排除。
                if (__instance.allowPermanentEnemyFaction.GetValue(slate) == true)
                {
                    return true;
                }

                // 当前存档没有有效的锁定普通派系关系：优先直接让原版执行，减少与其他 MOD 冲突。
                if (!GameComponent_MechanoidMechanitorStoryState
                        .HasLockedOrdinaryFactionRelations)
                {
                    return true;
                }

                List<Faction> candidates = Find.FactionManager
                    .GetFactions(
                        allowHidden: false,
                        allowDefeated: false,
                        allowNonHumanlike: false)
                    .Where(delegate(Faction x)
                    {
                        if (__instance.excludeFactionDefs.GetValue(slate) != null
                            && __instance.excludeFactionDefs.GetValue(slate)
                                .Contains(x.def))
                        {
                            return false;
                        }

                        if (__instance.mustHaveRoyalTitleInCurrentFaction.GetValue(slate)
                            && !x.def.HasRoyalTitles)
                        {
                            return false;
                        }

                        if (__instance.mustBeNonHostileToPlayer.GetValue(slate)
                            && x.HostileTo(Faction.OfPlayer))
                        {
                            return false;
                        }

                        if (slate.TryGet<Map>("map", out Map map)
                            && __instance.mustHaveSettlementOnLayer.GetValue(slate)
                            && map.Tile.Valid
                            && !Find.WorldObjects.AnyFactionSettlementOnLayer(
                                x,
                                map.Tile.Layer))
                        {
                            return false;
                        }

                        // 原版 permanentEnemy 判断旁额外排除 MOD 锁定的永久敌对派系。
                        if (__instance.allowPermanentEnemyFaction.GetValue(slate) != true
                            && x.def.permanentEnemy)
                        {
                            return false;
                        }

                        if (MechanoidMechanitorQuestFactionPolicy
                                .IsPermanentlyHostileToPlayer(x))
                        {
                            return false;
                        }

                        if ((int)x.def.techLevel
                            < (int)__instance.minTechLevel.GetValue(slate))
                        {
                            return false;
                        }

                        return (!__instance.factionMustBePermanent.GetValue(slate)
                            || !x.temporary)
                            ? true
                            : false;
                    })
                    .ToList();

                bool found = candidates.TryRandomElementByWeight(
                    x => x.HostileTo(Faction.OfPlayer)
                        ? (__instance.hostileWeight.GetValue(slate) ?? 1f)
                        : (__instance.nonHostileWeight.GetValue(slate) ?? 1f),
                    out faction);

                __result = found;
                return false;
            }
        }

        // ---------------------------------------------------------------------
        // 任务筛选：QuestGen_Pawns.IsGoodPawn
        // 仅在允许永久敌人判断生效（allowPermanentEnemyFaction 明确具有值且不为 true）时，
        // 额外排除属于被 MOD 锁定为对玩家永久敌对派系的 Pawn。
        // 参数为 null 时不排除现有 Pawn（保留原版语义）。
        // ---------------------------------------------------------------------
        [HarmonyPatch(typeof(QuestGen_Pawns), "IsGoodPawn")]
        public static class
            MechanoidMechanitorQuestFaction_QuestGenPawns_IsGoodPawn_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Pawn pawn, QuestGen_Pawns.GetPawnParms parms, ref bool __result)
            {
                if (!__result)
                {
                    return;
                }

                if (pawn == null)
                {
                    return;
                }

                Faction? pawnFaction = pawn.Faction;
                if (pawnFaction == null)
                {
                    return;
                }

                bool? allowPermanentEnemyFaction = parms.allowPermanentEnemyFaction;
                if (!allowPermanentEnemyFaction.HasValue
                    || allowPermanentEnemyFaction == true)
                {
                    return;
                }

                if (MechanoidMechanitorQuestFactionPolicy
                        .IsPermanentlyHostileToPlayer(pawnFaction))
                {
                    __result = false;
                }
            }
        }

        // ---------------------------------------------------------------------
        // 任务筛选：QuestGen_Pawns.TryFindFactionForPawnGeneration
        // 不允许随机选择后的失败式 Postfix。当 allowPermanentEnemyFaction 明确不为 true 且存在锁定关系时，
        // 按原版代码重新构建候选列表并额外排除 MOD 锁定的永久敌对派系。
        // ---------------------------------------------------------------------
        [HarmonyPatch(typeof(QuestGen_Pawns), "TryFindFactionForPawnGeneration")]
        public static class
            MechanoidMechanitorQuestFaction_QuestGenPawns_TryFindFactionForPawnGeneration_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(
                QuestGen_Pawns.GetPawnParms parms,
                out Faction? faction,
                ref bool __result)
            {
                faction = null;

                if (parms.allowPermanentEnemyFaction == true)
                {
                    return true;
                }

                if (!GameComponent_MechanoidMechanitorStoryState
                        .HasLockedOrdinaryFactionRelations)
                {
                    return true;
                }

                List<Faction> candidates = Find.FactionManager
                    .GetFactions(
                        allowTemporary: parms.allowTemporaryFactions,
                        allowHidden: parms.allowHidden,
                        allowDefeated: false,
                        allowNonHumanlike: false)
                    .Where(delegate(Faction x)
                    {
                        if (parms.mustBeOfFaction != null
                            && x != parms.mustBeOfFaction)
                        {
                            return false;
                        }

                        if (parms.excludeFactionDefs != null
                            && parms.excludeFactionDefs.Contains(x.def))
                        {
                            return false;
                        }

                        if (parms.mustHaveRoyalTitleInCurrentFaction
                            && !x.def.HasRoyalTitles)
                        {
                            return false;
                        }

                        if (parms.mustBeNonHostileToPlayer
                            && x.HostileTo(Faction.OfPlayer))
                        {
                            return false;
                        }

                        // 原版 permanentEnemy 判断旁额外排除 MOD 锁定的永久敌对派系。
                        if (parms.allowPermanentEnemyFaction != true
                            && x.def.permanentEnemy)
                        {
                            return false;
                        }

                        if (MechanoidMechanitorQuestFactionPolicy
                                .IsPermanentlyHostileToPlayer(x))
                        {
                            return false;
                        }

                        return ((int)x.def.techLevel >= (int)parms.minTechLevel)
                            ? true
                            : false;
                    })
                    .ToList();

                bool found = candidates.TryRandomElement(out faction);

                __result = found;
                return false;
            }
        }
    }
}

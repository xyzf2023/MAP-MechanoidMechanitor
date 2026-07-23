using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 机械巢节点守军 / 盟军的机械族生成器。统一从机械巢派系当前有效的 Combat 部队模板
    /// 按威胁点数生成，不写死具体 PawnKind，兼容其他 MOD 对 Combat 模板的扩展。
    /// 与“部队支援”使用同一套 PawnGroupMaker + Combat 模板链路。
    /// </summary>
    public static class MechHiveNodeCombatPawnGenerator
    {
        private const string BreachKindDefName = "Mech_Termite_Breach";

        private static bool loggedEmptyTemplateOnce;

        /// <summary>
        /// 从机械巢 Combat 模板生成一组机械族 Pawn（尚未落地）。失败返回空列表并记录一次日志。
        /// </summary>
        public static List<Pawn> GenerateCombatPawns(Faction? mechHive, int points, Map map)
        {
            List<Pawn> result = new List<Pawn>();
            if (mechHive == null || map == null || points <= 0)
            {
                return result;
            }

            List<PawnGroupMaker>? makers = mechHive.def?.pawnGroupMakers;
            if (makers == null || makers.Count == 0)
            {
                LogEmptyTemplateOnce(mechHive);
                return result;
            }

            IncidentParms parms = new IncidentParms
            {
                target = map,
                points = points,
                faction = mechHive,
                pawnGroupKind = PawnGroupKindDefOf.Combat,
                raidStrategy = RaidStrategyDefOf.ImmediateAttack,
                raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn,
                canKidnap = false,
                canSteal = false,
                canTimeoutOrFlee = false
            };

            PawnGroupMakerParms? groupParms;
            try
            {
                groupParms = IncidentParmsUtility.GetDefaultPawnGroupMakerParms(
                    PawnGroupKindDefOf.Combat,
                    parms,
                    ensureCanGenerateAtLeastOnePawn: true);
            }
            catch (Exception ex)
            {
                Log.Warning("[MAP] 机械巢节点守军生成参数构建失败: " + ex);
                return result;
            }

            if (groupParms == null)
            {
                LogEmptyTemplateOnce(mechHive);
                return result;
            }

            List<PawnGroupMaker> available = new List<PawnGroupMaker>();
            for (int i = 0; i < makers.Count; i++)
            {
                if (IsAcceptableCombatMaker(makers[i], groupParms))
                {
                    available.Add(makers[i]);
                }
            }

            if (available.Count == 0)
            {
                LogEmptyTemplateOnce(mechHive);
                return result;
            }

            if (!available.TryRandomElementByWeight(m => m != null ? m.commonality : 0f, out PawnGroupMaker maker)
                || maker == null)
            {
                LogEmptyTemplateOnce(mechHive);
                return result;
            }

            try
            {
                foreach (Pawn pawn in maker.GeneratePawns(groupParms, errorOnZeroResults: false))
                {
                    if (pawn != null)
                    {
                        result.Add(pawn);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[MAP] 机械巢节点守军生成异常: " + ex);
                DiscardPawns(result);
                return new List<Pawn>();
            }

            if (result.Count == 0)
            {
                LogEmptyTemplateOnce(mechHive);
            }

            return result;
        }

        private static bool IsAcceptableCombatMaker(
            PawnGroupMaker? maker,
            PawnGroupMakerParms groupParms)
        {
            if (maker == null)
            {
                return false;
            }

            try
            {
                if (maker.kindDef != PawnGroupKindDefOf.Combat
                    || ContainsBreachOption(maker)
                    || !maker.CanGenerateFrom(groupParms))
                {
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool ContainsBreachOption(PawnGroupMaker maker)
        {
            List<PawnGenOption>? options = maker.options;
            if (options == null)
            {
                return false;
            }

            for (int i = 0; i < options.Count; i++)
            {
                PawnKindDef? kind = options[i]?.kind;
                if (kind != null && kind.defName == BreachKindDefName)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>丢弃未使用的已生成 Pawn，避免残留世界 Pawn。</summary>
        public static void DiscardPawns(List<Pawn> pawns)
        {
            if (pawns == null)
            {
                return;
            }

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn == null || pawn.Spawned)
                {
                    continue;
                }

                try
                {
                    if (Find.WorldPawns.Contains(pawn))
                    {
                        Find.WorldPawns.RemovePawn(pawn);
                    }

                    Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.Discard);
                }
                catch (Exception ex)
                {
                    Log.Warning("[MAP] 丢弃未使用机械巢 Pawn 失败: " + ex);
                }
            }

            pawns.Clear();
        }

        private static void LogEmptyTemplateOnce(Faction mechHive)
        {
            if (loggedEmptyTemplateOnce)
            {
                return;
            }

            loggedEmptyTemplateOnce = true;
            Log.Warning(
                "[MAP] 机械巢派系（" + (mechHive?.def?.defName ?? "?")
                + "）没有可用的 Combat 部队模板，机械巢节点将不生成守军或盟军。");
        }
    }
}

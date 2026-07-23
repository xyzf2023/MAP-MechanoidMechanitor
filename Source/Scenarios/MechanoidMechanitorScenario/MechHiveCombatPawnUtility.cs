using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 机械巢 Combat 部队模板筛选与 Pawn 生成的公共工具。
    /// 供手动部队支援、机械巢节点守军、节点自动盟军共同复用。
    /// </summary>
    public static class MechHiveCombatPawnUtility
    {
        private const string BreachKindDefName = "Mech_Termite_Breach";

        private static bool loggedEmptyTemplateOnce;

        /// <summary>
        /// 构建 Combat 部队生成参数。失败返回 false。
        /// </summary>
        public static bool TryBuildCombatGroupParms(
            Faction? faction,
            Map? map,
            int points,
            out PawnGroupMakerParms? groupParms)
        {
            groupParms = null;
            if (faction == null || map == null || points <= 0)
            {
                return false;
            }

            IncidentParms parms = new IncidentParms
            {
                target = map,
                points = points,
                faction = faction,
                pawnGroupKind = PawnGroupKindDefOf.Combat,
                raidStrategy = RaidStrategyDefOf.ImmediateAttack,
                raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn,
                canKidnap = false,
                canSteal = false,
                canTimeoutOrFlee = false
            };

            try
            {
                groupParms = IncidentParmsUtility.GetDefaultPawnGroupMakerParms(
                    PawnGroupKindDefOf.Combat,
                    parms,
                    ensureCanGenerateAtLeastOnePawn: true);
            }
            catch (Exception ex)
            {
                Log.Warning("[MAP] 机械巢 Combat 部队参数构建失败: " + ex);
                groupParms = null;
                return false;
            }

            return groupParms != null;
        }

        /// <summary>
        /// 模板规则匹配（不含异常吞没）。供部队支援在外层记录异常模板日志。
        /// </summary>
        public static bool MatchesCombatTemplateRules(
            PawnGroupMaker? maker,
            PawnGroupMakerParms groupParms)
        {
            if (maker == null || groupParms == null)
            {
                return false;
            }

            if (maker.kindDef != PawnGroupKindDefOf.Combat
                || ContainsBreachOption(maker)
                || !maker.CanGenerateFrom(groupParms))
            {
                return false;
            }

            return true;
        }

        /// <summary>安全筛选：异常时返回 false。</summary>
        public static bool TryAcceptCombatMaker(
            PawnGroupMaker? maker,
            PawnGroupMakerParms groupParms)
        {
            try
            {
                return MatchesCombatTemplateRules(maker, groupParms);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>返回当前可用的 Combat 模板列表（含其他 MOD 追加项）。</summary>
        public static List<PawnGroupMaker> GetAvailableCombatMakers(
            Faction? faction,
            Map? map,
            int points)
        {
            List<PawnGroupMaker> result = new List<PawnGroupMaker>();
            if (!TryBuildCombatGroupParms(faction, map, points, out PawnGroupMakerParms? groupParms)
                || groupParms == null)
            {
                return result;
            }

            List<PawnGroupMaker>? makers = faction?.def?.pawnGroupMakers;
            if (makers == null || makers.Count == 0)
            {
                return result;
            }

            for (int i = 0; i < makers.Count; i++)
            {
                PawnGroupMaker? maker = makers[i];
                if (TryAcceptCombatMaker(maker, groupParms) && maker != null)
                {
                    result.Add(maker);
                }
            }

            return result;
        }

        /// <summary>
        /// 从指定模板生成 Pawn（尚未落地）。失败返回空列表。
        /// </summary>
        public static List<Pawn> GenerateFromMaker(
            PawnGroupMaker? maker,
            PawnGroupMakerParms? groupParms)
        {
            List<Pawn> result = new List<Pawn>();
            if (maker == null || groupParms == null)
            {
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
                Log.Warning("[MAP] 机械巢 Combat 部队生成异常: " + ex);
                DiscardPawns(result);
                return new List<Pawn>();
            }

            return result;
        }

        /// <summary>
        /// 从机械巢当前有效 Combat 模板按威胁点数生成机械族（尚未落地）。
        /// 模板为空或生成失败时安全退出，并记录一次明确日志。
        /// </summary>
        public static List<Pawn> GenerateCombatPawns(Faction? faction, Map? map, int points)
        {
            List<Pawn> result = new List<Pawn>();
            if (faction == null || map == null || points <= 0)
            {
                return result;
            }

            if (!TryBuildCombatGroupParms(faction, map, points, out PawnGroupMakerParms? groupParms)
                || groupParms == null)
            {
                LogEmptyTemplateOnce(faction);
                return result;
            }

            List<PawnGroupMaker>? makers = faction.def?.pawnGroupMakers;
            if (makers == null || makers.Count == 0)
            {
                LogEmptyTemplateOnce(faction);
                return result;
            }

            List<PawnGroupMaker> available = new List<PawnGroupMaker>();
            for (int i = 0; i < makers.Count; i++)
            {
                PawnGroupMaker? makerCandidate = makers[i];
                if (TryAcceptCombatMaker(makerCandidate, groupParms) && makerCandidate != null)
                {
                    available.Add(makerCandidate);
                }
            }

            if (available.Count == 0)
            {
                LogEmptyTemplateOnce(faction);
                return result;
            }

            if (!available.TryRandomElementByWeight(m => m != null ? m.commonality : 0f, out PawnGroupMaker maker)
                || maker == null)
            {
                LogEmptyTemplateOnce(faction);
                return result;
            }

            result = GenerateFromMaker(maker, groupParms);
            if (result.Count == 0)
            {
                LogEmptyTemplateOnce(faction);
            }

            return result;
        }

        /// <summary>丢弃未使用的已生成 Pawn，避免残留世界 Pawn。</summary>
        public static void DiscardPawns(List<Pawn>? pawns)
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

        private static bool ContainsBreachOption(PawnGroupMaker maker)
        {
            List<PawnGenOption>? options = maker.options;
            if (options == null || options.Count == 0)
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

        private static void LogEmptyTemplateOnce(Faction? faction)
        {
            if (loggedEmptyTemplateOnce)
            {
                return;
            }

            loggedEmptyTemplateOnce = true;
            Log.Warning(
                "[MAP] 机械巢派系（" + (faction?.def?.defName ?? "?")
                + "）没有可用的 Combat 部队模板，相关功能将不生成机械族部队。");
        }
    }
}

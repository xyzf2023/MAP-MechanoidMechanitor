using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using HarmonyLib;
using UnityEngine;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 主脑盟约支援系统的公共逻辑：资格检查、派系选择、点数计算、PawnGroup 生成、
    /// 真空服装备、安全落点、波次部署、主脑防御解除通知、旧存档补装、以及撤离（皇权穿梭机 / 奥德赛机械空投仓）。
    /// 所有对已部署援军的操作均不调用 Destroy / Discard / Clear；撤离全部交给原版 FlyShipLeaving。
    /// </summary>
    public static class SymbiosisCovenantCerebrexSupportUtility
    {
        private const string LogPrefix = "[MAP][SymbiosisCovenantCerebrexSupport]";

        private const string EvacPodDefName = "MAP_SymbiosisCovenant_CerebrexEvacDropPod";

        private static readonly AccessTools.FieldRef<QuestPart_CerebrexCore, Site> GetCerebrexCoreSite =
            AccessTools.FieldRefAccess<QuestPart_CerebrexCore, Site>("site");

        private static SymbiosisCovenantCerebrexSupportDef Config
            => SymbiosisCovenantCerebrexSupportDefOf.MAP_SymbiosisCovenant_CerebrexSupportConfig;

        private static List<Faction> GetCovenantMemberFactions(GameComponent_SymbiosisCovenantState comp)
        {
            List<Faction> result = new List<Faction>();
            foreach (SymbiosisCovenantFactionRecord record in comp.GetRecordsSorted())
            {
                if (record.CovenantMember && record.Faction != null)
                {
                    result.Add(record.Faction);
                }
            }

            return result;
        }

        private static int GetTrust(GameComponent_SymbiosisCovenantState comp, Faction faction)
            => comp.GetRecord(faction)?.Trust ?? 0;

        // ───────────────────────── 资格与派系 ─────────────────────────

        public static bool IsEligibleNow(QuestPart_SymbiosisCovenantCerebrexSupport part, Map map)
        {
            GameComponent_SymbiosisCovenantState? comp = GameComponent_SymbiosisCovenantState.CurrentComponent;
            if (comp == null || !GameComponent_SymbiosisCovenantState.IsActive)
            {
                return false;
            }

            if (comp.CovenantLevel < 2)
            {
                return false;
            }

            if (comp.CovenantMemberCount < 1)
            {
                return false;
            }

            return HasAnyEligibleFaction(part, map);
        }

        public static bool HasAnyEligibleFaction(QuestPart_SymbiosisCovenantCerebrexSupport part, Map map)
        {
            GameComponent_SymbiosisCovenantState? comp = GameComponent_SymbiosisCovenantState.CurrentComponent;
            if (comp == null)
            {
                return false;
            }

            foreach (Faction faction in GetCovenantMemberFactions(comp))
            {
                if (IsFactionEligible(faction, map, Faction.OfMechanoids, comp))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<Faction> GetEligibleFactions(QuestPart_SymbiosisCovenantCerebrexSupport part, Map map)
        {
            GameComponent_SymbiosisCovenantState? comp = GameComponent_SymbiosisCovenantState.CurrentComponent;
            List<Faction> result = new List<Faction>();
            if (comp == null)
            {
                return result;
            }

            foreach (Faction faction in GetCovenantMemberFactions(comp))
            {
                if (IsFactionEligible(faction, map, Faction.OfMechanoids, comp))
                {
                    result.Add(faction);
                }
            }

            return result;
        }

        public static bool IsFactionEligible(
            Faction? faction,
            Map map,
            Faction targetFaction,
            GameComponent_SymbiosisCovenantState comp)
        {
            if (faction == null || comp == null)
            {
                return false;
            }

            if (faction == Faction.OfPlayer)
            {
                return false;
            }

            if (faction == Faction.OfMechanoids)
            {
                return false;
            }

            if (targetFaction != null && faction == targetFaction)
            {
                return false;
            }

            if (comp.GetRecord(faction)?.CovenantMember != true)
            {
                return false;
            }

            if (faction.defeated)
            {
                return false;
            }

            if (faction.temporary)
            {
                return false;
            }

            if (faction.def == null)
            {
                return false;
            }

            if (faction.HostileTo(Faction.OfPlayer))
            {
                return false;
            }

            if (!faction.HostileTo(Faction.OfMechanoids))
            {
                return false;
            }

            if (!faction.def.pawnGroupMakers.Any(m => m.kindDef == PawnGroupKindDefOf.Combat))
            {
                return false;
            }

            return true;
        }

        // ───────────────────────── 威胁与点数 ─────────────────────────

        public static float ComputeThreatSnapshot(Map map, Site site)
        {
            if (site.ActualThreatPoints > 0f)
            {
                return site.ActualThreatPoints;
            }

            float sum = 0f;
            foreach (Pawn p in map.mapPawns.AllPawnsSpawned)
            {
                if (p == null || p.Dead)
                {
                    continue;
                }

                if (p.Faction == null || !p.Faction.HostileTo(Faction.OfPlayer))
                {
                    continue;
                }

                sum += p.kindDef.combatPower;
            }

            return sum;
        }

        public static float ComputePerWavePoints(
            SymbiosisCovenantCerebrexSupportDef cfg,
            int covenantLevel,
            float threatSnapshot)
        {
            float factor = covenantLevel >= 5
                ? cfg.level5ThreatOffsetFactor
                : covenantLevel == 4
                    ? cfg.level4ThreatOffsetFactor
                    : covenantLevel == 3
                        ? cfg.level3ThreatOffsetFactor
                        : cfg.level2ThreatOffsetFactor;

            float points = 10000f + threatSnapshot * factor;
            return Mathf.Clamp(points, cfg.minWavePoints, cfg.maxWavePoints);
        }

        private static List<Faction> SelectWaveFactions(
            List<Faction> prevFactions,
            List<Faction> eligible,
            GameComponent_SymbiosisCovenantState comp,
            SymbiosisCovenantCerebrexSupportDef cfg)
        {
            List<Faction> pool = eligible.ToList();

            // 优先选择上一波未参与的派系；同优先级内按 Trust 降序。
            pool.Sort((a, b) =>
            {
                bool aPrev = prevFactions.Contains(a);
                bool bPrev = prevFactions.Contains(b);
                if (aPrev != bPrev)
                {
                    return aPrev ? 1 : -1;
                }

                int ta = GetTrust(comp, a);
                int tb = GetTrust(comp, b);
                return tb.CompareTo(ta);
            });

            int take = Math.Min(cfg.maxParticipantsPerWave, pool.Count);
            return pool.Take(take).ToList();
        }

        private static List<(Faction faction, float points)> AllocatePoints(float total, List<Faction> factions)
        {
            List<(Faction, float)> result = new List<(Faction, float)>();
            if (factions.Count == 0)
            {
                return result;
            }

            float per = total / factions.Count;
            float remainder = total - (per * factions.Count);
            for (int i = 0; i < factions.Count; i++)
            {
                float pts = per + (i == factions.Count - 1 ? remainder : 0f);
                result.Add((factions[i], pts));
            }

            return result;
        }

        // ───────────────────────── 波次部署 ─────────────────────────

        public static (bool success, bool stopScheduling, SymbiosisCovenantCerebrexSupportWaveRecord? record) TryDeployWave(
            QuestPart_SymbiosisCovenantCerebrexSupport part, Map map, int now)
        {
            SymbiosisCovenantCerebrexSupportDef cfg = Config;
            GameComponent_SymbiosisCovenantState? comp = GameComponent_SymbiosisCovenantState.CurrentComponent;
            if (comp == null || !GameComponent_SymbiosisCovenantState.IsActive || comp.CovenantLevel < 2)
            {
                return (false, true, null);
            }

            List<Faction> eligible = GetEligibleFactions(part, map);
            if (eligible.Count == 0)
            {
                return (false, true, null);
            }

            List<Faction> prev = part.waves.LastOrDefault()?.factionRecords
                .Select(f => f.faction).Where(f => f != null).Cast<Faction>().ToList()
                ?? new List<Faction>();
            List<Faction> attemptFactions = SelectWaveFactions(prev, eligible, comp, cfg);
            if (attemptFactions.Count == 0)
            {
                return (false, true, null);
            }

            float total = part.perWaveSupportPoints;
            Dictionary<Faction, List<Pawn>>? factionPawns = null;

            // 整波生成必须使用最终参与派系重新分配全部点数：任何一派失败时，回收本轮
            // 尚未落图的 Pawn，再缩减派系并重试，绝不留下“只生成了部分点数”的波次。
            for (int attempt = 0; attempt < 3 && attemptFactions.Count > 0; attempt++)
            {
                float perFactionPoints = total / attemptFactions.Count;
                Dictionary<Faction, List<Pawn>> generated = new Dictionary<Faction, List<Pawn>>();
                List<Faction> failed = new List<Faction>();
                foreach (Faction fac in attemptFactions)
                {
                    List<Pawn> valid = new List<Pawn>();
                    foreach (Pawn pawn in GenerateCombatGroup(fac, map, perFactionPoints))
                    {
                        if (TryEquipVacsuit(pawn)) valid.Add(pawn);
                        else pawn.Destroy(DestroyMode.Vanish);
                    }

                    if (valid.Count == 0)
                    {
                        Log.Warning(LogPrefix + " 盟约派系 "
                            + (fac != null ? fac.Name + "(loadID=" + fac.loadID + ")" : "?")
                            + " 在 site=" + (part.site?.Label)
                            + " wave=" + part.waves.Count
                            + " 的全部战斗 Pawn 均因无法穿戴真空服被淘汰，本次尝试移除该派系。");
                        failed.Add(fac!);
                    }
                    else generated[fac!] = valid;
                }

                if (failed.Count == 0)
                {
                    factionPawns = generated;
                    break;
                }

                foreach (Pawn pawn in generated.Values.SelectMany(pawns => pawns))
                {
                    pawn.Destroy(DestroyMode.Vanish);
                }
                attemptFactions = attemptFactions.Where(f => !failed.Contains(f)).ToList();
            }

            if (factionPawns == null || factionPawns.Count == 0)
            {
                Log.Warning(LogPrefix + " site=" + (part.site?.Label)
                    + " wave=" + part.waves.Count
                    + " 整波没有任何可用 Pawn（所有派系援军均被淘汰），放弃本波空投。");
                return (false, false, null);
            }

            List<Faction> successFacs = factionPawns.Keys.ToList();
            List<(Faction faction, float points)> pointAllocations = AllocatePoints(total, successFacs);
            List<Pawn> allPawns = factionPawns.Values.SelectMany(x => x).ToList();

            // 改为复用原版“友军空投”到达链：只解析一次投放中心，整波援军围绕同一中心密集落下。
            // 不再使用主脑援军专用的整波精确落点预选（TryFindSafeEdgeDropCells / cells / cellIdx）。
            IncidentParms arrivalParms = new IncidentParms
            {
                target = map,
                faction = successFacs[0],
                raidArrivalMode = PawnsArrivalModeDefOf.CenterDrop,
                raidArrivalModeForQuickMilitaryAid = true,
                podOpenDelay = cfg.dropPodOpenDelayTicks
            };

            bool resolvedCenter = arrivalParms.raidArrivalMode.Worker.TryResolveRaidSpawnCenter(arrivalParms);
            PawnsArrivalModeDef? resolvedArrivalMode = arrivalParms.raidArrivalMode;
            if (!resolvedCenter || !arrivalParms.spawnCenter.IsValid || resolvedArrivalMode == null)
            {
                foreach (Pawn pawn in allPawns) pawn.Destroy(DestroyMode.Vanish);
                Log.Warning(LogPrefix + " 原版空投中心解析失败，整波 " + allPawns.Count
                    + " 名援军 Pawn 回滚（site=" + (part.site?.Label)
                    + " wave=" + part.waves.Count
                    + " plannedMode=CenterDrop"
                    + " resolvedMode=" + (resolvedArrivalMode != null ? resolvedArrivalMode.defName : "?")
                    + " spawnCenterValid=" + arrivalParms.spawnCenter.IsValid + "）。");
                return (false, false, null);
            }

            int waveIndex = part.waves.Count;
            string questId = part.quest?.id.ToString() ?? "q";
            List<SymbiosisCovenantCerebrexSupportFactionRecord> factionRecords =
                new List<SymbiosisCovenantCerebrexSupportFactionRecord>();
            foreach ((Faction fac, float points) allocation in pointAllocations)
            {
                Faction fac = allocation.fac;
                List<Pawn> pawns = factionPawns[fac];
                string aidTag = $"{questId}_{waveIndex}_{fac.loadID}";

                // 所有派系共享同一个已解析的投放中心；仅更新 faction，不再重新解析、也不各自选中心。
                arrivalParms.faction = fac;

                // 盟约援军“使命感”心情记忆：仅在整波成功生成且即将实际落地的这批 Pawn 上添加；
                // 失败派系/回滚 Pawn 已在前面销毁，不会获得无意义状态。
                foreach (Pawn deployed in pawns)
                {
                    deployed.needs?.mood?.thoughts?.memories?.TryGainMemory(
                        MAP_SymbiosisCovenantCerebrexSupportThoughtDefOf
                            .MAP_SymbiosisCovenant_CerebrexSupport_SenseOfMission);
                }

                resolvedArrivalMode.Worker.Arrive(pawns, arrivalParms);

                factionRecords.Add(new SymbiosisCovenantCerebrexSupportFactionRecord(fac, allocation.points, pawns.Count, aidTag));
                LordMaker.MakeNewLord(fac, new LordJob_SymbiosisCovenantCerebrexSupport(map.Center), map, pawns);
            }

            Log.Message($"{LogPrefix} 援军已抵达：site={part.site?.Label} wave={waveIndex} pawns={allPawns.Count} factions={successFacs.Count} mode={resolvedArrivalMode.defName} spawnCenter={arrivalParms.spawnCenter}。");

            return (true, false, new SymbiosisCovenantCerebrexSupportWaveRecord
            {
                waveIndex = waveIndex, deployedTick = now, initialPawnCount = allPawns.Count, pawns = allPawns,
                factionRecords = factionRecords, aidTag = $"{questId}_{waveIndex}", nextWaveTriggered = false
            });
        }

        private static List<Pawn> GenerateCombatGroup(Faction faction, Map map, float points)
        {
            PawnGroupMakerParms parms = new PawnGroupMakerParms
            {
                groupKind = PawnGroupKindDefOf.Combat,
                faction = faction,
                points = Mathf.Max(35f, points),
                raidStrategy = RaidStrategyDefOf.ImmediateAttackFriendly,
                tile = map.Tile
            };

            return PawnGroupMakerUtility.GeneratePawns(
                parms,
                warnOnZeroResults: true).ToList();
        }

        // ───────────────────────── 真空服装备 ─────────────────────────

        private static bool TryEquipVacsuit(Pawn pawn)
        {
            ThingDef suitDef = ThingDefOf.Apparel_Vacsuit;
            ThingDef helmDef = ThingDefOf.Apparel_VacsuitHelmet;

            if (!ApparelUtility.HasPartsToWear(pawn, suitDef)
                || !ApparelUtility.HasPartsToWear(pawn, helmDef))
            {
                return false;
            }

            RemoveConflictingAndDestroy(pawn, suitDef);
            RemoveConflictingAndDestroy(pawn, helmDef);

            Apparel suit = (Apparel)ThingMaker.MakeThing(suitDef);
            Apparel helm = (Apparel)ThingMaker.MakeThing(helmDef);

            pawn.apparel.Wear(suit, dropReplacedApparel: false, locked: true);
            pawn.apparel.Wear(helm, dropReplacedApparel: false, locked: true);

            return pawn.apparel.WornApparel.Contains(suit) && pawn.apparel.WornApparel.Contains(helm);
        }

        private static void RemoveConflictingAndDestroy(Pawn pawn, ThingDef newApparel)
        {
            foreach (Apparel worn in pawn.apparel.WornApparel.ToList())
            {
                if (!ApparelUtility.CanWearTogether(newApparel, worn.def, pawn.RaceProps.body))
                {
                    pawn.apparel.Remove(worn);
                    worn.Destroy(DestroyMode.Vanish);
                }
            }
        }

        // ───────────────────────── 安全落点（边缘） ─────────────────────────

        /// <summary>
        /// 在地图中定位真正的 CompCerebrexCore。绝不对 IEnumerable&lt;Thing&gt; 使用
        /// OfType&lt;CompCerebrexCore&gt;()——AllThings 中是 Thing，不是 ThingComp。
        /// 先按已知主脑建筑 Def 缩小范围，再安全回退到全图 TryGetComp 扫描（非高频调用）。
        /// </summary>
        public static CompCerebrexCore? FindCerebrexCore(Map map)
        {
            if (map == null)
            {
                return null;
            }

            ThingDef coreDef = ThingDefOf.CerebrexCore;
            if (coreDef != null)
            {
                foreach (Thing t in map.listerThings.ThingsOfDef(coreDef))
                {
                    CompCerebrexCore? comp = t.TryGetComp<CompCerebrexCore>();
                    if (comp != null)
                    {
                        return comp;
                    }
                }
            }

            // 安全回退：遍历地图 Thing，取第一个有效组件。
            foreach (Thing t in map.listerThings.AllThings)
            {
                CompCerebrexCore? comp = t.TryGetComp<CompCerebrexCore>();
                if (comp != null)
                {
                    return comp;
                }
            }

            return null;
        }

        private static bool IsPlatformEdge(IntVec3 c, Map map)
        {
            foreach (IntVec3 n in GenAdj.CardinalDirections.Select(d => c + d))
            {
                if (!n.InBounds(map) || !n.Standable(map))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 分层、有上限、可补充的空投落点搜索：
        /// 1) 原版 EdgeDrop 解析 spawn center 作为搜索锚点之一；
        /// 2) 平台外缘合法格（可站立且相邻不可站立/太空）；
        /// 3) 锚点附近 GenRadial 回退补充（有限尝试）；
        /// 4) 剩余平台合法格回退（有限数量，优先远离地图中心）。
        /// 所有候选必须满足 IsSafeDropCell，且同批互不冲突。
        /// 只有 result.Count == requiredCount 才返回 true。
        /// </summary>
        private static bool TryFindSafeEdgeDropCells(
            Map map,
            int requiredCount,
            SymbiosisCovenantCerebrexSupportDef cfg,
            out List<IntVec3> result,
            Site? site = null,
            int waveIndex = -1)
        {
            result = new List<IntVec3>();

            // 寻路目标仅在方法内计算一次，避免每个候选重复全图扫描。
            // 取得完整主脑核心建筑 Thing（而非仅位置），用于可达性 Touch 判定。
            CompCerebrexCore? coreComp = FindCerebrexCore(map);
            Thing? coreThing = coreComp?.parent;
            IntVec3 target = coreThing?.Position ?? map.Center;

            if (coreThing == null || !coreThing.Spawned || coreThing.Map != map)
            {
                Log.Warning(LogPrefix + " 主脑核心缺失/未生成/不属于当前地图，无法安全生成援军空投落点。"
                    + " site=" + (site != null ? site.ID.ToString() : "?")
                    + " wave=" + waveIndex
                    + " required=" + requiredCount);
                return false;
            }

            // 1) 原版 EdgeDrop 解析 spawn center 作为搜索锚点之一。
            IntVec3 anchor = map.Center;
            IncidentParms edgeParms = new IncidentParms
            {
                target = map,
                raidArrivalMode = PawnsArrivalModeDefOf.EdgeDrop
            };
            if (edgeParms.raidArrivalMode.Worker.TryResolveRaidSpawnCenter(edgeParms)
                && edgeParms.spawnCenter.IsValid)
            {
                anchor = edgeParms.spawnCenter;
            }

            List<IntVec3> candidates = new List<IntVec3>();
            int step = Math.Max(1, cfg.dropCellEdgeCandidateStep);
            int scanBudget = Mathf.Min(map.Size.x * map.Size.z, cfg.dropCellEdgeCandidateLimit * 16);

            // 2) 平台外缘合法格：枚举全图（按步长抽样）以覆盖真实平台边缘，而非仅地图最外圈（#8）。
            //    扫描工作量由 scanBudget 限制（#9）。
            int scanned = 0;
            for (int x = 0; x < map.Size.x && scanned < scanBudget; x += step)
            {
                for (int z = 0; z < map.Size.z && scanned < scanBudget; z += step, scanned++)
                {
                    IntVec3 c = new IntVec3(x, 0, z);
                    if (IsPlatformEdge(c, map) && IsSafeDropCell(c, map, coreThing))
                    {
                        candidates.Add(c);
                    }

                    if (candidates.Count >= cfg.dropCellEdgeCandidateLimit)
                    {
                        break;
                    }
                }

                if (candidates.Count >= cfg.dropCellEdgeCandidateLimit)
                {
                    break;
                }
            }

            // 3) 锚点附近环形补充（受 dropCellSearchRadius / 安全裕度限制，#10）。
            if (candidates.Count < requiredCount)
            {
                int maxRadius = Mathf.RoundToInt(cfg.dropCellSearchRadius * (1f - cfg.dropCellRadialPatternSafetyMargin));
                int r2 = maxRadius * maxRadius;
                int attempt = 0;
                while (attempt < cfg.dropCellAnchorAttemptLimit && candidates.Count < requiredCount)
                {
                    IntVec3 c = anchor + GenRadial.RadialPattern[attempt % GenRadial.RadialPattern.Length];
                    if (c.InBounds(map)
                        && c.DistanceToSquared(anchor) <= r2
                        && IsSafeDropCell(c, map, coreThing)
                        && !candidates.Contains(c))
                    {
                        candidates.Add(c);
                    }

                    attempt++;
                }
            }

            // 4) 核心周边区域补充（dropCellZoneRadius 围绕 target，#10）。
            if (candidates.Count < requiredCount)
            {
                int zoneR2 = Mathf.RoundToInt(cfg.dropCellZoneRadius * cfg.dropCellZoneRadius);
                foreach (IntVec3 off in GenRadial.RadialPatternInRadius(cfg.dropCellZoneRadius))
                {
                    IntVec3 c = target + off;
                    if (c.InBounds(map)
                        && c.DistanceToSquared(target) <= zoneR2
                        && IsSafeDropCell(c, map, coreThing)
                        && !candidates.Contains(c))
                    {
                        candidates.Add(c);
                    }
                }
            }

            // 5) 全图有界回退：扫描工作量受 scanBudget 限制，不再无脑遍历 AllCells（#9）。
            if (candidates.Count < requiredCount)
            {
                scanned = 0;
                for (int x = 0; x < map.Size.x && scanned < scanBudget; x += step)
                {
                    for (int z = 0; z < map.Size.z && scanned < scanBudget; z += step, scanned++)
                    {
                        IntVec3 c = new IntVec3(x, 0, z);
                        if (!candidates.Contains(c) && IsSafeDropCell(c, map, coreThing))
                        {
                            candidates.Add(c);
                        }

                        if (candidates.Count >= cfg.dropCellEdgeCandidateLimit)
                        {
                            break;
                        }
                    }

                    if (candidates.Count >= cfg.dropCellEdgeCandidateLimit)
                    {
                        break;
                    }
                }
            }

            candidates.SortByDescending(c => c.DistanceTo(map.Center));

            // 按最小间距挑选所需数量。
            foreach (IntVec3 c in candidates)
            {
                if (result.Count >= requiredCount)
                {
                    break;
                }

                bool tooClose = false;
                foreach (IntVec3 taken in result)
                {
                    if (taken.DistanceToSquared(c) < cfg.dropCellMinimumSpacingSquared)
                    {
                        tooClose = true;
                        break;
                    }
                }

                if (!tooClose)
                {
                    result.Add(c);
                }
            }

            if (result.Count < requiredCount)
            {
                Log.Warning(LogPrefix + " 落点不足：site=" + (site != null ? site.ID.ToString() : "?")
                    + " wave=" + waveIndex
                    + " required=" + requiredCount
                    + " candidates=" + candidates.Count
                    + " final=" + result.Count
                    + "，放弃本波空投。");
            }

            return result.Count == requiredCount;
        }

        private static bool IsSafeDropCell(IntVec3 c, Map map, Thing? coreThing)
        {
            if (!c.InBounds(map))
            {
                return false;
            }

            if (!c.Standable(map))
            {
                return false;
            }

            if (c.Fogged(map))
            {
                return false;
            }

            if (c.Roofed(map))
            {
                return false;
            }

            if (c.GetEdifice(map) != null)
            {
                return false;
            }

            if (!DropCellFinder.IsGoodDropSpot(c, map, allowFogged: false, canRoofPunch: false, allowIndoors: false))
            {
                return false;
            }

            if (c.GetFirstPawn(map) != null)
            {
                return false;
            }

            if (c.GetFirstBuilding(map) != null)
            {
                return false;
            }

            foreach (Thing t in c.GetThingList(map))
            {
                if (t is Skyfaller)
                {
                    return false;
                }
            }

            if (coreThing != null && coreThing.Spawned && coreThing.Map == map)
            {
                if (!map.reachability.CanReach(
                        c,
                        coreThing,
                        PathEndMode.Touch,
                        TraverseParms.For(TraverseMode.PassDoors)))
                {
                    return false;
                }
            }
            else
            {
                // 核心缺失 / 未生成 / 不在当前地图：不把 map.Center 当作主脑可达性替代目标，
                // 也不绕过可达性校验，直接拒绝该候选。
                return false;
            }

            return true;
        }

        // ───────────────────────── 主脑防御解除通知 ─────────────────────────

        public static void NotifyCoreDefencesLowered(Map map)
        {
            if (map == null || !ModsConfig.OdysseyActive)
            {
                return;
            }

            int now = Find.TickManager.TicksGame;
            foreach (Quest quest in Find.QuestManager.QuestsListForReading)
            {
                foreach (QuestPart_SymbiosisCovenantCerebrexSupport part in quest.PartsListForReading
                             .OfType<QuestPart_SymbiosisCovenantCerebrexSupport>())
                {
                    if (part.site != null && part.site.Map == map)
                    {
                        part.NotifyCoreDefencesLowered(now);
                    }
                }
            }
        }

        // ───────────────────────── 旧存档补装 ─────────────────────────

        public static void BackfillMissingSupportQuestParts()
        {
            if (!ModsConfig.OdysseyActive)
            {
                return;
            }

            foreach (Quest quest in Find.QuestManager.QuestsListForReading)
            {
                if (quest.root == null || quest.root.defName != "Gravcore_Mechhive")
                {
                    continue;
                }

                if (quest.State != QuestState.Ongoing)
                {
                    continue;
                }

                if (quest.PartsListForReading.Any(p => p is QuestPart_SymbiosisCovenantCerebrexSupport))
                {
                    continue;
                }

                QuestPart_CerebrexCore? corePart = quest.PartsListForReading.OfType<QuestPart_CerebrexCore>().FirstOrDefault();
                if (corePart == null)
                {
                    continue;
                }

                Site? site = GetCerebrexCoreSite(corePart);
                if (site == null)
                {
                    continue;
                }

                if (site.MainSitePartDef != SitePartDefOf.OrbitalMechhive)
                {
                    continue;
                }

                string signal = corePart.inSignalEnable
                    ?? QuestGenUtility.HardcodedSignalWithQuestID("site.MapGenerated");

                QuestPart_SymbiosisCovenantCerebrexSupport part =
                    new QuestPart_SymbiosisCovenantCerebrexSupport
                    {
                        site = site,
                        inSignalEnable = signal,
                        mapGeneratedSignal = signal,
                        offerId = quest.id + "_CerebrexSupport"
                    };

                if (site.HasMap && quest.State == QuestState.Ongoing)
                {
                    Map map = site.Map;
                    bool playerEntered = map.mapPawns.AllPawns.Any(p => p.Faction == Faction.OfPlayer);
                    CompCerebrexCore? core = FindCerebrexCore(map);
                    bool hasCore = core != null;
                    bool coreLowered = hasCore && core!.CanInteract().Accepted;

                    if (playerEntered && hasCore)
                    {
                        quest.AddPart(part);
                        part.MarkMapEnteredForBackfill(coreLowered);
                        Log.Message($"{LogPrefix} 旧存档补装支援 QuestPart（quest={quest.id}，coreLowered={coreLowered}）。");
                    }
                }
                else
                {
                    // 地图尚未生成：加入后由 MapGenerated 信号启用。
                    quest.AddPart(part);
                    Log.Message($"{LogPrefix} 旧存档补装支援 QuestPart（地图未生成，quest={quest.id}）。");
                }
            }
        }

        // ───────────────────────── 信件 ─────────────────────────

        public static void RemoveLetterByOfferId(string? offerId)
        {
            if (offerId == null)
            {
                return;
            }

            foreach (ChoiceLetter_SymbiosisCovenantCerebrexSupportOffer letter in Find.LetterStack
                         .LettersListForReading
                         .OfType<ChoiceLetter_SymbiosisCovenantCerebrexSupportOffer>()
                         .Where(l => l.offerId == offerId)
                         .ToList())
            {
                Find.LetterStack.RemoveLetter(letter);
            }
        }

        // ───────────────────────── 撤离 ─────────────────────────

        private static ThingDef? EvacPodDef()
            => DefDatabase<ThingDef>.GetNamed(EvacPodDefName, false);

        // ─────────────────────────────────────────────────────────────────────
        // 撤离辅助方法
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>统一的可用撤离 Pawn 判定：非空、未死亡、未销毁、未丢弃。</summary>
        private static bool IsUsableEvacPawn(Pawn? p)
            => p != null && !p.Dead && !p.Destroyed && !p.Discarded;

        /// <summary>
        /// 统一的破墙目标校验：只有“仍然是当前地图上、仍生成、是墙、有血条且允许摧毁”的
        /// Thing 才能进入破墙流程。必须走这里，不能只依赖 TraverseMode.PassAllDestroyableThings
        /// 或只检查 def.IsWall / useHitPoints / destroyable 其中之一。
        /// 原版 PathUtility.IsDestroyable 同时检查 def.useHitPoints 与 def.destroyable。
        /// </summary>
        private static bool IsValidEvacuationBreachWall(Thing? thing, Map? map)
        {
            if (thing == null || map == null)
            {
                return false;
            }

            if (thing.Destroyed || !thing.Spawned)
            {
                return false;
            }

            if (thing.Map != map)
            {
                return false;
            }

            if (!thing.def.IsWall)
            {
                return false;
            }

            // 门 / 栅栏 / 炮塔 / 普通建筑已由 IsWall 排除；此处排除不可摧毁或无血条的墙。
            return PathUtility.IsDestroyable(thing);
        }

        /// <summary>
        /// 援军「实际登上撤离载具」后移除其全部 Hediff_Injury 伤口（含已永久化的伤疤）。
        /// 只处理：属于本载具 rec.pawns、已死亡/销毁/丢弃者排除、且确实位于当前载具
        /// innerContainer 内的 Pawn。不处理仍在地图、正在走向载具、已被释放或属于其他载具的 Pawn。
        /// 只移除 Hediff_Injury，绝不动 Hediff_MissingPart / 疾病 / 植入体 / 基因 / 机械族专用 Hediff。
        /// </summary>
        private static void HealLoadedEvacuationPawns(
            SymbiosisCovenantCerebrexSupportEvacVehicle rec,
            CompTransporter? transporter)
        {
            if (rec == null || transporter == null)
            {
                return;
            }

            if (rec.pawns == null || rec.pawns.Count == 0)
            {
                return;
            }

            ThingOwner? inner = transporter.innerContainer;
            if (inner == null)
            {
                return;
            }

            foreach (Pawn pawn in inner.OfType<Pawn>().ToList())
            {
                if (!IsUsableEvacPawn(pawn))
                {
                    continue;
                }

                if (!rec.pawns.Contains(pawn))
                {
                    continue;
                }

                if (rec.releasedPawns != null && rec.releasedPawns.Contains(pawn))
                {
                    continue;
                }

                // 下列条件已由 IsUsableEvacPawn 覆盖，此处显式保留以保证语义不被后续改动破坏。
                if (pawn.Dead || pawn.Destroyed || pawn.Discarded)
                {
                    continue;
                }

                if (pawn.health?.hediffSet?.hediffs == null)
                {
                    continue;
                }

                // 先建快照再删除，严禁在原始 hediffs 列表上边遍历边删除。
                List<Hediff> injuries = pawn.health.hediffSet.hediffs
                    .Where(h => h is Hediff_Injury)
                    .ToList();
                if (injuries.Count == 0)
                {
                    continue;
                }

                foreach (Hediff injury in injuries)
                {
                    pawn.health.RemoveHediff(injury);
                }
            }
        }

        /// <summary>
        /// 在把 Pawn 加入新的撤离 Lord 之前，先从它们当前的旧 Lord（战斗 Lord）正式移除，
        /// 避免同一 Pawn 同时隶属两个 Lord 导致原版报错。仅移除本次名单中的 Pawn，
        /// 并在旧 Lord 不再拥有任何 Pawn 时从地图 LordManager 清理空 Lord。
        /// 已在目标载具内部容器中的 Pawn 不重复加入新的装载 Lord（由调用方名单排除）。
        /// </summary>
        private static void DetachPawnsFromOldLords(IEnumerable<Pawn> pawns, Map map)
        {
            if (map == null)
            {
                return;
            }

            HashSet<Lord> touchedLords = new HashSet<Lord>();
            foreach (Pawn pawn in pawns)
            {
                if (pawn == null)
                {
                    continue;
                }

                Lord? oldLord = pawn.GetLord();
                if (oldLord != null)
                {
                    oldLord.RemovePawn(pawn);
                    touchedLords.Add(oldLord);
                }
            }

            foreach (Lord lord in touchedLords)
            {
                if (lord.ownedPawns.Count == 0 && map.lordManager.lords.Contains(lord))
                {
                    map.lordManager.RemoveLord(lord);
                }
            }
        }

        /// <summary>
        /// 为本载具当前名单调度“存活援军搬运倒地存活援军”的搬运任务。
        /// 仅针对当前这艘载具的当前名单；不改动玩家搬运系统，也不给玩家 Pawn 下达任务。
        /// </summary>
        private static void TryAssignHaulersForDowned(
            SymbiosisCovenantCerebrexSupportEvacVehicle rec,
            Map map,
            Thing? transporterThing)
        {
            if (rec == null || map == null || transporterThing == null)
            {
                return;
            }

            // 装载中或已落地待装载阶段才调度搬运。
            if (rec.stage != CerebrexSupportEvacVehicleStage.Loading
                && rec.stage != CerebrexSupportEvacVehicleStage.Landed)
            {
                return;
            }

            Thing t = transporterThing;

            // 当前仍需撤离的倒地存活援军（仍在地图、已生成、尚未进入载具容器）。
            List<Pawn> downedTargets = rec.pawns
                .Where(p => IsUsableEvacPawn(p)
                            && p.Spawned && p.Map == map && p.Downed
                            && !IsInTransporterContainer(p, t))
                .ToList();
            if (downedTargets.Count == 0)
            {
                return;
            }

            // 已被本载具搬运任务占用的倒地目标与搬运者。
            HashSet<Pawn> claimedTargets = new HashSet<Pawn>();
            HashSet<Pawn> busyHaulers = new HashSet<Pawn>();
            foreach (Pawn other in map.mapPawns.AllPawnsSpawned)
            {
                if (other.CurJobDef != JobDefOf.HaulToTransporter)
                {
                    continue;
                }

                JobDriver_HaulToTransporter? drv = other.jobs.curDriver as JobDriver_HaulToTransporter;
                if (drv == null || drv.Transporter?.parent != t)
                {
                    continue;
                }

                busyHaulers.Add(other);
                if (drv.ThingToCarry is Pawn carried && downedTargets.Contains(carried))
                {
                    claimedTargets.Add(carried);
                }
            }

            // 候选搬运者：本记录中存活、在地图、未倒地、非玩家殖民者/机械族、能操作并能到达目标与载具的盟友。
            List<Pawn> candidates = rec.pawns
                .Where(p => IsUsableEvacPawn(p)
                            && p.Spawned && p.Map == map && !p.Downed
                            && !p.IsColonist && !p.IsColonyMech
                            && !busyHaulers.Contains(p))
                .ToList();

            foreach (Pawn target in downedTargets)
            {
                if (claimedTargets.Contains(target))
                {
                    continue;
                }

                Pawn? hauler = candidates.FirstOrDefault(h => HaulerCanReach(h, target, t));
                if (hauler == null)
                {
                    continue;
                }

                Job job = JobMaker.MakeJob(JobDefOf.HaulToTransporter, target, t);
                job.ignoreForbidden = true;
                hauler.jobs.TryTakeOrderedJob(job);
                claimedTargets.Add(target);
                candidates.Remove(hauler);
            }
        }

        private static bool IsInTransporterContainer(Pawn p, Thing transporterThing)
        {
            CompTransporter? ct = transporterThing.TryGetComp<CompTransporter>();
            return ct != null && ct.innerContainer.Contains(p);
        }

        private static bool HaulerCanReach(Pawn hauler, Pawn target, Thing transporterThing)
        {
            return hauler.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)
                && hauler.CanReach(target, PathEndMode.Touch, Danger.Deadly)
                && hauler.CanReach(transporterThing, PathEndMode.Touch, Danger.Deadly);
        }

        public static void BeginEvacuation(QuestPart_SymbiosisCovenantCerebrexSupport part, Map map, int now)
        {
            Dictionary<Faction, List<Pawn>> groups = GatherEvacPawns(part, map);
            if (groups.Count == 0)
            {
                Log.Message($"{LogPrefix} 没有合法存活援军，直接完成撤离（site={part.site?.Label}）。");
                part.MarkSupportCompleted(sendEvacDoneMessage: false);
                return;
            }

            foreach (KeyValuePair<Faction, List<Pawn>> pair in groups)
            {
                float capacity = GetEvacVehicleMassCapacity(part.evacMode);
                if (!TryPackPawnsByMass(pair.Value, capacity, out List<List<Pawn>> loads))
                {
                    part.MarkInvalid("存在超过撤离载具质量上限的盟约援军，已停止自动撤离以避免丢失 Pawn。");
                    return;
                }

                foreach (List<Pawn> load in loads)
                {
                    part.evacVehicles.Add(new SymbiosisCovenantCerebrexSupportEvacVehicle
                    {
                        mode = part.evacMode, faction = pair.Key, pawns = load
                    });
                }
            }

            Find.LetterStack.ReceiveLetter(
                "MAP_SymbiosisCovenant_CerebrexSupport_EvacTitle".Translate(),
                "MAP_SymbiosisCovenant_CerebrexSupport_EvacBody".Translate(), LetterDefOf.PositiveEvent);
            part.stage = QuestPart_SymbiosisCovenantCerebrexSupport.CerebrexSupportStage.EvacuationPreparing;
            part.loadingStartTick = now;
        }

        private static float GetEvacVehicleMassCapacity(CerebrexSupportEvacMode mode)
        {
            if (mode == CerebrexSupportEvacMode.RoyaltyShuttle)
            {
                return ThingDefOf.Shuttle.GetCompProperties<CompProperties_Transporter>()?.massCapacity ?? 150f;
            }

            ThingDef? podDef = EvacPodDef();
            float fallback = Config.evacPodMassCapacityFallback;
            float capacity = podDef?.GetCompProperties<CompProperties_Transporter>()?.massCapacity ?? fallback;
            return capacity > 0f ? capacity : fallback;
        }

        private static bool TryPackPawnsByMass(List<Pawn> pawns, float capacity, out List<List<Pawn>> loads)
        {
            loads = new List<List<Pawn>>();
            if (capacity <= 0f) return false;
            List<float> loadMasses = new List<float>();
            foreach (Pawn pawn in pawns)
            {
                float mass = CollectionsMassCalculator.MassUsage(new List<Thing> { pawn },
                    IgnorePawnsInventoryMode.IgnoreIfAssignedToUnload, includePawnsMass: true);
                if (mass > capacity)
                {
                    Log.Error($"{LogPrefix} Pawn {pawn.LabelShortCap} 质量 {mass:0.##} 超过撤离载具上限 {capacity:0.##}，不生成不可能装载的载具。");
                    return false;
                }

                int index = -1;
                for (int i = 0; i < loads.Count; i++)
                {
                    if (loadMasses[i] + mass <= capacity) { index = i; break; }
                }
                if (index < 0)
                {
                    loads.Add(new List<Pawn>());
                    loadMasses.Add(0f);
                    index = loads.Count - 1;
                }
                loads[index].Add(pawn);
                loadMasses[index] += mass;
            }
            return loads.Count > 0;
        }

        private static Dictionary<Faction, List<Pawn>> GatherEvacPawns(
            QuestPart_SymbiosisCovenantCerebrexSupport part,
            Map map)
        {
            Dictionary<Faction, List<Pawn>> dict = new Dictionary<Faction, List<Pawn>>();
            foreach (SymbiosisCovenantCerebrexSupportWaveRecord wave in part.waves)
            {
                foreach (Pawn p in wave.pawns)
                {
                    if (!IsUsableEvacPawn(p))
                    {
                        continue;
                    }

                    if (p.Faction == null || p.Faction.HostileTo(Faction.OfPlayer))
                    {
                        continue;
                    }

                    bool onMap = p.Spawned && p.Map == map;
                    bool inContainer = part.evacVehicles.Any(v => v.pawns.Contains(p));
                    if (!onMap && !inContainer)
                    {
                        continue;
                    }

                    if (!dict.TryGetValue(p.Faction, out List<Pawn>? list))
                    {
                        list = new List<Pawn>();
                        dict[p.Faction] = list;
                    }

                    list.Add(p);
                }
            }

            return dict;
        }



        public static void TickEvacuation(QuestPart_SymbiosisCovenantCerebrexSupport part, Map map, int now)
        {
            if (part.evacMode == CerebrexSupportEvacMode.RoyaltyShuttle)
            {
                TickRoyaltyEvacuation(part, map, now);
            }
            else if (part.evacMode == CerebrexSupportEvacMode.OdysseyMechPod)
            {
                // 注意：找不到撤离舱 Def 时，绝不能把仍在地图的援军标记为已安全撤离。
                // 交由正常重试逻辑处理（SpawnEmptyEvacPod 会把记录置为 SpawnRetryWaiting 并重试）。
                TickMechPodEvacuation(part, map, now);
            }

            if (part.evacVehicles.Count > 0 && part.evacVehicles.All(v => v.IsCompleted))
            {
                // 仅当至少一名被追踪援军仍存活时，才发送“安全撤离”成功提示；若全部死亡/销毁/丢弃，则仅完成清理，不谎报撤离成功。
                bool anyEvacuated = part.evacVehicles.Any(v => v.launchedPawnCount > 0);
                part.MarkSupportCompleted(sendEvacDoneMessage: anyEvacuated);
            }
        }

        // ── 皇权穿梭机撤离 ──

        private static void TickRoyaltyEvacuation(
            QuestPart_SymbiosisCovenantCerebrexSupport part,
            Map map,
            int now)
        {
            // 1) 收集需要生成穿梭机的记录（NotRequested / 生成重试 / 失败重试，且已过重试节流）。
            List<SymbiosisCovenantCerebrexSupportEvacVehicle> pending = part.evacVehicles
                .Where(v => v.vehicleThing == null
                            && (v.stage == CerebrexSupportEvacVehicleStage.NotRequested
                                || v.stage == CerebrexSupportEvacVehicleStage.SpawnRetryWaiting
                                || v.stage == CerebrexSupportEvacVehicleStage.FailedRetryable)
                            && (v.nextRetryTick < 0 || now >= v.nextRetryTick))
                .ToList();

            if (pending.Count > 0)
            {
                // 整批预验证落点：全部找到才生成，否则本批一艘也不生成。
                List<IntVec3> spots = new List<IntVec3>();
                foreach (SymbiosisCovenantCerebrexSupportEvacVehicle rec in pending)
                {
                    IntVec3 spot = FindShuttleLandingSpot(map, spots);
                    if (!spot.IsValid)
                    {
                        spots.Clear();
                        break;
                    }

                    spots.Add(spot);
                }

                if (spots.Count < pending.Count)
                {
                    int retry = now + Config.evacuationRetryTicks;
                    foreach (SymbiosisCovenantCerebrexSupportEvacVehicle rec in pending)
                    {
                        rec.stage = CerebrexSupportEvacVehicleStage.SpawnRetryWaiting;
                        rec.nextRetryTick = retry;
                    }

                    Log.Warning($"{LogPrefix} 皇权穿梭机整批落点预验证失败，延迟重试（site={part.site?.Label}）。");
                    return;
                }

                for (int i = 0; i < pending.Count; i++)
                {
                    pending[i].landingCell = spots[i];
                    pending[i].stage = CerebrexSupportEvacVehicleStage.LandingRequested;
                    pending[i].stateChangedTick = now;
                    TryCreateRoyaltyShuttle(part, map, pending[i], spots[i]);
                }

                return; // 等待穿梭机到达
            }

            // 2) 处理已生成 / 正在装载 / 正在离开 / 失败重试 的记录。
            foreach (SymbiosisCovenantCerebrexSupportEvacVehicle rec in part.evacVehicles)
            {
                if (rec.IsCompleted)
                {
                    continue;
                }

                if (rec.stage == CerebrexSupportEvacVehicleStage.FailedRetryable
                    || rec.stage == CerebrexSupportEvacVehicleStage.SpawnRetryWaiting)
                {
                    if (rec.nextRetryTick >= 0 && now >= rec.nextRetryTick)
                    {
                        rec.nextRetryTick = -1;
                        rec.stage = rec.vehicleThing != null
                            ? CerebrexSupportEvacVehicleStage.Landed
                            : CerebrexSupportEvacVehicleStage.NotRequested;
                    }

                    continue;
                }

                if (rec.stage == CerebrexSupportEvacVehicleStage.LandingRequested
                    || rec.stage == CerebrexSupportEvacVehicleStage.Landed
                    || rec.stage == CerebrexSupportEvacVehicleStage.Loading)
                {
                    // 严格区分“已创建运输船”与“穿梭机实际落地可装载”：未真正落地前保持等待，
                    // 不安排搬运、不刷新 requiredPawns、不判断装载、不触发离场、不创建 Lord、不标记完成/装载/离图。
                    if (!IsShuttleLandedAndLoadable(rec, map))
                    {
                        // 载具仍在生成 / 异步抵达中：vehicleThing 一旦丢失则标记失败重试，否则仅等待。
                        if (rec.vehicleThing == null && rec.stage != CerebrexSupportEvacVehicleStage.LandingRequested)
                        {
                            rec.stage = CerebrexSupportEvacVehicleStage.FailedRetryable;
                            rec.nextRetryTick = now + Config.evacuationRetryTicks;
                        }

                        continue;
                    }

                    CompShuttle compShuttle = rec.vehicleThing!.TryGetComp<CompShuttle>()!;
                    TransportShip ship = compShuttle.shipParent!;

                    // 穿梭机首次实际落地：先从战斗 Lord 正式移除这些 Pawn，再创建撤离 Lord（仅一次），
                    // 避免“同时属于两个 Lord”报错，且不在 ShipJob_Arrive 抵达期间就让援军登船。
                    if (rec.stage == CerebrexSupportEvacVehicleStage.LandingRequested)
                    {
                        List<Pawn> evacPawns = rec.pawns.Where(p => IsUsableEvacPawn(p)).ToList();
                        DetachPawnsFromOldLords(evacPawns, map);
                        LordMaker.MakeNewLord(
                            rec.faction ?? Faction.OfPlayer,
                            new LordJob_ExitOnShuttle(compShuttle.parent, addFleeToil: false),
                            map,
                            evacPawns);
                    }

                    rec.stage = CerebrexSupportEvacVehicleStage.Landed;

                    // 援军实际登机后移除其全部伤口（仅限已位于本穿梭机 innerContainer 的本载具援军）。
                    // 必须在 CountLoadedUsablePawns / AllRequiredThingsLoaded / 满载发射 /
                    // TickPartialLoadDeparture / ForceDepartPartialLoadShuttle 统计之前执行。
                    HealLoadedEvacuationPawns(rec, compShuttle.Transporter);

                    // 调度存活援军搬运倒地存活援军；并在发射前刷新实际名单，避免死亡/销毁/丢弃 Pawn 阻塞离图。
                    TryAssignHaulersForDowned(rec, map, rec.vehicleThing);
                    compShuttle.requiredPawns = rec.pawns.Where(p => IsUsableEvacPawn(p)).ToList();

                    // 首次路线分类与破墙（限制性能消耗），以及半数登机后强制发射。
                    TickEvacuationBreach(part, rec, rec.vehicleThing!, map, now);
                    TickPartialLoadDeparture(part, rec, rec.vehicleThing!, map, now);

                    if (compShuttle.AllRequiredThingsLoaded
                        && rec.stage != CerebrexSupportEvacVehicleStage.Departing)
                    {
                        // 仅发射一次：切换 Departing，绝不把 vehicleThing 置空。
                        // 记录实际已装入的有效 Pawn，供“安全撤离”提示判定使用。
                        rec.launchedPawnCount = compShuttle.Transporter.innerContainer
                            .OfType<Pawn>()
                            .Count(p => IsUsableEvacPawn(p) && rec.pawns.Contains(p));
                        ship.ForceJob(ShipJobDefOf.FlyAway);
                        rec.stage = CerebrexSupportEvacVehicleStage.Departing;
                        rec.stateChangedTick = now;
                    }

                    continue;
                }

                if (rec.stage == CerebrexSupportEvacVehicleStage.Departing)
                {
                    bool shipLeft = rec.vehicleThing == null
                        || !rec.vehicleThing.Spawned
                        || rec.vehicleThing.Map != map;
                    bool pawnsGone = !rec.HasUnreleasedTrackedPawnStillOnSupportMap(map);

                    // 严格判定：载具已离图且所有被追踪 Pawn 均不在支援地图（#16）。
                    if (shipLeft && pawnsGone)
                    {
                        rec.stage = CerebrexSupportEvacVehicleStage.Completed;
                        rec.stateChangedTick = now;
                        Log.Message($"{LogPrefix} 皇权穿梭机撤离记录完成（site={part.site?.Label}）。");
                    }
                }
            }
        }

        /// <summary>
        /// 严格区分“已创建运输船”与“穿梭机实际落地可装载”。
        /// 必须：载具已生成到当前主脑地图、能取得 CompShuttle、shipParent 已就绪，
        /// 且原版 TransportShip 已进入 ShipJob_Wait（抵达完成、停在地图等待装载），
        /// 而非仍处于 ShipJob_Arrive 异步抵达（此时载具只是作为天空坠落物在飞行，尚未可装载）。
        /// </summary>
        private static bool IsShuttleLandedAndLoadable(
            SymbiosisCovenantCerebrexSupportEvacVehicle rec, Map map)
        {
            Thing? vehicle = rec.vehicleThing;
            if (vehicle == null || !vehicle.Spawned || vehicle.Map != map)
            {
                return false;
            }

            CompShuttle? compShuttle = vehicle.TryGetComp<CompShuttle>();
            TransportShip? ship = compShuttle?.shipParent;
            if (ship == null)
            {
                return false;
            }

            // Waiting == ShipExistsAndIsSpawned && curJob is ShipJob_Wait：
            // 仅在抵达完成、停在地图等待装载时才视为可装载，排除 ShipJob_Arrive 飞行中状态。
            return ship.Waiting;
        }

        private static IntVec3 FindShuttleLandingSpot(Map map, List<IntVec3> taken)
        {
            Rot4 rotation = ThingDefOf.Shuttle.defaultPlacingRot;
            HashSet<IntVec3> reserved = new HashSet<IntVec3>();
            foreach (IntVec3 center in taken)
            {
                if (!center.IsValid) continue;
                reserved.UnionWith(GenAdj.CellsOccupiedBy(center, rotation, ThingDefOf.Shuttle.size));
                reserved.Add(ThingUtility.InteractionCellWhenAt(ThingDefOf.Shuttle, center, rotation, map));
            }

            bool IsFree(IntVec3 center)
            {
                if (reserved.Contains(ThingUtility.InteractionCellWhenAt(ThingDefOf.Shuttle, center, rotation, map)))
                {
                    return false;
                }

                return !GenAdj.CellsOccupiedBy(center, rotation, ThingDefOf.Shuttle.size).Any(reserved.Contains);
            }

            IntVec3 best = DropCellFinder.GetBestShuttleLandingSpot(map, Faction.OfPlayer);
            if (best.IsValid && RoyalTitlePermitWorker_CallShuttle.ShuttleCanLandHere(best, map).Accepted && IsFree(best))
            {
                return best;
            }

            for (int attempt = 0; attempt < Config.dropCellAnchorAttemptLimit; attempt++)
            {
                IntVec3 cell = (best.IsValid ? best : map.Center)
                    + GenRadial.RadialPattern[attempt % GenRadial.RadialPattern.Length];
                if (cell.InBounds(map)
                    && RoyalTitlePermitWorker_CallShuttle.ShuttleCanLandHere(cell, map).Accepted
                    && IsFree(cell))
                {
                    return cell;
                }
            }

            return IntVec3.Invalid;
        }

        private static void TryCreateRoyaltyShuttle(
            QuestPart_SymbiosisCovenantCerebrexSupport part,
            Map map,
            SymbiosisCovenantCerebrexSupportEvacVehicle rec,
            IntVec3 spot)
        {
            List<Pawn> pawns = rec.pawns
                .Where(p => IsUsableEvacPawn(p))
                .ToList();
            if (pawns.Count == 0)
            {
                // 无存活 Pawn：直接完成，不生成空穿梭机。
                rec.stage = CerebrexSupportEvacVehicleStage.Completed;
                return;
            }

            if (!RoyalTitlePermitWorker_CallShuttle.ShuttleCanLandHere(spot, map).Accepted)
            {
                rec.stage = CerebrexSupportEvacVehicleStage.SpawnRetryWaiting;
                rec.nextRetryTick = Find.TickManager.TicksGame + Config.evacuationRetryTicks;
                return;
            }

            Thing shuttle = ThingMaker.MakeThing(ThingDefOf.Shuttle);
            CompShuttle compShuttle = shuttle.TryGetComp<CompShuttle>();
            compShuttle.permitShuttle = false;
            // 仅由严格的援军 requiredPawns 名单控制，不额外开启儿童接纳。
            compShuttle.acceptChildren = false;
            compShuttle.requiredPawns = pawns;
            shuttle.SetFaction(Faction.OfEmpire);
            TransportShip ship = TransportShipMaker.MakeTransportShip(TransportShipDefOf.Ship_Shuttle, null, shuttle);
            ship.ArriveAt(spot, map.Parent);

            // 仅记录载具与状态；撤离 Lord 的创建推迟到穿梭机实际落地（见 TickRoyaltyEvacuation 的落地判定），
            // 避免在 ShipJob_Arrive 异步抵达期间就让援军对未落地载具寻路/登船。
            rec.vehicleThing = shuttle;
            rec.ResetEvacuationBreachClassification();
            rec.stateChangedTick = Find.TickManager.TicksGame;
        }

        // ── 奥德赛机械空投仓撤离 ──

        private static void TickMechPodEvacuation(
            QuestPart_SymbiosisCovenantCerebrexSupport part,
            Map map,
            int now)
        {
            ThingDef? podDef = EvacPodDef();
            if (podDef == null)
            {
                part.MarkInvalid("撤离舱 Def 缺失，未生成载具且未移除任何盟约援军。");
                return;
            }

            // 1) 收集需要生成空舱的记录（NotRequested / 生成重试 / 失败重试），整批预验证落点。
            List<SymbiosisCovenantCerebrexSupportEvacVehicle> pending = part.evacVehicles
                .Where(v => v.vehicleThing == null
                            && (v.stage == CerebrexSupportEvacVehicleStage.NotRequested
                                || v.stage == CerebrexSupportEvacVehicleStage.SpawnRetryWaiting
                                || v.stage == CerebrexSupportEvacVehicleStage.FailedRetryable)
                            && (v.nextRetryTick < 0 || now >= v.nextRetryTick))
                .ToList();

            if (pending.Count > 0)
            {
                if (!TryFindSafeEdgeDropCells(map, pending.Count, Config, out List<IntVec3> spots)
                    || spots.Count < pending.Count)
                {
                    int retry = now + Config.evacuationRetryTicks;
                    foreach (SymbiosisCovenantCerebrexSupportEvacVehicle rec in pending)
                    {
                        // 已有残留建筑则复用，否则进入生成重试；绝不谎报完成（#12）。
                        rec.stage = rec.vehicleThing != null
                            ? CerebrexSupportEvacVehicleStage.Landed
                            : CerebrexSupportEvacVehicleStage.SpawnRetryWaiting;
                        rec.nextRetryTick = retry;
                    }

                    return;
                }

                for (int i = 0; i < pending.Count; i++)
                {
                    pending[i].landingCell = spots[i];
                    pending[i].stage = CerebrexSupportEvacVehicleStage.LandingRequested;
                    pending[i].stateChangedTick = now;
                    SpawnEmptyEvacPod(map, pending[i], spots[i], podDef);
                }

                return; // 等待建筑落地
            }

            // 2) 重试推进：生成失败 -> NotRequested；装载失败（舱体仍在）-> Landed 复用同一舱体。
            foreach (SymbiosisCovenantCerebrexSupportEvacVehicle rec in part.evacVehicles)
            {
                if (rec.stage == CerebrexSupportEvacVehicleStage.SpawnRetryWaiting
                    || rec.stage == CerebrexSupportEvacVehicleStage.FailedRetryable)
                {
                    if (rec.nextRetryTick >= 0 && now >= rec.nextRetryTick)
                    {
                        rec.nextRetryTick = -1;
                        if (rec.vehicleThing != null && rec.vehicleThing.Spawned
                            && rec.vehicleThing.Map == map
                            && rec.vehicleThing.TryGetComp<CompTransporter>() != null)
                        {
                            rec.stage = CerebrexSupportEvacVehicleStage.Landed;
                        }
                        else
                        {
                            rec.vehicleThing = null;
                            rec.stage = CerebrexSupportEvacVehicleStage.NotRequested;
                        }
                    }

                    continue;
                }

                if (rec.stage == CerebrexSupportEvacVehicleStage.LoadingRetryWaiting)
                {
                    if (rec.nextRetryTick >= 0 && now >= rec.nextRetryTick)
                    {
                        rec.nextRetryTick = -1;
                        // 复用同一撤离舱。旧装载 Lord 必须先移除，否则会出现两个 Lord。
                        CompTransporter? retryTransporter = rec.vehicleThing?.TryGetComp<CompTransporter>();
                        if (rec.vehicleThing != null && rec.vehicleThing.Spawned
                            && rec.vehicleThing.Map == map && retryTransporter != null)
                        {
                            RemoveLoadingLord(retryTransporter, rec.groupID, map);
                            rec.stage = CerebrexSupportEvacVehicleStage.Landed;
                        }
                        else
                        {
                            rec.vehicleThing = null;
                            rec.stage = CerebrexSupportEvacVehicleStage.SpawnRetryWaiting;
                            rec.nextRetryTick = now + Config.evacuationRetryTicks;
                        }
                    }

                    continue;
                }
            }

            // 3) LandingRequested 但建筑尚未找回：等待落地建筑，超时则复用或生成重试（不删除 Pawn）。
            foreach (SymbiosisCovenantCerebrexSupportEvacVehicle rec in part.evacVehicles
                         .Where(v => v.stage == CerebrexSupportEvacVehicleStage.LandingRequested))
            {
                Thing? building = FindSpawnedPod(map, rec.landingCell, podDef);
                if (building != null)
                {
                    if (rec.faction != null)
                    {
                        building.SetFaction(rec.faction);
                    }

                    rec.vehicleThing = building;
                    rec.stage = CerebrexSupportEvacVehicleStage.Landed;
                    rec.stateChangedTick = now;
                }
                else if (rec.stateChangedTick >= 0
                         && now - rec.stateChangedTick > Config.evacuationLoadingTimeoutTicks)
                {
                    // 空舱始终未落地（生成失败）：有残留建筑则复用，否则生成重试。绝不谎报完成。
                    rec.stage = rec.vehicleThing != null
                        ? CerebrexSupportEvacVehicleStage.Landed
                        : CerebrexSupportEvacVehicleStage.SpawnRetryWaiting;
                    rec.nextRetryTick = now + Config.evacuationRetryTicks;
                }
            }

            // 4) 装载与发射。
            foreach (SymbiosisCovenantCerebrexSupportEvacVehicle rec in part.evacVehicles)
            {
                if (rec.IsCompleted
                    || rec.stage == CerebrexSupportEvacVehicleStage.NotRequested
                    || rec.stage == CerebrexSupportEvacVehicleStage.SpawnRetryWaiting
                    || rec.stage == CerebrexSupportEvacVehicleStage.LandingRequested)
                {
                    continue;
                }

                if (rec.vehicleThing == null)
                {
                    continue;
                }

                CompTransporter? ct = rec.vehicleThing.TryGetComp<CompTransporter>();
                if (ct == null)
                {
                    continue;
                }

                // 援军实际登机后移除其全部伤口（仅限已位于本撤离舱 innerContainer 的本载具援军）。
                // 必须在 allLoaded 判断 / TickPartialLoadDeparture / ForceDepartPartialLoadMechPod /
                // LaunchMechEvacPod / launchedPawnCount 统计之前执行。
                if (IsMechPodLandedAndLoadable(rec, map))
                {
                    HealLoadedEvacuationPawns(rec, ct);
                }

                if (rec.stage == CerebrexSupportEvacVehicleStage.Landed)
                {
                    BeginPodLoading(rec, ct, map);
                }

                if (rec.stage == CerebrexSupportEvacVehicleStage.Loading)
                {
                    TryAssignHaulersForDowned(rec, map, rec.vehicleThing);
                }

                if (rec.stage == CerebrexSupportEvacVehicleStage.Loading
                    || rec.stage == CerebrexSupportEvacVehicleStage.LoadingRetryWaiting)
                {
                    // 首次路线分类与破墙，以及半数登机后强制发射。
                    TickEvacuationBreach(part, rec, rec.vehicleThing!, map, now);
                    TickPartialLoadDeparture(part, rec, rec.vehicleThing!, map, now);
                }

                List<Pawn> stillIn = ct.innerContainer.OfType<Pawn>().ToList();
                bool allLoaded = rec.pawns
                    .Where(p => IsUsableEvacPawn(p))
                    .All(p => stillIn.Contains(p));

                if (allLoaded && rec.stage == CerebrexSupportEvacVehicleStage.Loading)
                {
                    LaunchMechEvacPod(part, rec, map);
                }
                else if (!allLoaded
                         && rec.stage == CerebrexSupportEvacVehicleStage.Loading
                         && rec.stateChangedTick >= 0
                         && now - rec.stateChangedTick > Config.evacuationLoadingTimeoutTicks)
                {
                    // 装载超时：舱体仍在（含已装入 Pawn）则复用同一舱体重试装载；
                    // 舱体丢失则重新生成。绝不重复生成新舱、绝不谎报完成（#1/#12/#15）。
                    if (rec.vehicleThing != null && rec.vehicleThing.Spawned && rec.vehicleThing.Map == map)
                    {
                        // 保留同一舱体及已在 innerContainer 的 Pawn；仅结束旧装载 Lord，下一次只重派仍在地图上的 Pawn。
                        RemoveLoadingLord(ct, rec.groupID, map);
                        rec.stage = CerebrexSupportEvacVehicleStage.LoadingRetryWaiting;
                    }
                    else
                    {
                        rec.stage = CerebrexSupportEvacVehicleStage.SpawnRetryWaiting;
                    }

                    rec.nextRetryTick = now + Config.evacuationRetryTicks;
                }
            }

            // 5) Departing -> Completed：严格判定载具已离图且所有被追踪 Pawn 均不在支援地图（#16）。
            foreach (SymbiosisCovenantCerebrexSupportEvacVehicle rec in part.evacVehicles
                         .Where(v => v.stage == CerebrexSupportEvacVehicleStage.Departing))
            {
                bool left = rec.vehicleThing == null || !rec.vehicleThing.Spawned || rec.vehicleThing.Map != map;
                bool pawnsGone = !rec.HasUnreleasedTrackedPawnStillOnSupportMap(map);

                if (left && pawnsGone)
                {
                    rec.stage = CerebrexSupportEvacVehicleStage.Completed;
                    rec.stateChangedTick = now;
                }
            }
        }

        private static Thing? FindSpawnedPod(Map map, IntVec3 cell, ThingDef? podDef)
        {
            if (podDef == null)
            {
                return null;
            }

            foreach (Thing t in map.listerThings.ThingsOfDef(podDef))
            {
                if (t.Position.DistanceToSquared(cell) <= 4
                    && t.TryGetComp<CompTransporter>() != null)
                {
                    return t;
                }
            }

            return null;
        }

        private static void SpawnEmptyEvacPod(
            Map map,
            SymbiosisCovenantCerebrexSupportEvacVehicle rec,
            IntVec3 spot,
            ThingDef? podDef)
        {
            if (podDef == null)
            {
                rec.stage = CerebrexSupportEvacVehicleStage.Invalid;
                Log.Error($"{LogPrefix} 撤离舱 Def 缺失，停止该记录以避免把仍在地图的 Pawn 误报为已撤离。");
                return;
            }

            ActiveTransporterInfo info = new ActiveTransporterInfo
            {
                sentTransporterDef = podDef,
                openDelay = Config.dropPodOpenDelayTicks
            };
            DropPodUtility.MakeDropPodAt(spot, map, info, Faction.OfMechanoids);
            rec.ResetEvacuationBreachClassification();
            rec.stage = CerebrexSupportEvacVehicleStage.LandingRequested;
            rec.stateChangedTick = Find.TickManager.TicksGame;
        }

        private static void RemoveLoadingLord(CompTransporter transporter, int recordedGroupID, Map map)
        {
            int groupID = recordedGroupID >= 0 ? recordedGroupID : transporter.groupID;
            if (groupID >= 0)
            {
                Lord? lord = TransporterUtility.FindLord(groupID, map);
                if (lord != null)
                {
                    map.lordManager.RemoveLord(lord);
                }
            }

            transporter.groupID = -1;
            transporter.leftToLoad = new List<TransferableOneWay>();
        }

        private static void BeginPodLoading(
            SymbiosisCovenantCerebrexSupportEvacVehicle rec,
            CompTransporter ct,
            Map map)
        {
            List<Pawn> living = rec.pawns
                .Where(p => IsUsableEvacPawn(p))
                .ToList();
            if (living.Count == 0)
            {
                rec.stage = CerebrexSupportEvacVehicleStage.Completed;
                return;
            }

            HashSet<Pawn> alreadyLoaded = new HashSet<Pawn>(ct.innerContainer.OfType<Pawn>());
            List<Pawn> pawnsToLoad = living
                .Where(p => !alreadyLoaded.Contains(p) && p.Spawned && p.Map == map)
                .ToList();

            // 不调用 CleanUpLoadingVars：它会把 innerContainer 中已经装好的 Pawn 丢回地图。
            RemoveLoadingLord(ct, rec.groupID, map);
            ct.leftToLoad = new List<TransferableOneWay>();
            foreach (Pawn pawn in pawnsToLoad)
            {
                TransferableOneWay transferable = new TransferableOneWay();
                transferable.things.Add(pawn);
                transferable.ForceTo(1);
                ct.leftToLoad.Add(transferable);
            }

            rec.groupID = TransporterUtility.InitiateLoading(new List<CompTransporter> { ct });
            rec.stage = CerebrexSupportEvacVehicleStage.Loading;
            rec.stateChangedTick = Find.TickManager.TicksGame;
            if (pawnsToLoad.Count > 0 && rec.groupID >= 0)
            {
                // 加入装载 Lord 前，先从战斗 Lord 正式移除这些 Pawn，避免“同时属于两个 Lord”报错。
                DetachPawnsFromOldLords(pawnsToLoad, map);
                LordMaker.MakeNewLord(
                    rec.faction ?? Faction.OfPlayer,
                    new LordJob_LoadAndEnterTransporters(rec.groupID),
                    map,
                    pawnsToLoad);
            }
        }

        private static void LaunchMechEvacPod(
            QuestPart_SymbiosisCovenantCerebrexSupport part,
            SymbiosisCovenantCerebrexSupportEvacVehicle rec,
            Map map)
        {
            CompLaunchable? launchable = rec.vehicleThing?.TryGetComp<CompLaunchable>();
            CompTransporter? ct = rec.vehicleThing?.TryGetComp<CompTransporter>();
            if (launchable == null || ct == null || rec.vehicleThing == null)
            {
                return;
            }

            // 记录实际装入并随舱体离图的有效 Pawn 数（普通满载与计时发射共用）。
            rec.launchedPawnCount = ct.innerContainer
                .OfType<Pawn>()
                .Count(p => IsUsableEvacPawn(p) && rec.pawns.Contains(p));

            ThingDef sentDef = EvacPodDef() ?? rec.vehicleThing.def;

            ActiveTransporter activeTransporter = (ActiveTransporter)ThingMaker.MakeThing(ThingDefOf.ActiveDropPod);
            activeTransporter.Contents = new ActiveTransporterInfo();
            activeTransporter.Contents.innerContainer.TryAddRangeOrTransfer(
                ct.GetDirectlyHeldThings(),
                canMergeWithExistingStacks: false,
                destroyLeftover: false);
            activeTransporter.Contents.sentTransporterDef = sentDef;

            FlyShipLeaving fly = (FlyShipLeaving)SkyfallerMaker.MakeSkyfaller(
                launchable.Props.skyfallerLeaving ?? ThingDefOf.DropPodLeaving,
                activeTransporter);
            fly.groupID = ct.groupID;
            fly.createWorldObject = false; // 不生成世界旅行运输仓；Pawn 离图由原版 FlyShipLeaving 完成

            IntVec3 pos = rec.vehicleThing.Position;
            ct.CleanUpLoadingVars(map);
            rec.vehicleThing.Destroy();
            GenSpawn.Spawn(fly, pos, map);
            rec.vehicleThing = fly; // 指向正在离开的 FlyShipLeaving，不置空、不重复发射
            rec.stage = CerebrexSupportEvacVehicleStage.Departing;
            rec.stateChangedTick = Find.TickManager.TicksGame;
        }

        // ── 半数登机强制发射与破墙 ──

        private static bool IsMechPodLandedAndLoadable(
            SymbiosisCovenantCerebrexSupportEvacVehicle rec, Map map)
        {
            if (rec == null || map == null || rec.vehicleThing == null)
            {
                return false;
            }

            if (!rec.vehicleThing.Spawned || rec.vehicleThing.Map != map)
            {
                return false;
            }

            if (rec.vehicleThing.TryGetComp<CompTransporter>() == null)
            {
                return false;
            }

            return rec.stage == CerebrexSupportEvacVehicleStage.Landed
                || rec.stage == CerebrexSupportEvacVehicleStage.Loading
                || rec.stage == CerebrexSupportEvacVehicleStage.LoadingRetryWaiting;
        }

        private static int CountLoadedUsablePawns(
            SymbiosisCovenantCerebrexSupportEvacVehicle rec, Thing vehicleThing)
        {
            CompTransporter? ct = vehicleThing.TryGetComp<CompTransporter>();
            if (ct == null)
            {
                return 0;
            }

            int count = 0;
            foreach (Thing t in ct.innerContainer)
            {
                if (t is Pawn p && IsUsableEvacPawn(p) && rec.pawns.Contains(p))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// 半数登机后的强制发射计时：每艘载具独立计时、独立发射。未落地、未达装载状态、
        /// 或尚未半数登机时绝不计时；计时开始后因人数变化或装载重试均不重置。
        /// </summary>
        private static void TickPartialLoadDeparture(
            QuestPart_SymbiosisCovenantCerebrexSupport part,
            SymbiosisCovenantCerebrexSupportEvacVehicle rec,
            Thing vehicleThing, Map map, int now)
        {
            if (rec == null || vehicleThing == null || map == null)
            {
                return;
            }

            if (rec.stage == CerebrexSupportEvacVehicleStage.Departing
                || rec.stage == CerebrexSupportEvacVehicleStage.Completed
                || rec.stage == CerebrexSupportEvacVehicleStage.NotRequested
                || rec.stage == CerebrexSupportEvacVehicleStage.LandingRequested)
            {
                return;
            }

            bool canLoad = rec.mode == CerebrexSupportEvacMode.RoyaltyShuttle
                ? (rec.stage == CerebrexSupportEvacVehicleStage.Landed
                    || rec.stage == CerebrexSupportEvacVehicleStage.Loading)
                : (rec.stage == CerebrexSupportEvacVehicleStage.Landed
                    || rec.stage == CerebrexSupportEvacVehicleStage.Loading
                    || rec.stage == CerebrexSupportEvacVehicleStage.LoadingRetryWaiting);
            if (!canLoad)
            {
                return;
            }

            if (rec.mode == CerebrexSupportEvacMode.RoyaltyShuttle && !IsShuttleLandedAndLoadable(rec, map))
            {
                return;
            }

            if (rec.mode == CerebrexSupportEvacMode.OdysseyMechPod && !IsMechPodLandedAndLoadable(rec, map))
            {
                return;
            }

            // 当前有效需求 Pawn 数：复用 IsUsableEvacPawn，并排除已被计时发射释放者。
            int requiredCount = rec.pawns.Count(p => IsUsableEvacPawn(p)
                && (rec.releasedPawns == null || !rec.releasedPawns.Contains(p)));
            if (requiredCount <= 0)
            {
                return;
            }

            int loadedCount = CountLoadedUsablePawns(rec, vehicleThing);
            int threshold = (requiredCount + 1) / 2;

            // 半数登机后启动 12h 倒计时；已启动则不被人数变化或装载重试重置。
            if (loadedCount >= threshold && rec.partialLoadDepartureTick < 0)
            {
                rec.partialLoadDepartureTick = now + Config.partialLoadDepartureDelayTicks;
            }

            if (rec.partialLoadDepartureTick >= 0 && now >= rec.partialLoadDepartureTick)
            {
                if (rec.mode == CerebrexSupportEvacMode.RoyaltyShuttle)
                {
                    ForceDepartPartialLoadShuttle(part, rec, vehicleThing, map);
                }
                else
                {
                    ForceDepartPartialLoadMechPod(part, rec, vehicleThing, map);
                }
            }
        }

        private static void ForceDepartPartialLoadShuttle(
            QuestPart_SymbiosisCovenantCerebrexSupport part,
            SymbiosisCovenantCerebrexSupportEvacVehicle rec,
            Thing vehicleThing, Map map)
        {
            CompShuttle? compShuttle = vehicleThing.TryGetComp<CompShuttle>();
            if (compShuttle == null)
            {
                return;
            }

            TransportShip? ship = compShuttle.shipParent;
            if (ship == null)
            {
                return;
            }

            List<Pawn> loaded = compShuttle.Transporter.innerContainer
                .Where(t => t is Pawn p && IsUsableEvacPawn(p) && rec.pawns.Contains(p))
                .Cast<Pawn>()
                .ToList();
            rec.launchedPawnCount = loaded.Count;

            // 释放仍在地图且未装入的有效 Pawn：不杀死、不销毁、不传送、不强塞入载具。
            List<Pawn> onMap = rec.pawns
                .Where(p => IsUsableEvacPawn(p)
                            && p.Spawned && p.Map == map
                            && !loaded.Contains(p))
                .ToList();
            foreach (Pawn p in onMap)
            {
                ReleasePawnFromEvacuation(rec, p, vehicleThing, map);
            }

            // 刷新 requiredPawns 为实际已登机者，避免 FlyAway.TryStart 在 AllRequiredThingsLoaded==false
            // 且容器非空时转入卸货。
            compShuttle.requiredPawns = loaded;

            // 移除撤离 Lord 并清除未完成装载需求；禁止 CleanUpLoadingVars，避免把已登机 Pawn 扔回地图。
            RemoveEvacLordForVehicle(rec, vehicleThing, map);

            if (rec.stage != CerebrexSupportEvacVehicleStage.Departing && ship.Waiting)
            {
                ship.ForceJob(ShipJobDefOf.FlyAway);
            }

            rec.stage = CerebrexSupportEvacVehicleStage.Departing;
            rec.stateChangedTick = Find.TickManager.TicksGame;
        }

        private static void ForceDepartPartialLoadMechPod(
            QuestPart_SymbiosisCovenantCerebrexSupport part,
            SymbiosisCovenantCerebrexSupportEvacVehicle rec,
            Thing vehicleThing, Map map)
        {
            CompTransporter? ct = vehicleThing.TryGetComp<CompTransporter>();
            if (ct == null)
            {
                return;
            }

            List<Pawn> loaded = ct.innerContainer
                .Where(t => t is Pawn p && IsUsableEvacPawn(p) && rec.pawns.Contains(p))
                .Cast<Pawn>()
                .ToList();
            rec.launchedPawnCount = loaded.Count;

            List<Pawn> onMap = rec.pawns
                .Where(p => IsUsableEvacPawn(p)
                            && p.Spawned && p.Map == map
                            && !loaded.Contains(p))
                .ToList();
            foreach (Pawn p in onMap)
            {
                ReleasePawnFromEvacuation(rec, p, vehicleThing, map);
            }

            // 复用同一舱体：清空装载 Lord 与 leftToLoad，保留 innerContainer 内容，
            // 由 LaunchMechEvacPod 把容器内容转入 FlyShipLeaving（不重复生成新舱）。
            RemoveLoadingLord(ct, rec.groupID, map);
            LaunchMechEvacPod(part, rec, map);
        }

        private static void ReleasePawnFromEvacuation(
            SymbiosisCovenantCerebrexSupportEvacVehicle rec, Pawn pawn, Thing? vehicleThing, Map map)
        {
            if (pawn == null || rec.releasedPawns.Contains(pawn))
            {
                return;
            }

            rec.releasedPawns.Add(pawn);

            Lord? lord = pawn.GetLord();
            if (lord != null)
            {
                lord.RemovePawn(pawn);
                if (lord.ownedPawns.Count == 0 && map.lordManager.lords.Contains(lord))
                {
                    map.lordManager.RemoveLord(lord);
                }
            }

            // 只中断与本载具相关的登机任务，不得粗暴打断其无关 Job。
            // 必须通过对象身份核对当前 Job 的目标载具就是正在强制发射的 vehicleThing。
            Job? cur = pawn.jobs?.curJob;
            if (cur != null && vehicleThing != null)
            {
                Thing? targetVehicle = null;
                if (cur.def == JobDefOf.EnterTransporter)
                {
                    JobDriver_EnterTransporter? enterDrv = pawn.jobs?.curDriver as JobDriver_EnterTransporter;
                    targetVehicle = enterDrv?.Transporter?.parent;
                    if (targetVehicle == null && cur.GetTarget(TargetIndex.A).Thing is Thing enterFallback)
                    {
                        targetVehicle = enterFallback;
                    }
                }
                else if (cur.def == JobDefOf.HaulToTransporter)
                {
                    JobDriver_HaulToTransporter? haulDrv = pawn.jobs?.curDriver as JobDriver_HaulToTransporter;
                    targetVehicle = haulDrv?.Transporter?.parent;
                }

                if (targetVehicle != null && targetVehicle == vehicleThing)
                {
                    pawn.jobs?.EndCurrentJob(JobCondition.InterruptForced);
                }
            }

            // 释放即退出本载具破墙候选，避免存档继续保留已脱离撤离逻辑的 Pawn。
            rec.breachEligiblePawns.Remove(pawn);
        }

        private static void RemoveEvacLordForVehicle(
            SymbiosisCovenantCerebrexSupportEvacVehicle rec, Thing vehicleThing, Map map)
        {
            if (rec.mode == CerebrexSupportEvacMode.RoyaltyShuttle)
            {
                Lord? lord = map.lordManager.lords.FirstOrDefault(l =>
                    l.LordJob is LordJob_ExitOnShuttle && l.ownedPawns.Any(p => rec.pawns.Contains(p)));
                if (lord != null)
                {
                    foreach (Pawn p in lord.ownedPawns.ToList())
                    {
                        lord.RemovePawn(p);
                    }

                    if (lord.ownedPawns.Count == 0 && map.lordManager.lords.Contains(lord))
                    {
                        map.lordManager.RemoveLord(lord);
                    }
                }
            }
            else
            {
                CompTransporter? ct = vehicleThing.TryGetComp<CompTransporter>();
                if (ct != null)
                {
                    RemoveLoadingLord(ct, rec.groupID, map);
                }
            }
        }

        /// <summary>
        /// 首次路线分类与破墙：仅在地载具真正落地、处于装载阶段时运行。未落地绝不路线分类、
        /// 绝不破墙、绝不计时。
        /// </summary>
        private static void TickEvacuationBreach(
            QuestPart_SymbiosisCovenantCerebrexSupport part,
            SymbiosisCovenantCerebrexSupportEvacVehicle rec,
            Thing vehicleThing, Map map, int now)
        {
            if (rec == null || vehicleThing == null || map == null)
            {
                return;
            }

            bool landedAndLoadable = rec.mode == CerebrexSupportEvacMode.RoyaltyShuttle
                ? IsShuttleLandedAndLoadable(rec, map)
                : IsMechPodLandedAndLoadable(rec, map);
            if (!landedAndLoadable)
            {
                return;
            }

            if (rec.stage == CerebrexSupportEvacVehicleStage.Departing
                || rec.stage == CerebrexSupportEvacVehicleStage.Completed)
            {
                return;
            }

            if (now < rec.nextBreachCheckTick)
            {
                return;
            }

            rec.nextBreachCheckTick = now + Config.evacuationBreachCheckIntervalTicks;

            ClassifyBreachRoutes(rec, vehicleThing, map);
            TryAssignEvacuationBreachJobs(part, rec, vehicleThing, map);
        }

        private static void ClassifyBreachRoutes(
            SymbiosisCovenantCerebrexSupportEvacVehicle rec, Thing vehicleThing, Map map)
        {
            int processed = 0;
            int capacity = Config.evacuationBreachInitialChecksPerInterval;
            foreach (Pawn pawn in rec.pawns)
            {
                if (processed >= capacity)
                {
                    break;
                }

                if (!EligibleForBreachClassification(pawn, rec, vehicleThing, map))
                {
                    continue;
                }

                if (rec.breachRouteCheckedPawns.Contains(pawn))
                {
                    continue;
                }

                ClassifySingleBreachRoute(pawn, rec, vehicleThing, map);
                processed++;
            }
        }

        private static bool EligibleForBreachClassification(
            Pawn pawn, SymbiosisCovenantCerebrexSupportEvacVehicle rec, Thing vehicleThing, Map map)
        {
            if (!IsUsableEvacPawn(pawn))
            {
                return false;
            }

            if (!pawn.Spawned || pawn.Map != map)
            {
                return false;
            }

            if (pawn.Downed)
            {
                return false;
            }

            if (IsInTransporterContainer(pawn, vehicleThing))
            {
                return false;
            }

            if (pawn.IsColonist)
            {
                return false;
            }

            if (pawn.IsColonyMech)
            {
                return false;
            }

            if (!rec.pawns.Contains(pawn))
            {
                return false;
            }

            return true;
        }

        private static void ClassifySingleBreachRoute(
            Pawn pawn, SymbiosisCovenantCerebrexSupportEvacVehicle rec, Thing vehicleThing, Map map)
        {
            // 标记已分类，避免重复昂贵搜索。
            rec.breachRouteCheckedPawns.Add(pawn);

            // 先尝试普通可达（不破坏任何物）。
            if (pawn.CanReach(vehicleThing, PathEndMode.Touch, Danger.Deadly))
            {
                rec.breachEligiblePawns.Remove(pawn);
                return;
            }

            // 普通不可达：用原版访客破墙思路做一次受限制路径搜索，找第一阻挡建筑。
            using (PawnPath path = map.pathFinder.FindPathNow(
                       pawn.Position,
                       vehicleThing,
                       TraverseParms.For(pawn, Danger.Deadly, TraverseMode.PassAllDestroyableThings),
                       null,
                       PathEndMode.Touch))
            {
                if (path == null)
                {
                    return;
                }

                IntVec3 cellBefore;
                Thing? blocker = path.FirstBlockingBuilding(out cellBefore, pawn);

                // 只有通过统一可破坏校验的墙才允许进入破墙候选名单；
                // 不可摧毁、无血条、已销毁、已离图或非墙一律不加入（并主动移出）。
                if (IsValidEvacuationBreachWall(blocker, map))
                {
                    rec.breachEligiblePawns.Add(pawn);
                }
                else
                {
                    rec.breachEligiblePawns.Remove(pawn);
                }
            }
        }

        private static void TryAssignEvacuationBreachJobs(
            QuestPart_SymbiosisCovenantCerebrexSupport part,
            SymbiosisCovenantCerebrexSupportEvacVehicle rec, Thing vehicleThing, Map map)
        {
            if (rec.breachEligiblePawns.Count == 0)
            {
                return;
            }

            // 本载具已有人在处理的目标墙，避免重复抢同一堵墙。
            HashSet<Thing> targetedWalls = new HashSet<Thing>();
            foreach (Pawn other in rec.pawns)
            {
                Thing? wall = CurrentBreachTarget(other, vehicleThing);
                if (wall != null)
                {
                    targetedWalls.Add(wall);
                }
            }

            foreach (Pawn pawn in rec.breachEligiblePawns.ToList())
            {
                if (!IsUsableEvacPawn(pawn)
                    || !pawn.Spawned
                    || pawn.Map != map
                    || pawn.Downed
                    || IsInTransporterContainer(pawn, vehicleThing))
                {
                    rec.breachEligiblePawns.Remove(pawn);
                    continue;
                }

                // 2. 已经在处理一堵仍有效的墙：加入已占用集合并直接跳过本 Pawn 后续昂贵逻辑。
                //    不重复 CanReach / FindPathNow / 重新下发 Job，避免反复重置拆墙任务或重复抢墙。
                Thing? handledWall = CurrentBreachTarget(pawn, vehicleThing);
                if (handledWall != null)
                {
                    targetedWalls.Add(handledWall);
                    continue;
                }

                // 3. 后来普通可达：从破墙候选移除，保留在已分类集合，永久不再破墙。
                if (pawn.CanReach(vehicleThing, PathEndMode.Touch, Danger.Deadly))
                {
                    rec.breachEligiblePawns.Remove(pawn);
                    if (!rec.breachRouteCheckedPawns.Contains(pawn))
                    {
                        rec.breachRouteCheckedPawns.Add(pawn);
                    }

                    continue;
                }

                using (PawnPath path = map.pathFinder.FindPathNow(
                           pawn.Position,
                           vehicleThing,
                           TraverseParms.For(pawn, Danger.Deadly, TraverseMode.PassAllDestroyableThings),
                           null,
                           PathEndMode.Touch))
                {
                    if (path == null)
                    {
                        continue;
                    }

                    IntVec3 cellBefore;
                    Thing? blocker = path.FirstBlockingBuilding(out cellBefore, pawn);

                    // 只有“仍生成在当前地图、是墙、有血条且允许摧毁”的阻挡物才允许破墙；
                    // 不可摧毁 / 无血条 / 已销毁 / 已离图的墙一律不作为破墙目标。
                    if (!IsValidEvacuationBreachWall(blocker, map))
                    {
                        rec.breachEligiblePawns.Remove(pawn);
                        continue;
                    }

                    // 已有人（含本轮前面或本载具其他 Pawn）在处理这堵墙，不再抢。
                    if (targetedWalls.Contains(blocker))
                    {
                        continue;
                    }

                    Job? job = DigUtility.PassBlockerJob(
                        pawn, blocker, cellBefore, canMineMineables: true, canMineNonMineables: true);
                    if (job == null)
                    {
                        continue;
                    }

                    pawn.jobs?.TryTakeOrderedJob(job);
                    targetedWalls.Add(blocker);
                }
            }
        }

        // 判定某 JobDef 是否属于撤离破墙系统允许分发的破墙任务类型。
        // 所有相关判断（CurrentBreachTarget、重复破墙占用判定）必须统一走这里，
        // 避免 Mine / AttackMelee / AttackStatic / UseVerbOnThing 在多处判断中不一致。
        private static bool IsEvacuationBreachJob(JobDef jobDef)
        {
            return jobDef == JobDefOf.Mine
                || jobDef == JobDefOf.AttackMelee
                || jobDef == JobDefOf.AttackStatic
                || jobDef == JobDefOf.UseVerbOnThing;
        }

        // 返回 Pawn 当前正在处理的有效破墙目标墙；否则返回 null。
        // 要求：Pawn 与当前 Job 有效；targetA 指向 Thing；
        // 且该 Thing 通过统一破墙校验（仍是当前地图上的墙、未销毁、有血条、允许摧毁）。
        // 已摧毁 / 已离图 / 不可摧毁 / 无血条 / 不再是墙的目标一律返回 null。
        private static Thing? CurrentBreachTarget(Pawn pawn, Thing vehicleThing)
        {
            if (pawn == null || !pawn.Spawned || pawn.Map == null)
            {
                return null;
            }

            Job? cur = pawn.jobs?.curJob;
            if (cur == null || !IsEvacuationBreachJob(cur.def))
            {
                return null;
            }

            if (cur.targetA.Thing is not Thing target)
            {
                return null;
            }

            return IsValidEvacuationBreachWall(target, pawn.Map) ? target : null;
        }
    }
}

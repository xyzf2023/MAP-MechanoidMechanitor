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
            QuestPart_SymbiosisCovenantCerebrexSupport part,
            Map map,
            int now)
        {
            SymbiosisCovenantCerebrexSupportDef cfg = Config;
            GameComponent_SymbiosisCovenantState? comp = GameComponent_SymbiosisCovenantState.CurrentComponent;
            if (comp == null || !GameComponent_SymbiosisCovenantState.IsActive || comp.CovenantLevel < 2)
            {
                // 盟约被关闭或等级降到 L2 以下：停止生成后续新波次。
                return (false, true, null);
            }

            List<Faction> eligible = GetEligibleFactions(part, map);
            if (eligible.Count == 0)
            {
                return (false, true, null);
            }

            List<Faction> prev = part.waves.LastOrDefault()?.factionRecords
                .Select(f => f.faction)
                .Where(f => f != null)
                .Cast<Faction>()
                .ToList() ?? new List<Faction>();

            List<Faction> chosen = SelectWaveFactions(prev, eligible, comp, cfg);
            if (chosen.Count == 0)
            {
                return (false, true, null);
            }

            float total = part.perWaveSupportPoints;

            // 生成并装备真空服；无法装备的未 Spawn Pawn 安全清理。
            Dictionary<Faction, List<Pawn>> factionPawns = new Dictionary<Faction, List<Pawn>>();
            foreach (Faction fac in chosen)
            {
                List<Pawn> generated = GenerateCombatGroup(fac, map, total / chosen.Count);
                if (generated.Count == 0)
                {
                    continue;
                }

                List<Pawn> valid = new List<Pawn>();
                foreach (Pawn p in generated)
                {
                    if (TryEquipVacsuit(p))
                    {
                        valid.Add(p);
                    }
                    else
                    {
                        p.Destroy(DestroyMode.Vanish);
                    }
                }

                if (valid.Count > 0)
                {
                    factionPawns[fac] = valid;
                }
            }

            if (factionPawns.Count == 0)
            {
                // 生成失败：延迟后重试，不计入波数。
                return (false, false, null);
            }

            // 实际生成时每个派系使用的点数 = 整波点数 / 选择派系数；记录值须与之相等。
            float perFactionPoints = total / chosen.Count;

            // 为生成失败的派系重生替代派系（最多一次），提高波次生成成功率。
            if (factionPawns.Count < chosen.Count && factionPawns.Count > 0)
            {
                List<Faction> failed = chosen.Where(f => !factionPawns.ContainsKey(f)).ToList();
                List<Faction> pool = eligible.Where(f => !chosen.Contains(f)).ToList();
                foreach (Faction f in failed)
                {
                    Faction? repl = pool.FirstOrDefault();
                    if (repl == null)
                    {
                        break;
                    }

                    pool.Remove(repl);
                    List<Pawn> gen = GenerateCombatGroup(repl, map, perFactionPoints);
                    List<Pawn> valid = new List<Pawn>();
                    foreach (Pawn p in gen)
                    {
                        if (TryEquipVacsuit(p))
                        {
                            valid.Add(p);
                        }
                        else
                        {
                            p.Destroy(DestroyMode.Vanish);
                        }
                    }

                    if (valid.Count > 0)
                    {
                        factionPawns[repl] = valid;
                    }
                }
            }

            // 重生后的替代派系必须加入实际部署与点数记录，否则其 Pawn 被计入人数/落点却不会真正空投。
            List<Faction> successFacs = factionPawns.Keys.ToList();

            List<Pawn> allPawns = factionPawns.Values.SelectMany(x => x).ToList();

            // 先为整波所有 Pawn 找齐安全落点（all-or-nothing）。
            if (!TryFindSafeEdgeDropCells(map, allPawns.Count, cfg, out List<IntVec3> cells))
            {
                foreach (Pawn p in allPawns)
                {
                    p.Destroy(DestroyMode.Vanish);
                }

                Log.Message($"{LogPrefix} 落点准备失败，整波回滚并重试（site={part.site?.Label}）。");
                return (false, false, null);
            }

            int waveIndex = part.waves.Count;
            string questId = part.quest?.id.ToString() ?? "q";
            List<SymbiosisCovenantCerebrexSupportFactionRecord> factionRecords =
                new List<SymbiosisCovenantCerebrexSupportFactionRecord>();

            int cellIdx = 0;
            foreach (Faction fac in successFacs)
            {
                List<Pawn> pawns = factionPawns[fac];
                string aidTag = $"{questId}_{waveIndex}_{fac.loadID}";
                float pts = perFactionPoints;

                foreach (Pawn p in pawns)
                {
                    IntVec3 cell = cells[cellIdx++];
                    ActiveTransporter at = (ActiveTransporter)ThingMaker.MakeThing(ThingDefOf.ActiveDropPod);
                    at.Contents.innerContainer.TryAdd(p);
                    at.Contents.sentTransporterDef = ThingDefOf.ActiveDropPod;
                    at.Contents.openDelay = cfg.dropPodOpenDelayTicks;
                    DropPodUtility.MakeDropPodAt(cell, map, at.Contents, null);
                }

                factionRecords.Add(new SymbiosisCovenantCerebrexSupportFactionRecord(fac, pts, pawns.Count, aidTag));
                LordMaker.MakeNewLord(
                    fac,
                    new LordJob_SymbiosisCovenantCerebrexSupport(map.Center),
                    map,
                    pawns);
            }

            SymbiosisCovenantCerebrexSupportWaveRecord record = new SymbiosisCovenantCerebrexSupportWaveRecord
            {
                waveIndex = waveIndex,
                deployedTick = now,
                initialPawnCount = allPawns.Count,
                pawns = allPawns,
                factionRecords = factionRecords,
                aidTag = $"{questId}_{waveIndex}",
                nextWaveTriggered = false
            };

            return (true, false, record);
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

            ThingDef? coreDef = DefDatabase<ThingDef>.GetNamedSilentFail("Building_CerebrexCore");
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
            out List<IntVec3> result)
        {
            result = new List<IntVec3>();

            // 寻路目标仅在方法内计算一次，避免每个候选重复全图扫描。
            IntVec3 target = FindCerebrexCore(map)?.parent?.Position ?? map.Center;

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
                    if (IsPlatformEdge(c, map) && IsSafeDropCell(c, map, target))
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
                        && IsSafeDropCell(c, map, target)
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
                        && IsSafeDropCell(c, map, target)
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
                        if (!candidates.Contains(c) && IsSafeDropCell(c, map, target))
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

            return result.Count == requiredCount;
        }

        private static bool IsSafeDropCell(IntVec3 c, Map map, IntVec3 target)
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

            if (!map.reachability.CanReach(
                    c,
                    target,
                    PathEndMode.OnCell,
                    TraverseParms.For(TraverseMode.PassDoors)))
            {
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

        public static void BeginEvacuation(QuestPart_SymbiosisCovenantCerebrexSupport part, Map map, int now)
        {
            Dictionary<Faction, List<Pawn>> groups = GatherEvacPawns(part, map);
            if (groups.Count == 0)
            {
                // 统一完成入口：即使无可用援军，也通过 MarkSupportCompleted 收尾，不得直接置内部 stage（#13）。
                Log.Message($"{LogPrefix} 没有合法存活援军，直接完成撤离（site={part.site?.Label}）。");
                part.MarkSupportCompleted();
                return;
            }

            foreach (KeyValuePair<Faction, List<Pawn>> kvp in groups)
            {
                int vehicleCount = ComputeVehicleCount(part.evacMode, kvp.Value);
                int per = Mathf.CeilToInt((float)kvp.Value.Count / vehicleCount);
                for (int i = 0; i < vehicleCount; i++)
                {
                    List<Pawn> subset = kvp.Value.Skip(i * per).Take(per).ToList();
                    if (subset.Count == 0)
                    {
                        continue;
                    }

                    part.evacVehicles.Add(new SymbiosisCovenantCerebrexSupportEvacVehicle
                    {
                        mode = part.evacMode,
                        faction = kvp.Key,
                        pawns = subset
                    });
                }
            }

            Find.LetterStack.ReceiveLetter(
                "MAP_SymbiosisCovenant_CerebrexSupport_EvacTitle".Translate(),
                "MAP_SymbiosisCovenant_CerebrexSupport_EvacBody".Translate(),
                LetterDefOf.PositiveEvent);

            part.stage = QuestPart_SymbiosisCovenantCerebrexSupport.CerebrexSupportStage.EvacuationPreparing;
            part.loadingStartTick = now;
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
                    if (p == null || p.Dead || p.Destroyed || p.Discarded)
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

        private static int ComputeVehicleCount(CerebrexSupportEvacMode mode, List<Pawn> pawns)
        {
            if (mode == CerebrexSupportEvacMode.RoyaltyShuttle)
            {
                CompProperties_Transporter? tp = ThingDefOf.Shuttle.GetCompProperties<CompProperties_Transporter>();
                float shuttleMassCap = tp?.massCapacity ?? 150f;
                float shuttleTotalMass = 0f;
                foreach (Pawn p in pawns)
                {
                    shuttleTotalMass += CollectionsMassCalculator.MassUsage(
                        new List<Thing> { p },
                        IgnorePawnsInventoryMode.IgnoreIfAssignedToUnload,
                        includePawnsMass: true);
                }

                return Mathf.Max(1, Mathf.CeilToInt(shuttleTotalMass / shuttleMassCap));
            }

            // Odyssey 机械空投仓：按实际运输质量分配。
            ThingDef? podDef = EvacPodDef();
            float massCap = Config.evacPodMassCapacityFallback;
            if (podDef != null)
            {
                CompProperties_Transporter? tp = podDef.GetCompProperties<CompProperties_Transporter>();
                if (tp != null && tp.massCapacity > 0f)
                {
                    massCap = tp.massCapacity;
                }
            }

            if (massCap <= 0f)
            {
                massCap = Config.evacPodMassCapacityFallback;
            }

            float totalMass = 0f;
            foreach (Pawn p in pawns)
            {
                totalMass += CollectionsMassCalculator.MassUsage(
                    new List<Thing> { p },
                    IgnorePawnsInventoryMode.IgnoreIfAssignedToUnload,
                    includePawnsMass: true);
            }

            return Mathf.Max(1, Mathf.CeilToInt(totalMass / massCap));
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
                part.MarkSupportCompleted();
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
                .Where(v => (v.stage == CerebrexSupportEvacVehicleStage.NotRequested
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
                        rec.stage = CerebrexSupportEvacVehicleStage.NotRequested;
                    }

                    continue;
                }

                if (rec.stage == CerebrexSupportEvacVehicleStage.LandingRequested
                    || rec.stage == CerebrexSupportEvacVehicleStage.Landed
                    || rec.stage == CerebrexSupportEvacVehicleStage.Loading)
                {
                    CompShuttle? compShuttle = rec.vehicleThing?.TryGetComp<CompShuttle>();
                    TransportShip? ship = compShuttle?.shipParent;
                    if (ship == null)
                    {
                        // 载具仍未就绪（生成/到达中）。vehicleThing 一旦丢失则标记失败重试。
                        if (rec.vehicleThing == null && rec.stage != CerebrexSupportEvacVehicleStage.LandingRequested)
                        {
                            rec.stage = CerebrexSupportEvacVehicleStage.FailedRetryable;
                            rec.nextRetryTick = now + Config.evacuationRetryTicks;
                        }

                        continue;
                    }

                    rec.stage = CerebrexSupportEvacVehicleStage.Landed;

                    if (compShuttle!.AllRequiredThingsLoaded
                        && rec.stage != CerebrexSupportEvacVehicleStage.Departing)
                    {
                        // 仅发射一次：切换 Departing，绝不把 vehicleThing 置空。
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
                    bool pawnsGone = !rec.IsTrackedPawnStillOnSupportMap(map);

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

        private static IntVec3 FindShuttleLandingSpot(Map map, List<IntVec3> taken)
        {
            // 已占用穿梭机的完整占地（按占地而非中心距离判断间隔，避免重叠，#7）。
            HashSet<IntVec3> takenFootprint = new HashSet<IntVec3>();
            foreach (IntVec3 t in taken)
            {
                if (t.IsValid)
                {
                    takenFootprint.UnionWith(GenAdj.CellsOccupiedBy(t, Rot4.North, ThingDefOf.Shuttle.size));
                }
            }

            int maxRadius = Mathf.RoundToInt(
                Config.dropCellSearchRadius * (1f - Config.dropCellRadialPatternSafetyMargin));
            bool FootprintFree(IntVec3 c) =>
                !takenFootprint.Overlaps(GenAdj.CellsOccupiedBy(c, Rot4.North, ThingDefOf.Shuttle.size));

            IntVec3 best = DropCellFinder.GetBestShuttleLandingSpot(map, Faction.OfPlayer);
            if (best.IsValid
                && RoyalTitlePermitWorker_CallShuttle.ShuttleCanLandHere(best, map).Accepted
                && FootprintFree(best))
            {
                return best;
            }

            int attempt = 0;
            while (attempt < Config.dropCellAnchorAttemptLimit)
            {
                IntVec3 c = (best.IsValid ? best : map.Center)
                    + GenRadial.RadialPattern[attempt % GenRadial.RadialPattern.Length];
                if (c.InBounds(map)
                    && RoyalTitlePermitWorker_CallShuttle.ShuttleCanLandHere(c, map).Accepted
                    && FootprintFree(c))
                {
                    return c;
                }

                attempt++;
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
                .Where(p => p != null && !p.Dead && !p.Destroyed && !p.Discarded)
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
            compShuttle.permitShuttle = true;
            compShuttle.acceptChildren = true;
            compShuttle.requiredPawns = pawns;
            TransportShip ship = TransportShipMaker.MakeTransportShip(TransportShipDefOf.Ship_Shuttle, null, shuttle);
            ship.ArriveAt(spot, map.Parent);

            rec.vehicleThing = shuttle;
            rec.stage = CerebrexSupportEvacVehicleStage.Landed;
            rec.stateChangedTick = Find.TickManager.TicksGame;

            LordMaker.MakeNewLord(
                rec.faction ?? Faction.OfPlayer,
                new LordJob_ExitOnShuttle(shuttle, addFleeToil: false),
                map,
                pawns);
        }

        // ── 奥德赛机械空投仓撤离 ──

        private static void TickMechPodEvacuation(
            QuestPart_SymbiosisCovenantCerebrexSupport part,
            Map map,
            int now)
        {
            ThingDef? podDef = EvacPodDef();

            // 1) 收集需要生成空舱的记录（NotRequested / 生成重试 / 失败重试），整批预验证落点。
            List<SymbiosisCovenantCerebrexSupportEvacVehicle> pending = part.evacVehicles
                .Where(v => (v.stage == CerebrexSupportEvacVehicleStage.NotRequested
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
                        rec.stage = CerebrexSupportEvacVehicleStage.NotRequested;
                    }

                    continue;
                }

                if (rec.stage == CerebrexSupportEvacVehicleStage.LoadingRetryWaiting)
                {
                    if (rec.nextRetryTick >= 0 && now >= rec.nextRetryTick)
                    {
                        rec.nextRetryTick = -1;
                        // 复用同一撤离舱，重新进入装载流程（不重复生成、不覆盖记录，#1/#15）。
                        rec.groupID = -1;
                        rec.stage = CerebrexSupportEvacVehicleStage.Landed;
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

                if (rec.stage == CerebrexSupportEvacVehicleStage.Landed)
                {
                    BeginPodLoading(rec, ct, map);
                }

                List<Pawn> stillIn = ct.innerContainer.OfType<Pawn>().ToList();
                bool allLoaded = rec.pawns
                    .Where(p => p != null && !p.Dead && !p.Destroyed && !p.Discarded)
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
                        rec.groupID = -1;
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
                bool pawnsGone = !rec.IsTrackedPawnStillOnSupportMap(map);

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
                // 撤离舱 Def 缺失：不谎报完成，进入生成重试（仍留在地图的援军不会被标记已撤离，#12）。
                rec.stage = CerebrexSupportEvacVehicleStage.SpawnRetryWaiting;
                rec.nextRetryTick = Find.TickManager.TicksGame + Config.evacuationRetryTicks;
                return;
            }

            // 只让装人的空仓从天空落下；不把盟军直接塞入尚未落地的撤离仓。
            ActiveTransporterInfo info = new ActiveTransporterInfo();
            info.sentTransporterDef = podDef;
            info.openDelay = Config.dropPodOpenDelayTicks;
            DropPodUtility.MakeDropPodAt(spot, map, info, Faction.OfMechanoids);
            rec.stage = CerebrexSupportEvacVehicleStage.LandingRequested;
            rec.stateChangedTick = Find.TickManager.TicksGame;
        }

        private static void BeginPodLoading(
            SymbiosisCovenantCerebrexSupportEvacVehicle rec,
            CompTransporter ct,
            Map map)
        {
            List<Pawn> pawns = rec.pawns
                .Where(p => p != null && !p.Dead && !p.Destroyed && !p.Discarded)
                .ToList();
            if (pawns.Count == 0)
            {
                // 无存活 Pawn：直接完成此撤离记录，不发射空仓。
                rec.stage = CerebrexSupportEvacVehicleStage.Completed;
                return;
            }

            if (rec.stage == CerebrexSupportEvacVehicleStage.Loading)
            {
                return; // InitiateLoading 只能执行一次。
            }

            ct.leftToLoad = new List<TransferableOneWay>();
            foreach (Pawn p in pawns)
            {
                TransferableOneWay t = new TransferableOneWay();
                t.things.Add(p);
                t.ForceTo(1);
                ct.leftToLoad.Add(t);
            }

            // 仅每组首次装载建立一次 Lord；重试时 groupID 已被重置为 -1，避免重复建立 Lord（#14）。
            bool firstTime = rec.groupID < 0;
            rec.groupID = TransporterUtility.InitiateLoading(new List<CompTransporter> { ct });
            rec.stage = CerebrexSupportEvacVehicleStage.Loading;
            rec.stateChangedTick = Find.TickManager.TicksGame;

            if (firstTime && rec.groupID >= 0)
            {
                LordMaker.MakeNewLord(
                    rec.faction ?? Faction.OfPlayer,
                    new LordJob_LoadAndEnterTransporters(rec.groupID),
                    map,
                    pawns);
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
    }
}

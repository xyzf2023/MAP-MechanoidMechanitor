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

            // 重新分配整波点数给实际能生成战斗编组的派系，保证总和等于整波点数。
            List<Faction> successFacs = factionPawns.Keys.ToList();
            List<(Faction faction, float points)> allocations = AllocatePoints(total, successFacs);

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
                float pts = allocations.First(a => a.faction == fac).points;

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

        private static bool TryFindSafeEdgeDropCells(
            Map map,
            int requiredCount,
            SymbiosisCovenantCerebrexSupportDef cfg,
            out List<IntVec3> result)
        {
            result = new List<IntVec3>();

            // 1) 优先尝试原版 EdgeDrop 的 spawn center 解析。
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

            // 2) 外缘候选格（有上限）。
            List<IntVec3> candidates = new List<IntVec3>();
            int edgeCount = 0;
            foreach (IntVec3 c in CellRect.WholeMap(map).EdgeCells)
            {
                if (edgeCount++ > cfg.dropCellEdgeCandidateLimit)
                {
                    break;
                }

                if (IsSafeDropCell(c, map))
                {
                    candidates.Add(c);
                }
            }

            if (candidates.Count == 0)
            {
                // 外缘无安全格：退化为以 anchor 为中心、有限半径内的安全格。
                int attempt = 0;
                while (attempt++ < cfg.dropCellAnchorAttemptLimit && candidates.Count < requiredCount)
                {
                    IntVec3 c = anchor + GenRadial.RadialPattern[attempt % GenRadial.RadialPattern.Length];
                    if (c.InBounds(map) && IsSafeDropCell(c, map))
                    {
                        candidates.Add(c);
                    }
                }
            }

            // 优先选择距离地图中心较远（外缘）的安全格。
            candidates.SortByDescending(c => c.DistanceTo(map.Center));

            // 3) 按最小间距挑选所需数量。
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

        private static bool IsSafeDropCell(IntVec3 c, Map map)
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

            // 能使用正常通行或 PassDoors 路径抵达主脑战斗区域/玩家主要可达区域。
            IntVec3 target = map.Center;
            CompCerebrexCore? core = map.listerThings.AllThings.OfType<CompCerebrexCore>().FirstOrDefault();
            if (core != null && core.parent != null)
            {
                target = core.parent.Position;
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
                    CompCerebrexCore? core = map.listerThings.AllThings.OfType<CompCerebrexCore>().FirstOrDefault();
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
                part.stage = QuestPart_SymbiosisCovenantCerebrexSupport.CerebrexSupportStage.Completed;
                Log.Message($"{LogPrefix} 没有合法存活援军，直接完成撤离（site={part.site?.Label}）。");
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
                TickMechPodEvacuation(part, map, now);
            }
        }

        // ── 皇权穿梭机撤离 ──

        private static void TickRoyaltyEvacuation(
            QuestPart_SymbiosisCovenantCerebrexSupport part,
            Map map,
            int now)
        {
            bool allGone = part.evacVehicles.Count > 0;

            foreach (SymbiosisCovenantCerebrexSupportEvacVehicle rec in part.evacVehicles)
            {
                if (rec.vehicleThing == null)
                {
                    if (!TryCreateRoyaltyShuttle(part, map, rec))
                    {
                        allGone = false;
                        continue;
                    }
                }

                CompShuttle? compShuttle = rec.vehicleThing?.TryGetComp<CompShuttle>();
                TransportShip? ship = compShuttle?.shipParent;
                if (ship == null)
                {
                    allGone = false;
                    continue;
                }

                if (compShuttle!.AllRequiredThingsLoaded)
                {
                    ship.ForceJob(ShipJobDefOf.FlyAway);
                    rec.vehicleThing = null;
                }
                else
                {
                    allGone = false;
                }
            }

            if (allGone && part.evacVehicles.All(v => v.vehicleThing == null))
            {
                part.stage = QuestPart_SymbiosisCovenantCerebrexSupport.CerebrexSupportStage.Completed;
                Messages.Message(
                    "MAP_SymbiosisCovenant_CerebrexSupport_EvacDone".Translate(),
                    MessageTypeDefOf.PositiveEvent);
                Log.Message($"{LogPrefix} 穿梭机撤离完成（site={part.site?.Label}）。");
            }
        }

        private static bool TryCreateRoyaltyShuttle(
            QuestPart_SymbiosisCovenantCerebrexSupport part,
            Map map,
            SymbiosisCovenantCerebrexSupportEvacVehicle rec)
        {
            List<Pawn> pawns = rec.pawns
                .Where(p => p != null && !p.Dead && !p.Destroyed && !p.Discarded)
                .ToList();
            if (pawns.Count == 0)
            {
                rec.vehicleThing = null;
                return true;
            }

            IntVec3 spot = DropCellFinder.GetBestShuttleLandingSpot(map, rec.faction ?? Faction.OfPlayer);
            if (!spot.IsValid || !RoyalTitlePermitWorker_CallShuttle.ShuttleCanLandHere(spot, map))
            {
                part.evacuationRetryTick = Find.TickManager.TicksGame + Config.evacuationRetryTicks;
                return false;
            }

            Thing shuttle = ThingMaker.MakeThing(ThingDefOf.Shuttle);
            CompShuttle compShuttle = shuttle.TryGetComp<CompShuttle>();
            compShuttle.permitShuttle = true;
            compShuttle.acceptChildren = true;
            compShuttle.requiredPawns = pawns;
            TransportShip ship = TransportShipMaker.MakeTransportShip(TransportShipDefOf.Ship_Shuttle, null, shuttle);
            ship.ArriveAt(spot, map.Parent);

            rec.vehicleThing = shuttle;
            LordMaker.MakeNewLord(
                rec.faction ?? Faction.OfPlayer,
                new LordJob_ExitOnShuttle(shuttle, addFleeToil: false),
                map,
                pawns);

            return true;
        }

        // ── 奥德赛机械空投仓撤离 ──

        private static void TickMechPodEvacuation(
            QuestPart_SymbiosisCovenantCerebrexSupport part,
            Map map,
            int now)
        {
            ThingDef? podDef = EvacPodDef();

            // 1) 尚未发出空仓的载具：先为全部寻找完整安全落点（all-or-nothing），再让空仓从天空落下。
            List<SymbiosisCovenantCerebrexSupportEvacVehicle> needSpot =
                part.evacVehicles.Where(v => !v.podRequested && v.vehicleThing == null).ToList();
            if (needSpot.Count > 0)
            {
                List<IntVec3> spots = new List<IntVec3>();
                bool ok = true;
                foreach (SymbiosisCovenantCerebrexSupportEvacVehicle rec in needSpot)
                {
                    if (!TryFindSafeEdgeDropCells(map, 1, Config, out List<IntVec3> one)
                        || one.Count == 0)
                    {
                        ok = false;
                        break;
                    }

                    IntVec3 c = one[0];
                    if (spots.Any(s => s.DistanceToSquared(c) < Config.dropCellMinimumSpacingSquared))
                    {
                        ok = false;
                        break;
                    }

                    spots.Add(c);
                }

                if (!ok)
                {
                    part.evacuationRetryTick = now + Config.evacuationRetryTicks;
                    return;
                }

                for (int i = 0; i < needSpot.Count; i++)
                {
                    needSpot[i].landingCell = spots[i];
                    needSpot[i].podRequested = true;
                    SpawnEmptyEvacPod(map, needSpot[i], spots[i], podDef);
                }

                return; // 等待空仓建筑实际落地
            }

            // 2) 已发出空仓但尚未取到建筑：尝试在落点附近找回已 Spawn 的建筑。
            foreach (SymbiosisCovenantCerebrexSupportEvacVehicle rec in part.evacVehicles
                         .Where(v => v.podRequested && v.vehicleThing == null && v.groupID < 0))
            {
                Thing? building = FindSpawnedPod(map, rec.landingCell, podDef);
                if (building != null)
                {
                    if (rec.faction != null)
                    {
                        building.SetFaction(rec.faction);
                    }

                    rec.vehicleThing = building;
                }
            }

            // 3) 装载与发射。
            bool allGone = part.evacVehicles.Count > 0;
            foreach (SymbiosisCovenantCerebrexSupportEvacVehicle rec in part.evacVehicles)
            {
                if (rec.vehicleThing == null)
                {
                    allGone = false;
                    continue;
                }

                CompTransporter? ct = rec.vehicleThing.TryGetComp<CompTransporter>();
                if (ct == null)
                {
                    allGone = false;
                    continue;
                }

                if (rec.groupID < 0)
                {
                    BeginPodLoading(rec, ct, map);
                }

                List<Pawn> stillIn = ct.innerContainer.OfType<Pawn>().ToList();
                bool allLoaded = rec.pawns
                    .Where(p => p != null && !p.Dead && !p.Destroyed && !p.Discarded)
                    .All(p => stillIn.Contains(p));

                if (allLoaded)
                {
                    LaunchMechEvacPod(part, rec, map);
                }
                else
                {
                    allGone = false;
                }
            }

            if (allGone && part.evacVehicles.All(v => v.vehicleThing == null))
            {
                part.stage = QuestPart_SymbiosisCovenantCerebrexSupport.CerebrexSupportStage.Completed;
                Messages.Message(
                    "MAP_SymbiosisCovenant_CerebrexSupport_EvacDone".Translate(),
                    MessageTypeDefOf.PositiveEvent);
                Log.Message($"{LogPrefix} 机械空投仓撤离完成（site={part.site?.Label}）。");
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
                return;
            }

            // 只让装人的空仓从天空落下；不把盟军直接塞入尚未落地的撤离仓。
            ActiveTransporter at = (ActiveTransporter)ThingMaker.MakeThing(ThingDefOf.ActiveDropPod);
            at.Contents.sentTransporterDef = podDef;
            at.Contents.openDelay = Config.dropPodOpenDelayTicks;
            DropPodUtility.MakeDropPodAt(spot, map, at.Contents, Faction.OfMechanoids);
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
                rec.groupID = -2;
                return;
            }

            ct.leftToLoad = new List<TransferableOneWay>();
            foreach (Pawn p in pawns)
            {
                TransferableOneWay t = new TransferableOneWay();
                t.things.Add(p);
                t.ForceTo(1);
                ct.leftToLoad.Add(t);
            }

            rec.groupID = TransporterUtility.InitiateLoading(new List<CompTransporter> { ct });
            LordMaker.MakeNewLord(
                rec.faction ?? Faction.OfPlayer,
                new LordJob_LoadAndEnterTransporters(rec.groupID),
                map,
                pawns);
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
            rec.vehicleThing = null;
        }
    }
}

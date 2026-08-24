using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using UnityEngine;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 玩家进攻奥德赛机械主巢时，共生盟约附加的援军支援 QuestPart。
    /// 这是原版 Gravcore_Mechhive 任务上的附加层，不修改原版任务目标/奖励/结局/稳定器/主脑互动/接管逻辑。
    /// 所有业务状态均存档；主脑战斗结束后由本 Part 主动把援军转交撤离载具。
    /// </summary>
    public sealed class QuestPart_SymbiosisCovenantCerebrexSupport : QuestPartActivable
    {
        private const string LogPrefix = "[MAP][SymbiosisCovenantCerebrexSupport]";

        public enum CerebrexSupportStage : byte
        {
            WaitingForMap,
            OfferDelay,
            OfferPending,
            DeclinedOrExpired,
            WaitingFirstWave,
            CombatSupport,
            CoreDefencesLowered,
            ThreatClearStabilizing,
            EvacuationPreparing,
            EvacuationLoading,
            Completed,
            Invalid
        }

        public Site? site;
        public string? mapGeneratedSignal;
        public string? offerId;

        public CerebrexSupportStage stage = CerebrexSupportStage.WaitingForMap;
        public bool offerSent;
        public bool offerResolved;
        public int offerCreatedTick;
        public int offerExpireTick;

        public int covenantLevelSnapshot;
        public float threatPointsSnapshot;
        public float perWaveSupportPoints;
        public int firstWaveDueTick;

        public int waveCount;
        public int lastSuccessfulWaveTick = -1;
        public int nextDeploymentRetryTick = -1;
        public int mapEnteredTick = -1;

        public List<SymbiosisCovenantCerebrexSupportWaveRecord> waves = new List<SymbiosisCovenantCerebrexSupportWaveRecord>();

        public bool coreDefencesLowered;
        public int coreDefencesLoweredTick = -1;
        public int threatClearStartTick = -1;
        public int nextThreatCheckTick = -1;

        public CerebrexSupportEvacMode evacMode = CerebrexSupportEvacMode.None;
        public List<SymbiosisCovenantCerebrexSupportEvacVehicle> evacVehicles = new List<SymbiosisCovenantCerebrexSupportEvacVehicle>();
        public int evacuationRetryTick = -1;
        public int loadingStartTick = -1;

        public bool stopSchedulingWaves;
        public string? invalidReason;

        private SymbiosisCovenantCerebrexSupportDef Config
            => SymbiosisCovenantCerebrexSupportDefOf.MAP_SymbiosisCovenant_CerebrexSupportConfig;

        public Map? SupportMap => site?.Map;

        public override string DescriptionPart
            => "MAP_SymbiosisCovenant_CerebrexSupport_Description".Translate();

        protected override void Enable(SignalArgs receivedArgs)
        {
            base.Enable(receivedArgs);
            if (mapEnteredTick < 0)
            {
                mapEnteredTick = Find.TickManager.TicksGame;
            }

            if (stage == CerebrexSupportStage.WaitingForMap)
            {
                stage = CerebrexSupportStage.OfferDelay;
            }
        }

        public override void QuestPartTick()
        {
            base.QuestPartTick();

            Map? map = SupportMap;
            if (map == null)
            {
                // 地图尚未生成（等待玩家进入）或 site 已失效。
                return;
            }

            if (stage == CerebrexSupportStage.Invalid
                || stage == CerebrexSupportStage.Completed
                || stage == CerebrexSupportStage.DeclinedOrExpired)
            {
                return;
            }

            int now = Find.TickManager.TicksGame;
            SymbiosisCovenantCerebrexSupportDef cfg = Config;

            switch (stage)
            {
                case CerebrexSupportStage.OfferDelay:
                    if (!offerSent && now - mapEnteredTick >= cfg.offerDelayTicks)
                    {
                        if (SymbiosisCovenantCerebrexSupportUtility.IsEligibleNow(this, map))
                        {
                            SendOffer(map, now);
                        }
                        else
                        {
                            // 进入时即不满足资格：永久关闭，不再每 tick 重试。
                            stage = CerebrexSupportStage.DeclinedOrExpired;
                            Log.Message($"{LogPrefix} 进入地图时资格不满足，关闭援军询问（site={site?.Label}）。");
                        }
                    }

                    break;

                case CerebrexSupportStage.OfferPending:
                    if (!offerResolved && now >= offerExpireTick)
                    {
                        HandleOfferTimeout(now);
                    }

                    break;

                case CerebrexSupportStage.WaitingFirstWave:
                    if (now >= firstWaveDueTick)
                    {
                        TryDeployNextWave(map, now);
                    }

                    break;

                case CerebrexSupportStage.CombatSupport:
                    if (coreDefencesLowered)
                    {
                        break;
                    }

                    if (now >= nextDeploymentRetryTick)
                    {
                        if (ShouldScheduleNextWave(map, now))
                        {
                            TryDeployNextWave(map, now);
                        }
                    }

                    break;

                case CerebrexSupportStage.CoreDefencesLowered:
                    if (now >= nextThreatCheckTick)
                    {
                        nextThreatCheckTick = now + cfg.threatCheckIntervalTicks;
                        bool anyThreat = GenHostility.AnyHostileActiveThreatToPlayer(
                            map,
                            countDormantPawnsAsHostile: true,
                            canBeFogged: true);
                        if (!anyThreat)
                        {
                            if (threatClearStartTick < 0)
                            {
                                threatClearStartTick = now;
                                Log.Message($"{LogPrefix} 无活动敌对威胁，开始稳定计时（site={site?.Label}）。");
                            }

                            if (now - threatClearStartTick >= cfg.threatClearStableTicks)
                            {
                                BeginEvacuation(map, now);
                            }
                        }
                        else
                        {
                            if (threatClearStartTick >= 0)
                            {
                                Log.Message($"{LogPrefix} 稳定期内重新出现威胁，重置稳定计时（site={site?.Label}）。");
                            }

                            threatClearStartTick = -1;
                        }
                    }

                    break;

                case CerebrexSupportStage.EvacuationPreparing:
                case CerebrexSupportStage.EvacuationLoading:
                    SymbiosisCovenantCerebrexSupportUtility.TickEvacuation(this, map, now);
                    break;
            }
        }

        private bool ShouldScheduleNextWave(Map map, int now)
        {
            if (coreDefencesLowered || stopSchedulingWaves)
            {
                return false;
            }

            if (waveCount >= Config.maxWaves)
            {
                return false;
            }

            if (now - lastSuccessfulWaveTick < Config.minimumWaveIntervalTicks)
            {
                return false;
            }

            SymbiosisCovenantCerebrexSupportWaveRecord? last = waves.LastOrDefault();
            if (last != null)
            {
                if (now - last.deployedTick < Config.minimumWaveAgeTicks)
                {
                    return false;
                }

                int capable = CountCombatCapable(last);
                int threshold = Mathf.CeilToInt(last.initialPawnCount * Config.remainingCombatCapableThreshold);
                if (capable > threshold)
                {
                    // 仍有足够战力，继续等待本轮波次消耗。
                    return false;
                }
            }

            if (!GenHostility.AnyHostileActiveThreatToPlayer(map, countDormantPawnsAsHostile: true, canBeFogged: true))
            {
                return false;
            }

            if (!SymbiosisCovenantCerebrexSupportUtility.HasAnyEligibleFaction(this, map))
            {
                return false;
            }

            return true;
        }

        private int CountCombatCapable(SymbiosisCovenantCerebrexSupportWaveRecord record)
        {
            int count = 0;
            Map? map = SupportMap;
            foreach (Pawn p in record.pawns)
            {
                if (p == null || p.Dead || p.Downed || p.Destroyed || p.Discarded)
                {
                    continue;
                }

                if (!p.Spawned || map == null || p.Map != map)
                {
                    continue;
                }

                if (!record.pawns.Contains(p))
                {
                    continue;
                }

                count++;
            }

            return count;
        }

        private void TryDeployNextWave(Map map, int now)
        {
            (bool success, bool stopScheduling, SymbiosisCovenantCerebrexSupportWaveRecord? record) =
                SymbiosisCovenantCerebrexSupportUtility.TryDeployWave(this, map, now);

            if (stopScheduling)
            {
                stopSchedulingWaves = true;
                nextDeploymentRetryTick = int.MaxValue;
                Log.Message($"{LogPrefix} 没有可参战派系，停止调度后续波次（site={site?.Label}）。");
                return;
            }

            if (!success || record == null)
            {
                nextDeploymentRetryTick = now + Config.failedDeploymentRetryTicks;
                return;
            }

            waves.Add(record);
            waveCount = waves.Count;
            lastSuccessfulWaveTick = now;
            nextDeploymentRetryTick = now + Config.minimumWaveIntervalTicks;

            if (stage == CerebrexSupportStage.WaitingFirstWave)
            {
                stage = CerebrexSupportStage.CombatSupport;
            }

            SendArrivalLetter(record);

            Log.Message($"{LogPrefix} 第 {record.waveIndex + 1} 波援军已部署（初始 {record.initialPawnCount} 人，派系 {record.factionRecords.Count}）。");
        }

        private void SendOffer(Map map, int now)
        {
            offerSent = true;
            offerResolved = false;
            offerCreatedTick = now;
            offerExpireTick = now + Config.offerTimeoutTicks;
            stage = CerebrexSupportStage.OfferPending;

            ChoiceLetter_SymbiosisCovenantCerebrexSupportOffer letter =
                new ChoiceLetter_SymbiosisCovenantCerebrexSupportOffer();
            letter.quest = quest;
            letter.offerId = offerId;
            letter.targetSite = site;
            letter.title = "MAP_SymbiosisCovenant_CerebrexSupport_OfferTitle".Translate();
            letter.Text = "MAP_SymbiosisCovenant_CerebrexSupport_OfferBody".Translate();
            letter.def = LetterDefOf.PositiveEvent;

            Find.LetterStack.ReceiveLetter(letter);
            letter.StartTimeout(Config.offerTimeoutTicks);

            Log.Message($"{LogPrefix} 已发送援军询问信件（offerId={offerId}，超时={offerExpireTick}）。");
        }

        private void SendArrivalLetter(SymbiosisCovenantCerebrexSupportWaveRecord record)
        {
            Pawn? lookTarget = record.pawns.FirstOrDefault(p => p != null && !p.Dead);
            string title = "MAP_SymbiosisCovenant_CerebrexSupport_ArrivalTitle".Translate(record.waveIndex + 1);
            string text = "MAP_SymbiosisCovenant_CerebrexSupport_ArrivalBody".Translate(
                record.waveIndex + 1,
                record.initialPawnCount);
            Find.LetterStack.ReceiveLetter(title, text, LetterDefOf.PositiveEvent, lookTarget);
        }

        private void HandleOfferTimeout(int now)
        {
            if (offerResolved)
            {
                return;
            }

            offerResolved = true;
            SymbiosisCovenantCerebrexSupportUtility.RemoveLetterByOfferId(offerId);
            if (stage == CerebrexSupportStage.OfferPending)
            {
                stage = CerebrexSupportStage.DeclinedOrExpired;
            }

            Log.Message($"{LogPrefix} 询问信件超时，永久关闭援军（offerId={offerId}）。");
        }

        /// <summary>
        /// 接受援助：再次校验 Quest/Site/地图/盟约/派系，失败显示中文失败消息且不生成部分援军。
        /// </summary>
        public void TryAcceptOffer()
        {
            int now = Find.TickManager.TicksGame;

            if (offerResolved)
            {
                Messages.Message(
                    "MAP_SymbiosisCovenant_CerebrexSupport_AcceptFailInvalid".Translate(),
                    MessageTypeDefOf.RejectInput);
                return;
            }

            if (now >= offerExpireTick)
            {
                offerResolved = true;
                SymbiosisCovenantCerebrexSupportUtility.RemoveLetterByOfferId(offerId);
                if (stage == CerebrexSupportStage.OfferPending)
                {
                    stage = CerebrexSupportStage.DeclinedOrExpired;
                }

                Messages.Message(
                    "MAP_SymbiosisCovenant_CerebrexSupport_AcceptFailInvalid".Translate(),
                    MessageTypeDefOf.RejectInput);
                return;
            }

            Map? map = SupportMap;
            if (quest == null || site == null || map == null)
            {
                Messages.Message(
                    "MAP_SymbiosisCovenantCerebrexSupport_AcceptFailInvalid".Translate(),
                    MessageTypeDefOf.RejectInput);
                return;
            }

            if (coreDefencesLowered)
            {
                // 主脑防御已解除，不再允许接受援军。
                offerResolved = true;
                SymbiosisCovenantCerebrexSupportUtility.RemoveLetterByOfferId(offerId);
                stage = CerebrexSupportStage.DeclinedOrExpired;
                Messages.Message(
                    "MAP_SymbiosisCovenant_CerebrexSupport_AcceptFailInvalid".Translate(),
                    MessageTypeDefOf.RejectInput);
                return;
            }

            if (!SymbiosisCovenantCerebrexSupportUtility.IsEligibleNow(this, map))
            {
                GameComponent_SymbiosisCovenantState? comp = GameComponent_SymbiosisCovenantState.CurrentComponent;
                if (comp == null || !GameComponent_SymbiosisCovenantState.IsActive || comp.CovenantLevel < 2)
                {
                    Messages.Message(
                        "MAP_SymbiosisCovenant_CerebrexSupport_AcceptFailCovenant".Translate(),
                        MessageTypeDefOf.RejectInput);
                }
                else
                {
                    Messages.Message(
                        "MAP_SymbiosisCovenant_CerebrexSupport_AcceptFailNoAlly".Translate(),
                        MessageTypeDefOf.RejectInput);
                }

                return;
            }

            // 快照：盟约等级、威胁点数、每波固定点数。
            GameComponent_SymbiosisCovenantState comp2 = GameComponent_SymbiosisCovenantState.CurrentComponent!;
            covenantLevelSnapshot = comp2.CovenantLevel;
            threatPointsSnapshot = SymbiosisCovenantCerebrexSupportUtility.ComputeThreatSnapshot(map, site);
            perWaveSupportPoints = SymbiosisCovenantCerebrexSupportUtility.ComputePerWavePoints(
                Config,
                covenantLevelSnapshot,
                threatPointsSnapshot);
            firstWaveDueTick = now + Config.firstWaveDelayTicks;
            offerResolved = true;
            stage = CerebrexSupportStage.WaitingFirstWave;

            SymbiosisCovenantCerebrexSupportUtility.RemoveLetterByOfferId(offerId);

            Log.Message($"{LogPrefix} 玩家接受援军。等级快照={covenantLevelSnapshot}，威胁快照={threatPointsSnapshot:0}，每波点数={perWaveSupportPoints:0}。");
        }

        /// <summary>
        /// 婉拒援助：标记永久关闭，不产生任何惩罚，不再触发。
        /// </summary>
        public void ResolveDecline()
        {
            if (offerResolved)
            {
                return;
            }

            offerResolved = true;
            SymbiosisCovenantCerebrexSupportUtility.RemoveLetterByOfferId(offerId);
            stage = CerebrexSupportStage.DeclinedOrExpired;
            Log.Message($"{LogPrefix} 玩家婉拒援军（offerId={offerId}）。");
        }

        private void BeginEvacuation(Map map, int now)
        {
            stage = CerebrexSupportStage.EvacuationPreparing;
            nextThreatCheckTick = -1;
            threatClearStartTick = -1;

            if (ModsConfig.RoyaltyActive)
            {
                evacMode = CerebrexSupportEvacMode.RoyaltyShuttle;
            }
            else
            {
                evacMode = CerebrexSupportEvacMode.OdysseyMechPod;
            }

            SymbiosisCovenantCerebrexSupportUtility.BeginEvacuation(this, map, now);
            Log.Message($"{LogPrefix} 主脑战斗结束，开始撤离（模式={evacMode}，site={site?.Label}）。");
        }

        /// <summary>
        /// 由支援系统通知：主脑防御已解除。
        /// </summary>
        public void NotifyCoreDefencesLowered(int now)
        {
            if (coreDefencesLowered)
            {
                return;
            }

            coreDefencesLowered = true;
            coreDefencesLoweredTick = now;

            // 若玩家尚未接受援军，则不再允许接受。
            if (!offerResolved && (stage == CerebrexSupportStage.OfferPending || stage == CerebrexSupportStage.OfferDelay))
            {
                offerResolved = true;
                SymbiosisCovenantCerebrexSupportUtility.RemoveLetterByOfferId(offerId);
                stage = CerebrexSupportStage.DeclinedOrExpired;
            }

            if (stage == CerebrexSupportStage.WaitingFirstWave || stage == CerebrexSupportStage.CombatSupport)
            {
                stage = CerebrexSupportStage.CoreDefencesLowered;
                nextThreatCheckTick = now + Config.threatCheckIntervalTicks;
            }

            Log.Message($"{LogPrefix} 主脑防御已解除（site={site?.Label}）。");
        }

        /// <summary>
        /// 旧存档补装后调用：玩家已进入地图且地图已生成时，直接恢复到正确阶段，无需重放 MapGenerated 信号。
        /// </summary>
        public void MarkMapEnteredForBackfill(bool coreAlreadyLowered)
        {
            // 确保 part 进入 Enabled，使 QuestPartTick 正常驱动。
            if (State != QuestPartState.Enabled)
            {
                Enable(new SignalArgs());
            }

            if (mapEnteredTick < 0)
            {
                mapEnteredTick = Find.TickManager.TicksGame;
            }

            if (stage == CerebrexSupportStage.WaitingForMap)
            {
                stage = CerebrexSupportStage.OfferDelay;
            }

            if (coreAlreadyLowered && !coreDefencesLowered)
            {
                coreDefencesLowered = true;
                coreDefencesLoweredTick = Find.TickManager.TicksGame;
                if (stage == CerebrexSupportStage.OfferDelay
                    || stage == CerebrexSupportStage.OfferPending
                    || stage == CerebrexSupportStage.WaitingFirstWave
                    || stage == CerebrexSupportStage.CombatSupport)
                {
                    offerResolved = true;
                    SymbiosisCovenantCerebrexSupportUtility.RemoveLetterByOfferId(offerId);
                    stage = CerebrexSupportStage.CoreDefencesLowered;
                    nextThreatCheckTick = Find.TickManager.TicksGame + Config.threatCheckIntervalTicks;
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_References.Look(ref site, "site");
            Scribe_Values.Look(ref mapGeneratedSignal, "mapGeneratedSignal");
            Scribe_Values.Look(ref offerId, "offerId");

            Scribe_Values.Look(ref stage, "stage", CerebrexSupportStage.WaitingForMap);
            Scribe_Values.Look(ref offerSent, "offerSent", false);
            Scribe_Values.Look(ref offerResolved, "offerResolved", false);
            Scribe_Values.Look(ref offerCreatedTick, "offerCreatedTick", 0);
            Scribe_Values.Look(ref offerExpireTick, "offerExpireTick", 0);

            Scribe_Values.Look(ref covenantLevelSnapshot, "covenantLevelSnapshot", 0);
            Scribe_Values.Look(ref threatPointsSnapshot, "threatPointsSnapshot", 0f);
            Scribe_Values.Look(ref perWaveSupportPoints, "perWaveSupportPoints", 0f);
            Scribe_Values.Look(ref firstWaveDueTick, "firstWaveDueTick", 0);

            Scribe_Values.Look(ref waveCount, "waveCount", 0);
            Scribe_Values.Look(ref lastSuccessfulWaveTick, "lastSuccessfulWaveTick", -1);
            Scribe_Values.Look(ref nextDeploymentRetryTick, "nextDeploymentRetryTick", -1);
            Scribe_Values.Look(ref mapEnteredTick, "mapEnteredTick", -1);

            Scribe_Collections.Look(ref waves, "waves", LookMode.Deep);

            Scribe_Values.Look(ref coreDefencesLowered, "coreDefencesLowered", false);
            Scribe_Values.Look(ref coreDefencesLoweredTick, "coreDefencesLoweredTick", -1);
            Scribe_Values.Look(ref threatClearStartTick, "threatClearStartTick", -1);
            Scribe_Values.Look(ref nextThreatCheckTick, "nextThreatCheckTick", -1);

            Scribe_Values.Look(ref evacMode, "evacMode", CerebrexSupportEvacMode.None);
            Scribe_Collections.Look(ref evacVehicles, "evacVehicles", LookMode.Deep);
            Scribe_Values.Look(ref evacuationRetryTick, "evacuationRetryTick", -1);
            Scribe_Values.Look(ref loadingStartTick, "loadingStartTick", -1);

            Scribe_Values.Look(ref stopSchedulingWaves, "stopSchedulingWaves", false);
            Scribe_Values.Look(ref invalidReason, "invalidReason");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                waves ??= new List<SymbiosisCovenantCerebrexSupportWaveRecord>();
                evacVehicles ??= new List<SymbiosisCovenantCerebrexSupportEvacVehicle>();
                waves.RemoveAll(w => w == null);

                // 主脑已允许互动但存档未保存该状态：无副作用恢复。
                Map? map = SupportMap;
                if (map != null && !coreDefencesLowered)
                {
                    CompCerebrexCore? core = map.listerThings.AllThings
                        .OfType<CompCerebrexCore>()
                        .FirstOrDefault();
                    if (core != null && core.CanInteract().Accepted)
                    {
                        coreDefencesLowered = true;
                        coreDefencesLoweredTick = Find.TickManager.TicksGame;
                    }
                }

                if (map != null && coreDefencesLowered
                    && (stage == CerebrexSupportStage.OfferDelay
                        || stage == CerebrexSupportStage.OfferPending
                        || stage == CerebrexSupportStage.WaitingFirstWave
                        || stage == CerebrexSupportStage.CombatSupport))
                {
                    stage = CerebrexSupportStage.CoreDefencesLowered;
                    if (nextThreatCheckTick < 0)
                    {
                        nextThreatCheckTick = Find.TickManager.TicksGame + Config.threatCheckIntervalTicks;
                    }
                }
            }
        }

        public override void Cleanup()
        {
            base.Cleanup();

            if (stage == CerebrexSupportStage.Invalid)
            {
                return;
            }
        }

        public void MarkInvalid(string reason)
        {
            stage = CerebrexSupportStage.Invalid;
            invalidReason = reason;
            Log.Warning($"{LogPrefix} 进入 Invalid：{reason}");
        }
    }
}

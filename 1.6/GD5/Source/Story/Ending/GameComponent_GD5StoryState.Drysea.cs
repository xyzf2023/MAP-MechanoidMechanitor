using System;
using System.Collections.Generic;
using System.Linq;
using GD3;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.GD5
{
    public sealed partial class GameComponent_GD5StoryState
    {
        private const int DryseaArrivalDelayTicks = 180;
        private const int DryseaRemovalDelayTicks = 180;

        private Map? returnMap;
        private bool dryseaVisit;
        private IntVec3 returnCell = IntVec3.Invalid;
        private List<GD5BlackHiveReturnPoint> returnPoints = new List<GD5BlackHiveReturnPoint>();
        private bool endingAfterReturn;
        private int travelRetryTick = -1;
        private int dryseaRemovalTick = -1;
        private int dryseaArrivalTick = -1;
        private Pawn? returningSpeaker;
        // 队列属于当前运行，不保存委托；读档后由 Tick 重新排队。
        private bool travelEventQueued;
        private bool travelFailureReported;

        // 口袋地图依赖原地图；全员暂时离开任务地图时，也必须保留可返程的原地图。
        internal bool KeepsTravelMap(Map? map) => HasVisit && dryseaVisit && map != null
            && (map == returnMap || map == visitMap);

        internal bool CanReturnFromDrysea(Pawn? target) => target != null && CanTalkTo(target)
            && returnMap != null && Find.Maps.Contains(returnMap);

        internal bool CanReturnSpeaker(Pawn? target, Pawn? speaker) => CanReturnFromDrysea(target)
            && GD5BlackHiveEndingService.CanSpeak(speaker) && speaker?.Map == visitMap;

        internal bool TryCancelVisit(Pawn? target)
        {
            if (!CanReturnFromDrysea(target)) return false;
            DismissVisitor();
            return true;
        }

        internal bool TryReturnSpeaker(Pawn? target, Pawn? speaker)
        {
            if (!CanReturnSpeaker(target, speaker)) return false;
            ClearSkipCameraJump();
            returningSpeaker = speaker;
            if (!returnPoints.Any(p => p.pawn == speaker))
                returnPoints.Add(new GD5BlackHiveReturnPoint { pawn = speaker, cell = returnCell });
            visitPhase = GD5BlackHiveVisitPhase.ReturningSpeaker;
            travelRetryTick = -1;
            QueueDryseaTravel();
            return true;
        }

        private void QueueDryseaTravel()
        {
            if (travelEventQueued) return;
            travelEventQueued = true;
            LongEventHandler.QueueLongEvent(() =>
            {
                bool singleReturn = visitPhase == GD5BlackHiveVisitPhase.ReturningSpeaker;
                try
                {
                    if (!ReferenceEquals(Current, this)) return;
                    if (IsArriving)
                    {
                        if (visitMap == null) GenerateDrysea();
                        else TransferToDrysea();
                    }
                    else if (visitPhase == GD5BlackHiveVisitPhase.Returning) ReturnFromDrysea();
                    else if (visitPhase == GD5BlackHiveVisitPhase.ReturningSpeaker) ReturnSpeakerFromDrysea();
                }
                catch (Exception ex)
                {
                    ClearSkipCameraJump();
                    ReportTravelFailure(ex.ToString());
                    // 生成或部分传送失败后走同一返程路径；不能遗弃已转移的角色。
                    // 单人返程失败只能重试此人，不能扩大成全员撤离。
                    if (!singleReturn)
                        visitPhase = GD5BlackHiveVisitPhase.Returning;
                    travelRetryTick = Find.TickManager.TicksGame + 250;
                }
                finally { travelEventQueued = false; }
            }, "GeneratingMap", doAsynchronously: false, exceptionHandler: null);
        }

        private void GenerateDrysea()
        {
            Map? source = returnMap;
            if (source == null || !Find.Maps.Contains(source) || !GD5BlackHiveEndingService.IsUnlocked
                || !GD5BlackHiveEndingService.IsFriendly)
            {
                DismissVisitor();
                return;
            }
            List<Pawn> candidates = MechanoidStoryDepartureUtility.GetCandidates(source);
            if (candidates.Count == 0)
            {
                DismissVisitor();
                return;
            }

            // 先完整生成、布置地图与毒蜂；这之前不恢复、不移出任何玩家角色。
            var existingMaps = new HashSet<Map>(Find.Maps);
            try
            {
                visitMap = PocketMapUtility.GeneratePocketMap(GD5BlackHiveDryseaUtility.Size, GDDefOf.Drysea, null, source);
            }
            catch
            {
                // 原版在内容生成前就 AddMap；异常时也要保留这张半成品的引用，以便安全清理。
                visitMap = Find.Maps.FirstOrDefault(m => !existingMaps.Contains(m)
                    && m.IsPocketMap && m.generatorDef == GDDefOf.Drysea);
                throw;
            }
            Map sea = visitMap;
            GD5BlackHiveDryseaUtility.PrepareMap(sea);
            if (!sea.Center.Standable(sea) || !GD5BlackHiveDryseaUtility.Entrance.Standable(sea))
                throw new InvalidOperationException("枯海中心或任务入口不可站立。");
            visitor = PawnGenerator.GeneratePawn(GD5BlackHiveEndingService.VisitorKind, GD5BlackHiveEndingService.Hive);
            GenSpawn.Spawn(visitor, sea.Center, sea, Rot4.North);
            if (!visitor.Spawned || visitor.Map != sea)
                throw new InvalidOperationException("枯海毒蜂未能生成。");
            GD5BlackHiveEndingService.PlaySkip(sea, sea.Center, arriving: true);

            // 地图和毒蜂完整生成后才开始计时；等待期间不恢复或移出殖民地角色。
            dryseaArrivalTick = Find.TickManager.TicksGame + DryseaArrivalDelayTicks;
            travelRetryTick = dryseaArrivalTick;
        }

        private void TransferToDrysea()
        {
            if (dryseaArrivalTick < 0)
                dryseaArrivalTick = Find.TickManager.TicksGame + DryseaArrivalDelayTicks;
            if (Find.TickManager.TicksGame < dryseaArrivalTick)
            {
                travelRetryTick = dryseaArrivalTick;
                return;
            }
            Map? source = returnMap;
            Map? sea = visitMap;
            if (source == null || sea == null || !Find.Maps.Contains(source) || !Find.Maps.Contains(sea)
                || !GD5BlackHiveEndingService.IsUnlocked || !GD5BlackHiveEndingService.IsFriendly
                || visitor?.Spawned != true || visitor.Map != sea)
            {
                DismissVisitor();
                return;
            }
            // 在真正折跃时确定名单和返回位置，避免等待期间的移动、死亡或离图造成过期快照。
            List<Pawn> candidates = MechanoidStoryDepartureUtility.GetCandidates(source);
            if (candidates.Count == 0)
            {
                DismissVisitor();
                return;
            }

            foreach (Pawn pawn in candidates)
            {
                if (!MechTransformationUtility.TryGetCurrentRepresentation(pawn, out Thing? representation)
                    || representation?.MapHeld != source)
                    throw new InvalidOperationException("机械师已离开联络地图：" + pawn.LabelShortCap);
                returnPoints.Add(new GD5BlackHiveReturnPoint { pawn = pawn, cell = representation.PositionHeld });
            }
            // 整批先恢复本体/取出容器，避免携带者先离图后，容器中的另一位机械师无法找到。
            foreach (Pawn pawn in candidates)
                if (!MechanoidStoryDepartureUtility.TryPrepareForTransfer(pawn, source, out string reason))
                    throw new InvalidOperationException(pawn.LabelShortCap + "：" + reason);
            foreach (Pawn pawn in candidates)
                if (!GD5BlackHiveDryseaUtility.TryTransfer(pawn, sea, GD5BlackHiveDryseaUtility.Entrance, Rot4.South))
                    throw new InvalidOperationException("机械师未能传送至枯海：" + pawn.LabelShortCap);

            visitPhase = GD5BlackHiveVisitPhase.Visiting;
            dryseaArrivalTick = -1;
            travelRetryTick = -1;
            QueueSkipCameraJump(sea, GD5BlackHiveDryseaUtility.Entrance);
        }

        private void ReturnSpeakerFromDrysea()
        {
            Pawn? speaker = returningSpeaker;
            Map? sea = visitMap;
            Map? destination = returnMap;
            if (speaker == null || speaker.Dead || speaker.Destroyed || speaker.Discarded
                || sea == null || !Find.Maps.Contains(sea) || destination == null || !Find.Maps.Contains(destination)
                || (speaker.MapHeld != null && speaker.MapHeld != sea && speaker.MapHeld != destination))
            {
                ReportTravelFailure("对话者或返程地图已不可用，取消本次单人返程。");
                returningSpeaker = null;
                ResumeVisit();
                return;
            }
            bool arrived = speaker.Spawned && speaker.Map == destination;
            if (!arrived)
            {
                bool prepared = speaker.MapHeld == null && speaker.holdingOwner == null;
                if (!prepared) prepared = MechanoidStoryDepartureUtility.TryPrepareForTransfer(speaker, sea, out _);
                IntVec3 cell = returnPoints.FirstOrDefault(p => p.pawn == speaker)?.cell ?? returnCell;
                arrived = prepared && GD5BlackHiveDryseaUtility.TryTransfer(speaker, destination,
                    cell.IsValid ? cell : destination.Center, Rot4.South);
            }
            if (!arrived)
            {
                ReportTravelFailure("对话者尚未安全返回，保留枯海并稍后重试该角色。");
                travelRetryTick = Find.TickManager.TicksGame + 250;
                return;
            }
            returningSpeaker = null;
            travelRetryTick = -1;
            ResumeVisit();
            // 即使送回的是最后一位对话者，也保留枯海与毒蜂，不触发全员返程或地图销毁。
            QueueSkipCameraJump(destination, speaker.Position);
        }

        private void ReturnFromDrysea()
        {
            // 全员已折跃后只等待清图，不重复执行传送、派系结算或重新播放折跃。
            if (dryseaRemovalTick >= 0)
            {
                // 兼容上一版已保存的结局清图倒计时：改为先显示字幕，不在这里销毁地图。
                if (endingAfterReturn) BeginEndingCredits();
                else if (Find.TickManager.TicksGame >= dryseaRemovalTick) FinishDryseaTravel();
                return;
            }
            Map? sea = visitMap;
            if (sea != null && !Find.Maps.Contains(sea)) sea = null;
            Map? destination = returnMap;
            if (destination == null && sea != null && !dryseaVisit)
            {
                // 兼容旧版毒蜂受伤触发的撤离；此时没有本模块生成的地图。
                if (visitor != null && !visitor.Destroyed) GD5BlackHiveEndingService.DismissPawn(visitor);
                ResetVisit();
                return;
            }
            if (destination == null || !Find.Maps.Contains(destination))
            {
                if (sea == null && returnPoints.Count == 0) { ResetVisit(); return; }
                ReportTravelFailure("返程地图已不可用，保留枯海与返程记录。");
                travelRetryTick = Find.TickManager.TicksGame + 250;
                return;
            }

            var pawns = sea == null ? new List<Pawn>() : GD5BlackHiveDryseaUtility.GetReturnPawns(sea, visitor);
            // 仅找回移动异常留下的世界角色；已经正式加入黑衣巢的同行者不能被返程逻辑接回。
            pawns.AddRange(returnPoints.Select(p => p.pawn).OfType<Pawn>().Where(p =>
                !p.Dead && !p.Destroyed && !p.Discarded && p.MapHeld == null
                && p.Faction == Faction.OfPlayerSilentFail && !GameComponent_MechanoidStoryDeparture.HasDeparted(p)));
            pawns = pawns.Distinct().ToList();
            foreach (Pawn pawn in pawns)
                if (!returnPoints.Any(p => p.pawn == pawn))
                    returnPoints.Add(new GD5BlackHiveReturnPoint { pawn = pawn, cell = returnCell });

            bool returned = true;
            var prepared = new List<Pawn>();
            foreach (Pawn pawn in pawns)
            {
                try
                {
                    if (pawn.MapHeld == null && pawn.holdingOwner == null) prepared.Add(pawn);
                    else if (sea != null && MechanoidStoryDepartureUtility.TryPrepareForTransfer(pawn, sea, out _))
                        prepared.Add(pawn);
                    else returned = false;
                }
                catch (Exception ex) { returned = false; ReportTravelFailure(ex.ToString()); }
            }
            foreach (Pawn pawn in prepared)
            {
                IntVec3 cell = returnPoints.First(p => p.pawn == pawn).cell;
                if (!GD5BlackHiveDryseaUtility.TryTransfer(pawn, destination,
                    cell.IsValid ? cell : destination.Center, Rot4.South)) returned = false;
            }
            // 等待期间死亡的机械族也不能随着清图遗失。
            var corpses = returnPoints.Select(p => p.pawn?.Corpse).OfType<Corpse>();
            if (sea != null)
                corpses = corpses.Concat(sea.listerThings.ThingsInGroup(ThingRequestGroup.Corpse)
                    .OfType<Corpse>().Where(c => c.InnerPawn.RaceProps.IsMechanoid))
                    .Concat(sea.mapPawns.AllPawns.Where(p => p.Dead && p.RaceProps.IsMechanoid)
                        .Select(p => p.Corpse).OfType<Corpse>());
            foreach (Corpse corpse in corpses.Distinct().ToList())
                if (sea != null && corpse.MapHeld == sea
                    && !GD5BlackHiveDryseaUtility.TryReturnCorpse(corpse, destination, returnCell)) returned = false;

            if (!returned || (sea != null && (GD5BlackHiveDryseaUtility.GetReturnPawns(sea, visitor).Count > 0
                || sea.mapPawns.AllPawns.Any(p => p != visitor && p.Faction == Faction.OfPlayerSilentFail))))
            {
                ReportTravelFailure("仍有角色未能安全返回，保留枯海并稍后重试。");
                travelRetryTick = Find.TickManager.TicksGame + 250;
                return;
            }

            // 结局保留枯海直到字幕已显示；全员返程仍在折跃完成 180 tick 后清图。
            if (visitor != null && !visitor.Destroyed) GD5BlackHiveEndingService.DismissPawn(visitor);
            visitor = null;
            if (endingAfterReturn)
            {
                BeginEndingCredits();
                return;
            }
            dryseaRemovalTick = Find.TickManager.TicksGame + DryseaRemovalDelayTicks;
            travelRetryTick = dryseaRemovalTick;
            if (sea != null && Find.CurrentMap == sea)
                QueueSkipCameraJump(destination, returnCell.IsValid ? returnCell : destination.Center);
        }

        private void FinishDryseaTravel()
        {
            Map? sea = visitMap;
            if (sea != null && !Find.Maps.Contains(sea)) sea = null;
            if (sea != null)
            {
                // 延迟期间若又有机械族或玩家角色进入，先重新返程，不直接清图。
                if (GD5BlackHiveDryseaUtility.GetReturnPawns(sea, visitor).Count > 0
                    || sea.mapPawns.AllPawns.Any(p => p.Faction == Faction.OfPlayerSilentFail)
                    || sea.listerThings.ThingsInGroup(ThingRequestGroup.Corpse).OfType<Corpse>()
                        .Any(c => c.InnerPawn.RaceProps.IsMechanoid))
                {
                    dryseaRemovalTick = -1;
                    ReturnFromDrysea();
                    return;
                }
                if (!dryseaVisit || !sea.IsPocketMap || sea.generatorDef != GDDefOf.Drysea || sea == returnMap)
                    throw new InvalidOperationException("拒绝销毁非本次枯海的地图。");
                if (Find.CurrentMap == sea && returnMap != null && Find.Maps.Contains(returnMap))
                    CameraJumper.TryJumpAndSelect(new GlobalTargetInfo(
                        returnCell.IsValid ? returnCell : returnMap.Center, returnMap));
                PocketMapUtility.DestroyPocketMap(sea);
            }
            ResetVisit();
        }

        private void MigrateLegacyVisit()
        {
            if (dryseaVisit || returnMap != null || visitMap == null || visitPhase == GD5BlackHiveVisitPhase.Returning) return;
            dryseaVisit = true;
            returnMap = visitMap;
            returnCell = visitor?.PositionHeld ?? visitMap.Center;
            if (visitor != null && !visitor.Destroyed) GD5BlackHiveEndingService.DismissPawn(visitor);
            visitor = null;
            visitMap = null;
            departureTick = -1;
            visitPhase = GD5BlackHiveVisitPhase.Arriving;
        }

        private void ReportTravelFailure(string detail)
        {
            if (travelFailureReported) return;
            travelFailureReported = true;
            Log.Error("[MAP-GD5] 枯海往返尚未完成：" + detail);
            Messages.Message("MAP_GD5.Ending.Unavailable".Translate(), MessageTypeDefOf.RejectInput);
        }

        private void ResetDryseaTravel()
        {
            ResetSkipPresentation();
            returnMap = null;
            dryseaVisit = false;
            returnCell = IntVec3.Invalid;
            returnPoints.Clear();
            endingAfterReturn = false;
            travelRetryTick = -1;
            dryseaRemovalTick = -1;
            dryseaArrivalTick = -1;
            returningSpeaker = null;
            travelEventQueued = travelFailureReported = false;
        }

        private void ExposeDryseaTravel()
        {
            ExposeSkipPresentation();
            Scribe_References.Look(ref returnMap, "gd5BlackHiveReturnMap");
            Scribe_Values.Look(ref dryseaVisit, "gd5BlackHiveDryseaVisit", false);
            Scribe_Values.Look(ref returnCell, "gd5BlackHiveReturnCell", IntVec3.Invalid);
            Scribe_Collections.Look(ref returnPoints, "gd5BlackHiveReturnPoints", LookMode.Deep);
            Scribe_Values.Look(ref endingAfterReturn, "gd5BlackHiveEndingAfterReturn", false);
            Scribe_Values.Look(ref travelRetryTick, "gd5BlackHiveTravelRetryTick", -1);
            Scribe_Values.Look(ref dryseaRemovalTick, "gd5BlackHiveDryseaRemovalTick", -1);
            Scribe_Values.Look(ref dryseaArrivalTick, "gd5BlackHiveDryseaArrivalTick", -1);
            Scribe_References.Look(ref returningSpeaker, "gd5BlackHiveReturningSpeaker");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                returnPoints ??= new List<GD5BlackHiveReturnPoint>();
                returnPoints.RemoveAll(p => p == null || p.pawn == null || p.pawn.Discarded);
                travelEventQueued = travelFailureReported = false;
            }
        }
    }
}

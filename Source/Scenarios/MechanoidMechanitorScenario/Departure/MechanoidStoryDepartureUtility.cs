using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>条件联动程序集共用的剧情接人入口；形态恢复仍由原框架完成。</summary>
    public static class MechanoidStoryDepartureUtility
    {
        public static List<Pawn> GetCandidates(Map map)
        {
            return GameComponent_MechanoidMechanitorRegistry.CurrentRegisteredMechanitors
                .Where(p => IsCandidate(p, map)).ToList();
        }

        private static bool IsCandidate(Pawn pawn, Map map)
        {
            return pawn.Faction == Faction.OfPlayerSilentFail
                && OrbitalBackupGameEndUtility.IsLivingMechanitor(pawn)
                && MechTransformationUtility.TryGetCurrentRepresentation(pawn, out Thing? representation)
                && representation?.MapHeld == map;
        }

        /// <summary>
        /// 在调用时确定名单，先完成整批离图，再变更派系。回调分别用于离场视觉与单人失败提示。
        /// 返回实际成功转入目标派系的世界角色；不销毁 Pawn、不撤销身份。
        /// </summary>
        public static IReadOnlyList<Pawn> Depart(Map map, Faction destination,
            Action<Pawn, IntVec3> beforeExit, Action<Pawn, string> failed)
        {
            var completed = new List<Pawn>();
            var state = GameComponent_MechanoidStoryDeparture.Current;
            if (state == null || state.processing || map == null || !Find.Maps.Contains(map)
                || destination == null || destination == Faction.OfPlayerSilentFail) return completed;

            state.processing = true;
            var exited = new List<Pawn>();
            var positions = new Dictionary<Pawn, IntVec3>();
            try
            {
                var prepared = new List<Pawn>();
                // 必须先准备整批，再离图；容器中的同行者不能被其携带者提前带入世界。
                foreach (Pawn pawn in GetCandidates(map))
                {
                    try
                    {
                        if (TryPrepareForTransfer(pawn, map, out string reason)) prepared.Add(pawn);
                        else failed(pawn, reason);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("[MAP-机械族机械师] 剧情离场准备失败：" + pawn + "\n" + ex);
                        failed(pawn, "恢复形态或取出容器时发生异常。");
                    }
                }

                foreach (Pawn pawn in prepared)
                {
                    if (!IsCandidate(pawn, map) || !pawn.Spawned) continue;
                    IntVec3 position = pawn.Position;
                    positions[pawn] = position;
                    bool wasTeleporting = pawn.teleporting;
                    try
                    {
                        pawn.jobs?.EndCurrentJob(JobCondition.InterruptForced);
                        // ExitMap 会销毁手上物品或连带带走被搬运者，因此必须先安全放下。
                        if (pawn.carryTracker?.CarriedThing != null
                            && !pawn.carryTracker.TryDropCarriedThing(position, ThingPlaceMode.Near, out _))
                        {
                            failed(pawn, "无法安全放下正在搬运的对象。");
                            continue;
                        }
                        // 对第三方嵌套容器采取保守处理，防止顺带带走非名单中的角色。
                        if (ThingOwnerUtility.GetAllThingsRecursively(pawn).Any(t => t is Pawn))
                        {
                            failed(pawn, "携带的容器中仍有未取出的角色。");
                            continue;
                        }
                        beforeExit(pawn, position);
                        pawn.teleporting = true;
                        pawn.ExitMap(allowedToJoinOrCreateCaravan: false, Rot4.North);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("[MAP-机械族机械师] 剧情离图失败：" + pawn + "\n" + ex);
                        failed(pawn, "离开地图时发生异常。");
                    }
                    finally
                    {
                        pawn.teleporting = wasTeleporting;
                    }
                    // 某些通知可能在 DeSpawn 后抛异常；仍接管真正离图者，避免遗失世界归属。
                    if (!pawn.Spawned && pawn.MapHeld == null && !pawn.Destroyed && !pawn.Dead)
                    {
                        exited.Add(pawn);
                    }
                }

                // 此处才允许出现 SetFaction。先解除全部监管关系，同批同行的下属也不例外。
                var disconnected = new HashSet<Pawn>();
                foreach (Pawn pawn in exited)
                {
                    try
                    {
                        // ExitMap 默认使用可回收世界存储；剧情同行者须保留数据，供以后事件再次使用。
                        if (pawn.IsWorldPawn()) Find.WorldPawns.RemovePawn(pawn);
                        Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.KeepForever);
                        MAPOverseerAssignmentUtility.DisconnectAllOverseerRelations(pawn);
                        disconnected.Add(pawn);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("[MAP-机械族机械师] 剧情离场监管解除失败：" + pawn + "\n" + ex);
                        failed(pawn, "未能完全解除监管控制，已取消该角色的派系转换。");
                    }
                }
                foreach (Pawn pawn in exited)
                {
                    if (!disconnected.Contains(pawn)) continue;
                    try
                    {
                        if (pawn.Spawned || pawn.MapHeld != null
                            || pawn.mechanitor?.OverseenPawns.Any() == true)
                            throw new InvalidOperationException("离场角色仍有地图归属，或监管下属尚未全部解除。");
                        if (pawn.Faction != destination) pawn.SetFaction(destination);
                        if (pawn.Faction != destination)
                            throw new InvalidOperationException("角色未能加入目标派系。");
                        completed.Add(pawn);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("[MAP-机械族机械师] 剧情离场派系转换失败：" + pawn + "\n" + ex);
                        failed(pawn, "离图后转换派系失败，已尝试送回原地图。");
                        // 若第三方 Postfix 在换派系之后才抛错，角色已经完成目标状态。
                        if (!pawn.Spawned && pawn.MapHeld == null && pawn.Faction == destination)
                            completed.Add(pawn);
                    }
                }
                // 全批派系转换完成后再送回失败者，保持转换期间同行者全部离图的顺序。
                foreach (Pawn pawn in exited)
                {
                    if (completed.Contains(pawn) || pawn.Spawned || pawn.MapHeld != null || pawn.Destroyed) continue;
                    try
                    {
                        if (pawn.IsWorldPawn()) Find.WorldPawns.RemovePawn(pawn);
                        if (!GenPlace.TryPlaceThing(pawn, positions[pawn], map, ThingPlaceMode.Near))
                        {
                            failed(pawn, "原位置无法安全放回，角色暂时保留为玩家派系世界角色。");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error("[MAP-机械族机械师] 剧情离场失败后的地图恢复异常：" + pawn + "\n" + ex);
                    }
                    finally
                    {
                        if (!pawn.Spawned && pawn.MapHeld == null && !pawn.Destroyed && !pawn.IsWorldPawn())
                            Find.WorldPawns.PassToWorld(pawn);
                    }
                }
            }
            finally
            {
                try { state.CompleteDeparture(completed); }
                finally { state.processing = false; }
                OrbitalBackupGameEndUtility.Refresh();
                Find.GameEnder?.CheckOrUpdateGameOver();
            }
            return completed;
        }

        /// <summary>跨地图移动与正式离场共用：恢复本体并从容器取出，不改变派系或监管关系。</summary>
        public static bool TryPrepareForTransfer(Pawn pawn, Map map, out string reason)
        {
            reason = "角色已经离开本地图，或当前不能安全恢复。";
            if (pawn == null || pawn.Dead || pawn.Discarded
                || !MechTransformationUtility.TryGetCurrentRepresentation(pawn, out Thing? representation)
                || representation?.MapHeld != map) return false;
            if (MechTransformationUtility.IsTransitionInProgress(pawn))
            {
                reason = "形态转换尚未完成。";
                return false;
            }
            if (GameComponent_MechFusionSessionRegistry.TryGetSessionForSource(pawn, out var session)
                && session != null && !session.TeardownCompleted)
            {
                MechFusionTeardownService.TryTeardown(session, MechFusionExitReason.Manual, force: true);
                if (!session.TeardownCompleted)
                {
                    reason = "合体尚未安全解除。";
                    return false;
                }
            }
            if (GameComponent_MechTransformationRegistry.TryGetRecord(pawn, out var record)
                && record?.CurrentForm == MechTransformationForm.Building)
            {
                if (!MechBuildingConversionService.CanRestore(record.ExternalCarrier, false, out string? failure)
                    || !MechBuildingConversionService.TryRestore(record.ExternalCarrier!, sendFailureMessage: false))
                {
                    reason = failure ?? "建筑形态未能安全还原。";
                    return false;
                }
            }
            if (!MechTransformationUtility.IsInPawnForm(pawn)) return false;
            if (!pawn.Spawned)
            {
                ThingOwner? owner = pawn.holdingOwner;
                IntVec3 position = pawn.PositionHeld;
                if (owner == null || pawn.MapHeld != map || !position.InBounds(map)
                    || !owner.TryDrop(pawn, position, map, ThingPlaceMode.Near, out _))
                {
                    reason = "无法从当前容器中安全取出。";
                    return false;
                }
            }
            return pawn.Spawned && pawn.Map == map;
        }
    }
}

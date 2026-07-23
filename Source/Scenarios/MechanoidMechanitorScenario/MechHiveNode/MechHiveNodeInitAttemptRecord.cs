using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI.Group;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 完整节点单次初始化尝试的精确对象登记与失败清理记录。
    /// 由 <see cref="MAPMechHiveNode"/> 持有并可随存档保存；只清理已登记对象，绝不按派系扫描。
    /// </summary>
    public sealed class MechHiveNodeInitAttemptRecord : IExposable
    {
        private List<Thing> trackedThings = new List<Thing>();

        private List<Pawn> trackedPawns = new List<Pawn>();

        private int trackedLordLoadId = -1;

        private bool failureLogged;

        private bool cleanupFullyCompleted;

        public IReadOnlyList<Thing> TrackedThings => trackedThings;

        public IReadOnlyList<Pawn> TrackedPawns => trackedPawns;

        public int TrackedLordLoadId => trackedLordLoadId;

        public bool FailureLogged
        {
            get => failureLogged;
            set => failureLogged = value;
        }

        public bool CleanupFullyCompleted => cleanupFullyCompleted;

        /// <summary>回滚是否仍未完成；唯一完成依据是 <see cref="CleanupFullyCompleted"/>。</summary>
        public bool HasPendingCleanup => !cleanupFullyCompleted;

        /// <summary>当前是否仍登记有精确对象（不代替 CleanupFullyCompleted）。</summary>
        public bool HasTrackedObjects =>
            trackedLordLoadId >= 0
            || (trackedPawns != null && trackedPawns.Count > 0)
            || (trackedThings != null && trackedThings.Count > 0);

        /// <summary>供草图 Spawn 直接写入的同一列表，保证落地即登记。</summary>
        public List<Thing> ThingsListForRegistration => trackedThings;

        public void ResetForNewAttempt()
        {
            trackedThings.Clear();
            trackedPawns.Clear();
            trackedLordLoadId = -1;
            failureLogged = false;
            cleanupFullyCompleted = false;
        }

        /// <summary>成功初始化后丢弃追踪，不销毁任何内容。</summary>
        public void AbandonTrackingKeepContent()
        {
            trackedThings.Clear();
            trackedPawns.Clear();
            trackedLordLoadId = -1;
            failureLogged = false;
            cleanupFullyCompleted = true;
        }

        public void RegisterThing(Thing? thing)
        {
            if (thing == null || thing.Destroyed)
            {
                return;
            }

            if (!trackedThings.Contains(thing))
            {
                trackedThings.Add(thing);
            }
        }

        public void RegisterThingsFromList(List<Thing>? things)
        {
            if (things == null)
            {
                return;
            }

            for (int i = 0; i < things.Count; i++)
            {
                RegisterThing(things[i]);
            }
        }

        public void RegisterPawn(Pawn? pawn)
        {
            if (pawn == null || pawn.Destroyed)
            {
                return;
            }

            if (!trackedPawns.Contains(pawn))
            {
                trackedPawns.Add(pawn);
            }
        }

        public void RegisterLord(Lord? lord)
        {
            if (lord == null)
            {
                trackedLordLoadId = -1;
                return;
            }

            trackedLordLoadId = lord.loadID;
            cleanupFullyCompleted = false;
        }

        /// <summary>
        /// 尝试清理全部已登记对象。成功项从列表移除；失败项保留供下次重试。
        /// 全部完成后返回 true。
        /// </summary>
        public bool TryCleanupRegisteredContent(Map? map)
        {
            if (cleanupFullyCompleted)
            {
                return true;
            }

            // 1) 精确 Lord（仅 loadID 匹配者）
            if (trackedLordLoadId >= 0)
            {
                if (map == null || map.Disposed)
                {
                    trackedLordLoadId = -1;
                }
                else
                {
                    Lord? lord = FindLordByLoadId(map, trackedLordLoadId);
                    if (lord == null)
                    {
                        trackedLordLoadId = -1;
                    }
                    else
                    {
                        try
                        {
                            map.lordManager.RemoveLord(lord);
                            trackedLordLoadId = -1;
                        }
                        catch (Exception ex)
                        {
                            Log.Warning("[MAP] 精确回滚 Lord 失败（loadID="
                                + trackedLordLoadId
                                + "）: "
                                + ex);
                        }
                    }
                }
            }

            // 2) 已登记守军（含已落地与未落地）
            for (int i = trackedPawns.Count - 1; i >= 0; i--)
            {
                Pawn? pawn = trackedPawns[i];
                if (IsPawnFullyDiscarded(pawn))
                {
                    trackedPawns.RemoveAt(i);
                    continue;
                }

                try
                {
                    if (pawn != null)
                    {
                        WorldPawns? worldPawns = Find.WorldPawns;
                        bool containedByWorldPawns =
                            worldPawns != null && worldPawns.Contains(pawn);

                        if (!pawn.Spawned && !containedByWorldPawns)
                        {
                            if (worldPawns != null)
                            {
                                worldPawns.PassToWorld(
                                    pawn,
                                    PawnDiscardDecideMode.Discard);
                            }
                        }
                        else
                        {
                            MechHiveCombatPawnUtility.SafelyDiscardPawn(pawn);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning("[MAP] 精确回滚守军失败: " + ex);
                }

                if (IsPawnFullyDiscarded(pawn))
                {
                    trackedPawns.RemoveAt(i);
                }
            }

            // 3) 已登记建筑
            for (int i = trackedThings.Count - 1; i >= 0; i--)
            {
                Thing? thing = trackedThings[i];
                if (thing == null || thing.Destroyed)
                {
                    trackedThings.RemoveAt(i);
                    continue;
                }

                try
                {
                    thing.Destroy(DestroyMode.Vanish);
                    if (thing.Destroyed)
                    {
                        trackedThings.RemoveAt(i);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(
                        "[MAP] 精确回滚建筑失败（"
                            + (thing.def?.defName ?? "?")
                            + "）: "
                            + ex);
                }
            }

            cleanupFullyCompleted =
                trackedLordLoadId < 0
                && trackedPawns.Count == 0
                && trackedThings.Count == 0;
            return cleanupFullyCompleted;
        }

        /// <summary>
        /// 仅当 null，或已同时完成 Destroyed+Discarded 且未 Spawn、不在 WorldPawns 时视为清理完成。
        /// “未 Spawn 且不在 WorldPawns”本身是 PawnGroupMaker 刚生成未落地的正常状态，不得单独当作完成。
        /// </summary>
        private static bool IsPawnFullyDiscarded(Pawn? pawn)
        {
            if (pawn == null)
            {
                return true;
            }

            bool containedByWorldPawns =
                Find.WorldPawns != null && Find.WorldPawns.Contains(pawn);

            return pawn.Destroyed
                && pawn.Discarded
                && !pawn.Spawned
                && !containedByWorldPawns;
        }

        private static Lord? FindLordByLoadId(Map map, int loadId)
        {
            if (loadId < 0 || map?.lordManager?.lords == null)
            {
                return null;
            }

            List<Lord> lords = map.lordManager.lords;
            for (int i = 0; i < lords.Count; i++)
            {
                Lord lord = lords[i];
                if (lord != null && lord.loadID == loadId)
                {
                    return lord;
                }
            }

            return null;
        }

        public void ExposeData()
        {
            Scribe_Collections.Look(ref trackedThings, "MAP_initTrackedThings", LookMode.Reference);
            Scribe_Collections.Look(ref trackedPawns, "MAP_initTrackedPawns", LookMode.Reference);
            Scribe_Values.Look(ref trackedLordLoadId, "MAP_initTrackedLordLoadId", -1);
            Scribe_Values.Look(ref failureLogged, "MAP_initFailureLogged", false);
            Scribe_Values.Look(ref cleanupFullyCompleted, "MAP_initCleanupFullyCompleted", false);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                trackedThings ??= new List<Thing>();
                trackedPawns ??= new List<Pawn>();

                for (int i = trackedThings.Count - 1; i >= 0; i--)
                {
                    if (trackedThings[i] == null || trackedThings[i].Destroyed)
                    {
                        trackedThings.RemoveAt(i);
                    }
                }

                for (int i = trackedPawns.Count - 1; i >= 0; i--)
                {
                    if (IsPawnFullyDiscarded(trackedPawns[i]))
                    {
                        trackedPawns.RemoveAt(i);
                    }
                }

                // 不在此推断 cleanupFullyCompleted；须由 TryCleanupRegisteredContent 明确写入。
            }
        }
    }
}

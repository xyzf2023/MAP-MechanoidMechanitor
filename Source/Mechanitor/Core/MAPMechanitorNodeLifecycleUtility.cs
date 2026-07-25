using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// MAP 机械师节点生命周期所需的 tracker 集合安全初始化。
    /// 这是防御性保险，不是替代正确时序；不会每 Tick 执行，
    /// 只针对已有 mechanitor Tracker 或明确的 MAP 机械师节点。
    /// </summary>
    public static class MAPMechanitorNodeLifecycleUtility
    {
        private static readonly AccessTools.FieldRef<Pawn_MechanitorTracker, List<Pawn>> ControlledPawnsField =
            AccessTools.FieldRefAccess<Pawn_MechanitorTracker, List<Pawn>>("controlledPawns");

        public static void EnsureBasicTrackers(Pawn pawn)
        {
            if (pawn == null || !ModsConfig.BiotechActive)
            {
                return;
            }

            if (!MAPMechanitorNodeUtility.IsMechanitorNodeController(pawn))
            {
                return;
            }

            if (pawn.relations == null)
            {
                pawn.relations = new Pawn_RelationsTracker(pawn);
            }

            if (pawn.mechanitor == null)
            {
                pawn.mechanitor = new Pawn_MechanitorTracker(pawn);
            }

            if (pawn.mechanitor.controlGroups == null)
            {
                pawn.mechanitor.controlGroups = new List<MechanitorControlGroup>();
            }

            EnsureMechanitorTrackerCollections(pawn.mechanitor);
        }

        /// <summary>
        /// 确保 mechanitor tracker 的关键集合不为 null。
        /// 使用 HarmonyLib 缓存的 FieldRef 访问私有字段，避免每次调用重新反射。
        /// </summary>
        internal static void EnsureMechanitorTrackerCollections(Pawn_MechanitorTracker? tracker)
        {
            if (tracker == null)
            {
                return;
            }

            if (tracker.controlGroups == null)
            {
                tracker.controlGroups = new List<MechanitorControlGroup>();
            }

            ref List<Pawn> controlled = ref ControlledPawnsField(tracker);
            if (controlled == null)
            {
                controlled = new List<Pawn>();
            }
        }
    }
}

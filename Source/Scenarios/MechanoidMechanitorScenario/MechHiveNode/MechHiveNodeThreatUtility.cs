using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 机械巢节点沿用原版集群的威胁解除与战败流程。
    /// 完成判定只检查节点 LordJob 记录的主要威胁建筑；守军仅作为联合行动初始化的证据。
    /// </summary>
    public static class MechHiveNodeThreatUtility
    {
        private static readonly AccessTools.FieldRef<LordJob_MechanoidDefendBase, bool>
            IsMechCluster = AccessTools.FieldRefAccess<LordJob_MechanoidDefendBase, bool>(
                "isMechCluster");

        private static readonly AccessTools.FieldRef<LordJob_MechanoidDefendBase, bool>
            MechClusterDefeated = AccessTools.FieldRefAccess<LordJob_MechanoidDefendBase, bool>(
                "mechClusterDefeated");

        internal static bool ClusterDefeatEnabled(LordJob_MechanoidDefendBase lordJob)
        {
            return IsMechCluster(lordJob);
        }

        /// <summary>初始化成功后启用原版战败处理；生成与失败回滚期间关闭。</summary>
        internal static void SetClusterDefeatEnabled(Lord? lord, bool enabled)
        {
            if (lord?.LordJob is not LordJob_MechanoidDefendBase lordJob)
            {
                return;
            }

            IsMechCluster(lordJob) = enabled;
            if (!enabled)
            {
                return;
            }

            // 新节点已由公共建筑初始化登记；旧存档也补齐护盾通知，原版 AddUnique 防止重复。
            foreach (Thing thing in lordJob.things)
            {
                if (!thing.DestroyedOrNull()
                    && thing.TryGetComp<CompProjectileInterceptor>() != null)
                {
                    lordJob.AddThingToNotifyOnDefeat(thing);
                }
            }
        }

        /// <summary>结算前确保原版战败流程已完成，包括护盾关机与建筑失去阵营。</summary>
        internal static bool TryFinishClusterDefeat(Lord? lord)
        {
            if (lord == null)
            {
                return true;
            }

            if (lord.LordJob is not LordJob_MechanoidDefendBase lordJob
                || MechClusterUtility.AnyThreatBuilding(lordJob.things))
            {
                return false;
            }

            if (!MechClusterDefeated(lordJob))
            {
                SetClusterDefeatEnabled(lord, true);
                // 直接让原版 Tick 执行 OnDefeat，不复制其通知、奖励或护盾关机逻辑。
                lordJob.LordJobTick();
            }

            return MechClusterDefeated(lordJob);
        }

        public static bool AnyMechHiveThreatOnMap(Map? map)
        {
            if (map == null || map.Disposed || map.Parent is not MAPMechHiveNode node)
            {
                return false;
            }

            Lord? lord = node.ResolveClusterLord();
            return lord?.LordJob is LordJob_MechanoidDefendBase lordJob
                && MechClusterUtility.AnyThreatBuilding(lordJob.things);
        }

        /// <summary>初始化证据仍允许存活守军（含休眠/倒地），不作为集群完成条件。</summary>
        public static bool AnyMechHiveThreatEvidenceOnMap(Map? map)
        {
            if (map == null || map.Disposed)
            {
                return false;
            }

            Faction? mechHive = MechHiveNodeRelationUtility.GetMechHive();
            if (mechHive == null)
            {
                return false;
            }

            // 存活机械巢机械族 Pawn（包含休眠、包含被击倒但未死亡）。
            // 1.6 中 AllPawnsSpawned 返回 IReadOnlyList<Pawn>，此处只读遍历即可。
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn == null || pawn.Dead || pawn.Destroyed)
                {
                    continue;
                }

                if (pawn.Faction == mechHive && pawn.RaceProps != null && pawn.RaceProps.IsMechanoid)
                {
                    return true;
                }
            }

            return AnyMechHiveThreatOnMap(map);
        }
    }
}

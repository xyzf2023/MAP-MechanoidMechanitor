using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    internal static class MechResurrectionHealthUtility
    {
        private const string PackageId = "xyzf.mechanoidmechanitor";

        internal static bool AppliesTo(Pawn? pawn)
        {
            // 死亡 Pawn 可以处于 Destroyed 状态，资格查询不能据此排除尸体。
            return pawn != null && !pawn.Discarded && pawn.RaceProps.IsMechanoid
                && (string.Equals(pawn.def.modContentPack?.PackageIdPlayerFacing,
                        PackageId, StringComparison.OrdinalIgnoreCase)
                    || GameComponent_MechanoidMechanitorRegistry.HasPersistentRecord(pawn));
        }

        // 与原版实例调用保持同一栈签名，供培育账单 Transpiler 替换调用。
        internal static void RemoveUnprotectedOrAllHediffs(Pawn_HealthTracker health)
        {
            if (!AppliesTo(health.hediffSet.pawn))
            {
                health.RemoveAllHediffs();
                return;
            }

            RemoveUnprotectedHediffs(health.hediffSet.pawn);
        }

        internal static void RemoveUnprotectedHediffs(Pawn pawn)
        {
            if (!AppliesTo(pawn) || !CanSynchronize(pawn)) return;

            Pawn_HealthTracker health = pawn.health;
            var snapshot = new List<Hediff>(health.hediffSet.hediffs);
            var protectedHediffs = new HashSet<Hediff>();
            var addedParts = new HashSet<BodyPartRecord>();
            foreach (Hediff hediff in snapshot)
            {
                if (!ShouldPreserve(hediff)) continue;
                protectedHediffs.Add(hediff);
                if (hediff is Hediff_AddedPart && hediff.Part != null)
                    addedParts.Add(hediff.Part);
            }

            foreach (Hediff hediff in snapshot)
            {
                if (hediff is not Hediff_MissingPart || hediff.def.forceRemoveOnResurrection)
                    continue;

                // 义体覆盖的下级缺失是身体结构的一部分，不是待治疗的断肢。
                for (BodyPartRecord? parent = hediff.Part?.parent; parent != null; parent = parent.parent)
                {
                    if (!addedParts.Contains(parent)) continue;
                    protectedHediffs.Add(hediff);
                    break;
                }
            }

            // 与原版相同，反向移除；回调新建的合法效果不进入本次清理快照。
            for (int i = snapshot.Count - 1; i >= 0; i--)
            {
                if (!CanSynchronize(pawn)) break;
                Hediff hediff = snapshot[i];
                if (!protectedHediffs.Contains(hediff) && health.hediffSet.hediffs.Contains(hediff))
                    health.RemoveHediff(hediff);
            }
        }

        private static bool ShouldPreserve(Hediff hediff)
        {
            return !hediff.def.forceRemoveOnResurrection
                && (hediff.def.GetModExtension<HediffDefExtension_MechResurrectionProtection>() != null
                    || hediff is Hediff_Implant
                    || hediff.def.countsAsAddedPartOrImplant);
        }

        private static bool CanSynchronize(Pawn? pawn)
        {
            return pawn?.health?.hediffSet != null && !pawn.Dead && !pawn.Destroyed
                && !pawn.Discarded && !pawn.health.isBeingKilled;
        }

        /// <summary>
        /// 清理及监管分配完成后调用。真实模块始终保留，派生效果按当前来源重算。
        /// 不重发复活通知，也不另存一份身份或数据分配配置。
        /// </summary>
        internal static void SynchronizeAfterResurrection(Pawn? pawn)
        {
            if (pawn == null || !AppliesTo(pawn) || !CanSynchronize(pawn)) return;
            // 先排队，后续同步即使失败也有安全阶段兜底；不在账单调用栈内重做升格。
            if (GameComponent_MechanoidMechanitorRegistry.HasPersistentRecord(pawn))
                GameComponent_MechanoidMechanitorRegistry.QueuePostSpawnInitialization(pawn);
            try
            {
                MAPMechanitorNodeLifecycleUtility.EnsureBasicTrackers(pawn);
                MechanoidMechanitorCapabilityLifecycleUtility.EnsureInfrastructure(pawn);

                MechanoidMechanitorSelfWorkModeUtility.SyncSelfWorkModeEffects(pawn);
                MechanitorControlGroup? group = pawn.GetMechControlGroup();
                if (group != null)
                    MechanoidMechanitorWorkModeUtility.ApplyWorkModeHediff(pawn, group.WorkMode);
                if (pawn.mechanitor?.controlGroups != null)
                {
                    // 健康回调可能调整控制组，不能直接枚举可变的原列表。
                    var groups = new List<MechanitorControlGroup>(pawn.mechanitor.controlGroups);
                    foreach (MechanitorControlGroup ownGroup in groups)
                    {
                        if (!CanSynchronize(pawn)) return;
                        MechanoidMechanitorWorkModeUtility.SyncMechanitorWithPrimaryControlGroup(ownGroup);
                    }
                }

                if (!CanSynchronize(pawn)) return;
                // 原有队列同时负责动态和固定额度恢复，在下一游戏刻使用最终监管关系。
                DataProcessingPawnLifecycleCoordinator.EnqueueReactivation(pawn);
                ImplantEffectUtility.RefreshDistributedEffects(pawn);
                Pawn? overseer = pawn.GetOverseer();
                if (overseer != null && overseer != pawn)
                    ImplantEffectUtility.RefreshDistributedEffects(overseer);
            }
            catch (Exception ex)
            {
                // 产物已经复活，不让附加同步失败中断账单结算并丢失产物。
                Log.Error("[MAP-机械族机械师] 复活后功能状态同步失败：" + pawn + "：" + ex);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class EmergencyMechanicalConsciousnessTransferUtility
    {
        public const string EmergencyTransferHediffDefName = "MAP_EmergencyConsciousnessTransfer";
        public const int EmergencyTransferDurationTicks = 60000;

        private static readonly HashSet<Pawn> attemptGuard = new HashSet<Pawn>();
        private static HediffDef? cachedEmergencyTransferHediffDef;

        public static bool HasEmergencyConsciousnessTransferHediff(Pawn? pawn)
        {
            HediffDef? def = GetEmergencyTransferHediffDef();
            if (pawn == null || def == null || pawn.health?.hediffSet == null)
            {
                return false;
            }

            return pawn.health.hediffSet.HasHediff(def);
        }

        internal static bool IsAttemptInProgress(Pawn? pawn)
        {
            return pawn != null && attemptGuard.Contains(pawn);
        }

        public static bool TryBeginEmergencyTransferAttempt(Pawn? pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Destroyed)
            {
                return false;
            }

            // 研究“轨道数据网络”后紧急处理转为“紧急控制权交接”，触发源仍只认旧的
            // mechanicalConsciousnessHost，因此这里以“机械意识传输 或 轨道数据网络”任一解锁为准。
            bool emergencyProcessingUnlocked =
                ResearchFeatureUnlockUtility.IsMechanicalConsciousnessTransferUnlocked()
                || ResearchFeatureUnlockUtility.IsOrbitalDataNetworkUnlocked();
            if (!ModsConfig.BiotechActive
                || !emergencyProcessingUnlocked
                || !MechanoidMechanitorScenarioUtility.IsScenarioActive
                || !GameComponent_MechanoidMechanitorRegistry.IsMechanicalConsciousnessHost(pawn)
                || !MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
            {
                return false;
            }

            return attemptGuard.Add(pawn);
        }

        public static void TryExecuteEmergencyTransfer(Pawn pawn)
        {
            try
            {
                TryEmergencyTransferInternal(pawn);
            }
            catch (Exception ex)
            {
                LogEmergencyTriggerFailure(pawn, ex);
            }
        }

        public static void ClearAttemptGuard(Pawn? pawn)
        {
            if (pawn != null)
            {
                attemptGuard.Remove(pawn);
            }
        }

        public static Pawn? SelectEmergencyTransferTarget(Pawn source, Pawn? excludePawn = null)
        {
            Pawn? firstOtherMechanitor = null;
            HashSet<Pawn> seen = new HashSet<Pawn>();

            foreach (Pawn candidate in GameComponent_MechanoidMechanitorRegistry
                         .GetMechanicalConsciousnessCandidates())
            {
                if (candidate == null
                    || candidate.Destroyed
                    || !seen.Add(candidate)
                    || ReferenceEquals(candidate, excludePawn))
                {
                    continue;
                }

                if (!MechanicalConsciousnessTransferUtility.CanTransferMechanicalConsciousness(
                        source,
                        candidate))
                {
                    continue;
                }

                if (JusticePawnUtility.IsJustice(candidate))
                {
                    return candidate;
                }

                if (firstOtherMechanitor == null)
                {
                    firstOtherMechanitor = candidate;
                }
            }

            return firstOtherMechanitor;
        }

        /// <summary>
        /// 轨道数据网络研究完成后的“紧急控制权交接”目标选择。
        /// 候选优先级与旧紧急意识转移完全一致：目标不需要具备宿主资格，
        /// 但必须通过控制权交接资格判断（已注册、存活、初始化完成、玩家阵营、剧本激活）。
        /// 本方法会被安全补丁 EmergencyControlHandoffSafeTargetSelectionPatch 整体替换，
        /// 以套用全局事务 guard（注册参与方、跳过正在死亡/已参与的目标）。
        /// </summary>
        public static Pawn? SelectEmergencyControlHandoffTarget(
            Pawn source,
            Pawn? excludePawn = null)
        {
            Pawn? firstOtherMechanitor = null;
            HashSet<Pawn> seen = new HashSet<Pawn>();

            foreach (Pawn candidate in GameComponent_MechanoidMechanitorRegistry
                         .GetMechanicalConsciousnessCandidates())
            {
                if (candidate == null
                    || candidate.Destroyed
                    || !seen.Add(candidate)
                    || ReferenceEquals(candidate, excludePawn))
                {
                    continue;
                }

                if (!MechanicalControlHandoffUtility.CanTransferControl(source, candidate))
                {
                    continue;
                }

                if (JusticePawnUtility.IsJustice(candidate))
                {
                    return candidate;
                }

                if (firstOtherMechanitor == null)
                {
                    firstOtherMechanitor = candidate;
                }
            }

            return firstOtherMechanitor;
        }

        public static void ApplyOrRefreshEmergencyConsciousnessTransferHediff(Pawn target)
        {
            if (target == null || target.Destroyed)
            {
                return;
            }

            HediffDef? def = GetEmergencyTransferHediffDef();
            if (def == null)
            {
                Log.Error(
                    "[MAP-机械族机械师] 转移后 Hediff 失败：未找到 " +
                    $"{EmergencyTransferHediffDefName}，target={target.LabelShort} " +
                    $"（{target.ThingID}）。");
                return;
            }

            if (target.health?.hediffSet == null)
            {
                Log.Error(
                    $"[MAP-机械族机械师] 转移后 Hediff 失败：target={target.LabelShort} " +
                    $"（{target.ThingID}）缺少 健康追踪器。");
                return;
            }

            try
            {
                Hediff? existing = target.health.hediffSet.GetFirstHediffOfDef(def);
                if (existing != null)
                {
                    RefreshEmergencyTransferDuration(existing, target);
                    return;
                }

                Hediff hediff = HediffMaker.MakeHediff(def, target);
                target.health.AddHediff(hediff);
                RefreshEmergencyTransferDuration(hediff, target);
            }
            catch (Exception ex)
            {
                Log.Error(
                    $"[MAP-机械族机械师] 转移后 Hediff 失败：target={target.LabelShort} " +
                    $"（{target.ThingID}）：{ex}");
            }
        }

        private static bool ShouldAttemptEmergencyTransfer(Pawn source)
        {
            // 触发源判定保留：无论研究前后都只认旧 mechanicalConsciousnessHost。
            // 研究“轨道数据网络”后对应语义为紧急控制权交接，因此以任一相关科研解锁为准。
            bool emergencyProcessingUnlocked =
                ResearchFeatureUnlockUtility.IsMechanicalConsciousnessTransferUnlocked()
                || ResearchFeatureUnlockUtility.IsOrbitalDataNetworkUnlocked();
            return ModsConfig.BiotechActive
                && emergencyProcessingUnlocked
                && MechanoidMechanitorScenarioUtility.IsScenarioActive
                && !source.Dead
                && !source.Destroyed
                && GameComponent_MechanoidMechanitorRegistry.IsMechanicalConsciousnessHost(source)
                && MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(source);
        }

        private static void TryEmergencyTransferInternal(Pawn source)
        {
            if (!ShouldAttemptEmergencyTransfer(source))
            {
                return;
            }

            if (!GameComponent_MechanoidMechanitorRegistry.IsMechanicalConsciousnessHost(source))
            {
                return;
            }

            // 轨道数据网络研究完成后，机械意识已通过轨道网络同步保存，
            // 宿主濒死时只执行“紧急控制权交接”，绝不执行任何意识迁移。
            if (ResearchFeatureUnlockUtility.IsOrbitalDataNetworkUnlocked())
            {
                TryEmergencyControlHandoffInternal(source);
                return;
            }

            // —— 轨道数据网络研究前：保持原有完整紧急意识转移不变 ——
            Pawn? excludePawn = null;
            CompDormantJustice? dormantCarrier = CompDormantJustice.FindDesignatedEmergencyCarrier();
            if (dormantCarrier != null)
            {
                Building? dormantBuilding = dormantCarrier.parent as Building;
                if (DormantJusticeActivationUtility.TryActivate(
                        dormantCarrier,
                        out Pawn? newJustice)
                    && newJustice != null)
                {
                    Pawn? hostBeforeDormant =
                        GameComponent_MechanoidMechanitorRegistry.CurrentMechanicalConsciousnessHost;
                    if (MechanicalConsciousnessTransferUtility.TryTransferMechanicalConsciousness(
                            source,
                            newJustice,
                            MechanicalConsciousnessTransferContext.Emergency))
                    {
                        return;
                    }

                    excludePawn = newJustice;
                    Log.Error(
                        "[MAP-机械族机械师] 紧急意识转移：未启动正义已生成但转移失败，" +
                        $"source={source.LabelShort}（{source.ThingID}），" +
                        $"building={dormantBuilding?.LabelShort ?? "null"} " +
                        $"（{dormantBuilding?.ThingID ?? "null"}），" +
                        $"newJustice={newJustice.LabelShort}（{newJustice.ThingID}），" +
                        $"hostBefore={hostBeforeDormant?.LabelShort ?? "null"}。");
                }
                else
                {
                    Log.Error(
                        "[MAP-机械族机械师] 紧急意识转移：未启动正义启动失败，" +
                        $"source={source.LabelShort}（{source.ThingID}），" +
                        $"building={dormantBuilding?.LabelShort ?? "null"} " +
                        $"（{dormantBuilding?.ThingID ?? "null"}），" +
                        $"newJustice={newJustice?.LabelShort ?? "null"} " +
                        $"（{newJustice?.ThingID ?? "null"}）。");
                }
            }

            Pawn? target = SelectEmergencyTransferTarget(source, excludePawn);
            if (target == null)
            {
                return;
            }

            Pawn? hostBefore =
                GameComponent_MechanoidMechanitorRegistry.CurrentMechanicalConsciousnessHost;
            if (!MechanicalConsciousnessTransferUtility.TryTransferMechanicalConsciousness(
                    source,
                    target,
                    MechanicalConsciousnessTransferContext.Emergency))
            {
                Log.Error(
                    $"[MAP-机械族机械师] 紧急意识转移失败：source={source.LabelShort} " +
                    $"（{source.ThingID}），target={target.LabelShort} " +
                    $"（{target.ThingID}），hostBefore={hostBefore?.LabelShort ?? "null"}。");
                return;
            }

            ApplyOrRefreshEmergencyConsciousnessTransferHediff(target);
        }

        /// <summary>
        /// 轨道数据网络研究完成后的紧急控制权交接：
        ///  第一优先级 = 指定紧急载体“正义（未启动）”自动启动后作为目标；
        ///  其次 = 已启动的正义；再次 = 其他合法机械族机械师。
        /// 只迁移芯片带宽奖励与监管机械族控制权；成功后一律不添加
        /// MAP_EmergencyConsciousnessTransfer 负面 Hediff；不替换宿主。
        /// </summary>
        private static void TryEmergencyControlHandoffInternal(Pawn source)
        {
            Pawn? excludePawn = null;
            CompDormantJustice? dormantCarrier = CompDormantJustice.FindDesignatedEmergencyCarrier();
            if (dormantCarrier != null)
            {
                Building? dormantBuilding = dormantCarrier.parent as Building;
                if (DormantJusticeActivationUtility.TryActivate(
                        dormantCarrier,
                        out Pawn? newJustice)
                    && newJustice != null)
                {
                    if (TryEmergencyControlHandoffToTarget(source, newJustice))
                    {
                        return;
                    }

                    excludePawn = newJustice;
                    Log.Error(
                        "[MAP-机械族机械师] 紧急控制权交接：未启动正义已生成但交接失败，" +
                        $"source={source.LabelShort}（{source.ThingID}），" +
                        $"building={dormantBuilding?.LabelShort ?? "null"} " +
                        $"（{dormantBuilding?.ThingID ?? "null"}），" +
                        $"newJustice={newJustice.LabelShort}（{newJustice.ThingID}）。");
                }
                else
                {
                    Log.Error(
                        "[MAP-机械族机械师] 紧急控制权交接：未启动正义启动失败，" +
                        $"source={source.LabelShort}（{source.ThingID}），" +
                        $"building={dormantBuilding?.LabelShort ?? "null"} " +
                        $"（{dormantBuilding?.ThingID ?? "null"}），" +
                        $"newJustice={newJustice?.LabelShort ?? "null"} " +
                        $"（{newJustice?.ThingID ?? "null"}）。");
                }
            }

            Pawn? target = SelectEmergencyControlHandoffTarget(source, excludePawn);
            if (target == null)
            {
                return;
            }

            if (!TryEmergencyControlHandoffToTarget(source, target))
            {
                Log.Error(
                    $"[MAP-机械族机械师] 紧急控制权交接失败：source={source.LabelShort} " +
                    $"（{source.ThingID}），target={target.LabelShort} " +
                    $"（{target.ThingID}）。");
            }
        }

        /// <summary>
        /// 以全局事务 guard 语义执行一次紧急控制权交接：
        /// 先注册目标参与方，再调用权威入口 MechanicalControlHandoffUtility.TryTransferControl。
        /// 与旧紧急意识转移的 guard 语义保持一致；失败时清除当前目标并返回 false。
        /// </summary>
        private static bool TryEmergencyControlHandoffToTarget(Pawn source, Pawn? target)
        {
            if (target == null
                || !EmergencyMechanicalConsciousnessTransferGlobalGuard.TryRegisterTarget(target)
                || !MechanicalControlHandoffUtility.TryTransferControl(source, target))
            {
                EmergencyMechanicalConsciousnessTransferGlobalGuard.MarkCurrentTargetFailed(target);
                return false;
            }

            return true;
        }

        private static HediffDef? GetEmergencyTransferHediffDef()
        {
            return cachedEmergencyTransferHediffDef ??=
                DefDatabase<HediffDef>.GetNamedSilentFail(EmergencyTransferHediffDefName);
        }

        private static void RefreshEmergencyTransferDuration(Hediff hediff, Pawn target)
        {
            HediffComp_Disappears? disappears = hediff.TryGetComp<HediffComp_Disappears>();
            if (disappears == null)
            {
                Log.Error(
                    $"[MAP-机械族机械师] 转移后 Hediff 失败：target={target.LabelShort} " +
                    $"（{target.ThingID}）上的 {EmergencyTransferHediffDefName} 缺少 HediffComp_Disappears。");
                return;
            }

            disappears.SetDuration(EmergencyTransferDurationTicks);
        }

        private static void LogEmergencyTriggerFailure(Pawn source, Exception ex)
        {
            Pawn? currentHost =
                GameComponent_MechanoidMechanitorRegistry.CurrentMechanicalConsciousnessHost;
            Log.Error(
                $"[MAP-机械族机械师] 紧急意识转移触发失败：source={source.LabelShort} " +
                $"（{source.ThingID}），currentHost={currentHost?.LabelShort ?? "null"}：{ex}");
        }
    }
}

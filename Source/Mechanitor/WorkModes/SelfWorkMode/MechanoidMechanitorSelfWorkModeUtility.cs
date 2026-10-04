using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    public static class MechanoidMechanitorSelfWorkModeUtility
    {
        public const string AutonomousDirectiveDefName = "MAP_WorkMode_AutonomousDirective";
        private const string SelfShutdownDefName = "SelfShutdown";
        private const string RechargeDefName = "Recharge";
        private const string AutonomousDirectiveHediffDefName =
            "MAP_MechanoidMechanitor_SelfWorkMode_AutonomousDirective";
        private const string SelfRepairHediffDefName =
            "MAP_MechanoidMechanitor_SelfWorkMode_SelfRepair";

        private static MechWorkModeDef? autonomousDirectiveDef;
        private static HediffDef? autonomousDirectiveHediffDef;
        private static HediffDef? selfRepairHediffDef;

        public static bool HasSelfWorkMode(Pawn? pawn) =>
            MechanoidMechanitorCapabilityUtility.HasCapability(
                pawn,
                MechanoidMechanitorCapability.SelfWorkMode);

        public static bool HasSelfWorkModeHediffs(Pawn? pawn)
        {
            if (pawn?.health?.hediffSet == null)
            {
                return false;
            }

            HediffDef? autonomous = TryGetAutonomousDirectiveHediffDef();
            HediffDef? selfRepair = TryGetSelfRepairHediffDef();
            return (autonomous != null && pawn.health.hediffSet.HasHediff(autonomous))
                || (selfRepair != null && pawn.health.hediffSet.HasHediff(selfRepair));
        }

        public static bool IsAutonomousDirectiveMode(MechWorkModeDef? mode) =>
            mode?.defName == AutonomousDirectiveDefName;

        public static bool IsRechargeMode(MechWorkModeDef? mode) =>
            mode == MechWorkModeDefOf.Recharge
            || mode?.defName == RechargeDefName;

        public static bool IsSelfShutdownMode(MechWorkModeDef? mode) =>
            mode == MechWorkModeDefOf.SelfShutdown
            || mode?.defName == SelfShutdownDefName;

        /// <summary>
        /// 将机械族机械师本体工作模式映射到原版思维树使用的 MechWorkModeDef。
        /// </summary>
        public static MechWorkModeDef GetMappedVanillaWorkMode(MechWorkModeDef? mode)
        {
            if (IsRechargeMode(mode))
            {
                return MechWorkModeDefOf.Recharge;
            }

            if (IsSelfShutdownMode(mode))
            {
                return MechWorkModeDefOf.SelfShutdown;
            }

            return MechWorkModeDefOf.Work;
        }

        /// <summary>
        /// 是否应用独立自律记录的个人充电阈值。不要求本体工作模式资格，
        /// 因此普通自律机械体也适用；本体模式与作息的优先级由原有消费端保留。
        /// </summary>
        public static bool ShouldApplySelfRechargeThresholds(Pawn? pawn)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead)
            {
                return false;
            }

            if (!pawn.RaceProps.IsMechanoid)
            {
                return false;
            }

            if (pawn.Faction != Faction.OfPlayer)
            {
                return false;
            }

            if (!AutonomousMechUtility.UsesPersonalRechargeSettings(pawn))
            {
                return false;
            }

            return pawn.GetMechControlGroup() == null;
        }

        /// <summary>
        /// 按科研解锁与当前本体工作模式同步健康状态。
        /// 未解锁时移除状态但不改变工作模式。
        /// </summary>
        public static void SyncSelfWorkModeEffects(Pawn? pawn)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead || pawn.health?.hediffSet == null)
            {
                return;
            }

            HediffDef? autonomousHediff = TryGetAutonomousDirectiveHediffDef();
            HediffDef? selfRepairHediff = TryGetSelfRepairHediffDef();
            if (autonomousHediff == null && selfRepairHediff == null)
            {
                return;
            }

            bool unlocked =
                ResearchFeatureUnlockUtility.IsAutonomousDirectiveOptimizationUnlocked();
            if (!HasSelfWorkMode(pawn) || !unlocked)
            {
                if (autonomousHediff != null)
                {
                    RemoveAllHediffsOfDef(pawn, autonomousHediff);
                }

                if (selfRepairHediff != null)
                {
                    RemoveAllHediffsOfDef(pawn, selfRepairHediff);
                }

                return;
            }

            if (!TryGetCurrentMode(pawn, out MechWorkModeDef? mode) || mode == null)
            {
                if (autonomousHediff != null)
                {
                    RemoveAllHediffsOfDef(pawn, autonomousHediff);
                }

                if (selfRepairHediff != null)
                {
                    RemoveAllHediffsOfDef(pawn, selfRepairHediff);
                }

                return;
            }

            ApplyUnlockedModeHediffs(pawn, mode, autonomousHediff, selfRepairHediff);
        }

        /// <summary>
        /// 征召状态实际发生变化时的通知入口。
        /// 进行廉价的提前过滤后，交由 <see cref="SyncSelfWorkModeEffects"/> 统一决定
        /// “自我修复”的添加或移除；本方法不直接增删任何 Hediff。
        /// </summary>
        public static void NotifyDraftedStateChanged(Pawn? pawn)
        {
            // 1. Pawn 不为空，且未死亡、未 Destroyed。
            if (pawn == null || pawn.Dead || pawn.Destroyed)
            {
                return;
            }

            // 2. 必须为已注册的机械族机械师（单 Pawn 查询，不扫描地图）。
            if (!GameComponent_MechanoidMechanitorRegistry.TryGetMechanitorRecord(pawn, out _))
            {
                return;
            }

            // 3. “自律指令优化”科研必须已完成。
            if (!ResearchFeatureUnlockUtility.IsAutonomousDirectiveOptimizationUnlocked())
            {
                return;
            }

            // 4. 当前本体工作模式必须为休眠（SelfShutdown）。
            if (!IsSelfShutdown(pawn))
            {
                return;
            }

            // 5. 交由统一规则决定最终状态。
            SyncSelfWorkModeEffects(pawn);
        }

        public static bool TryGetCurrentMode(Pawn? pawn, out MechWorkModeDef? mode)
        {
            mode = null;
            if (pawn == null)
            {
                return false;
            }

            CompMechanoidMechanitorSelfWorkModeUser? nativeComp =
                CompMechanoidMechanitorSelfWorkModeUser.GetFor(pawn);
            if (nativeComp != null)
            {
                mode = nativeComp.CurrentSelfWorkMode;
                return true;
            }

            if (GameComponent_MechanoidMechanitorRegistry.TryGetAcquiredMechanitorRecord(
                    pawn,
                    out MechanoidMechanitorRecord? record)
                && record != null)
            {
                mode = SanitizeWorkMode(record.SelfWorkMode);
                return true;
            }

            // 独立自律仅复用行为模式显示与 AI，不授予 SelfWorkMode 科研增益资格。
            if (AutonomousMechUtility.IsPlayerAutonomousMech(pawn)
                && GameComponent_AutonomousMechRegistry.TryGetRecord(pawn,
                    out AutonomousMechAuthorizationRecord? autonomousRecord))
            {
                mode = autonomousRecord!.BehaviorMode;
                return true;
            }

            return false;
        }

        public static bool IsSelfShutdown(Pawn? pawn)
        {
            return TryGetCurrentMode(pawn, out MechWorkModeDef? mode)
                && IsSelfShutdownMode(mode);
        }

        public static void SetSelfWorkMode(Pawn pawn, MechWorkModeDef? mode)
        {
            CompMechanoidMechanitorSelfWorkModeUser? nativeComp =
                CompMechanoidMechanitorSelfWorkModeUser.GetFor(pawn);
            if (nativeComp != null)
            {
                nativeComp.SetSelfWorkMode(mode);
                return;
            }

            if (!GameComponent_MechanoidMechanitorRegistry.TryGetAcquiredMechanitorRecord(
                    pawn,
                    out MechanoidMechanitorRecord? record)
                || record == null)
            {
                GameComponent_AutonomousMechRegistry.TrySetBehaviorMode(pawn, mode);
                return;
            }

            MechWorkModeDef sanitized = SanitizeWorkMode(mode);
            if (SanitizeWorkMode(record.SelfWorkMode) == sanitized)
            {
                return;
            }

            record.SelfWorkMode = sanitized;
            ApplyAcquiredSelfWorkMode(pawn, sanitized);
            NotifyModeChanged(pawn, sanitized);
        }

        public static void AddSelfWorkModeFloatMenuOptions(
            List<FloatMenuOption> options,
            Pawn pawn)
        {
            MechWorkModeDef autonomous = GetAutonomousDirectiveDef();
            options.Add(new FloatMenuOption(
                autonomous.LabelCap,
                () => SetSelfWorkMode(pawn, autonomous),
                autonomous.uiIcon,
                Color.white));

            if (HasSelfWorkMode(pawn) || AutonomousMechUtility.UsesPersonalRechargeSettings(pawn))
            {
                MechWorkModeDef recharge = MechWorkModeDefOf.Recharge;
                options.Add(new FloatMenuOption(
                    recharge.LabelCap,
                    () => SetSelfWorkMode(pawn, recharge),
                    recharge.uiIcon,
                    Color.white));
            }

            MechWorkModeDef selfShutdown = MechWorkModeDefOf.SelfShutdown;
            options.Add(new FloatMenuOption(
                selfShutdown.LabelCap,
                () => SetSelfWorkMode(pawn, selfShutdown),
                selfShutdown.uiIcon,
                Color.white));
        }

        /// <summary>
        /// 本体允许：自律指令、充电（原版 Recharge）、休眠（SelfShutdown）。
        /// 其他未知模式回退为自律指令。
        /// </summary>
        public static MechWorkModeDef SanitizeWorkMode(MechWorkModeDef? mode)
        {
            if (IsAutonomousDirectiveMode(mode) && mode != null)
            {
                return mode;
            }

            if (IsRechargeMode(mode))
            {
                return MechWorkModeDefOf.Recharge;
            }

            if (IsSelfShutdownMode(mode))
            {
                return MechWorkModeDefOf.SelfShutdown;
            }

            return GetAutonomousDirectiveDef();
        }

        public static void ApplyAcquiredSelfWorkMode(Pawn pawn, MechWorkModeDef mode)
        {
            SyncSelfWorkModeEffects(pawn);
        }

        public static void NotifyModeChanged(Pawn pawn, MechWorkModeDef mode)
        {
            PawnComponentsUtility.AddAndRemoveDynamicComponents(pawn, actAsIfSpawned: true);
            // 切离充电模式时中断 MechCharge（含前往途中，不依赖 IsCharging）
            if (!IsRechargeMode(mode)
                && pawn.CurJobDef == JobDefOf.MechCharge)
            {
                // 仅清除工作，由后续 CheckForJobOverride 统一重算，避免 EndCurrentJob 默认立刻再分配一次
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            }

            pawn.TryGetComp<CompCanBeDormant>()?.WakeUp();
            pawn.jobs?.CheckForJobOverride();
        }

        private static void ApplyUnlockedModeHediffs(
            Pawn pawn,
            MechWorkModeDef mode,
            HediffDef? autonomousHediff,
            HediffDef? selfRepairHediff)
        {
            // 自律指令：仅保留自律指令 Hediff
            if (IsAutonomousDirectiveMode(mode))
            {
                if (selfRepairHediff != null)
                {
                    RemoveAllHediffsOfDef(pawn, selfRepairHediff);
                }

                if (autonomousHediff != null
                    && pawn.health.hediffSet.GetFirstHediffOfDef(autonomousHediff) == null)
                {
                    pawn.health.AddHediff(autonomousHediff);
                }

                return;
            }

            // 休眠：始终移除自律指令；仅在未征召时保留自我修复 Hediff
            if (IsSelfShutdownMode(mode))
            {
                if (autonomousHediff != null)
                {
                    RemoveAllHediffsOfDef(pawn, autonomousHediff);
                }

                if (selfRepairHediff != null)
                {
                    // 已征召：必须移除全部自我修复 Hediff，不得保留
                    if (pawn.Drafted)
                    {
                        RemoveAllHediffsOfDef(pawn, selfRepairHediff);
                    }
                    // 未征召：允许添加或保留自我修复 Hediff
                    else if (pawn.health.hediffSet.GetFirstHediffOfDef(selfRepairHediff) == null)
                    {
                        pawn.health.AddHediff(selfRepairHediff);
                    }
                }

                return;
            }

            // 充电及其他：不附加任何本体模式 Hediff（充电仅用原版充电站）
            if (autonomousHediff != null)
            {
                RemoveAllHediffsOfDef(pawn, autonomousHediff);
            }

            if (selfRepairHediff != null)
            {
                RemoveAllHediffsOfDef(pawn, selfRepairHediff);
            }
        }

        private static MechWorkModeDef GetAutonomousDirectiveDef()
        {
            return autonomousDirectiveDef ??=
                DefDatabase<MechWorkModeDef>.GetNamed(AutonomousDirectiveDefName);
        }

        private static HediffDef? TryGetAutonomousDirectiveHediffDef()
        {
            return autonomousDirectiveHediffDef ??=
                DefDatabase<HediffDef>.GetNamedSilentFail(AutonomousDirectiveHediffDefName);
        }

        private static HediffDef? TryGetSelfRepairHediffDef()
        {
            return selfRepairHediffDef ??=
                DefDatabase<HediffDef>.GetNamedSilentFail(SelfRepairHediffDefName);
        }

        private static void RemoveAllHediffsOfDef(Pawn pawn, HediffDef def)
        {
            HediffSet hediffSet = pawn.health.hediffSet;
            for (int i = hediffSet.hediffs.Count - 1; i >= 0; i--)
            {
                Hediff hediff = hediffSet.hediffs[i];
                if (hediff.def == def)
                {
                    pawn.health.RemoveHediff(hediff);
                }
            }
        }
    }
}

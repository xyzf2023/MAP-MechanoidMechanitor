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
        /// 无原版控制组的机械族机械师本体，应使用本 MOD 的默认充电阈值。
        /// </summary>
        public static bool ShouldApplyDefaultRechargeThresholds(Pawn? pawn)
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

            if (!HasSelfWorkMode(pawn))
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

            MechWorkModeDef recharge = MechWorkModeDefOf.Recharge;
            options.Add(new FloatMenuOption(
                recharge.LabelCap,
                () => SetSelfWorkMode(pawn, recharge),
                recharge.uiIcon,
                Color.white));

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
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
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

            // 休眠：仅保留自我修复 Hediff
            if (IsSelfShutdownMode(mode))
            {
                if (autonomousHediff != null)
                {
                    RemoveAllHediffsOfDef(pawn, autonomousHediff);
                }

                if (selfRepairHediff != null
                    && pawn.health.hediffSet.GetFirstHediffOfDef(selfRepairHediff) == null)
                {
                    pawn.health.AddHediff(selfRepairHediff);
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

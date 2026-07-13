using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    public class CompProperties_MechanoidMechanitorSelfWorkModeUser : CompProperties
    {
        public CompProperties_MechanoidMechanitorSelfWorkModeUser()
        {
            compClass = typeof(CompMechanoidMechanitorSelfWorkModeUser);
        }
    }

    // 机械族机械师本体的 Self Work Mode，与 mechanitor 控制组 WorkMode 是两套系统。
    //
    // 允许三种本体模式：
    // - MAP_WorkMode_AutonomousDirective（自律指令）
    // - MechWorkModeDefOf.Recharge（充电，前往机械充电站）
    // - MechWorkModeDefOf.SelfShutdown（休眠/自机充电）
    //
    // MAP_WorkMode_AutonomousDirective 不得出现在机械族机械师控制组菜单中
    // （由 MechanoidMechanitorWorkModeUtility.IsMechanoidMechanitorSelfOnlyWorkMode 过滤）。
    // 控制组模式（高效执行、机动作战、阵地防御等）用于机械族机械师监管的机械体，
    // 不用于此处保存的本体模式。原版 Recharge 可同时作为控制组模式与本体模式。
    public class CompMechanoidMechanitorSelfWorkModeUser : ThingComp
    {
        private bool selfWorkModeEffectsInitialized;

        public MechWorkModeDef CurrentSelfWorkMode =>
            MechanoidMechanitorSelfWorkModeUtility.SanitizeWorkMode(GetAuthoritativeSelfWorkMode());

        public bool IsAutonomousDirective =>
            MechanoidMechanitorSelfWorkModeUtility.IsAutonomousDirectiveMode(CurrentSelfWorkMode);

        public bool IsRecharge =>
            MechanoidMechanitorSelfWorkModeUtility.IsRechargeMode(CurrentSelfWorkMode);

        public bool IsSelfShutdown =>
            MechanoidMechanitorSelfWorkModeUtility.IsSelfShutdownMode(CurrentSelfWorkMode);

        public static CompMechanoidMechanitorSelfWorkModeUser? GetFor(Pawn? pawn) =>
            pawn?.GetComp<CompMechanoidMechanitorSelfWorkModeUser>();

        public void SetSelfWorkMode(MechWorkModeDef? mode)
        {
            if (parent is not Pawn pawn
                || !GameComponent_MechanoidMechanitorRegistry.TryGetNativeMechanitorRecord(
                    pawn,
                    out MechanoidMechanitorRecord? record)
                || record == null)
            {
                return;
            }

            MechWorkModeDef sanitized =
                MechanoidMechanitorSelfWorkModeUtility.SanitizeWorkMode(mode);
            if (MechanoidMechanitorSelfWorkModeUtility.SanitizeWorkMode(record.SelfWorkMode)
                == sanitized)
            {
                return;
            }

            record.SelfWorkMode = sanitized;

            if (SyncSelfWorkModeHediff())
            {
                selfWorkModeEffectsInitialized = true;
            }

            PawnComponentsUtility.AddAndRemoveDynamicComponents(pawn, actAsIfSpawned: true);
            // 切离充电模式时中断 MechCharge（含前往途中）；切到充电则保留并立即重算工作
            if (!MechanoidMechanitorSelfWorkModeUtility.IsRechargeMode(sanitized)
                && pawn.CurJobDef == JobDefOf.MechCharge)
            {
                // 仅清除工作，由后续 CheckForJobOverride 统一重算，避免 EndCurrentJob 默认立刻再分配一次
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            }

            pawn.TryGetComp<CompCanBeDormant>()?.WakeUp();
            pawn.jobs?.CheckForJobOverride();
        }

        // 读档同步不是工作模式切换：只同步 Hediff/动态组件，保留存档恢复的当前工作。
        internal void SyncSelfWorkModeEffectsFromAuthoritativeState()
        {
            if (SyncSelfWorkModeHediff())
            {
                selfWorkModeEffectsInitialized = true;
            }

            if (parent is not Pawn workPawn)
            {
                return;
            }

            PawnComponentsUtility.AddAndRemoveDynamicComponents(workPawn, actAsIfSpawned: true);
        }

        public static string GetDisplayLabel(MechWorkModeDef mode)
        {
            return mode.LabelCap;
        }

        public static void AddSelfWorkModeFloatMenuOptions(
            List<FloatMenuOption> options,
            CompMechanoidMechanitorSelfWorkModeUser comp)
        {
            MechWorkModeDef autonomous =
                MechanoidMechanitorSelfWorkModeUtility.SanitizeWorkMode(null);
            options.Add(new FloatMenuOption(
                GetDisplayLabel(autonomous),
                () => comp.SetSelfWorkMode(autonomous),
                autonomous.uiIcon,
                Color.white));

            MechWorkModeDef recharge = MechWorkModeDefOf.Recharge;
            options.Add(new FloatMenuOption(
                GetDisplayLabel(recharge),
                () => comp.SetSelfWorkMode(recharge),
                recharge.uiIcon,
                Color.white));

            MechWorkModeDef selfShutdown = MechWorkModeDefOf.SelfShutdown;
            options.Add(new FloatMenuOption(
                GetDisplayLabel(selfShutdown),
                () => comp.SetSelfWorkMode(selfShutdown),
                selfShutdown.uiIcon,
                Color.white));
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (respawningAfterLoad || selfWorkModeEffectsInitialized)
            {
                return;
            }

            if (SyncSelfWorkModeHediff())
            {
                selfWorkModeEffectsInitialized = true;
            }
        }

        private MechWorkModeDef? GetAuthoritativeSelfWorkMode()
        {
            if (parent is Pawn pawn
                && GameComponent_MechanoidMechanitorRegistry.TryGetNativeMechanitorRecord(
                    pawn,
                    out MechanoidMechanitorRecord? record)
                && record != null
                && record.SelfWorkMode != null)
            {
                return record.SelfWorkMode;
            }

            return MechanoidMechanitorSelfWorkModeUtility.SanitizeWorkMode(null);
        }

        private bool SyncSelfWorkModeHediff()
        {
            if (parent is not Pawn pawn)
            {
                return false;
            }

            MechanoidMechanitorSelfWorkModeUtility.SyncSelfWorkModeEffects(pawn);
            return pawn.health?.hediffSet != null;
        }
    }
}

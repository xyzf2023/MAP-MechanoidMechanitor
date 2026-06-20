using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    public class CompProperties_JusticeSelfWorkMode : CompProperties
    {
        public CompProperties_JusticeSelfWorkMode()
        {
            compClass = typeof(CompJusticeSelfWorkMode);
        }
    }

    // 正义本体的 Self Work Mode，与 mechanitor 控制组 WorkMode 是两套系统。
    //
    // 仅允许：MAP_WorkMode_AutonomousDirective（自律指令）与
    // MechWorkModeDefOf.SelfShutdown（休眠/自机充电）。不允许 Recharge——
    // Recharge 是去充电器充电；本体休眠自充电应使用 SelfShutdown。
    //
    // MAP_WorkMode_AutonomousDirective 不得出现在正义控制组菜单中
    // （由 WorkModeUtility.IsJusticeSelfOnlyWorkMode 过滤）。
    // 控制组模式（高效执行、机动作战、阵地防御等）用于正义监管的机械体，
    // 不用于此处保存的正义本体模式。
    public class CompJusticeSelfWorkMode : ThingComp
    {
        private const string AutonomousDirectiveDefName = "MAP_WorkMode_AutonomousDirective";
        private const string SelfShutdownDefName = "SelfShutdown";
        private const string AutonomousDirectiveHediffDefName = "MAP_Justice_SelfWorkMode_AutonomousDirective";
        private const string SelfRepairHediffDefName = "MAP_Justice_SelfWorkMode_SelfRepair";

        private MechWorkModeDef? selfWorkMode;
        private bool selfWorkModeHediffInitialized;

        private static MechWorkModeDef? autonomousDirectiveDef;
        private static HediffDef? autonomousDirectiveHediffDef;
        private static HediffDef? selfRepairHediffDef;

        public MechWorkModeDef CurrentSelfWorkMode => SanitizeWorkMode(selfWorkMode);

        public bool IsAutonomousDirective =>
            CurrentSelfWorkMode.defName == AutonomousDirectiveDefName;

        public bool IsSelfShutdown =>
            CurrentSelfWorkMode.defName == SelfShutdownDefName;

        public static CompJusticeSelfWorkMode? GetFor(Pawn? pawn) =>
            pawn?.GetComp<CompJusticeSelfWorkMode>();

        public void SetSelfWorkMode(MechWorkModeDef? mode)
        {
            MechWorkModeDef sanitized = SanitizeWorkMode(mode);
            if (CurrentSelfWorkMode == sanitized)
            {
                return;
            }

            selfWorkMode = sanitized;
            if (SyncSelfWorkModeHediff())
            {
                selfWorkModeHediffInitialized = true;
            }

            if (parent is not Pawn pawn)
            {
                return;
            }

            PawnComponentsUtility.AddAndRemoveDynamicComponents(pawn, actAsIfSpawned: true);
            if (sanitized != MechWorkModeDefOf.Recharge
                && pawn.CurJobDef == JobDefOf.MechCharge
                && pawn.IsCharging())
            {
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
            }

            pawn.TryGetComp<CompCanBeDormant>()?.WakeUp();
            pawn.jobs?.CheckForJobOverride();
        }

        public static string GetDisplayLabel(MechWorkModeDef mode)
        {
            return mode.LabelCap;
        }

        public static void AddSelfWorkModeFloatMenuOptions(
            List<FloatMenuOption> options,
            CompJusticeSelfWorkMode comp)
        {
            MechWorkModeDef autonomous = GetAutonomousDirectiveDef();
            options.Add(new FloatMenuOption(
                GetDisplayLabel(autonomous),
                () => comp.SetSelfWorkMode(autonomous),
                autonomous.uiIcon,
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
            if (respawningAfterLoad || selfWorkModeHediffInitialized)
            {
                return;
            }

            if (SyncSelfWorkModeHediff())
            {
                selfWorkModeHediffInitialized = true;
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Defs.Look(ref selfWorkMode, "justiceSelfWorkMode");
            Scribe_Values.Look(
                ref selfWorkModeHediffInitialized,
                "justiceSelfWorkModeHediffInitialized",
                false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                selfWorkMode = SanitizeWorkMode(selfWorkMode);
            }
        }

        private bool SyncSelfWorkModeHediff()
        {
            if (parent is not Pawn pawn || pawn.health?.hediffSet == null)
            {
                return false;
            }

            HediffDef autonomousHediffDef = GetAutonomousDirectiveHediffDef();
            HediffDef selfRepairHediffDef = GetSelfRepairHediffDef();

            if (IsAutonomousDirective)
            {
                RemoveAllHediffsOfDef(pawn, selfRepairHediffDef);
                if (pawn.health.hediffSet.GetFirstHediffOfDef(autonomousHediffDef) == null)
                {
                    pawn.health.AddHediff(autonomousHediffDef);
                }
            }
            else if (IsSelfShutdown)
            {
                RemoveAllHediffsOfDef(pawn, autonomousHediffDef);
                if (pawn.health.hediffSet.GetFirstHediffOfDef(selfRepairHediffDef) == null)
                {
                    pawn.health.AddHediff(selfRepairHediffDef);
                }
            }

            return true;
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

        private static MechWorkModeDef GetAutonomousDirectiveDef()
        {
            return autonomousDirectiveDef ??=
                DefDatabase<MechWorkModeDef>.GetNamed(AutonomousDirectiveDefName);
        }

        private static MechWorkModeDef SanitizeWorkMode(MechWorkModeDef? mode)
        {
            if (mode?.defName == AutonomousDirectiveDefName)
            {
                return mode;
            }

            if (mode?.defName == SelfShutdownDefName)
            {
                return MechWorkModeDefOf.SelfShutdown;
            }

            return GetAutonomousDirectiveDef();
        }

        private static HediffDef GetAutonomousDirectiveHediffDef()
        {
            return autonomousDirectiveHediffDef ??=
                DefDatabase<HediffDef>.GetNamed(AutonomousDirectiveHediffDefName);
        }

        private static HediffDef GetSelfRepairHediffDef()
        {
            return selfRepairHediffDef ??=
                DefDatabase<HediffDef>.GetNamed(SelfRepairHediffDefName);
        }
    }
}

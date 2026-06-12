using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class CompProperties_JusticeSelfWorkMode : CompProperties
    {
        public CompProperties_JusticeSelfWorkMode()
        {
            compClass = typeof(CompJusticeSelfWorkMode);
        }
    }

    public class CompJusticeSelfWorkMode : ThingComp
    {
        private const string AutonomousDirectiveDefName = "MAP_WorkMode_AutonomousDirective";
        private const string RechargeDefName = "Recharge";

        private MechWorkModeDef? selfWorkMode;

        private static MechWorkModeDef? autonomousDirectiveDef;

        public MechWorkModeDef CurrentSelfWorkMode => SanitizeWorkMode(selfWorkMode);

        public bool IsAutonomousDirective =>
            CurrentSelfWorkMode.defName == AutonomousDirectiveDefName;

        public bool IsSelfRecharge =>
            CurrentSelfWorkMode.defName == RechargeDefName;

        public static CompJusticeSelfWorkMode? GetFor(Pawn? pawn) =>
            pawn?.GetComp<CompJusticeSelfWorkMode>();

        public void SetSelfWorkMode(MechWorkModeDef? mode)
        {
            MechWorkModeDef sanitized = SanitizeWorkMode(mode);
            if (selfWorkMode == sanitized)
            {
                return;
            }

            selfWorkMode = sanitized;
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

            MechWorkModeDef recharge = MechWorkModeDefOf.Recharge;
            options.Add(new FloatMenuOption(
                GetDisplayLabel(recharge),
                () => comp.SetSelfWorkMode(recharge),
                recharge.uiIcon,
                Color.white));
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Defs.Look(ref selfWorkMode, "justiceSelfWorkMode");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                selfWorkMode = SanitizeWorkMode(selfWorkMode);
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

            if (mode?.defName == RechargeDefName)
            {
                return MechWorkModeDefOf.Recharge;
            }

            return GetAutonomousDirectiveDef();
        }
    }
}

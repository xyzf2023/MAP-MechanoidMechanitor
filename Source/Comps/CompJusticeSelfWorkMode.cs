using System.Collections.Generic;
using RimWorld;
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

        public void CycleSelfWorkMode()
        {
            if (IsAutonomousDirective)
            {
                SetSelfWorkMode(MechWorkModeDefOf.Recharge);
            }
            else
            {
                SetSelfWorkMode(GetAutonomousDirectiveDef());
            }
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

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (parent is not Pawn pawn || !ShouldShowGizmo(pawn))
            {
                yield break;
            }

            MechWorkModeDef mode = CurrentSelfWorkMode;
            Command_Action command = new Command_Action
            {
                defaultLabel = mode.LabelCap,
                defaultDesc = GetGizmoDescription(mode),
                icon = mode.uiIcon ?? BaseContent.BadTex,
                action = CycleSelfWorkMode
            };
            yield return command;
        }

        private static bool ShouldShowGizmo(Pawn pawn)
        {
            return pawn.Spawned && pawn.IsColonistPlayerControlled && !pawn.Dead;
        }

        private static string GetGizmoDescription(MechWorkModeDef mode)
        {
            string description = mode.description;
            if (string.IsNullOrEmpty(description))
            {
                return mode.LabelCap;
            }

            return $"{mode.LabelCap}\n\n{description}";
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

using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 恋人专用安装 JobDriver。恋人自己走到物品旁，经过一段安装时间后使用。
    /// 不创建医生手术账单，不调用完整医疗手术流程。
    /// 通过 job.count 保存具体身体部位在身体树中的索引，以支持存读档与左右部位区分。
    /// </summary>
    public class JobDriver_LoverInstallImplant : JobDriver
    {
        private const int InstallDurationTicks = 600;

        private Thing ImplantItem => TargetThingA;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // 固定只预留一件物品，绝不使用 job.count 作为预留数量。
            return pawn.Reserve(
                job.GetTarget(TargetIndex.A),
                job,
                1,
                1,
                null,
                errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedNullOrForbidden(TargetIndex.A);
            this.FailOnIncapable(PawnCapacityDefOf.Manipulation);

            AddFailCondition(
                () => !LoverImplantFeatureState.EnabledForSession
                    || !LoverImplantUtility.IsLover(pawn));

            yield return Toils_Goto.GotoThing(
                TargetIndex.A,
                PathEndMode.Touch);

            Toil wait =
                Toils_General.Wait(
                    InstallDurationTicks,
                    TargetIndex.A);

            wait.WithProgressBarToilDelay(TargetIndex.A);
            wait.FailOnCannotTouch(TargetIndex.A, PathEndMode.Touch);

            yield return wait;

            yield return Toils_General.Do(ApplyInstallation);
        }

        private void ApplyInstallation()
        {
            Thing item = ImplantItem;
            if (item == null || item.Destroyed || item.stackCount <= 0)
            {
                Messages.Message(
                    "MAP_MechanoidMechanitor.Lover.Implant.InstallFailed".Translate(),
                    pawn,
                    MessageTypeDefOf.RejectInput);
                return;
            }

            if (!LoverRecipeImplantRegistrar.TryGetRecipe(item.def, out RecipeDef recipe))
            {
                Messages.Message(
                    "MAP_MechanoidMechanitor.Lover.Implant.InvalidSelection".Translate(),
                    pawn,
                    MessageTypeDefOf.RejectInput);
                return;
            }

            BodyPartRecord? selectedPart = ResolvePart();
            if (selectedPart == null)
            {
                Messages.Message(
                    "MAP_MechanoidMechanitor.Lover.Implant.InvalidSelection".Translate(),
                    pawn,
                    MessageTypeDefOf.RejectInput);
                return;
            }

            string itemLabel = item.LabelNoCount;
            string partLabel = selectedPart.LabelCap;

            if (!LoverImplantUtility.TryInstall(pawn, item, recipe, selectedPart, out string? failureReason))
            {
                Messages.Message(
                    failureReason ?? "MAP_MechanoidMechanitor.Lover.Implant.InstallFailed".Translate(),
                    pawn,
                    MessageTypeDefOf.RejectInput);
                return;
            }

            Messages.Message(
                "MAP_MechanoidMechanitor.Lover.Implant.InstallSucceeded".Translate(
                    pawn.LabelShort,
                    itemLabel,
                    partLabel),
                pawn,
                MessageTypeDefOf.PositiveEvent);
        }

        private BodyPartRecord? ResolvePart()
        {
            int partIndex = job.count - 1;

            List<BodyPartRecord> allParts =
                pawn.RaceProps.body.AllParts;

            if (partIndex < 0 || partIndex >= allParts.Count)
            {
                return null;
            }

            return allParts[partIndex];
        }
    }
}

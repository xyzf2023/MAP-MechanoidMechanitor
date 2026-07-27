using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 恋人专用右键菜单：选中恋人后右键符合条件的植入体物品，出现安装选项。
    /// 不依赖机械族机械师脑部植入体设置，也不向物品添加任何 Comp。
    /// </summary>
    public class FloatMenuOptionProvider_LoverInstallImplant : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;
        protected override bool MechanoidCanDo => true;

        protected override bool AppliesInt(FloatMenuContext context)
        {
            return LoverImplantUtility.IsLover(
                context.FirstSelectedPawn);
        }

        public override IEnumerable<FloatMenuOption> GetOptionsFor(
            Thing clickedThing,
            FloatMenuContext context)
        {
            if (clickedThing == null)
            {
                yield break;
            }

            Pawn pawn = context.FirstSelectedPawn;
            if (!LoverImplantUtility.IsLover(pawn))
            {
                yield break;
            }

            if (!LoverRecipeImplantRegistrar.TryGetRecipe(clickedThing.def, out RecipeDef recipe))
            {
                yield break;
            }

            List<BodyPartRecord> validParts = LoverImplantUtility.GetValidParts(pawn, recipe);
            if (validParts.NullOrEmpty())
            {
                yield return new FloatMenuOption(
                    "MAP_LoverImplant.Install".Translate(clickedThing.LabelNoCount)
                        + "：" + "MAP_LoverImplant.NoValidPart".Translate(),
                    null);
                yield break;
            }

            if (!pawn.CanReach(clickedThing, PathEndMode.Touch, Danger.Deadly))
            {
                yield return new FloatMenuOption(
                    "MAP_LoverImplant.Install".Translate(clickedThing.LabelNoCount)
                        + "：" + "NoPath".Translate().CapitalizeFirst(),
                    null);
                yield break;
            }

            if (!pawn.CanReserve(clickedThing))
            {
                yield return new FloatMenuOption(
                    "MAP_LoverImplant.Install".Translate(clickedThing.LabelNoCount)
                        + "：" + "Reserved".Translate().CapitalizeFirst(),
                    null);
                yield break;
            }

            if (validParts.Count == 1)
            {
                BodyPartRecord selectedPart = validParts[0];
                yield return FloatMenuUtility.DecoratePrioritizedTask(
                    new FloatMenuOption(
                        "MAP_LoverImplant.Install".Translate(clickedThing.LabelNoCount),
                        () => StartJob(pawn, clickedThing, selectedPart)),
                    pawn,
                    clickedThing,
                    reservedText: "Reserved");
                yield break;
            }

            // 多个合法部位：主选项点击后创建第二级 FloatMenu 选择具体部位。
            yield return FloatMenuUtility.DecoratePrioritizedTask(
                new FloatMenuOption(
                    "MAP_LoverImplant.Install".Translate(clickedThing.LabelNoCount),
                    () => OpenPartMenu(pawn, clickedThing, validParts)),
                pawn,
                clickedThing,
                reservedText: "Reserved");
        }

        private static void OpenPartMenu(
            Pawn pawn,
            Thing item,
            List<BodyPartRecord> validParts)
        {
            List<FloatMenuOption> partOptions =
                new List<FloatMenuOption>();

            for (int i = 0; i < validParts.Count; i++)
            {
                BodyPartRecord selectedPart = validParts[i];

                FloatMenuOption option = new FloatMenuOption(
                    "MAP_LoverImplant.InstallToPart".Translate(
                        item.LabelNoCount,
                        selectedPart.LabelCap),
                    () => StartJob(pawn, item, selectedPart));

                partOptions.Add(
                    FloatMenuUtility.DecoratePrioritizedTask(
                        option,
                        pawn,
                        item,
                        reservedText: "Reserved"));
            }

            Find.WindowStack.Add(new FloatMenu(partOptions));
        }

        private static void StartJob(Pawn pawn, Thing item, BodyPartRecord selectedPart)
        {
            item.SetForbidden(false, false);

            List<BodyPartRecord> allParts = pawn.RaceProps.body.AllParts;
            int partIndex = allParts.IndexOf(selectedPart);
            if (partIndex < 0)
            {
                Messages.Message(
                    "MAP_LoverImplant.InvalidSelection".Translate(),
                    pawn,
                    MessageTypeDefOf.RejectInput);
                return;
            }

            Job job = JobMaker.MakeJob(
                MAPMechanitor_JobDefOf.MAP_LoverInstallImplant,
                item);

            // 使用 partIndex + 1 避免零值与未设置值混淆，该索引随 Job 一起被原版存档系统保存。
            job.count = partIndex + 1;

            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }
    }
}

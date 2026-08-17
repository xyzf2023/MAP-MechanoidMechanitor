using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 通用类人植入体右键菜单：选中被授权 Pawn 后右键符合条件的植入体物品，出现安装选项。
    /// 不向物品添加任何 Comp。受独立的“类人植入体”设置（HumanImplantFeatureState）控制。
    /// 不判断具体种族 defName，只依赖 CompHumanImplantUser。
    /// </summary>
    public class FloatMenuOptionProvider_HumanImplant : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;
        protected override bool MechanoidCanDo => true;

        protected override bool AppliesInt(FloatMenuContext context)
        {
            return HumanImplantFeatureState.EnabledForSession
                && HumanImplantUtility.CanUseHumanImplants(
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

            if (!HumanImplantFeatureState.EnabledForSession)
            {
                yield break;
            }

            Pawn pawn = context.FirstSelectedPawn;
            if (!HumanImplantUtility.CanUseHumanImplants(pawn))
            {
                yield break;
            }

            // 1. 查询注册表候选 Recipe；真正针对 Pawn 的合法性在点击时解决。
            List<RecipeDef> candidates =
                HumanImplantRecipeRegistrar.GetCandidateRecipes(clickedThing.def);

            if (candidates.NullOrEmpty())
            {
                yield break;
            }

            // 2. 对每个候选获取实际合法部位，仅保留存在合法部位的配方。
            RecipeDef? chosenRecipe = null;
            List<BodyPartRecord> chosenParts = new List<BodyPartRecord>();
            int validRecipeCount = 0;

            for (int i = 0; i < candidates.Count; i++)
            {
                RecipeDef recipe = candidates[i];
                List<BodyPartRecord> validParts =
                    HumanImplantUtility.GetValidParts(pawn, recipe);

                if (validParts.NullOrEmpty())
                {
                    continue;
                }

                validRecipeCount++;
                chosenRecipe = recipe;
                chosenParts = validParts;
            }

            // 0 个合法配方：提示没有可安装部位。
            if (validRecipeCount == 0 || chosenRecipe == null)
            {
                yield return new FloatMenuOption(
                    "MAP_MechanoidMechanitor.HumanImplant.Install".Translate(clickedThing.LabelNoCount)
                        + "：" + "MAP_MechanoidMechanitor.HumanImplant.NoValidPart".Translate(),
                    null);
                yield break;
            }

            // 多个不同 Recipe 对同一物品、同一 Pawn 同时合法：判定为歧义，不执行安装。
            if (validRecipeCount > 1)
            {
                yield return new FloatMenuOption(
                    "MAP_MechanoidMechanitor.HumanImplant.Install".Translate(clickedThing.LabelNoCount)
                        + "：" + "MAP_MechanoidMechanitor.HumanImplant.AmbiguousRecipe".Translate(),
                    null);
                yield break;
            }

            if (!pawn.CanReach(clickedThing, PathEndMode.Touch, Danger.Deadly))
            {
                yield return new FloatMenuOption(
                    "MAP_MechanoidMechanitor.HumanImplant.Install".Translate(clickedThing.LabelNoCount)
                        + "：" + "NoPath".Translate().CapitalizeFirst(),
                    null);
                yield break;
            }

            if (!pawn.CanReserve(clickedThing))
            {
                yield return new FloatMenuOption(
                    "MAP_MechanoidMechanitor.HumanImplant.Install".Translate(clickedThing.LabelNoCount)
                        + "：" + "Reserved".Translate().CapitalizeFirst(),
                    null);
                yield break;
            }

            if (chosenParts.Count == 1)
            {
                BodyPartRecord selectedPart = chosenParts[0];
                yield return FloatMenuUtility.DecoratePrioritizedTask(
                    new FloatMenuOption(
                        "MAP_MechanoidMechanitor.HumanImplant.Install".Translate(clickedThing.LabelNoCount),
                        () => StartJob(pawn, clickedThing, chosenRecipe, selectedPart)),
                    pawn,
                    clickedThing,
                    reservedText: "Reserved");
                yield break;
            }

            // 多个合法部位：主选项点击后创建第二级 FloatMenu 选择具体部位。
            yield return FloatMenuUtility.DecoratePrioritizedTask(
                new FloatMenuOption(
                    "MAP_MechanoidMechanitor.HumanImplant.Install".Translate(clickedThing.LabelNoCount),
                    () => OpenPartMenu(pawn, clickedThing, chosenRecipe, chosenParts)),
                pawn,
                clickedThing,
                reservedText: "Reserved");
        }

        private static void OpenPartMenu(
            Pawn pawn,
            Thing item,
            RecipeDef recipe,
            List<BodyPartRecord> validParts)
        {
            List<FloatMenuOption> partOptions =
                new List<FloatMenuOption>();

            for (int i = 0; i < validParts.Count; i++)
            {
                BodyPartRecord selectedPart = validParts[i];

                FloatMenuOption option = new FloatMenuOption(
                    "MAP_MechanoidMechanitor.HumanImplant.InstallToPart".Translate(
                        item.LabelNoCount,
                        selectedPart.LabelCap),
                    () => StartJob(pawn, item, recipe, selectedPart));

                partOptions.Add(
                    FloatMenuUtility.DecoratePrioritizedTask(
                        option,
                        pawn,
                        item,
                        reservedText: "Reserved"));
            }

            Find.WindowStack.Add(new FloatMenu(partOptions));
        }

        private static void StartJob(Pawn pawn, Thing item, RecipeDef recipe, BodyPartRecord selectedPart)
        {
            item.SetForbidden(false, false);

            List<BodyPartRecord> allParts = pawn.RaceProps.body.AllParts;
            int partIndex = allParts.IndexOf(selectedPart);
            if (partIndex < 0)
            {
                Messages.Message(
                    "MAP_MechanoidMechanitor.HumanImplant.InvalidSelection".Translate(),
                    pawn,
                    MessageTypeDefOf.RejectInput);
                return;
            }

            Job job = JobMaker.MakeJob(
                MAPMechanitor_JobDefOf.MAP_HumanImplantInstall,
                item);

            // 使用 partIndex + 1 避免零值与未设置值混淆，该索引随 Job 一起被原版存档系统保存。
            job.count = partIndex + 1;

            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }
    }
}

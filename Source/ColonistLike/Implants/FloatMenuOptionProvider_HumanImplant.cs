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

            // 1. 查询注册表候选 Recipe（同一物品可能对应多个 Recipe）。
            List<RecipeDef> candidates =
                HumanImplantRecipeRegistrar.GetCandidateRecipes(clickedThing.def);

            if (candidates.NullOrEmpty())
            {
                yield break;
            }

            // 2. 按“具体部位”分组候选 Recipe：只有对同一具体部位同时有多个合法 Recipe 才是真正歧义。
            Dictionary<BodyPartRecord, List<RecipeDef>> recipesByPart =
                new Dictionary<BodyPartRecord, List<RecipeDef>>();

            for (int i = 0; i < candidates.Count; i++)
            {
                RecipeDef candidate = candidates[i];
                List<BodyPartRecord> validParts =
                    HumanImplantUtility.GetValidParts(pawn, candidate);

                if (validParts.NullOrEmpty())
                {
                    continue;
                }

                for (int j = 0; j < validParts.Count; j++)
                {
                    BodyPartRecord part = validParts[j];

                    if (!recipesByPart.TryGetValue(part, out List<RecipeDef>? list))
                    {
                        list = new List<RecipeDef>();
                        recipesByPart.Add(part, list);
                    }

                    if (!list.Contains(candidate))
                    {
                        list.Add(candidate);
                    }
                }
            }

            // 没有任何合法部位。
            if (recipesByPart.Count == 0)
            {
                yield return new FloatMenuOption(
                    "MAP_MechanoidMechanitor.HumanImplant.Install".Translate(clickedThing.LabelNoCount)
                        + "：" + "MAP_MechanoidMechanitor.HumanImplant.NoValidPart".Translate(),
                    null);
                yield break;
            }

            // 区分唯一可解析部位与歧义部位（同一部位多个 Recipe 合法）。
            List<BodyPartRecord> uniqueParts = new List<BodyPartRecord>();
            List<BodyPartRecord> ambiguousParts = new List<BodyPartRecord>();

            foreach (KeyValuePair<BodyPartRecord, List<RecipeDef>> kvp in recipesByPart)
            {
                if (kvp.Value.Count == 1)
                {
                    uniqueParts.Add(kvp.Key);
                }
                else
                {
                    ambiguousParts.Add(kvp.Key);
                }
            }

            // 仅一个合法且唯一部位：直接显示安装项。
            if (uniqueParts.Count == 1 && ambiguousParts.Count == 0)
            {
                BodyPartRecord selectedPart = uniqueParts[0];

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

                yield return FloatMenuUtility.DecoratePrioritizedTask(
                    new FloatMenuOption(
                        "MAP_MechanoidMechanitor.HumanImplant.Install".Translate(clickedThing.LabelNoCount),
                        () => StartJob(pawn, clickedThing, selectedPart)),
                    pawn,
                    clickedThing,
                    reservedText: "Reserved");
                yield break;
            }

            // 仅一个部位但它是歧义部位：直接显示禁用项，绝不随机选一个。
            if (recipesByPart.Count == 1)
            {
                yield return new FloatMenuOption(
                    "MAP_MechanoidMechanitor.HumanImplant.Install".Translate(clickedThing.LabelNoCount)
                        + "：" + "MAP_MechanoidMechanitor.HumanImplant.AmbiguousRecipe".Translate(),
                    null);
                yield break;
            }

            // 多个部位（含可能的歧义部位）：打开第二级部位选择菜单。
            List<FloatMenuOption> partOptions = new List<FloatMenuOption>();

            foreach (KeyValuePair<BodyPartRecord, List<RecipeDef>> kvp in recipesByPart)
            {
                BodyPartRecord part = kvp.Key;

                if (kvp.Value.Count == 1)
                {
                    // 唯一可解析部位：正常可点击。
                    FloatMenuOption option = new FloatMenuOption(
                        "MAP_MechanoidMechanitor.HumanImplant.InstallToPart".Translate(
                            clickedThing.LabelNoCount,
                            part.LabelCap),
                        () => StartJob(pawn, clickedThing, part));

                    partOptions.Add(
                        FloatMenuUtility.DecoratePrioritizedTask(
                            option,
                            pawn,
                            clickedThing,
                            reservedText: "Reserved"));
                }
                else
                {
                    // 同一部位存在多个可用配方：禁用项，提示歧义。
                    partOptions.Add(
                        new FloatMenuOption(
                            "MAP_MechanoidMechanitor.HumanImplant.InstallToPart".Translate(
                                clickedThing.LabelNoCount,
                                part.LabelCap)
                                + "：" + "MAP_MechanoidMechanitor.HumanImplant.AmbiguousRecipe".Translate(),
                            null));
                }
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
                    "MAP_MechanoidMechanitor.HumanImplant.InvalidSelection".Translate(),
                    pawn,
                    MessageTypeDefOf.RejectInput);
                return;
            }

            Job job = JobMaker.MakeJob(
                MAPMechanitor_JobDefOf.MAP_HumanImplantInstall,
                item);

            // 使用 partIndex + 1 避免零值与未设置值混淆，该索引随 Job 一起被原版存档系统保存。
            // Job 中不保存任何 Recipe 对象；执行时由 Pawn + 物品 + 具体部位重新唯一解析。
            job.count = partIndex + 1;

            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }
    }
}

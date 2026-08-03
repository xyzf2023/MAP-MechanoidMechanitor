using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 惰性扫描并缓存全部符合原版机械培育结构的普通生产与机械族复活配方。
    /// 不含科研可用性过滤；界面仍由 AvailableNow / AvailableOnNow 实时决定是否显示。
    /// </summary>
    public static class MassProductionMechGestatorRecipeRegistry
    {
        private static List<RecipeDef>? eligibleRecipes;

        private static readonly object InitLock = new object();

        public static IReadOnlyList<RecipeDef> EligibleRecipes
        {
            get
            {
                EnsureInitialized();
                return eligibleRecipes!;
            }
        }

        public static void EnsureInitialized()
        {
            if (eligibleRecipes != null)
            {
                return;
            }

            lock (InitLock)
            {
                if (eligibleRecipes != null)
                {
                    return;
                }

                eligibleRecipes = BuildEligibleRecipes();
            }
        }

        private static List<RecipeDef> BuildEligibleRecipes()
        {
            List<RecipeDef> accepted = new List<RecipeDef>();
            HashSet<RecipeDef> acceptedSet = new HashSet<RecipeDef>();

            List<string> rejectedNoProduct = new List<string>();
            List<string> rejectedNotPawn = new List<string>();
            List<string> rejectedNotMechanoid = new List<string>();
            List<string> rejectedNoPawnKind = new List<string>();
            List<string> rejectedBadCount = new List<string>();

            int productionRecipeCount = 0;
            int resurrectionRecipeCount = 0;

            List<RecipeDef> allRecipes = DefDatabase<RecipeDef>.AllDefsListForReading;
            for (int i = 0; i < allRecipes.Count; i++)
            {
                RecipeDef? recipe = allRecipes[i];
                if (recipe == null
                    || recipe.gestationCycles <= 0
                    || !recipe.mechanitorOnlyRecipe)
                {
                    continue;
                }

                // 复活配方的产品由送入的机械族尸体决定，原版结构没有固定 products。
                // mechResurrection 同时也是 BillUtility 选择 Bill_ResurrectMech 的权威标志。
                if (recipe.mechResurrection)
                {
                    if (acceptedSet.Add(recipe))
                    {
                        accepted.Add(recipe);
                        resurrectionRecipeCount++;
                    }

                    continue;
                }

                // 普通机械培育配方仍维持严格校验，避免误收其他自主工作台配方。
                if (!TryGetSingleProduct(recipe, out ThingDefCountClass? productEntry, out string? rejectReason))
                {
                    if (rejectReason == "noProduct")
                    {
                        rejectedNoProduct.Add(recipe.defName);
                    }
                    else if (rejectReason == "badCount")
                    {
                        rejectedBadCount.Add(recipe.defName);
                    }

                    continue;
                }

                ThingDef productDef = productEntry!.thingDef;
                if (productDef.category != ThingCategory.Pawn)
                {
                    rejectedNotPawn.Add(recipe.defName);
                    continue;
                }

                if (productDef.race == null || !productDef.race.IsMechanoid)
                {
                    rejectedNotMechanoid.Add(recipe.defName);
                    continue;
                }

                if (!HasMatchingPawnKind(productDef))
                {
                    rejectedNoPawnKind.Add(recipe.defName);
                    continue;
                }

                if (acceptedSet.Add(recipe))
                {
                    accepted.Add(recipe);
                    productionRecipeCount++;
                }
            }

            // 此处只决定动态配方追加到 ThingDef.AllRecipes 时的稳定顺序。
            //
            // ITab_Bills 创建菜单选项时会传入 orderInPriority = -recipe.displayPriority，
            // FloatMenu 随后按 orderInPriority 从大到小排序，因此原版最终菜单实际上是
            // displayPriority 数值越小越靠前。
            //
            // 量产仓专用 ITab 会在菜单构建期间临时统一复活配方优先级，
            // 使全部 mechResurrection 配方形成连续的置顶区块。
            accepted.Sort(CompareRecipesForStableAppend);

            if (Prefs.DevMode)
            {
                LogDevSummary(
                    accepted,
                    productionRecipeCount,
                    resurrectionRecipeCount,
                    rejectedNoProduct,
                    rejectedNotPawn,
                    rejectedNotMechanoid,
                    rejectedNoPawnKind,
                    rejectedBadCount);
            }

            return accepted;
        }

        private static bool TryGetSingleProduct(
            RecipeDef recipe,
            out ThingDefCountClass? productEntry,
            out string? rejectReason)
        {
            productEntry = null;
            rejectReason = null;

            if (recipe.products.NullOrEmpty())
            {
                rejectReason = "noProduct";
                return false;
            }

            if (recipe.products.Count != 1)
            {
                rejectReason = "badCount";
                return false;
            }

            ThingDefCountClass entry = recipe.products[0];
            if (entry == null || entry.thingDef == null)
            {
                rejectReason = "noProduct";
                return false;
            }

            if (entry.count != 1)
            {
                rejectReason = "badCount";
                return false;
            }

            productEntry = entry;
            return true;
        }

        private static bool HasMatchingPawnKind(ThingDef raceDef)
        {
            List<PawnKindDef> kinds = DefDatabase<PawnKindDef>.AllDefsListForReading;
            for (int i = 0; i < kinds.Count; i++)
            {
                if (kinds[i].race == raceDef)
                {
                    return true;
                }
            }

            return false;
        }

        private static int CompareRecipesForStableAppend(RecipeDef a, RecipeDef b)
        {
            // 与 ITab_Bills 的 orderInPriority: -displayPriority 一致：数值更大的优先。
            int priorityCompare = b.displayPriority.CompareTo(a.displayPriority);
            if (priorityCompare != 0)
            {
                return priorityCompare;
            }

            string labelA = a.ProducedThingDef?.label ?? a.label ?? string.Empty;
            string labelB = b.ProducedThingDef?.label ?? b.label ?? string.Empty;
            int labelCompare = string.CompareOrdinal(labelA, labelB);
            if (labelCompare != 0)
            {
                return labelCompare;
            }

            return string.CompareOrdinal(a.defName, b.defName);
        }

        private static void LogDevSummary(
            List<RecipeDef> accepted,
            int productionRecipeCount,
            int resurrectionRecipeCount,
            List<string> rejectedNoProduct,
            List<string> rejectedNotPawn,
            List<string> rejectedNotMechanoid,
            List<string> rejectedNoPawnKind,
            List<string> rejectedBadCount)
        {
            StringBuilder names = new StringBuilder();
            for (int i = 0; i < accepted.Count; i++)
            {
                if (i > 0)
                {
                    names.Append(", ");
                }

                names.Append(accepted[i].defName);
            }

            Log.Message(
                "[MAP-MechanoidMechanitor] Mass production gestator recipe registry initialized. Eligible count="
                + accepted.Count
                + ", production="
                + productionRecipeCount
                + ", resurrection="
                + resurrectionRecipeCount
                + ". Candidates=["
                + names
                + "].");

            LogRejectedGroup("no product", rejectedNoProduct);
            LogRejectedGroup("product count != 1", rejectedBadCount);
            LogRejectedGroup("product is not Pawn", rejectedNotPawn);
            LogRejectedGroup("product is not mechanoid", rejectedNotMechanoid);
            LogRejectedGroup("no matching PawnKindDef", rejectedNoPawnKind);
        }

        private static void LogRejectedGroup(string reason, List<string> defNames)
        {
            if (defNames.Count == 0)
            {
                return;
            }

            Log.Warning(
                "[MAP-MechanoidMechanitor] Suspected mech gestation recipes excluded ("
                + reason
                + "): "
                + string.Join(", ", defNames));
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 类人植入体配方注册表（组件驱动，不再绑定任何特定 BodyDef / 种族）。
    /// 仅保存“物品 Def → 可能适用的安装 RecipeDef 列表”的对应关系，绝不在物品上添加任何 Comp。
    /// 真正的身体部位合法性在玩家点击时通过 recipe.Worker.GetPartsToApplyOn(pawn, recipe) 解决。
    /// 与机械族机械师脑部植入体系统（MechanoidMechanitorRecipeImplantRegistrar）相互独立。
    /// </summary>
    public static class HumanImplantRecipeRegistrar
    {
        private const string LogPrefix = "[MAP-机械族机械师] 类人植入体：";

        // 一个物品可能对应多个合法 RecipeDef（例如附着与替换并存），因此用 List。
        private static readonly Dictionary<ThingDef, List<RecipeDef>> recipesByItem =
            new Dictionary<ThingDef, List<RecipeDef>>();

        private static readonly HashSet<ThingDef> ambiguousItems =
            new HashSet<ThingDef>();

        private static bool initialized;

        public static void Register()
        {
            if (!HumanImplantFeatureState.EnabledForSession)
            {
                return;
            }

            if (initialized)
            {
                return;
            }

            initialized = true;

            foreach (RecipeDef recipe in DefDatabase<RecipeDef>.AllDefsListForReading)
            {
                try
                {
                    if (!IsSupportedRecipe(recipe))
                    {
                        continue;
                    }

                    if (!TryResolveImplantThing(recipe, out ThingDef? itemDef)
                        || itemDef == null)
                    {
                        if (Prefs.DevMode)
                        {
                            Log.Message(
                                $"{LogPrefix}跳过配方 {recipe.defName}，无法唯一识别安装物品。");
                        }

                        continue;
                    }

                    RegisterMapping(itemDef, recipe);
                }
                catch (Exception ex)
                {
                    Log.Error(
                        $"{LogPrefix}处理配方 {recipe?.defName ?? "null"} 时发生异常，已跳过：{ex}");
                }
            }

            if (Prefs.DevMode)
            {
                Log.Message(
                    $"{LogPrefix}注册完成，有效物品 {recipesByItem.Count} 个，歧义物品 {ambiguousItems.Count} 个。");
            }
        }

        /// <summary>
        /// 返回该物品当前所有非歧义候选 RecipeDef。歧义或未知物品返回空列表。
        /// </summary>
        public static List<RecipeDef> GetCandidateRecipes(ThingDef itemDef)
        {
            List<RecipeDef> result = new List<RecipeDef>();

            if (!HumanImplantFeatureState.EnabledForSession
                || itemDef == null
                || ambiguousItems.Contains(itemDef)
                || !recipesByItem.TryGetValue(itemDef, out List<RecipeDef>? found))
            {
                return result;
            }

            result.AddRange(found);
            return result;
        }

        /// <summary>
        /// 该物品 + 配方是否为已注册的非歧义组合。
        /// </summary>
        public static bool IsRegistered(ThingDef itemDef, RecipeDef recipe)
        {
            if (!HumanImplantFeatureState.EnabledForSession
                || itemDef == null
                || recipe == null
                || ambiguousItems.Contains(itemDef)
                || !recipesByItem.TryGetValue(itemDef, out List<RecipeDef>? found))
            {
                return false;
            }

            return found.Contains(recipe);
        }

        /// <summary>
        /// 该物品是否恰好对应一个非歧义安装配方。JobDriver 在运行时仅凭物品解析配方使用。
        /// 歧义或未知物品返回 false。
        /// </summary>
        public static bool TryGetSingleRecipe(ThingDef itemDef, out RecipeDef? recipe)
        {
            recipe = null;

            if (!HumanImplantFeatureState.EnabledForSession
                || itemDef == null
                || ambiguousItems.Contains(itemDef)
                || !recipesByItem.TryGetValue(itemDef, out List<RecipeDef>? found)
                || found == null
                || found.Count != 1)
            {
                return false;
            }

            recipe = found[0];
            return true;
        }

        public static bool IsSupportedRecipe(RecipeDef recipe)
        {
            if (!IsAttachmentRecipe(recipe) && !IsReplacementRecipe(recipe))
            {
                return false;
            }

            if (recipe.appliedOnFixedBodyParts.NullOrEmpty()
                && recipe.appliedOnFixedBodyPartGroups.NullOrEmpty())
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 附着型植入物：标准 Recipe_InstallImplant，添加的健康状态不是 Hediff_AddedPart。
        /// </summary>
        public static bool IsAttachmentRecipe(RecipeDef recipe)
        {
            if (recipe?.workerClass != typeof(Recipe_InstallImplant)
                || recipe.addsHediff == null
                || !recipe.addsHediff.countsAsAddedPartOrImplant)
            {
                return false;
            }

            Type hediffClass = recipe.addsHediff.hediffClass;
            return hediffClass != null
                && !typeof(Hediff_AddedPart).IsAssignableFrom(hediffClass);
        }

        /// <summary>
        /// 替换型义体：标准 Recipe_InstallArtificialBodyPart，addsHediff.hediffClass 继承 Hediff_AddedPart。
        /// </summary>
        public static bool IsReplacementRecipe(RecipeDef recipe)
        {
            if (recipe?.workerClass != typeof(Recipe_InstallArtificialBodyPart)
                || recipe.addsHediff == null)
            {
                return false;
            }

            Type hediffClass = recipe.addsHediff.hediffClass;
            return hediffClass != null
                && typeof(Hediff_AddedPart).IsAssignableFrom(hediffClass);
        }

        private static bool TryResolveImplantThing(
            RecipeDef recipe,
            out ThingDef? itemDef)
        {
            bool requireTechHediff = IsReplacementRecipe(recipe);

            ThingDef? spawnedOnRemoval = recipe.addsHediff?.spawnThingOnRemoved;
            if (IsCandidate(recipe, spawnedOnRemoval, requireTechHediff))
            {
                itemDef = spawnedOnRemoval;
                return true;
            }

            ThingDef? uniqueCandidate = null;
            if (!recipe.ingredients.NullOrEmpty())
            {
                for (int i = 0; i < recipe.ingredients.Count; i++)
                {
                    IngredientCount ingredient = recipe.ingredients[i];
                    if (ingredient?.filter == null
                        || ingredient.filter.AllowedDefCount != 1)
                    {
                        continue;
                    }

                    ThingDef? candidate = ingredient.filter.AnyAllowedDef;

                    // 备用成分候选，即使是附着型，也必须额外要求 candidate.isTechHediff == true，
                    // 且所有候选都必须归入 BodyParts 分类（见 IsCandidate）。
                    // 注意：原版 WoodLog 的 isTechHediff 也是 true，因此不能只靠 isTechHediff
                    // 排除木材，必须由 BodyParts 分类限制通用材料。
                    if (!IsCandidate(recipe, candidate, true))
                    {
                        continue;
                    }

                    if (uniqueCandidate != null && uniqueCandidate != candidate)
                    {
                        itemDef = null;
                        return false;
                    }

                    uniqueCandidate = candidate;
                }
            }

            itemDef = uniqueCandidate;
            return itemDef != null;
        }

        private static bool IsCandidate(
            RecipeDef recipe,
            ThingDef? candidate,
            bool requireTechHediff)
        {
            return candidate != null
                && candidate.category == ThingCategory.Item
                && candidate.IsWithinCategory(ThingCategoryDefOf.BodyParts)
                && (!requireTechHediff || candidate.isTechHediff)
                && recipe.IsIngredient(candidate);
        }

        private static void RegisterMapping(ThingDef? itemDef, RecipeDef recipe)
        {
            if (itemDef == null)
            {
                return;
            }

            if (ambiguousItems.Contains(itemDef))
            {
                return;
            }

            if (!recipesByItem.TryGetValue(itemDef, out List<RecipeDef>? existing))
            {
                existing = new List<RecipeDef>();
                recipesByItem.Add(itemDef, existing);
            }
            else if (existing.Contains(recipe))
            {
                return;
            }

            if (existing.Count >= 1)
            {
                // 同一物品已对应不同配方，标记为歧义并禁用自动注册，避免错误安装。
                recipesByItem.Remove(itemDef);
                ambiguousItems.Add(itemDef);

                if (Prefs.DevMode)
                {
                    Log.Warning(
                        $"{LogPrefix}物品 {itemDef.defName} 同时对应多个安装配方，为避免错误安装，已禁用该物品的自动注册。");
                }

                return;
            }

            existing.Add(recipe);
        }
    }
}

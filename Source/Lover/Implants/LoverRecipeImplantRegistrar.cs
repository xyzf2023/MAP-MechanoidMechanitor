using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 恋人专用植入体配方注册表。
    /// 仅保存“物品 Def → 安装配方”的对应关系，绝不在物品上添加任何 Comp。
    /// 与机械族机械师脑部植入体系统（MechanoidMechanitorRecipeImplantRegistrar）相互独立。
    /// </summary>
    public static class LoverRecipeImplantRegistrar
    {
        private const string LogPrefix = "[MAP-机械族机械师] 恋人植入体：";

        private static readonly Dictionary<ThingDef, RecipeDef> recipesByItem =
            new Dictionary<ThingDef, RecipeDef>();

        private static readonly HashSet<ThingDef> ambiguousItems =
            new HashSet<ThingDef>();

        private static bool initialized;

        public static void Register()
        {
            if (initialized)
            {
                return;
            }

            initialized = true;

            BodyDef loverBody =
                DefDatabase<BodyDef>.GetNamedSilentFail("MAP_Body_Lover");

            if (loverBody == null)
            {
                Log.Error("[MAP-机械族机械师] 未找到 MAP_Body_Lover，恋人植入体注册已停止。");
                return;
            }

            foreach (RecipeDef recipe in DefDatabase<RecipeDef>.AllDefsListForReading)
            {
                try
                {
                    if (!IsSupportedRecipe(recipe))
                    {
                        continue;
                    }

                    if (!TargetsLoverBody(recipe, loverBody))
                    {
                        continue;
                    }

                    if (!TryResolveImplantThing(recipe, out ThingDef? itemDef)
                        || itemDef == null)
                    {
                        if (Prefs.DevMode)
                        {
                            Log.Message(
                                $"[MAP-机械族机械师] 恋人植入体：跳过配方 {recipe.defName}，无法唯一识别安装物品。");
                        }

                        continue;
                    }

                    RegisterMapping(itemDef, recipe);
                }
                catch (Exception ex)
                {
                    Log.Error(
                        $"[MAP-机械族机械师] 恋人植入体：处理配方 {recipe?.defName ?? "null"} 时发生异常，已跳过：{ex}");
                }
            }

            if (Prefs.DevMode)
            {
                Log.Message(
                    $"[MAP-机械族机械师] 恋人植入体：注册完成，有效物品 {recipesByItem.Count} 个，歧义物品 {ambiguousItems.Count} 个。");
            }
        }

        /// <summary>
        /// 仅对非歧义物品返回配方。
        /// </summary>
        public static bool TryGetRecipe(ThingDef itemDef, out RecipeDef recipe)
        {
            recipe = null!;

            if (itemDef != null
                && !ambiguousItems.Contains(itemDef)
                && recipesByItem.TryGetValue(itemDef, out RecipeDef found))
            {
                recipe = found;
                return true;
            }

            return false;
        }

        private static bool IsSupportedRecipe(RecipeDef recipe)
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

        private static bool TargetsLoverBody(RecipeDef recipe, BodyDef loverBody)
        {
            if (!recipe.appliedOnFixedBodyParts.NullOrEmpty())
            {
                for (int i = 0; i < recipe.appliedOnFixedBodyParts.Count; i++)
                {
                    BodyPartDef targetDef = recipe.appliedOnFixedBodyParts[i];
                    if (targetDef != null
                        && loverBody.AllParts.Any(part => part.def == targetDef))
                    {
                        return true;
                    }
                }
            }

            if (!recipe.appliedOnFixedBodyPartGroups.NullOrEmpty())
            {
                for (int i = 0; i < recipe.appliedOnFixedBodyPartGroups.Count; i++)
                {
                    BodyPartGroupDef targetGroup =
                        recipe.appliedOnFixedBodyPartGroups[i];

                    if (targetGroup != null
                        && loverBody.AllParts.Any(
                            part => part.groups != null
                                && part.groups.Contains(targetGroup)))
                    {
                        return true;
                    }
                }
            }

            return false;
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
                    // 否则木腿之类用普通木材作原料的配方会把所有木材识别为义体。
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

            if (!recipesByItem.TryGetValue(itemDef, out RecipeDef existing))
            {
                recipesByItem.Add(itemDef, recipe);
                return;
            }

            if (existing == recipe)
            {
                return;
            }

            recipesByItem.Remove(itemDef);
            ambiguousItems.Add(itemDef);

            if (Prefs.DevMode)
            {
                Log.Warning(
                    $"[MAP-机械族机械师] 恋人植入体：物品 {itemDef.defName} 同时对应配方 {existing.defName} 与 {recipe.defName}，为避免错误安装，已禁用该物品的自动注册。");
            }
        }
    }
}

using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class MechanoidMechanitorRecipeImplantRegistrar
    {
        private const string LogPrefix =
            "[MAP-机械族机械师] MechanoidMechanitorRecipeImplantRegistrar：";

        private static readonly Dictionary<ThingDef, RecipeDef> recipesByImplant =
            new Dictionary<ThingDef, RecipeDef>();

        private static bool initialized;

        public static void Register()
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            int scannedCount = 0;
            int eligibleRecipeCount = 0;
            int registeredCount = 0;
            int skippedCount = 0;

            List<RecipeDef> recipes = DefDatabase<RecipeDef>.AllDefsListForReading;
            for (int i = 0; i < recipes.Count; i++)
            {
                RecipeDef recipe = recipes[i];
                scannedCount++;

                try
                {
                    if (!IsSupportedBrainImplantRecipe(recipe))
                    {
                        continue;
                    }

                    eligibleRecipeCount++;
                    if (!TryResolveImplantThing(recipe, out ThingDef? implantDef)
                        || implantDef == null)
                    {
                        skippedCount++;
                        LogSkippedRecipe(recipe, "无法唯一识别植入体物品");
                        continue;
                    }

                    if (recipesByImplant.TryGetValue(implantDef, out RecipeDef existingRecipe))
                    {
                        skippedCount++;
                        LogSkippedRecipe(
                            recipe,
                            $"物品 {implantDef.defName} 已由配方 {existingRecipe.defName} 注册");
                        continue;
                    }

                    if (!TryAttachComponents(implantDef, recipe, out string? failureReason))
                    {
                        skippedCount++;
                        LogSkippedRecipe(recipe, failureReason ?? "无法添加使用组件");
                        continue;
                    }

                    recipesByImplant.Add(implantDef, recipe);
                    registeredCount++;
                }
                catch (Exception ex)
                {
                    skippedCount++;
                    Log.Error(
                        $"{LogPrefix}处理配方 {recipe?.defName ?? "null"} 时发生异常，" +
                        $"已跳过该配方：{ex}");
                }
            }

            if (Prefs.DevMode)
            {
                Log.Message(
                    $"{LogPrefix}扫描 {scannedCount} 个配方，" +
                    $"发现 {eligibleRecipeCount} 个标准脑部植入配方，" +
                    $"注册 {registeredCount} 个物品，跳过 {skippedCount} 个。");
            }
        }

        public static bool TryGetRecipe(ThingDef? implantDef, out RecipeDef? recipe)
        {
            if (implantDef != null
                && recipesByImplant.TryGetValue(implantDef, out RecipeDef found))
            {
                recipe = found;
                return true;
            }

            recipe = null;
            return false;
        }

        private static bool IsSupportedBrainImplantRecipe(RecipeDef recipe)
        {
            if (recipe.workerClass != typeof(Recipe_InstallImplant)
                || recipe.addsHediff == null
                || recipe.appliedOnFixedBodyParts == null
                || recipe.appliedOnFixedBodyParts.Count != 1
                || recipe.appliedOnFixedBodyParts[0] == null
                || recipe.appliedOnFixedBodyParts[0].defName != "Brain"
                || !recipe.appliedOnFixedBodyPartGroups.NullOrEmpty())
            {
                return false;
            }

            Type? hediffClass = recipe.addsHediff.hediffClass;
            return recipe.addsHediff.countsAsAddedPartOrImplant
                && hediffClass != null
                && !typeof(Hediff_AddedPart).IsAssignableFrom(hediffClass);
        }

        private static bool TryResolveImplantThing(
            RecipeDef recipe,
            out ThingDef? implantDef)
        {
            ThingDef? spawnedOnRemoval = recipe.addsHediff?.spawnThingOnRemoved;
            if (IsStrongCandidate(recipe, spawnedOnRemoval))
            {
                implantDef = spawnedOnRemoval;
                return true;
            }

            ThingDef? uniqueCandidate = null;
            for (int i = 0; i < recipe.ingredients.Count; i++)
            {
                IngredientCount ingredient = recipe.ingredients[i];
                if (ingredient?.filter == null || ingredient.filter.AllowedDefCount != 1)
                {
                    continue;
                }

                ThingDef? candidate = ingredient.filter.AnyAllowedDef;
                if (!IsFallbackCandidate(recipe, candidate))
                {
                    continue;
                }

                if (uniqueCandidate != null && uniqueCandidate != candidate)
                {
                    implantDef = null;
                    return false;
                }

                uniqueCandidate = candidate;
            }

            implantDef = uniqueCandidate;
            return implantDef != null;
        }

        private static bool IsStrongCandidate(RecipeDef recipe, ThingDef? candidate)
        {
            return IsStructurallyUsableCandidate(recipe, candidate);
        }

        private static bool IsFallbackCandidate(RecipeDef recipe, ThingDef? candidate)
        {
            return candidate?.isTechHediff == true
                && IsStructurallyUsableCandidate(recipe, candidate);
        }

        private static bool IsStructurallyUsableCandidate(
            RecipeDef recipe,
            ThingDef? candidate)
        {
            return candidate != null
                && candidate.category == ThingCategory.Item
                && candidate.thingClass != null
                && typeof(ThingWithComps).IsAssignableFrom(candidate.thingClass)
                && recipe.IsIngredient(candidate);
        }

        private static bool TryAttachComponents(
            ThingDef implantDef,
            RecipeDef recipe,
            out string? failureReason)
        {
            implantDef.comps ??= new List<CompProperties>();

            if (HasCompAssignableTo<CompUsable>(implantDef.comps))
            {
                failureReason = $"物品 {implantDef.defName} 已包含可使用组件";
                return false;
            }

            if (HasCompAssignableTo<CompUseEffect_InstallImplant>(implantDef.comps))
            {
                failureReason = $"物品 {implantDef.defName} 已包含植入效果组件";
                return false;
            }

            JobDef? useItemJob = DefDatabase<JobDef>.GetNamedSilentFail("UseItem");
            if (useItemJob == null)
            {
                failureReason = "未找到原版 UseItem 工作定义";
                return false;
            }

            implantDef.comps.Add(
                new CompProperties_UsableMechanoidMechanitorBrainImplant
                {
                    sourceRecipe = recipe,
                    useJob = useItemJob,
                    useLabel =
                        "MAP_MechanoidMechanitor.BrainImplant.InstallLabel".Translate()
                });

            implantDef.comps.Add(
                new CompProperties_UseEffectInstallImplant
                {
                    hediffDef = recipe.addsHediff,
                    bodyPart = recipe.appliedOnFixedBodyParts[0],
                    canUpgrade = false,
                    allowNonColonists = false
                });

            if (!HasCompAssignableTo<CompUseEffect_DestroySelf>(implantDef.comps))
            {
                implantDef.comps.Add(new CompProperties_UseEffectDestroySelf());
            }

            if (!HasCompAssignableTo<CompMechanoidMechanitorImplantMarker>(implantDef.comps))
            {
                implantDef.comps.Add(
                    new CompProperties_MechanoidMechanitorImplantMarker());
            }

            failureReason = null;
            return true;
        }

        private static bool HasCompAssignableTo<TComp>(List<CompProperties> comps)
            where TComp : ThingComp
        {
            Type targetType = typeof(TComp);
            for (int i = 0; i < comps.Count; i++)
            {
                Type? compClass = comps[i]?.compClass;
                if (compClass != null && targetType.IsAssignableFrom(compClass))
                {
                    return true;
                }
            }

            return false;
        }

        private static void LogSkippedRecipe(RecipeDef recipe, string reason)
        {
            if (!Prefs.DevMode)
            {
                return;
            }

            Log.Message($"{LogPrefix}跳过配方 {recipe.defName}：{reason}。");
        }
    }
}
using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(
        typeof(QualityUtility),
        nameof(QualityUtility.GenerateQualityCreatedByPawn),
        new Type[] { typeof(Pawn), typeof(SkillDef), typeof(bool) })]
    public static class ProductivityCoreQualityPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn pawn, ref QualityCategory __result)
        {
            int offset = ProductivityCoreUtility.GetEffectiveLevelForWorker(pawn);
            if (offset <= 0)
            {
                return;
            }

            __result = (QualityCategory)Mathf.Min(
                (int)__result + offset,
                (int)QualityCategory.Legendary);
        }
    }

    [HarmonyPatch(typeof(Bill), nameof(Bill.PawnAllowedToStartAnew))]
    public static class ProductivityCoreBillSkillRangePatch
    {
        [HarmonyPrefix]
        public static void Prefix(Bill __instance, Pawn p, out IntRange? __state)
        {
            __state = null;
            if (!ProductivityCoreUtility.HasActiveEffect(p))
            {
                return;
            }

            __state = __instance.allowedSkillRange;
            __instance.allowedSkillRange = new IntRange(int.MinValue, int.MaxValue);
        }

        [HarmonyPostfix]
        public static void Postfix(Bill __instance, IntRange? __state)
        {
            if (__state.HasValue)
            {
                __instance.allowedSkillRange = __state.Value;
            }
        }
    }

    [HarmonyPatch(typeof(SkillRequirement), nameof(SkillRequirement.PawnSatisfies))]
    public static class ProductivityCoreSkillRequirementPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn pawn, ref bool __result)
        {
            if (!ProductivityCoreUtility.HasActiveEffect(pawn))
            {
                return true;
            }

            __result = true;
            return false;
        }
    }

    [HarmonyPatch(
        typeof(GenConstruct),
        nameof(GenConstruct.CanConstruct),
        new Type[] { typeof(Thing), typeof(Pawn), typeof(bool), typeof(bool), typeof(JobDef) })]
    public static class ProductivityCoreConstructionSkillPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Pawn p, ref bool checkSkills)
        {
            if (ProductivityCoreUtility.HasActiveEffect(p))
            {
                checkSkills = false;
            }
        }
    }

    [HarmonyPatch(
        typeof(WorkGiver_GrowerSow),
        nameof(WorkGiver_GrowerSow.JobOnCell),
        new Type[] { typeof(Pawn), typeof(IntVec3), typeof(bool) })]
    public static class ProductivityCoreSowingSkillPatch
    {
        private static readonly FieldInfo? WantedPlantDefField =
            AccessTools.Field(typeof(WorkGiver_Grower), "wantedPlantDef");

        [HarmonyPrefix]
        public static void Prefix(Pawn pawn, out SowSkillState __state)
        {
            __state = default;
            if (!ProductivityCoreUtility.HasActiveEffect(pawn))
            {
                return;
            }

            ThingDef? plantDef = WantedPlantDefField?.GetValue(null) as ThingDef;
            if (plantDef?.plant == null)
            {
                return;
            }

            __state = new SowSkillState(plantDef.plant, plantDef.plant.sowMinSkill);
            plantDef.plant.sowMinSkill = 0;
        }

        [HarmonyPostfix]
        public static void Postfix(SowSkillState __state)
        {
            __state.Restore();
        }

        public readonly struct SowSkillState
        {
            private readonly PlantProperties? properties;
            private readonly int originalMinimum;

            public SowSkillState(PlantProperties properties, int originalMinimum)
            {
                this.properties = properties;
                this.originalMinimum = originalMinimum;
            }

            public void Restore()
            {
                if (properties != null)
                {
                    properties.sowMinSkill = originalMinimum;
                }
            }
        }
    }

    [HarmonyPatch(typeof(CompFoodPoisonable), nameof(CompFoodPoisonable.Notify_RecipeProduced))]
    public static class ProductivityCoreFoodPoisonPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn pawn)
        {
            return !ProductivityCoreUtility.HasActiveEffect(pawn);
        }
    }

    [HarmonyPatch(typeof(Recipe_Surgery), "CheckSurgeryFail")]
    public static class ProductivityCoreSurgeryFailurePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn surgeon, ref bool __result)
        {
            if (!ProductivityCoreUtility.HasActiveEffect(surgeon))
            {
                return true;
            }

            __result = false;
            return false;
        }
    }
}

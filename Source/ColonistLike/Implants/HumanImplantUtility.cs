using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 类人植入体安装核心逻辑：
    /// - 通过 CompHumanImplantUser 判断 Pawn 是否被授权（不依赖具体种族 defName）；
    /// - 调用原配方获取当前真正合法的具体部位（保留左右臂/左右眼等区分）；
    /// - 执行附着型安装或替换型安装；
    /// - 返还被替换掉的旧义体与附属植入物；
    /// - 每次只消耗一件物品。
    /// 不进入完整医疗手术流程，不计算医生能力、药品、成功率、心情、派系或意识形态事件。
    /// </summary>
    public static class HumanImplantUtility
    {
        /// <summary>
        /// 统一身份判断：仅依赖 CompHumanImplantUser 组件与功能启用状态。
        /// 不知道也不关心 Pawn 是“恋人”还是“月亮”，或任何具体 defName。
        /// </summary>
        public static bool CanUseHumanImplants(Pawn? pawn)
        {
            return HumanImplantFeatureState.EnabledForSession
                && pawn != null
                && !pawn.Dead
                && !pawn.Destroyed
                && pawn.health?.hediffSet != null
                && pawn.GetComp<CompHumanImplantUser>() != null;
        }

        /// <summary>
        /// 直接使用原配方的 GetPartsToApplyOn 获取当前合法的具体 BodyPartRecord 列表，
        /// 左臂/右臂、左眼/右眼等会被正确区分。
        /// </summary>
        public static List<BodyPartRecord> GetValidParts(
            Pawn pawn,
            RecipeDef recipe)
        {
            List<BodyPartRecord> result = new List<BodyPartRecord>();

            if (!CanUseHumanImplants(pawn)
                || recipe == null
                || recipe.Worker == null)
            {
                return result;
            }

            try
            {
                IEnumerable<BodyPartRecord> parts =
                    recipe.Worker.GetPartsToApplyOn(pawn, recipe);

                if (parts == null)
                {
                    return result;
                }

                foreach (BodyPartRecord part in parts)
                {
                    if (part != null && !result.Contains(part))
                    {
                        result.Add(part);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(
                    $"[MAP-机械族机械师] 类人植入体：配方 {recipe.defName} 获取可安装部位时发生异常：{ex}");
            }

            return result;
        }

        /// <summary>
        /// 统一安装入口。会再次验证授权组件、功能启用、物品、注册的配方、具体部位与配方类型，
        /// 只有全部通过才执行安装并消耗一件物品。
        /// </summary>
        public static bool TryInstall(
            Pawn pawn,
            Thing item,
            RecipeDef recipe,
            BodyPartRecord selectedPart,
            out string? failureReason)
        {
            failureReason = null;

            if (!CanUseHumanImplants(pawn))
            {
                failureReason = "MAP_MechanoidMechanitor.HumanImplant.InstallFailed".Translate();
                return false;
            }

            if (item == null || item.Destroyed || item.stackCount <= 0)
            {
                failureReason = "MAP_MechanoidMechanitor.HumanImplant.InstallFailed".Translate();
                return false;
            }

            if (!HumanImplantRecipeRegistrar.IsRegistered(item.def, recipe))
            {
                failureReason = "MAP_MechanoidMechanitor.HumanImplant.InvalidSelection".Translate();
                return false;
            }

            if (selectedPart == null
                || !pawn.RaceProps.body.AllParts.Contains(selectedPart))
            {
                failureReason = "MAP_MechanoidMechanitor.HumanImplant.InvalidSelection".Translate();
                return false;
            }

            List<BodyPartRecord> validParts = GetValidParts(pawn, recipe);
            if (!validParts.Contains(selectedPart))
            {
                failureReason = "MAP_MechanoidMechanitor.HumanImplant.InvalidSelection".Translate();
                return false;
            }

            if (recipe.addsHediff == null)
            {
                failureReason = "MAP_MechanoidMechanitor.HumanImplant.InstallFailed".Translate();
                return false;
            }

            if (HumanImplantRecipeRegistrar.IsReplacementRecipe(recipe))
            {
                return TryInstallReplacement(pawn, item, recipe, selectedPart, out failureReason);
            }

            if (HumanImplantRecipeRegistrar.IsAttachmentRecipe(recipe))
            {
                return TryInstallAttachment(pawn, item, recipe, selectedPart, out failureReason);
            }

            failureReason = "MAP_MechanoidMechanitor.HumanImplant.InstallFailed".Translate();
            return false;
        }

        private static bool TryInstallAttachment(
            Pawn pawn,
            Thing item,
            RecipeDef recipe,
            BodyPartRecord selectedPart,
            out string? failureReason)
        {
            Hediff installed =
                pawn.health.AddHediff(recipe.addsHediff, selectedPart);

            if (installed == null
                || installed.Part != selectedPart
                || !pawn.health.hediffSet.hediffs.Contains(installed))
            {
                failureReason = "MAP_MechanoidMechanitor.HumanImplant.InstallFailed".Translate();
                return false;
            }

            ConsumeOne(item);
            failureReason = null;
            return true;
        }

        private static bool TryInstallReplacement(
            Pawn pawn,
            Thing item,
            RecipeDef recipe,
            BodyPartRecord selectedPart,
            out string? failureReason)
        {
            Hediff oldDirectAddedPart =
                pawn.health.hediffSet.GetDirectlyAddedPartFor(selectedPart);

            List<ThingDef> returnedItems =
                CollectReturnedItems(pawn, selectedPart);

            pawn.health.RestorePart(selectedPart);

            Hediff installed =
                pawn.health.AddHediff(recipe.addsHediff, selectedPart);

            if (installed == null
                || installed.Part != selectedPart
                || !pawn.health.hediffSet.hediffs.Contains(installed))
            {
                SpawnReturnedItems(pawn, returnedItems);
                failureReason = "MAP_MechanoidMechanitor.HumanImplant.InstallFailed".Translate();
                return false;
            }

            SpawnReturnedItems(pawn, returnedItems);

            oldDirectAddedPart?.Notify_SurgicallyReplaced(pawn);

            ConsumeOne(item);

            failureReason = null;
            return true;
        }

        /// <summary>
        /// 收集目标部位及其所有下级部位中需要返还的植入物物品（HediffDef.spawnThingOnRemoved）。
        /// 不去重，因为同种植入物可能分别安装在不同下级部位。
        /// </summary>
        private static List<ThingDef> CollectReturnedItems(
            Pawn pawn,
            BodyPartRecord selectedPart)
        {
            HashSet<BodyPartRecord> affectedParts =
                new HashSet<BodyPartRecord>(
                    selectedPart.GetPartAndAllChildParts());

            List<ThingDef> result = new List<ThingDef>();

            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff hediff = hediffs[i];

                if (hediff.Part != null
                    && affectedParts.Contains(hediff.Part)
                    && hediff.def != null
                    && !hediff.def.keepOnBodyPartRestoration
                    && hediff.def.spawnThingOnRemoved != null)
                {
                    result.Add(hediff.def.spawnThingOnRemoved);
                }
            }

            return result;
        }

        private static void SpawnReturnedItems(
            Pawn pawn,
            List<ThingDef> returnedDefs)
        {
            if (pawn?.Map == null || returnedDefs.NullOrEmpty())
            {
                return;
            }

            for (int i = 0; i < returnedDefs.Count; i++)
            {
                ThingDef def = returnedDefs[i];
                if (def == null)
                {
                    continue;
                }

                Thing returnedThing = ThingMaker.MakeThing(def);
                GenPlace.TryPlaceThing(
                    returnedThing,
                    pawn.Position,
                    pawn.Map,
                    ThingPlaceMode.Near);
            }
        }

        /// <summary>
        /// 每次只消耗一件物品，避免一整堆植入体全部消失。
        /// </summary>
        private static void ConsumeOne(Thing item)
        {
            if (item == null || item.Destroyed || item.stackCount <= 0)
            {
                return;
            }

            Thing consumed = item.SplitOff(1);
            consumed.Destroy(DestroyMode.Vanish);
        }
    }
}

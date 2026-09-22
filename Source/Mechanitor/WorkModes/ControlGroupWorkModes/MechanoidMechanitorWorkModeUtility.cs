using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class MechanoidMechanitorWorkModeUtility
    {
        private sealed class MobileCombatMarker
        {
        }

        private static readonly ConditionalWeakTable<Pawn, MobileCombatMarker> mobileCombatPawns =
            new ConditionalWeakTable<Pawn, MobileCombatMarker>();

        private static readonly HashSet<string> mechanoidMechanitorWorkModeDefNames = new HashSet<string>
        {
            "MAP_WorkMode_EfficientExecution",
            "MAP_WorkMode_MobileCombat",
            "MAP_WorkMode_MobileCombat_Guard",
            "MAP_WorkMode_FortifiedDefense"
        };
        private static readonly HashSet<string> mechanoidMechanitorSelfOnlyWorkModeDefNames = new HashSet<string>
        {
            "MAP_WorkMode_AutonomousDirective"
        };
        private static readonly HashSet<string> mechanoidMechanitorWorkEquivalentModeDefNames = new HashSet<string>
        {
            "MAP_WorkMode_EfficientExecution",
            "MAP_WorkMode_MobileCombat",
            "MAP_WorkMode_FortifiedDefense"
        };
        private static readonly HashSet<string> mechanoidMechanitorEscortEquivalentModeDefNames = new HashSet<string>
        {
            "MAP_WorkMode_MobileCombat_Guard"
        };
        private static readonly HashSet<string> mechanoidMechanitorWorkModeHediffDefNames = new HashSet<string>
        {
            "MAP_MechanoidMechanitor_WorkMode_EfficientExecution",
            "MAP_MechanoidMechanitor_WorkMode_MobileCombat",
            "MAP_MechanoidMechanitor_WorkMode_FortifiedDefense"
        };
        private static HediffDef? efficientExecutionDef;
        private static HediffDef? mobileCombatDef;
        private static HediffDef? fortifiedDefenseDef;

        private static HediffDef? GetEfficientExecutionDef()
        {
            return efficientExecutionDef ??= DefDatabase<HediffDef>.GetNamedSilentFail(
                "MAP_MechanoidMechanitor_WorkMode_EfficientExecution");
        }

        private static HediffDef? GetMobileCombatDef()
        {
            return mobileCombatDef ??= DefDatabase<HediffDef>.GetNamedSilentFail(
                "MAP_MechanoidMechanitor_WorkMode_MobileCombat");
        }

        private static HediffDef? GetFortifiedDefenseDef()
        {
            return fortifiedDefenseDef ??= DefDatabase<HediffDef>.GetNamedSilentFail(
                "MAP_MechanoidMechanitor_WorkMode_FortifiedDefense");
        }

        public static void SetMobileCombatFlag(Pawn pawn, bool active)
        {
            if (pawn == null)
            {
                return;
            }

            mobileCombatPawns.Remove(pawn);
            if (active)
            {
                mobileCombatPawns.Add(pawn, new MobileCombatMarker());
            }
        }

        public static bool HasMobileCombatFlag(Pawn pawn)
        {
            return pawn != null && mobileCombatPawns.TryGetValue(pawn, out _);
        }

        public static bool IsMobileCombat(Pawn pawn)
        {
            return HasMobileCombatFlag(pawn);
        }

        public static void EnsureMobileCombatHediff(Pawn? pawn)
        {
            if (pawn?.health?.hediffSet == null || JusticePawnUtility.IsBossJustice(pawn))
            {
                return;
            }

            HediffDef? mobileDef = GetMobileCombatDef();
            if (mobileDef == null)
            {
                Log.ErrorOnce(
                    "[MAP] MAP_MechanoidMechanitor_WorkMode_MobileCombat HediffDef missing.",
                    87422031);
                return;
            }

            if (pawn.health.hediffSet.GetFirstHediffOfDef(mobileDef) == null)
            {
                pawn.health.AddHediff(mobileDef);
            }
        }

        /// <summary>
        /// 幂等同步机械族机械师工作模式 Hediff。
        /// 已经恰好拥有目标状态时完全不触碰健康系统；需要切换时先补目标，
        /// 确认目标存在且 Pawn 仍存活后，再移除错误状态与重复项。
        /// </summary>
        public static void ApplyWorkModeHediff(Pawn pawn, MechWorkModeDef workMode)
        {
            if (pawn?.health?.hediffSet?.hediffs == null
                || pawn.Dead
                || pawn.Destroyed
                || pawn.Discarded
                || pawn.health.isBeingKilled)
            {
                return;
            }

            HediffDef? efficientDef = GetEfficientExecutionDef();
            HediffDef? mobileDef = GetMobileCombatDef();
            HediffDef? fortifiedDef = GetFortifiedDefenseDef();
            HediffDef? targetDef = ResolveTargetHediffDef(
                workMode,
                efficientDef,
                mobileDef,
                fortifiedDef);

            if (workMode != null
                && IsMechanoidMechanitorWorkMode(workMode)
                && targetDef == null)
            {
                Log.ErrorOnce(
                    "[MAP-机械族机械师] 工作模式对应的 HediffDef 缺失，" +
                    $"已保留现有状态：workMode={workMode.defName}。",
                    unchecked(0x4D415057 + workMode.index));
                return;
            }

            List<Hediff> snapshot =
                new List<Hediff>(pawn.health.hediffSet.hediffs);
            Hediff? keeper = null;
            int customCount = 0;

            for (int i = 0; i < snapshot.Count; i++)
            {
                Hediff? hediff = snapshot[i];
                if (hediff?.def == null
                    || !IsMechanoidMechanitorWorkMode(hediff.def))
                {
                    continue;
                }

                customCount++;
                if (targetDef != null && hediff.def == targetDef && keeper == null)
                {
                    keeper = hediff;
                }
            }

            if (targetDef != null && keeper != null && customCount == 1)
            {
                return;
            }

            if (targetDef != null && keeper == null)
            {
                try
                {
                    pawn.health.AddHediff(targetDef);
                }
                catch (Exception ex)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 添加工作模式健康状态失败：" +
                        $"pawn={pawn.LabelShort}（{pawn.ThingID}），" +
                        $"hediff={targetDef.defName}：{ex}");
                    return;
                }

                if (pawn.Dead
                    || pawn.Destroyed
                    || pawn.Discarded
                    || pawn.health?.isBeingKilled == true)
                {
                    return;
                }

                keeper = pawn.health?.hediffSet?.GetFirstHediffOfDef(targetDef);
                if (keeper == null)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 添加工作模式健康状态后验证失败：" +
                        $"pawn={pawn.LabelShort}（{pawn.ThingID}），" +
                        $"hediff={targetDef.defName}。");
                    return;
                }
            }

            Pawn_HealthTracker? health = pawn.health;
            if (health?.hediffSet?.hediffs == null)
            {
                return;
            }

            List<Hediff>? current = health.hediffSet.hediffs;
            snapshot = new List<Hediff>(current);
            for (int i = snapshot.Count - 1; i >= 0; i--)
            {
                Hediff? hediff = snapshot[i];
                if (hediff?.def == null
                    || !IsMechanoidMechanitorWorkMode(hediff.def)
                    || ReferenceEquals(hediff, keeper))
                {
                    continue;
                }

                try
                {
                    health.RemoveHediff(hediff);
                }
                catch (Exception ex)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 移除错误或重复工作模式健康状态失败：" +
                        $"pawn={pawn.LabelShort}（{pawn.ThingID}），" +
                        $"hediff={hediff.def.defName}：{ex}");
                }

                if (pawn.Dead
                    || pawn.Destroyed
                    || pawn.Discarded
                    || pawn.health?.isBeingKilled == true)
                {
                    return;
                }
            }
        }

        private static HediffDef? ResolveTargetHediffDef(
            MechWorkModeDef? workMode,
            HediffDef? efficientDef,
            HediffDef? mobileDef,
            HediffDef? fortifiedDef)
        {
            if (workMode == null)
            {
                return null;
            }

            switch (workMode.defName)
            {
                case "MAP_WorkMode_EfficientExecution":
                    return efficientDef;
                case "MAP_WorkMode_MobileCombat":
                case "MAP_WorkMode_MobileCombat_Guard":
                    return mobileDef;
                case "MAP_WorkMode_FortifiedDefense":
                    return fortifiedDef;
                default:
                    return null;
            }
        }

        public static bool IsMechanoidMechanitorControlGroup(MechanitorControlGroup controlGroup)
        {
            Pawn? mechanitor = controlGroup?.Tracker?.Pawn;
            return mechanitor != null
                && MechanoidMechanitorCapabilityUtility.HasCapability(mechanitor, MechanoidMechanitorCapability.EnhancedControlModes);
        }

        public static bool IsMechanoidMechanitorWorkMode(MechWorkModeDef workMode)
        {
            return workMode != null
                && mechanoidMechanitorWorkModeDefNames.Contains(workMode.defName);
        }

        public static bool IsMechanoidMechanitorSelfOnlyWorkMode(MechWorkModeDef workMode)
        {
            return workMode != null
                && mechanoidMechanitorSelfOnlyWorkModeDefNames.Contains(workMode.defName);
        }

        public static bool SatisfiesVanillaWorkMode(
            MechWorkModeDef? actualMode,
            MechWorkModeDef? requestedMode)
        {
            if (actualMode == null || requestedMode == null)
            {
                return false;
            }

            if (actualMode == requestedMode)
            {
                return true;
            }

            if (requestedMode == MechWorkModeDefOf.Work)
            {
                return mechanoidMechanitorWorkEquivalentModeDefNames.Contains(actualMode.defName);
            }

            if (requestedMode == MechWorkModeDefOf.Escort)
            {
                return mechanoidMechanitorEscortEquivalentModeDefNames.Contains(actualMode.defName);
            }

            return false;
        }

        public static bool IsMechanoidMechanitorWorkMode(HediffDef def)
        {
            return def != null
                && mechanoidMechanitorWorkModeHediffDefNames.Contains(def.defName);
        }

        public static void SyncMechanitorWithPrimaryControlGroup(MechanitorControlGroup? controlGroup)
        {
            MechanitorControlGroup? group = controlGroup;
            if (group == null || !IsMechanoidMechanitorControlGroup(group) || group.Index != 1)
            {
                return;
            }

            Pawn? mechanitor = group.Tracker?.Pawn;
            if (mechanitor == null)
            {
                return;
            }

            MechWorkModeDef? workMode = group.WorkMode;
            if (workMode == null)
            {
                return;
            }

            ApplyWorkModeHediff(mechanitor, workMode);
        }
    }
}

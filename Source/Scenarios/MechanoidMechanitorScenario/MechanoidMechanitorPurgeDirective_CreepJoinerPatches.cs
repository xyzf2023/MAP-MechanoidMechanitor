using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// Anomaly Creepjoiner 人口限制：UI 禁用 Accept + QuestPart_SetFaction 最终保险。
    /// Creepjoiner 类型（ChoiceLetter_AcceptCreepJoiner / Pawn_CreepJoinerTracker）属于
    /// Anomaly DLC，编译期不可见，因此用字符串 HarmonyPatch + 反射访问成员。
    /// </summary>
    public static class MechanoidMechanitorPurgeDirective_CreepJoinerPatches
    {
        private static Pawn? GetLetterPawn(object letter)
        {
            if (letter == null)
            {
                return null;
            }

            FieldInfo? pawnField = letter.GetType().GetField(
                "pawn",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return pawnField?.GetValue(letter) as Pawn;
        }

        private static bool GetArchivedOnly(object letter)
        {
            if (letter == null)
            {
                return false;
            }

            // Letter.ArchivedOnly 在核心 Letter 上，用反射读取，失败视为未归档。
            PropertyInfo? prop = typeof(Letter).GetProperty(
                "ArchivedOnly",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            object? value = prop?.GetValue(letter);
            return value is bool archived && archived;
        }

        /// <summary>
        /// UI：只禁用活动信件中的第一个 Accept 选项，其余 Capture/Reject 等保持正常。
        /// 归档（ArchivedOnly）信件只显示 Close，不处理。
        /// </summary>
        [HarmonyPatch(
            "RimWorld.ChoiceLetter_AcceptCreepJoiner",
            "Choices",
            MethodType.Getter)]
        public static class
            MechanoidMechanitorPurgeDirective_ChoiceLetterAcceptCreepJoiner_Choices_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(
                object __instance,
                ref IEnumerable<DiaOption> __result)
            {
                if (!ModsConfig.AnomalyActive)
                {
                    return;
                }

                if (!MechanoidMechanitorPurgeDirectivePopulationPolicy
                        .RestrictionActive)
                {
                    return;
                }

                Pawn? pawn = GetLetterPawn(__instance);
                if (pawn == null || !pawn.Spawned)
                {
                    return;
                }

                if (GetArchivedOnly(__instance))
                {
                    return;
                }

                if (!MechanoidMechanitorPurgeDirectivePopulationPolicy
                        .WouldAddForbiddenFreeColonist(
                            pawn,
                            Faction.OfPlayerSilentFail))
                {
                    return;
                }

                __result = FilterChoices(__result);
            }

            private static IEnumerable<DiaOption> FilterChoices(
                IEnumerable<DiaOption> original)
            {
                bool first = true;
                foreach (DiaOption option in original)
                {
                    if (first)
                    {
                        first = false;
                        option.Disable(
                            MechanoidMechanitorPurgeDirectivePopulationPolicy
                                .BlockReason);
                        yield return option;
                        continue;
                    }

                    yield return option;
                }
            }
        }

        /// <summary>
        /// 最终保险：仅过滤 Creepjoiner Pawn，不影响其他 QuestPart_SetFaction 用法。
        /// 用与 QuestPart_JoinPlayer 相同的临时过滤列表方式。
        /// </summary>
        [HarmonyPatch(
            typeof(QuestPart_SetFaction),
            nameof(QuestPart_SetFaction.Notify_QuestSignalReceived))]
        public static class
            MechanoidMechanitorPurgeDirective_QuestPartSetFaction_Patch
        {
            public sealed class PatchState
            {
                public List<Thing>? OriginalThings;
            }

            private static readonly FieldInfo? CreepjoinerField =
                AccessTools.Field(typeof(Pawn), "creepjoiner");

            [HarmonyPrefix]
            public static void Prefix(
                QuestPart_SetFaction __instance,
                Signal signal,
                ref PatchState? __state)
            {
                __state = null;

                Faction? player = Faction.OfPlayerSilentFail;
                if (!MechanoidMechanitorPurgeDirectivePopulationPolicy
                        .RestrictionActive
                    || player == null
                    || signal.tag != __instance.inSignal
                    || __instance.faction != player)
                {
                    return;
                }

                List<Thing> original = __instance.things;

                List<Thing> filtered = new List<Thing>(original.Count);
                bool blockedAny = false;

                for (int i = 0; i < original.Count; i++)
                {
                    Thing thing = original[i];
                    if (thing is Pawn pawn
                        && CreepjoinerField != null
                        && CreepjoinerField.GetValue(pawn) != null
                        && MechanoidMechanitorPurgeDirectivePopulationPolicy
                            .WouldAddForbiddenFreeColonist(
                                pawn,
                                __instance.faction))
                    {
                        blockedAny = true;
                        continue;
                    }

                    filtered.Add(thing);
                }

                if (!blockedAny)
                {
                    return;
                }

                __state = new PatchState
                {
                    OriginalThings = original
                };

                __instance.things = filtered;
            }

            [HarmonyPostfix]
            public static void Postfix(
                QuestPart_SetFaction __instance,
                PatchState? __state)
            {
                if (__state?.OriginalThings != null)
                {
                    __instance.things = __state.OriginalThings;
                }
            }
        }
    }
}

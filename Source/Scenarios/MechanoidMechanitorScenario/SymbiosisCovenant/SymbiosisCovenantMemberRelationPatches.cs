using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public static class SymbiosisCovenantMemberRelationUtility
    {
        private const int AllianceLockedLevel = 5;
        private const int AllianceLockedGoodwill = 100;

        public static bool IsLevelFiveAllianceLocked(Faction? first, Faction? second)
        {
            if (!GameComponent_SymbiosisCovenantState.IsActive
                || first == null
                || second == null
                || first == second)
            {
                return false;
            }

            GameComponent_SymbiosisCovenantState? state =
                GameComponent_SymbiosisCovenantState.CurrentComponent;
            if (state == null || state.CovenantLevel != AllianceLockedLevel)
            {
                return false;
            }

            SymbiosisCovenantFactionRecord? firstRecord = state.GetRecord(first);
            SymbiosisCovenantFactionRecord? secondRecord = state.GetRecord(second);
            return firstRecord?.CovenantMember == true
                && secondRecord?.CovenantMember == true;
        }

        public static void EnsureCurrentMemberRelations()
        {
            if (!GameComponent_SymbiosisCovenantState.IsActive)
            {
                return;
            }

            GameComponent_SymbiosisCovenantState? state =
                GameComponent_SymbiosisCovenantState.CurrentComponent;
            if (state == null)
            {
                return;
            }

            IReadOnlyList<SymbiosisCovenantFactionRecord> records =
                state.GetRecordsSorted();
            List<Faction> members = new List<Faction>();
            for (int i = 0; i < records.Count; i++)
            {
                SymbiosisCovenantFactionRecord record = records[i];
                if (record.CovenantMember && record.Faction != null)
                {
                    members.Add(record.Faction);
                }
            }

            bool forceAlliance = state.CovenantLevel == AllianceLockedLevel;
            for (int i = 0; i < members.Count; i++)
            {
                for (int j = i + 1; j < members.Count; j++)
                {
                    EnsureRelationBetween(members[i], members[j], forceAlliance);
                }
            }
        }

        private static void EnsureRelationBetween(
            Faction first,
            Faction second,
            bool forceAlliance)
        {
            FactionRelation? firstRelation = first.RelationWith(second, allowNull: true);
            FactionRelation? secondRelation = second.RelationWith(first, allowNull: true);
            if (firstRelation == null || secondRelation == null)
            {
                first.TryMakeInitialRelationsWith(second);
                firstRelation = first.RelationWith(second, allowNull: true);
                secondRelation = second.RelationWith(first, allowNull: true);
                if (firstRelation == null || secondRelation == null)
                {
                    return;
                }
            }

            FactionRelationKind firstPrevious = firstRelation.kind;
            FactionRelationKind secondPrevious = secondRelation.kind;

            int targetGoodwill = forceAlliance
                ? AllianceLockedGoodwill
                : Math.Min(
                    100,
                    Math.Max(
                        0,
                        Math.Max(firstRelation.baseGoodwill, secondRelation.baseGoodwill)));

            FactionRelationKind targetKind;
            if (forceAlliance)
            {
                targetKind = FactionRelationKind.Ally;
            }
            else if (firstRelation.kind == FactionRelationKind.Ally
                || secondRelation.kind == FactionRelationKind.Ally)
            {
                // L1-L4 只保证最低中立，不主动拆散已经存在的盟友关系。
                targetKind = FactionRelationKind.Ally;
            }
            else
            {
                targetKind = FactionRelationKind.Neutral;
            }

            firstRelation.baseGoodwill = targetGoodwill;
            secondRelation.baseGoodwill = targetGoodwill;
            firstRelation.kind = targetKind;
            secondRelation.kind = targetKind;

            if (firstPrevious != targetKind)
            {
                first.Notify_RelationKindChanged(
                    second,
                    firstPrevious,
                    canSendLetter: false,
                    reason: null,
                    GlobalTargetInfo.Invalid,
                    out _);
            }

            if (secondPrevious != targetKind)
            {
                second.Notify_RelationKindChanged(
                    first,
                    secondPrevious,
                    canSendLetter: false,
                    reason: null,
                    GlobalTargetInfo.Invalid,
                    out _);
            }
        }
    }

    [HarmonyPatch(typeof(Faction), nameof(Faction.CanChangeGoodwillFor))]
    public static class SymbiosisCovenant_MemberGoodwillLock_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Faction __instance, Faction other, ref bool __result)
        {
            if (__result
                && SymbiosisCovenantMemberRelationUtility
                    .IsLevelFiveAllianceLocked(__instance, other))
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch(
        typeof(GameComponent_SymbiosisCovenantState),
        "EnsureNeutralAmongMembers")]
    public static class SymbiosisCovenant_LegacyNeutralReconcile_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            GameComponent_SymbiosisCovenantState? state =
                GameComponent_SymbiosisCovenantState.CurrentComponent;

            // L1-L4 继续使用原有“至少中立”逻辑；L5 由新的盟友策略一次性处理，
            // 避免同一次同步先触发 Hostile -> Neutral，再触发 Neutral -> Ally。
            return state == null || state.CovenantLevel != 5;
        }
    }

    [HarmonyPatch(
        typeof(GameComponent_SymbiosisCovenantState),
        "RecalculateCovenantLevel")]
    public static class SymbiosisCovenant_RecalculateMemberRelations_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            // 该方法在日常同步、团结度变化、成员加入/退出和DEV重算时都会触发。
            // 因此这里既能在等级变化时立即应用关系策略，也能作为2500 tick兜底校准。
            SymbiosisCovenantMemberRelationUtility.EnsureCurrentMemberRelations();
        }
    }
}

using UnityEngine;
using Verse;
using RimWorld;

namespace MAP_MechanoidMechanitor
{
    [DefOf]
    internal static class StartingPawnValueProtectionDefOf
    {
        public static HediffDef MAP_StartingPawnValueProtection = null!;

        static StartingPawnValueProtectionDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(StartingPawnValueProtectionDefOf));
        }
    }

    /// <summary>
    /// 价值保护的唯一规则入口。
    /// 不判断 Pawn 类型、剧本或 Hediff 来源：只要 Pawn 拥有指定 Hediff，就按当前读档快照计算有效倍率。
    /// </summary>
    public static class StartingPawnValueProtectionUtility
    {
        public static Hediff_StartingPawnValueProtection? GetProtectionHediff(Pawn? pawn)
        {
            if (pawn?.health?.hediffSet == null)
            {
                return null;
            }

            return pawn.health.hediffSet.GetFirstHediffOfDef(
                    StartingPawnValueProtectionDefOf.MAP_StartingPawnValueProtection)
                as Hediff_StartingPawnValueProtection;
        }

        public static bool HasProtection(Pawn? pawn) =>
            GetProtectionHediff(pawn) != null;

        public static bool TryAddProtection(Pawn? pawn)
        {
            if (pawn?.health == null || pawn.Destroyed || GetProtectionHediff(pawn) != null)
            {
                return false;
            }

            Hediff hediff = HediffMaker.MakeHediff(
                StartingPawnValueProtectionDefOf.MAP_StartingPawnValueProtection,
                pawn);
            hediff.Severity = 0f;
            pawn.health.AddHediff(hediff);
            return true;
        }

        public static float GetProgress(Pawn? pawn)
        {
            Hediff_StartingPawnValueProtection? hediff = GetProtectionHediff(pawn);
            return hediff == null ? 1f : Mathf.Clamp01(hediff.Severity);
        }

        public static float GetEffectiveFactor(Pawn? pawn)
        {
            Hediff_StartingPawnValueProtection? hediff = GetProtectionHediff(pawn);
            if (hediff == null)
            {
                return 1f;
            }

            GameComponent_StartingPawnValueProtectionRuntime? runtime =
                GameComponent_StartingPawnValueProtectionRuntime.Current;
            if (runtime == null || !runtime.ProtectionEnabled)
            {
                return 1f;
            }

            float progress = Mathf.Clamp01(hediff.Severity);
            float minimumFactor = Mathf.Clamp01(runtime.MinimumFactorSnapshot);
            return Mathf.Clamp01(
                minimumFactor + (1f - minimumFactor) * progress);
        }

        public static float ApplyCombatPowerFactor(float combatPower, Pawn pawn)
        {
            return combatPower * GetEffectiveFactor(pawn);
        }
    }
}

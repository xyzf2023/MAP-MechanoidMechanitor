using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// “机体同调”可见标记的唯一增删入口。它只是可见标记与属性快照的载体，
    /// 真正的实例数据保存在权威合体记录中，禁止动态修改全局 HediffDef。
    /// </summary>
    public static class MechFusionBodySynchronizationUtility
    {
        internal static void ApplyToWearer(MechFusionSession session)
        {
            Pawn? wearer = session.WearerPawn;
            HediffSet? hediffSet = wearer?.health?.hediffSet;
            if (wearer == null || hediffSet == null)
            {
                return;
            }

            HediffDef? def = GetHediffDef();
            if (def == null || hediffSet.HasHediff(def))
            {
                return;
            }

            Hediff hediff = HediffMaker.MakeHediff(def, wearer);
            wearer.health.AddHediff(hediff);
        }

        internal static void RemoveFromWearer(Pawn? wearer)
        {
            HediffSet? hediffSet = wearer?.health?.hediffSet;
            if (wearer == null || hediffSet == null || wearer.Dead)
            {
                return;
            }

            HediffDef? def = GetHediffDef();
            if (def == null)
            {
                return;
            }

            Hediff? hediff = hediffSet.GetFirstHediffOfDef(def);
            if (hediff != null)
            {
                wearer.health.RemoveHediff(hediff);
            }
        }

        internal static HediffDef? GetHediffDef()
        {
            return DefDatabase<HediffDef>.GetNamedSilentFail(
                MechFusionDefNames.BodySynchronizationHediffDefName);
        }
    }
}

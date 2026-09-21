using System.Collections.Generic;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class HediffExtraDescriptionProvider_SunBossStage : HediffExtraDescriptionProvider
    {
        public override IEnumerable<HediffExtraDescriptionEntry> GetEntries(Hediff hediff)
        {
            Pawn? pawn = hediff.pawn;
            if (pawn == null) yield break;
            CompSunBossState? state = pawn.GetComp<CompSunBossState>();
            if (state == null) yield break;
            SunBossStage stage = state.Stage;
            yield return new HediffExtraDescriptionEntry("MAP_SunBoss_StageDamage", (stage.DamageFactor * 100f).ToString("0"));
            if (!CompAnnihilationCannon.HasEmitter(pawn))
                yield return new HediffExtraDescriptionEntry("MAP_SunBoss_StageCannonDestroyed");
            else if (stage.CannonCooldown == 10 * 60)
                yield return new HediffExtraDescriptionEntry("MAP_SunBoss_StageCannonOverloaded");
            else if (stage.CannonCooldown == 15 * 60)
                yield return new HediffExtraDescriptionEntry("MAP_SunBoss_StageCannonHighPower");
            else
                yield return new HediffExtraDescriptionEntry("MAP_SunBoss_StageCannonEnabled");
            yield return new HediffExtraDescriptionEntry("MAP_SunBoss_StageHeatField", stage.HeatRadius);
        }
    }
}

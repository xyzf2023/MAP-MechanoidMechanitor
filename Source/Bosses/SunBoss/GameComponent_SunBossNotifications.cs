using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>存档级太阳 BOSS 通知记录，多地图和多个 BOSS 共用一次性标记。</summary>
    public sealed class GameComponent_SunBossNotifications : GameComponent
    {
        private bool heatWarningSent;

        public GameComponent_SunBossNotifications(Game game) { }

        public void NotifyHeatDamage(Pawn target)
        {
            if (heatWarningSent) return;

            // 先置位，防止同一轮多个受害者或通知回调重复触发。
            heatWarningSent = true;
            NamedArgument pawnName = target.LabelShort.Named("PAWN");
            Find.LetterStack.ReceiveLetter(
                "MAP_SunBoss_HeatWarningLabel".Translate(pawnName),
                "MAP_SunBoss_HeatWarningText".Translate(pawnName),
                LetterDefOf.ThreatBig, target);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref heatWarningSent, "sunBossHeatWarningSent", false);
        }
    }
}

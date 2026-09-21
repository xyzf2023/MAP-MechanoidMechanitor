using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>存档级太阳 BOSS 通知记录，多地图和多个 BOSS 共用一次性标记。</summary>
    public sealed class GameComponent_SunBossNotifications : GameComponent
    {
        private bool heatWarningSent;
        private bool shieldWarningSent;
        private bool cannonWarningSent;

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

        public void NotifyShieldAbsorbed(Pawn boss)
        {
            if (shieldWarningSent) return;

            // 先置位，防止一次攻击的多部位结算或信件回调重复触发。
            shieldWarningSent = true;
            Find.LetterStack.ReceiveLetter(
                "MAP_SunBoss_ShieldWarningLabel".Translate(),
                "MAP_SunBoss_ShieldWarningText".Translate(),
                LetterDefOf.NegativeEvent, boss);
        }

        public void NotifyCannonCharging(Pawn boss)
        {
            if (cannonWarningSent) return;

            // 在首个蓄力 Toil 真正开始时置位，中断后重试不重复发信。
            cannonWarningSent = true;
            Find.LetterStack.ReceiveLetter(
                "MAP_SunBoss_CannonWarningLabel".Translate(),
                "MAP_SunBoss_CannonWarningText".Translate(),
                LetterDefOf.ThreatBig, boss);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref heatWarningSent, "sunBossHeatWarningSent", false);
            Scribe_Values.Look(ref shieldWarningSent, "sunBossShieldWarningSent", false);
            Scribe_Values.Look(ref cannonWarningSent, "sunBossCannonWarningSent", false);
        }
    }
}

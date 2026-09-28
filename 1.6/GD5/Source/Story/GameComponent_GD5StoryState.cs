using Verse;

namespace MAP_MechanoidMechanitor.GD5
{
    public sealed partial class GameComponent_GD5StoryState : GameComponent
    {
        // 表示两项前置在联动中已经满足，不创建虚假的 Quest 实例。
        public bool firstContactCompleted;

        public GameComponent_GD5StoryState(Game game) { }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref firstContactCompleted, "gd5FirstContactCompleted", false);
            ExposeEndingData();
        }

        public override void GameComponentTick() => TickEnding();

        public override void GameComponentUpdate()
        {
            UpdateSkipPresentation();
            UpdateEnding();
        }

        internal static GameComponent_GD5StoryState? Current =>
            Verse.Current.Game?.GetComponent<GameComponent_GD5StoryState>();
    }
}

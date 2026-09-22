using LudeonTK;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>仅预览动画与压暗，不创建爆炸、破坏地形或生成永久弹坑。</summary>
    public sealed class AnnihilationHitPreview : Thing
    {
        private int startedTick = -1;
        private AnnihilationSettings settings = null!;
        private bool visualFailed;

        internal void Initialize(AnnihilationSettings snapshot) => settings = snapshot;

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            settings ??= AnnihilationHitDebugActions.CreatePreviewSettings();
            if (startedTick < 0) startedTick = Find.TickManager.TicksGame;
            // 地图条件自行存档；读档不重新启动或延长压暗。
            if (!respawningAfterLoad)
                AnnihilationHitEffect.StartBlackout(map, settings.VisualDurationTicks);
        }

        protected override void Tick()
        {
            if (settings == null || Find.TickManager.TicksGame - startedTick >= settings.VisualDurationTicks)
                Destroy(DestroyMode.Vanish);
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            if (settings == null || startedTick < 0) return;
            AnnihilationHitEffect.Draw(Position.ToVector3Shifted(), thingIDNumber, settings,
                Find.TickManager.TicksGame - startedTick, ref visualFailed);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref startedTick, "annihilationPreviewStartedTick", -1);
            Scribe_Deep.Look(ref settings, "settings");
        }
    }

    internal static class AnnihilationHitDebugActions
    {
        internal static AnnihilationSettings CreatePreviewSettings()
        {
            // 读取当前玩家太阳 Def，使预览遵循 XML 时长、伤害强度和特效半径。
            CompProperties_AnnihilationCannon props = DefDatabase<ThingDef>.GetNamed("MAP_Mech_Sun")
                .GetCompProperties<CompProperties_AnnihilationCannon>();
            return new AnnihilationSettings(props);
        }

        [DebugAction("MAP-机械族机械师", "湮灭炮命中动画：点击地图预览（无伤害）",
            false, false, false, false, false, 0, false,
            actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.Playing)]
        private static void PreviewAtMouse()
        {
            Map? map = Find.CurrentMap;
            IntVec3 cell = UI.MouseCell();
            if (map == null || !cell.InBounds(map)) return;
            AnnihilationHitPreview preview = (AnnihilationHitPreview)ThingMaker.MakeThing(
                AnnihilationCannonDefOf.MAP_AnnihilationHitPreview);
            preview.Initialize(CreatePreviewSettings());
            GenSpawn.Spawn(preview, cell, map);
        }
    }
}

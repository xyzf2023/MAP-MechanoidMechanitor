using UnityEngine;
using Verse;
using RimWorld;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 单次游戏运行中的价值保护设置快照。
    /// 不参与存档序列化：每次新开局和每次读档都重新读取当前 MOD 设置。
    /// </summary>
    public sealed class GameComponent_StartingPawnValueProtectionRuntime : GameComponent
    {
        public int DurationDaysSnapshot { get; private set; }
        public float MinimumFactorSnapshot { get; private set; }
        public float ProgressPerTick { get; private set; }

        public bool ProtectionEnabled => DurationDaysSnapshot > 0;

        public GameComponent_StartingPawnValueProtectionRuntime(Game game)
        {
            RefreshFromSettings();
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            RefreshFromSettings();
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            RefreshFromSettings();
        }

        public void RefreshFromSettings()
        {
            StartingPawnValueProtectionSettings.Normalize();

            DurationDaysSnapshot = StartingPawnValueProtectionSettings.durationDays;
            MinimumFactorSnapshot = Mathf.Clamp01(
                StartingPawnValueProtectionSettings.minimumFactorPercent / 100f);

            ProgressPerTick = DurationDaysSnapshot > 0
                ? 1f / (DurationDaysSnapshot * (float)GenDate.TicksPerDay)
                : 0f;
        }

        public static GameComponent_StartingPawnValueProtectionRuntime? Current =>
            CurrentGameComponentCache<GameComponent_StartingPawnValueProtectionRuntime>.Get();
    }
}

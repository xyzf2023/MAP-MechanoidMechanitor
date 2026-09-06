using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 轨道数据网络的持久化状态。
    ///
    /// 只保存两类数据：
    /// 1. 轨道设施中已备份的各技能历史最高基础等级（只增不减）；
    /// 2. 上一次成功投放备用机体的游戏 tick（-1 表示从未投放）。
    ///
    /// 剩余冷却时间不单独保存，始终由“上次投放 tick + 冷却长度 - 当前 tick”实时计算，
    /// 读档后不会重置。
    /// </summary>
    public sealed class GameComponent_OrbitalDataNetworkState : GameComponent
    {
        /// <summary>备用机体投放冷却：72 游戏小时 = 3 游戏日。</summary>
        public const int DeploymentCooldownTicks = 180000;

        /// <summary>一游戏小时的 tick 数。与原版“生成流浪者”等待时间的换算一致。</summary>
        public const int TicksPerGameHour = 2500;

        /// <summary>低频安全检查间隔：仅用于补漏，不代替事件驱动。</summary>
        private const int BackupLetterSafetyCheckIntervalTicks = 250;

        private const int MinBackupLevel = 0;
        private const int MaxBackupLevel = 20;

        private Dictionary<SkillDef, int> backedUpSkillLevels =
            new Dictionary<SkillDef, int>();

        private int lastBackupDeploymentTick = -1;
        private int nextBackupLetterSafetyCheckTick;

        public GameComponent_OrbitalDataNetworkState(Game game)
        {
        }

        public static GameComponent_OrbitalDataNetworkState? CurrentState
        {
            get
            {
                if (Current.Game == null)
                {
                    return null;
                }

                return Current.Game
                    .GetComponent<GameComponent_OrbitalDataNetworkState>();
            }
        }

        /// <summary>上一次成功投放备用机体的 tick；-1 表示从未投放。</summary>
        public static int LastDeploymentTick =>
            CurrentState?.lastBackupDeploymentTick ?? -1;

        /// <summary>剩余冷却小时数（向上取整）。组件不可用时返回 0。</summary>
        public static int RemainingCooldownHours =>
            CurrentState?.GetRemainingCooldownHours() ?? 0;

        public int LastBackupDeploymentTick => lastBackupDeploymentTick;

        /// <summary>记录一次成功投放。只有空投仓真正生成成功后才应调用。</summary>
        public static bool TryRecordDeployment(int tick)
        {
            GameComponent_OrbitalDataNetworkState? state = CurrentState;
            if (state == null)
            {
                return false;
            }

            state.MarkDeployment(tick);
            return true;
        }

        public int BackupEntryCount => backedUpSkillLevels?.Count ?? 0;

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            backedUpSkillLevels ??= new Dictionary<SkillDef, int>();
            backedUpSkillLevels.Clear();
            lastBackupDeploymentTick = -1;
            nextBackupLetterSafetyCheckTick = 0;
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            nextBackupLetterSafetyCheckTick = 0;
        }

        /// <summary>
        /// 低频安全检查：只用于修复信件被异常移除、外部 MOD 改变游戏结束状态等情况。
        /// 死亡与科研完成等已知事件由各自入口驱动，不依赖本检查。
        /// </summary>
        public override void GameComponentTick()
        {
            base.GameComponentTick();

            TickManager? tickManager = Find.TickManager;
            if (tickManager == null)
            {
                return;
            }

            int now = tickManager.TicksGame;
            if (now < nextBackupLetterSafetyCheckTick)
            {
                return;
            }

            nextBackupLetterSafetyCheckTick =
                now + BackupLetterSafetyCheckIntervalTicks;

            if (!OrbitalBackupGameEndUtility.IsActive)
            {
                return;
            }

            if (Current.ProgramState != ProgramState.Playing
                || LongEventHandler.AnyEventNowOrWaiting)
            {
                return;
            }

            OrbitalBackupGameEndUtility.Refresh();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(
                ref backedUpSkillLevels,
                "backedUpSkillLevels",
                LookMode.Def,
                LookMode.Value);
            Scribe_Values.Look(
                ref lastBackupDeploymentTick,
                "lastBackupDeploymentTick",
                -1);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                backedUpSkillLevels = SanitizeBackup(backedUpSkillLevels);
                if (lastBackupDeploymentTick < -1)
                {
                    lastBackupDeploymentTick = -1;
                }

                nextBackupLetterSafetyCheckTick = 0;
            }
        }

        /// <summary>记录一次成功投放。只有空投仓真正生成成功后才应调用。</summary>
        public void MarkDeployment(int tick)
        {
            lastBackupDeploymentTick = tick;
        }

        /// <summary>当前剩余冷却 tick；从未投放或已冷却完毕时返回 0。</summary>
        public int GetRemainingCooldownTicks()
        {
            if (lastBackupDeploymentTick < 0)
            {
                return 0;
            }

            TickManager? tickManager = Find.TickManager;
            if (tickManager == null)
            {
                return 0;
            }

            int remaining =
                DeploymentCooldownTicks - (tickManager.TicksGame - lastBackupDeploymentTick);
            return remaining > 0 ? remaining : 0;
        }

        /// <summary>
        /// 剩余冷却小时数。参考原版“生成流浪者”的换算方式，向上取整。
        /// </summary>
        public int GetRemainingCooldownHours()
        {
            int remainingTicks = GetRemainingCooldownTicks();
            if (remainingTicks <= 0)
            {
                return 0;
            }

            return (int)Math.Ceiling(remainingTicks / (float)TicksPerGameHour);
        }

        public bool TryGetBackedUpLevel(SkillDef? skill, out int level)
        {
            level = 0;
            if (skill == null || backedUpSkillLevels == null)
            {
                return false;
            }

            return backedUpSkillLevels.TryGetValue(skill, out level);
        }

        /// <summary>
        /// 合并技能等级到轨道备份。只增不减，等级限制在 0～20。
        /// </summary>
        public void MergeSkillLevel(SkillDef? skill, int level)
        {
            if (skill == null)
            {
                return;
            }

            backedUpSkillLevels ??= new Dictionary<SkillDef, int>();

            int clamped = Mathf.Clamp(level, MinBackupLevel, MaxBackupLevel);
            if (backedUpSkillLevels.TryGetValue(skill, out int existing))
            {
                if (clamped > existing)
                {
                    backedUpSkillLevels[skill] = clamped;
                }

                return;
            }

            backedUpSkillLevels[skill] = clamped;
        }

        public Dictionary<SkillDef, int> GetBackupSnapshot()
        {
            Dictionary<SkillDef, int> result = new Dictionary<SkillDef, int>();
            if (backedUpSkillLevels == null)
            {
                return result;
            }

            foreach (KeyValuePair<SkillDef, int> pair in backedUpSkillLevels)
            {
                if (pair.Key != null)
                {
                    result[pair.Key] = pair.Value;
                }
            }

            return result;
        }

        /// <summary>
        /// 读档后的局部清理：丢弃空 SkillDef、把等级限制到 0～20、合并重复项。
        /// 单个无效条目不会导致整个组件失效；旧存档缺少本字段时使用安全默认值。
        /// </summary>
        private static Dictionary<SkillDef, int> SanitizeBackup(
            Dictionary<SkillDef, int>? raw)
        {
            Dictionary<SkillDef, int> result = new Dictionary<SkillDef, int>();
            if (raw == null)
            {
                return result;
            }

            List<KeyValuePair<SkillDef, int>> entries;
            try
            {
                entries = new List<KeyValuePair<SkillDef, int>>(raw);
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] 轨道技能备份读取失败，已使用空备份继续游戏：" + ex);
                return result;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                KeyValuePair<SkillDef, int> entry = entries[i];
                SkillDef? def = entry.Key;
                if (def == null)
                {
                    continue;
                }

                int level = Mathf.Clamp(entry.Value, MinBackupLevel, MaxBackupLevel);
                if (result.TryGetValue(def, out int existing))
                {
                    if (level > existing)
                    {
                        result[def] = level;
                    }

                    continue;
                }

                result[def] = level;
            }

            return result;
        }
    }
}

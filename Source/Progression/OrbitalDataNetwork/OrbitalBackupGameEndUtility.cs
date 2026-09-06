using System;
using System.Collections.Generic;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 轨道数据网络的游戏结束流程协调。
    ///
    /// 三组最终规则：
    /// 1. 非机械族机械师剧本：完全交还原版。
    /// 2. 机械族机械师剧本但未研究轨道数据网络：保持原逻辑（唯一载体存活即阻止结束）。
    /// 3. 机械族机械师剧本且已研究：以注册表判定是否还有存活机械族机械师，
    ///    全部损毁后使用自定义备用机体信件替代原版游戏结束信件。
    /// </summary>
    public static class OrbitalBackupGameEndUtility
    {
        public static bool IsActive =>
            MechanoidMechanitorScenarioUtility.IsScenarioActive
            && ResearchFeatureUnlockUtility.IsOrbitalDataNetworkUnlocked();

        /// <summary>
        /// 是否还有任意存活且初始化完成的注册机械族机械师。
        /// 位于地图、远行队、运输仓或其他正常离图状态的存活注册机械族机械师同样被认可。
        /// </summary>
        public static bool AnyLivingRegisteredMechanitor()
        {
            IReadOnlyList<Pawn> registered =
                GameComponent_MechanoidMechanitorRegistry.CurrentRegisteredMechanitors;

            for (int i = 0; i < registered.Count; i++)
            {
                if (IsLivingMechanitor(registered[i]))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsLivingMechanitor(Pawn? pawn)
        {
            return pawn != null
                && !pawn.Dead
                && !pawn.Destroyed
                && !pawn.Discarded
                && pawn.health != null
                && !pawn.health.Dead;
        }

        /// <summary>
        /// 拦截 GameEnder.CheckOrUpdateGameOver。
        /// 仍有存活机械师时直接取消错误的游戏结束状态；
        /// 全部损毁时清理原版信件、确保自定义信件，并把“是否结束”的判断交还原版，
        /// 以便还有人类殖民者存活时不会被强行标记为结束。
        /// </summary>
        public static void HandleCheckOrUpdateGameOver(GameEnder gameEnder)
        {
            RemoveVanillaGameEndedLetters();

            if (AnyLivingRegisteredMechanitor())
            {
                RemoveBackupLetter();
                gameEnder.gameEnding = false;
                return;
            }

            EnsureBackupLetter();
        }

        /// <summary>
        /// 拦截 GameEnder.GameEndTick：阻止原版“所有人都死了”信件，
        /// 以及等待 20000 tick 后替换为“生成流浪者”的信件。
        /// </summary>
        public static void HandleGameEndTick(GameEnder gameEnder)
        {
            RemoveVanillaGameEndedLetters();

            if (AnyLivingRegisteredMechanitor())
            {
                RemoveBackupLetter();
                gameEnder.gameEnding = false;
                return;
            }

            EnsureBackupLetter();
        }

        /// <summary>
        /// 统一状态刷新：科研完成、读档、死亡、复活等已知事件，以及低频补漏检查都走这里。
        /// 返回 false 表示本轮刷新失败，交由科研管理器的分级退避重试。
        /// </summary>
        public static bool Refresh()
        {
            try
            {
                if (!IsActive)
                {
                    return true;
                }

                RemoveVanillaGameEndedLetters();

                if (AnyLivingRegisteredMechanitor())
                {
                    RemoveBackupLetter();
                    GameEnder? gameEnder = Find.GameEnder;
                    if (gameEnder != null)
                    {
                        gameEnder.gameEnding = false;
                    }

                    return true;
                }

                EnsureBackupLetter();
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] 备用机体信件状态刷新失败：" + ex);
                return false;
            }
        }

        /// <summary>确保存在且只存在一封未归档的备用机体信件。</summary>
        public static void EnsureBackupLetter()
        {
            if (Current.Game == null || Find.LetterStack == null)
            {
                return;
            }

            if (Current.ProgramState != ProgramState.Playing
                || LongEventHandler.AnyEventNowOrWaiting)
            {
                return;
            }

            if (HasBackupLetter())
            {
                return;
            }

            LetterDef? def = OrbitalBackupJusticeUtility.BackupLetterDef;
            if (def == null)
            {
                Log.ErrorOnce(
                    "[MAP-机械族机械师] 缺少 LetterDef " +
                    OrbitalBackupJusticeUtility.BackupLetterDefName +
                    "，无法发送备用机体信件。",
                    0x4D415042);
                return;
            }

            try
            {
                ChoiceLetter? letter = LetterMaker.MakeLetter(
                    ChoiceLetter_OrbitalBackupReady.TitleKey.Translate(),
                    ChoiceLetter_OrbitalBackupReady.TextKey.Translate(),
                    def);
                if (letter == null)
                {
                    return;
                }

                Find.LetterStack.ReceiveLetter(letter);
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] 发送备用机体信件失败：" + ex);
            }
        }

        /// <summary>移除未归档的备用机体信件；不影响其他 MOD 或其他事件的普通信件。</summary>
        public static void RemoveBackupLetter()
        {
            RemoveLettersOfDef(OrbitalBackupJusticeUtility.BackupLetterDef);
        }

        /// <summary>清理功能切换过程中可能已经存在的原版游戏结束信件。</summary>
        public static void RemoveVanillaGameEndedLetters()
        {
            RemoveLettersOfDef(LetterDefOf.GameEnded);
        }

        public static bool HasBackupLetter()
        {
            LetterDef? def = OrbitalBackupJusticeUtility.BackupLetterDef;
            if (def == null || Current.Game == null || Find.LetterStack == null)
            {
                return false;
            }

            List<Letter> letters = Find.LetterStack.LettersListForReading;
            for (int i = 0; i < letters.Count; i++)
            {
                Letter? letter = letters[i];
                if (letter != null && ReferenceEquals(letter.def, def))
                {
                    return true;
                }
            }

            return false;
        }

        private static void RemoveLettersOfDef(LetterDef? def)
        {
            if (def == null || Current.Game == null || Find.LetterStack == null)
            {
                return;
            }

            List<Letter> letters = Find.LetterStack.LettersListForReading;
            for (int i = letters.Count - 1; i >= 0; i--)
            {
                Letter? letter = letters[i];
                if (letter != null && ReferenceEquals(letter.def, def))
                {
                    Find.LetterStack.RemoveLetter(letter);
                }
            }
        }
    }
}

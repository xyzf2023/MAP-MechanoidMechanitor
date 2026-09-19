using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.GlitterworldDestroyer5
{
    /// <summary>
    /// GD5 科研兼容的恢复入口。接管资格沿用现有持久状态，支持信件独立保存待发记录。
    /// </summary>
    public sealed class GameComponent_GD5ResearchSupport : GameComponent
    {
        private const int RetryIntervalTicks = 2500;
        private HashSet<string> pendingLetterResearchIds = new HashSet<string>();
        private readonly HashSet<string> attemptedLetterResearchIds = new HashSet<string>();

        public GameComponent_GD5ResearchSupport(Game game)
        {
        }

        internal static void QueueSupportLetter(ResearchProjectDef research)
        {
            GameComponent_GD5ResearchSupport? component =
                CurrentGameComponentCache<GameComponent_GD5ResearchSupport>.Get();
            if (component == null)
            {
                Log.Error("[MAP-机械族机械师] GD5 科研已完成，但缺少支持信件状态组件：" + research.defName);
                return;
            }

            component.pendingLetterResearchIds.Add(research.defName);
            component.TrySendPendingLetters();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref pendingLetterResearchIds, "pendingLetterResearchIds", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                pendingLetterResearchIds ??= new HashSet<string>();
                pendingLetterResearchIds.RemoveWhere(string.IsNullOrEmpty);
            }
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            attemptedLetterResearchIds.Clear();
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            TickManager? tickManager = Find.TickManager;
            if (tickManager == null
                || tickManager.TicksGame % RetryIntervalTicks != 0
                || Current.ProgramState != ProgramState.Playing
                || LongEventHandler.AnyEventNowOrWaiting)
            {
                return;
            }

            // 目标 Def 仅在兼容模块成功安装后配置；未启用 GD5 时该入口不完成任何科研。
            CerebrexTakeoverResearchCompatibilityPatch.EnsureTakeoverResearchCompleted();
            TrySendPendingLetters();
        }

        private void TrySendPendingLetters()
        {
            if (pendingLetterResearchIds.Count == 0
                || Current.ProgramState != ProgramState.Playing
                || LongEventHandler.AnyEventNowOrWaiting
                || Find.ResearchManager == null
                || Find.LetterStack == null)
            {
                return;
            }

            List<string> snapshot = new List<string>(pendingLetterResearchIds);
            for (int i = 0; i < snapshot.Count; i++)
            {
                string id = snapshot[i];
                ResearchProjectDef? research = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(id);
                if (research == null || !research.IsFinished)
                {
                    pendingLetterResearchIds.Remove(id);
                    continue;
                }

                if (!attemptedLetterResearchIds.Add(id))
                {
                    continue;
                }

                try
                {
                    GlitterworldDestroyer5ResearchSupportUtility.SendResearchSupportLetter(research);
                    pendingLetterResearchIds.Remove(id);
                }
                catch (Exception ex)
                {
                    // 沿用统一科研信件策略：当前会话只尝试一次，失败记录留待读档后再试。
                    Log.Warning(
                        "[MAP-机械族机械师] GD5 科研支持信件发送失败，已保留记录供下次读档重试："
                        + id + "。\n" + ex);
                }
            }
        }
    }
}

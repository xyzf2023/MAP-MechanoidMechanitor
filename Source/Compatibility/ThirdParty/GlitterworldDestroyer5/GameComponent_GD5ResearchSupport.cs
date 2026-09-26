using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.GlitterworldDestroyer5
{
    /// <summary>
    /// GD5 科研的存档兼容边界：保留原程序集、类型名和字段；业务恢复由条件程序集注册。
    /// 主 DLL 不引用 GD3 或 GD5 联动程序集，未加载联动时不执行科研补做。
    /// </summary>
    public sealed class GameComponent_GD5ResearchSupport : GameComponent
    {
        private const int RetryIntervalTicks = 2500;
        private static Action? takeoverRecovery;
        private HashSet<string> pendingLetterResearchIds = new HashSet<string>();
        private readonly HashSet<string> attemptedLetterResearchIds = new HashSet<string>();

        public GameComponent_GD5ResearchSupport(Game game)
        {
        }

        /// <summary>仅由成功安装的条件兼容模块注册；回调不得捕获 Game、Pawn 或组件实例。</summary>
        public static void RegisterTakeoverRecovery(Action recovery)
        {
            takeoverRecovery = recovery ?? throw new ArgumentNullException(nameof(recovery));
        }

        public static void QueueSupportLetter(ResearchProjectDef research)
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

            // 使用当前游戏的权威接管状态；不在主 DLL 中静态依赖条件程序集。
            takeoverRecovery?.Invoke();
            TrySendPendingLetters();
        }

        private static void SendResearchSupportLetter(ResearchProjectDef research)
        {
            TaggedString title = "MAP_GD5.PurgeResearchSupport.Title".Translate();
            TaggedString text = "MAP_GD5.PurgeResearchSupport.Text".Translate(research.LabelCap);
            Find.LetterStack.ReceiveLetter(title, text, LetterDefOf.PositiveEvent);
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
                    SendResearchSupportLetter(research);
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

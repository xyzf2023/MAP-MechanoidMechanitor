using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 合体源的世界暂存入口。保留原版保存、计时和通知，仅阻止此次暂存的
    /// Notify_PassedToWorld 自动改派阵营，避免 SetFaction 连带清理机械师关系。
    /// </summary>
    internal static class MechFusionWorldPawnStorage
    {
        [ThreadStatic] private static Pawn? storingSource;

        internal static bool IsStoring(Pawn pawn) => ReferenceEquals(storingSource, pawn);

        internal static void EnsureStored(MechFusionSession session, Pawn source)
        {
            if (!ReferenceEquals(session.SourcePawn, source) || source.Spawned)
                throw new InvalidOperationException("合体暂存需要会话中的已离图源 Pawn。");
            if (source.Destroyed || source.Discarded)
                return;

            bool alreadyStored = Find.WorldPawns.Contains(source);
            // 补丁结构不匹配时在转入世界前失败，由现有事务恢复，不能冒险改派阵营。
            if (!alreadyStored && !MechFusionWorldPawnFactionPatch.Installed)
                throw new InvalidOperationException("合体世界暂存阵营保护未安装，已取消转入世界。");

            RestoreStoredFaction(session, source);
            if (alreadyStored)
                return;

            Pawn? previous = storingSource;
            storingSource = source;
            try
            {
                Find.WorldPawns.PassToWorld(source, PawnDiscardDecideMode.KeepForever);
            }
            finally
            {
                // 先恢复线程上下文，即使后续图形刷新或第三方回调异常也不泄漏保护范围。
                storingSource = previous;
                if (!source.Destroyed && !source.Discarded)
                    RestoreStoredFaction(session, source);
            }
        }

        private static void RestoreStoredFaction(MechFusionSession session, Pawn source)
        {
            if (ReferenceEquals(source.Faction, session.OriginalSourceFaction))
                return;

            // 这里只维护暂存身份，不恢复监管者、不重新接管下属，也不重新触发 SetFaction。
            source.SetFactionDirect(session.OriginalSourceFaction);
            source.Drawer?.renderer?.SetAllGraphicsDirty();
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Notify_PassedToWorld))]
    internal static class MechFusionWorldPawnFactionPatch
    {
        internal static bool Installed { get; private set; }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            // 本机 1.6 IL 使用 Thing.SetFaction 虚调用，运行时才分派到 Pawn 的重写。
            MethodInfo target = AccessTools.Method(typeof(Thing), nameof(Thing.SetFaction),
                new[] { typeof(Faction), typeof(Pawn) });
            MethodInfo replacement = AccessTools.Method(typeof(MechFusionWorldPawnFactionPatch),
                nameof(SetFactionUnlessFusionStorage));
            int count = 0;
            foreach (CodeInstruction code in codes)
                if (code.Calls(target))
                    count++;

            // 当前 1.6：额外母阵营、随机阵营、已败阵营和空阵营共四个调用。
            // 完整匹配后才替换；其他通知及组件回调保留原样。
            Installed = count == 4;
            if (!Installed)
            {
                Log.Error("[MAP-机械族机械师] 合体暂存阵营保护目标不匹配："
                    + $"预期 4 个 SetFaction 调用，实际 {count}；已禁用合体世界暂存。");
                return codes;
            }

            foreach (CodeInstruction code in codes)
            {
                if (!code.Calls(target))
                    continue;
                code.opcode = OpCodes.Call;
                code.operand = replacement;
            }
            return codes;
        }

        private static void SetFactionUnlessFusionStorage(Thing thing, Faction faction, Pawn recruiter)
        {
            if (thing is Pawn pawn && MechFusionWorldPawnStorage.IsStoring(pawn))
                return;
            thing.SetFaction(faction, recruiter);
        }
    }
}

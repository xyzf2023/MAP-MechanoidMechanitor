using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.GlitterworldDestroyer5ExpansionMechFusion
{
    /// <summary>
    /// MechFusion 兼容补丁：仅在扩展 MOD 已加载、且本 MOD 的动态补丁被安装后才会执行。
    /// 本文件中的共享补发方法是本次任务中唯一调用 pawn.health.AddHediff(mechBondDef) 的位置。
    /// 本 MOD 不计算任何研究进度，也绝不主动移除 MechBond（移除完全交给扩展 MOD 自己的 HediffComp）。
    /// </summary>
    internal static class MechFusionMechBondCompatibilityPatch
    {
        private const string TheoryResearchDefName = "MF_Theory";

        private const string MechBondHediffDefName = "MechBond";

        // 稳定的 ErrorOnce 键，避免与其他模块冲突。
        private const int ErrorKeyTheoryMissing = unchecked((int)0x5E3F_0001);

        private const int ErrorKeyMechBondMissing = unchecked((int)0x5E3F_0002);

        /// <summary>
        /// 入口一：玩家完成研究 MF_Theory 时由扩展 MOD 自己的补丁同步触发，
        /// 本 MOD 仅做“缺少才添加”，顺序不影响结果。
        /// </summary>
        internal static void Postfix_ResearchFinish(ResearchProjectDef? proj)
        {
            if (proj == null)
            {
                return;
            }

            if (proj.defName != TheoryResearchDefName)
            {
                return;
            }

            GrantMechBondIfMissing();
        }

        /// <summary>
        /// 入口二：每次完整存档读取完成后调用一次，为缺失状态的合格机械族机械师补发 MechBond。
        /// </summary>
        internal static void Postfix_LoadedGame()
        {
            GrantMechBondIfMissing();
        }

        /// <summary>
        /// 在扩展 MOD 已加载的前提下，为缺失 MechBond 状态的合格机械族机械师补发该状态。
        /// 这是本次任务中唯一调用 AddHediff 的位置。
        /// </summary>
        internal static void GrantMechBondIfMissing()
        {
            ResearchProjectDef? theory =
                DefDatabase<ResearchProjectDef>.GetNamedSilentFail(
                    TheoryResearchDefName);
            if (theory == null)
            {
                Log.ErrorOnce(
                    "[MAP-机械族机械师] 第三方兼容（MechFusion）：未找到研究 "
                    + TheoryResearchDefName
                    + "，无法判定机械族机械师是否需要补发 MechBond。",
                    ErrorKeyTheoryMissing);
                return;
            }

            if (!theory.IsFinished)
            {
                return;
            }

            HediffDef? mechBondDef =
                DefDatabase<HediffDef>.GetNamedSilentFail(MechBondHediffDefName);
            if (mechBondDef == null)
            {
                Log.ErrorOnce(
                    "[MAP-机械族机械师] 第三方兼容（MechFusion）：未找到健康状态 "
                    + MechBondHediffDefName
                    + "，无法为机械族机械师补发。",
                    ErrorKeyMechBondMissing);
                return;
            }

            IReadOnlyList<Pawn> mechanitors =
                GameComponent_MechanoidMechanitorRegistry.CurrentRegisteredMechanitors;
            for (int i = 0; i < mechanitors.Count; i++)
            {
                Pawn pawn = mechanitors[i];
                if (pawn == null || pawn.Destroyed || pawn.Dead)
                {
                    continue;
                }

                if (pawn.health == null || pawn.health.hediffSet == null)
                {
                    continue;
                }

                if (pawn.Faction != Faction.OfPlayer)
                {
                    continue;
                }

                if (!pawn.RaceProps.IsMechanoid)
                {
                    continue;
                }

                if (!MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
                {
                    continue;
                }

                if (pawn.mechanitor == null)
                {
                    continue;
                }

                if (pawn.health.hediffSet.HasHediff(mechBondDef))
                {
                    continue;
                }

                pawn.health.AddHediff(mechBondDef);
            }
        }
    }
}

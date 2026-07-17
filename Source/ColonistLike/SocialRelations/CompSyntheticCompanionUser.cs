using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 仿生伴侣组件：保存 Lovin 开关与仿生生育方式，提供配偶交互与仿生孕育的静态能力来源。
    /// 不负责社交面板可见性（由 CompColonistLikeSocialTabUser 独立提供）。
    /// </summary>
    public sealed class CompProperties_SyntheticCompanionUser : CompProperties
    {
        public CompProperties_SyntheticCompanionUser()
        {
            compClass = typeof(CompSyntheticCompanionUser);
        }
    }

    public sealed class CompSyntheticCompanionUser : ThingComp, ISyntheticCompanionState
    {
        private bool lovinWithSpouseEnabled;

        /// <summary>
        /// 默认避孕；新生成时均回落到避孕。
        /// </summary>
        private SyntheticPregnancyApproach pregnancyApproach =
            SyntheticPregnancyApproach.AvoidPregnancy;

        public bool LovinWithSpouseEnabled => lovinWithSpouseEnabled;

        public SyntheticPregnancyApproach PregnancyApproach => pregnancyApproach;

        public void ToggleLovinWithSpouse()
        {
            lovinWithSpouseEnabled = !lovinWithSpouseEnabled;
        }

        public void DisableLovinWithSpouse()
        {
            lovinWithSpouseEnabled = false;
        }

        public void SetPregnancyApproach(SyntheticPregnancyApproach approach)
        {
            pregnancyApproach = approach;
        }

        public void ResetPregnancyApproachToAvoid()
        {
            pregnancyApproach = SyntheticPregnancyApproach.AvoidPregnancy;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref lovinWithSpouseEnabled, "lovinWithSpouseEnabled", false);
            Scribe_Values.Look(
                ref pregnancyApproach,
                "syntheticPregnancyApproach",
                SyntheticPregnancyApproach.AvoidPregnancy);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }

            if (parent is not Pawn pawn)
            {
                yield break;
            }

            foreach (Gizmo gizmo in SyntheticCompanionGizmoUtility.GetGizmos(pawn))
            {
                yield return gizmo;
            }
        }
    }
}

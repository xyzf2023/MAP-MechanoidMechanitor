using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 显式授权标记：仅挂载本组件的 Pawn 参与显式社交关系、配偶关系与配偶 Lovin 响应。
    /// </summary>
    public sealed class CompProperties_ExplicitSocialRelationUser : CompProperties
    {
        public CompProperties_ExplicitSocialRelationUser()
        {
            compClass = typeof(CompExplicitSocialRelationUser);
        }
    }

    public sealed class CompExplicitSocialRelationUser : ThingComp
    {
        private const string LovinToggleLabelKey =
            "MAP_MechanoidMechanitor.ExplicitSocial.LovinWithSpouseToggle";
        private const string LovinToggleDescKey =
            "MAP_MechanoidMechanitor.ExplicitSocial.LovinWithSpouseToggleDesc";

        private bool lovinWithSpouseEnabled;

        /// <summary>
        /// 默认避孕；新生成与旧存档缺字段时均回落到避孕。
        /// </summary>
        private LoverPregnancyApproach pregnancyApproach = LoverPregnancyApproach.AvoidPregnancy;

        /// <summary>
        /// 玩家是否授权该恋人响应人类配偶发起的原版 Lovin。默认关闭。
        /// </summary>
        public bool LovinWithSpouseEnabled => lovinWithSpouseEnabled;

        /// <summary>
        /// 恋人专用生育方式（不写入原版 PregnancyApproach 字典）。
        /// </summary>
        public LoverPregnancyApproach PregnancyApproach => pregnancyApproach;

        public void DisableLovinWithSpouse()
        {
            lovinWithSpouseEnabled = false;
        }

        public void SetPregnancyApproach(LoverPregnancyApproach approach)
        {
            pregnancyApproach = approach;
        }

        /// <summary>
        /// 更换配偶后重置为避孕，避免新配偶下意外受孕。
        /// </summary>
        public void ResetPregnancyApproachToAvoid()
        {
            pregnancyApproach = LoverPregnancyApproach.AvoidPregnancy;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref lovinWithSpouseEnabled, "lovinWithSpouseEnabled", false);
            Scribe_Values.Look(
                ref pregnancyApproach,
                "loverPregnancyApproach",
                LoverPregnancyApproach.AvoidPregnancy);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }

            if (parent is not Pawn pawn
                || pawn.Faction != Faction.OfPlayer
                || !HasAnyDirectSpouse(pawn))
            {
                yield break;
            }

            yield return new Command_Toggle
            {
                defaultLabel = LovinToggleLabelKey.Translate(),
                defaultDesc = LovinToggleDescKey.Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/LovinJob"),
                isActive = () => lovinWithSpouseEnabled,
                toggleAction = delegate
                {
                    lovinWithSpouseEnabled = !lovinWithSpouseEnabled;
                },
                activateIfAmbiguous = true,
            };
        }

        /// <summary>
        /// 直接遍历 DirectRelations；禁止使用受 IsFlesh 限制的 GetSpouses 等扩展。
        /// </summary>
        private static bool HasAnyDirectSpouse(Pawn pawn)
        {
            if (pawn.relations == null)
            {
                return false;
            }

            List<DirectPawnRelation> relations = pawn.relations.DirectRelations;
            for (int i = 0; i < relations.Count; i++)
            {
                DirectPawnRelation relation = relations[i];
                if (relation.def == PawnRelationDefOf.Spouse && relation.otherPawn != null)
                {
                    return true;
                }
            }

            return false;
        }
    }
}

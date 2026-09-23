using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 机械身体不适用的原版文化情境想法。只读资格，不初始化身份或文化追踪器。
    /// 身体评价统一中立；不处理实施手术、仪式质量等行为记忆。
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class MechanoidMechanitorIdeologyBodyRequirementUtility
    {
        private static readonly HashSet<ThoughtDef> PersonalThoughts;
        private static readonly HashSet<ThoughtDef> BodyOpinions;

        internal static readonly ThoughtDef? XenotypeMakeupThought;

        static MechanoidMechanitorIdeologyBodyRequirementUtility()
        {
            // 固定列举原版 Def，在定义加载后解析引用，不按 worker 或名称前缀扩展到第三方。
            PersonalThoughts = ResolveThoughts(
                "AgeReversalDemanded",
                "NeedNeuralSupercharge",
                "HighLife",
                "SelfDislikedXenotype",
                "HasProsthetic_Abhorrent",
                "HasProsthetic_Disapproved",
                "HasNoProsthetic_Disapproved",
                "HasProsthetic_Approved",
                "Scarification_Extreme",
                "Scarification_Heavy",
                "Scarification_Minor",
                "Blindness_Respected_Blind",
                "Blindness_Elevated_Blind",
                "Blindness_Sublime_Blind",
                "Blindness_ArtificialBlind",
                "Blindness_Elevated_HalfBlind",
                "Blindness_Sublime_HalfBlind",
                "Blindness_Elevated_NonBlind",
                "Blindness_Sublime_NonBlind");

            BodyOpinions = ResolveThoughts(
                "HasProsthetic_Abhorrent_Social",
                "HasProsthetic_Disapproved_Social",
                "HasNoProsthetic_Disapproved_Social",
                "Scarification_Extreme_Opinion",
                "Scarification_Heavy_Opinion",
                "Scarification_Minor_Opinion",
                "Blindness_Respected_Blind_Social",
                "Blindness_Elevated_Blind_Social",
                "Blindness_Sublime_Blind_Social",
                "Blindness_Elevated_HalfBlind_Social",
                "Blindness_Sublime_HalfBlind_Social",
                "Blindness_Respected_NonBlind_Social",
                "Blindness_Elevated_NonBlind_Social",
                "Blindness_Sublime_NonBlind_Social",
                "PreferredXenotype");

            XenotypeMakeupThought =
                DefDatabase<ThoughtDef>.GetNamedSilentFail("PreferredXenotypeMakeup");
        }

        internal static bool IsExemptMechanicalMember(Pawn? pawn)
        {
            // 不依赖心情、阵营、宿主或文化档位。降低档位后保留的文化数据仍适用身体豁免。
            return ModsConfig.IdeologyActive
                && pawn?.RaceProps?.IsMechanoid == true
                && GameComponent_MechanoidMechanitorRegistry.TryGetMechanitorRecord(pawn, out _);
        }

        internal static bool ShouldSuppressPersonalThought(Pawn? pawn, ThoughtDef? thought)
        {
            return thought != null
                && PersonalThoughts.Contains(thought)
                && IsExemptMechanicalMember(pawn);
        }

        internal static bool ShouldSuppressBodyOpinion(Pawn? otherPawn, ThoughtDef? thought)
        {
            return thought != null
                && BodyOpinions.Contains(thought)
                && IsExemptMechanicalMember(otherPawn);
        }

        private static HashSet<ThoughtDef> ResolveThoughts(params string[] defNames)
        {
            HashSet<ThoughtDef> result = new HashSet<ThoughtDef>();
            foreach (string defName in defNames)
            {
                ThoughtDef? thought = DefDatabase<ThoughtDef>.GetNamedSilentFail(defName);
                if (thought != null)
                {
                    result.Add(thought);
                }
            }

            return result;
        }
    }
}

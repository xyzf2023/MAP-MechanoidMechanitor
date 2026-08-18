using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 机械族机械师灵感中央工具。
    /// 负责维护原版灵感白名单、机械族专用资格判定、blocking Hediff 检查、
    /// 以及“只在本系统主动发放瞬间临时覆盖 InspirationCanOccur”的严格 Scope。
    /// 全部使用真正的原版 InspirationDef，不创建任何复制版 InspirationDef。
    /// </summary>
    public static class MechanoidMechanitorInspirationUtility
    {
        /// <summary>
        /// 唯一权威的 InspirationDef 白名单。只允许这 5 个真正的原版灵感。
        /// 不要加入 Inspired_Trade / Inspired_Recruitment / Inspired_Taming。
        /// </summary>
        private static readonly string[] AllowedInspirationDefNames =
        {
            "Frenzy_Work",
            "Frenzy_Go",
            "Frenzy_Shoot",
            "Inspired_Surgery",
            "Inspired_Creativity"
        };

        private static List<InspirationDef>? cachedAllowedInspirations;

        // 严格成对的 GrantScope 状态（按线程隔离）。
        [ThreadStatic]
        private static Pawn? scopedGrantPawn;

        [ThreadStatic]
        private static InspirationDef? scopedGrantDef;

        private static void EnsureCaches()
        {
            if (cachedAllowedInspirations != null)
            {
                return;
            }

            cachedAllowedInspirations = new List<InspirationDef>();
            foreach (string defName in AllowedInspirationDefNames)
            {
                InspirationDef? def =
                    DefDatabase<InspirationDef>.GetNamedSilentFail(defName);
                if (def == null)
                {
                    continue;
                }

                cachedAllowedInspirations.Add(def);
            }
        }

        /// <summary>
        /// 当前生效的白名单 InspirationDef 列表（按 DefDatabase 解析，找不到的已跳过）。
        /// </summary>
        public static IReadOnlyList<InspirationDef> AllowedInspirations
        {
            get
            {
                EnsureCaches();
                return cachedAllowedInspirations!;
            }
        }

        public static bool IsWhitelistedInspiration(InspirationDef? def)
        {
            if (def == null)
            {
                return false;
            }

            EnsureCaches();
            return cachedAllowedInspirations!.Contains(def);
        }

        /// <summary>
        /// 系统总开关（与娱乐共享设置项）。
        /// </summary>
        public static bool Enabled
        {
            get
            {
                return MAPMechanitorMod.Settings?.enableMechanoidMechanitorRecreation
                    == true;
            }
        }

        /// <summary>
        /// 机械族专用灵感资格判定。
        /// 基本复制原版 InspirationWorker.InspirationCanOccur，
        /// 但明确删除两个原版限制：
        ///  A. allowedOnNonColonists / Pawn.IsColonist（机械族机械师不是 Humanlike 殖民者）；
        ///  B. ageTracker.AgeBiologicalYearsFloat &lt; minAge（机械族生物年龄语义不适用）。
        /// 其余资格条件（动物/倒地/Stat/Skill/WorkType/Capacity/Trait/WorkTag）尽量完整保留。
        /// </summary>
        public static bool CanOccurForMechanoidMechanitor(
            Pawn? pawn,
            InspirationDef? def)
        {
            if (pawn == null || def == null)
            {
                return false;
            }

            if (!MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
            {
                return false;
            }

            if (!IsWhitelistedInspiration(def))
            {
                return false;
            }

            if (pawn.Dead)
            {
                return false;
            }

            if (!def.allowedOnAnimals && pawn.RaceProps.Animal)
            {
                return false;
            }

            if (!def.allowedOnDownedPawns && pawn.Downed)
            {
                return false;
            }

            if (def.requiredNonDisabledStats != null)
            {
                for (int i = 0; i < def.requiredNonDisabledStats.Count; i++)
                {
                    if (def.requiredNonDisabledStats[i].Worker.IsDisabledFor(pawn))
                    {
                        return false;
                    }
                }
            }

            if (def.requiredSkills != null)
            {
                for (int j = 0; j < def.requiredSkills.Count; j++)
                {
                    if (!def.requiredSkills[j].PawnSatisfies(pawn))
                    {
                        return false;
                    }
                }
            }

            if (!def.requiredAnySkill.NullOrEmpty())
            {
                bool anySatisfied = false;
                for (int k = 0; k < def.requiredAnySkill.Count; k++)
                {
                    if (def.requiredAnySkill[k].PawnSatisfies(pawn))
                    {
                        anySatisfied = true;
                        break;
                    }
                }

                if (!anySatisfied)
                {
                    return false;
                }
            }

            if (def.requiredNonDisabledWorkTypes != null)
            {
                for (int l = 0; l < def.requiredNonDisabledWorkTypes.Count; l++)
                {
                    if (pawn.WorkTypeIsDisabled(def.requiredNonDisabledWorkTypes[l]))
                    {
                        return false;
                    }
                }
            }

            if (!def.requiredAnyNonDisabledWorkType.NullOrEmpty())
            {
                bool anyEnabled = false;
                for (int m = 0; m < def.requiredAnyNonDisabledWorkType.Count; m++)
                {
                    if (!pawn.WorkTypeIsDisabled(
                            def.requiredAnyNonDisabledWorkType[m]))
                    {
                        anyEnabled = true;
                        break;
                    }
                }

                if (!anyEnabled)
                {
                    return false;
                }
            }

            if (def.requiredCapacities != null)
            {
                for (int n = 0; n < def.requiredCapacities.Count; n++)
                {
                    if (!pawn.health.capacities.CapableOf(
                            def.requiredCapacities[n]))
                    {
                        return false;
                    }
                }
            }

            if (pawn.story != null
                && pawn.story.traits?.allTraits != null)
            {
                for (int t = 0; t < pawn.story.traits.allTraits.Count; t++)
                {
                    Trait trait = pawn.story.traits.allTraits[t];
                    if (!trait.Suppressed
                        && trait.CurrentData.disallowedInspirations != null
                        && trait.CurrentData.disallowedInspirations.Contains(def))
                    {
                        return false;
                    }
                }
            }

            if (def.requiredNonDisabledWorkTags != WorkTags.None
                && (pawn.CombinedDisabledWorkTags
                    & def.requiredNonDisabledWorkTags) != WorkTags.None)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 已被 Hediff 明确禁止灵感的 Pawn 不进入随机选择（双重保护）。
        /// </summary>
        private static bool HasInspirationBlockingHediff(Pawn pawn)
        {
            if (pawn?.health?.hediffSet?.hediffs == null)
            {
                return false;
            }

            foreach (Hediff hediff in pawn.health.hediffSet.hediffs)
            {
                if (hediff?.CurStage?.blocksInspirations == true)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 从当前真正满足资格的白名单灵感中随机选择一个并真正发放。
        /// 使用原版 TryStartInspiration，从而原版 letter、duration、PostStart、
        /// expiration、EndInspiration 等全部继续正常工作。
        /// </summary>
        public static bool TryGrantRandomEligibleInspiration(
            Pawn pawn,
            bool sendLetter = true)
        {
            if (pawn == null)
            {
                return false;
            }

            if (!Enabled)
            {
                return false;
            }

            if (!MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
            {
                return false;
            }

            if (pawn.mindState?.inspirationHandler == null)
            {
                return false;
            }

            if (pawn.Inspired)
            {
                return false;
            }

            if (HasInspirationBlockingHediff(pawn))
            {
                return false;
            }

            List<InspirationDef> candidates =
                AllowedInspirations
                    .Where(def => CanOccurForMechanoidMechanitor(pawn, def))
                    .ToList();

            if (candidates.Count == 0)
            {
                return false;
            }

            // 保留原版 CommonalityFor 权重选择。
            InspirationDef? chosen =
                candidates.RandomElementByWeightWithFallback(
                    def => def!.Worker.CommonalityFor(pawn));

            if (chosen == null)
            {
                return false;
            }

            // 只在本次发放瞬间临时覆盖 InspirationCanOccur，
            // 通过严格成对的 Scope 确保只影响本次机械族机械师发放。
            using (BeginGrantScope(pawn, chosen))
            {
                return pawn.mindState.inspirationHandler.TryStartInspiration(
                    chosen,
                    "MAP_MechanoidMechanitor.Recreation.InspirationReason"
                        .Translate(),
                    sendLetter);
            }
        }

        /// <summary>
        /// 进入一次灵感发放 Scope。Scope 完全匹配（引用相等）时，
        /// InspirationCanOccur 的补丁才会跳过原版、改用本系统的机械族资格判定。
        /// 支持嵌套恢复（保存并恢复上一层的 scoped pawn/def）。
        /// </summary>
        public static IDisposable BeginGrantScope(Pawn pawn, InspirationDef def)
        {
            return new GrantScope(pawn, def);
        }

        /// <summary>
        /// 当前 Scope 是否精确匹配给定的 pawn 与 def（引用/精确对象匹配）。
        /// </summary>
        public static bool IsGrantScopeMatch(Pawn pawn, InspirationDef def)
        {
            return scopedGrantPawn == pawn && scopedGrantDef == def;
        }

        private sealed class GrantScope : IDisposable
        {
            private readonly Pawn? previousPawn;
            private readonly InspirationDef? previousDef;
            private bool disposed;

            public GrantScope(Pawn pawn, InspirationDef def)
            {
                previousPawn = scopedGrantPawn;
                previousDef = scopedGrantDef;
                scopedGrantPawn = pawn;
                scopedGrantDef = def;
            }

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                scopedGrantPawn = previousPawn;
                scopedGrantDef = previousDef;
            }
        }
    }
}

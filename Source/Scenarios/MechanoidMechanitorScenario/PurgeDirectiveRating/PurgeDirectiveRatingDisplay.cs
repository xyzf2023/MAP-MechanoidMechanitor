using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 肃清评级「只读显示快照」与显示用辅助。UI 只读取本类，不得自己推导权限表。
    /// 所有权限描述都从统一配置（PurgeDirectiveRatingConfigDef）/ 统一查询（PurgeDirectiveRatingUtility）推导，
    /// 不维护第二套等级表。
    /// </summary>
    public sealed class PurgeDirectiveRatingDisplay
    {
        public bool IsRatingMode { get; private set; }
        public bool IsTakeover { get; private set; }
        public string RatingNameText { get; private set; } = string.Empty;
        public int RatingValue { get; private set; }
        public int LevelStart { get; private set; }
        public int NextLevelStart { get; private set; }
        public float LevelProgress { get; private set; }   // 当前等级内部进度 0..1
        public int PointsToNextLevel { get; private set; }
        public int CurrentLevel { get; private set; }
        public int MaxMechWeightLevel { get; private set; } // 当前可调度的最高机械族重量级（1..4）
        public int GoodsCatalogLevel { get; private set; }  // 1=基础 2=标准 3=完整
        public float Discount { get; private set; }
        public bool ForceSupportAvailable { get; private set; }
        public int ForceSupportMax { get; private set; }
        public bool ClusterAvailable { get; private set; }
        public int ClusterMax { get; private set; }
        public bool EnvironmentAllowed { get; private set; }
        public string CurrentQuestTargetLabel { get; private set; } = string.Empty;
        public string CurrentQuestStage { get; private set; } = string.Empty;
        public int OfferOrActionRemainingTicks { get; private set; }
        public string NextLevelNewPermissions { get; private set; } = string.Empty;

        public static PurgeDirectiveRatingDisplay Build()
        {
            PurgeDirectiveRatingDisplay s = new PurgeDirectiveRatingDisplay();
            bool takeover = GameComponent_CerebrexTakeoverState.IsActive;
            s.IsTakeover = takeover;
            s.IsRatingMode = !takeover;

            if (takeover)
            {
                s.RatingNameText = "控制权限：完全接管";
                s.MaxMechWeightLevel = 4;
                s.GoodsCatalogLevel = 3;
                s.Discount = PurgeDirectiveRatingUtility.GetDiscountRate();
                s.ForceSupportAvailable = true;
                s.ForceSupportMax = int.MaxValue;
                s.ClusterAvailable = true;
                s.ClusterMax = MechClusterDeploymentOrder.MaxThreatPoints;
                s.EnvironmentAllowed = true;
            }
            else
            {
                int level = PurgeDirectiveRatingUtility.CurrentRatingLevel;
                int value = PurgeDirectiveRatingUtility.CurrentRatingValue();
                s.CurrentLevel = level;
                s.RatingValue = value;
                s.RatingNameText = RatingName(level);
                int start = PurgeDirectiveRatingUtility.Config.GetLevelStart(level);
                int next = PurgeDirectiveRatingUtility.Config.GetNextLevelStart(level);
                s.LevelStart = start;
                s.NextLevelStart = next;
                int span = next - start;
                s.LevelProgress = span <= 0 ? 1f : Mathf.Clamp01((float)(value - start) / span);
                s.PointsToNextLevel = level >= 5 ? 0 : Mathf.Max(0, next - value);
                s.MaxMechWeightLevel = MaxUnlockedMechWeightLevel(level);
                s.GoodsCatalogLevel = Mathf.Min(level, 3);
                s.Discount = PurgeDirectiveRatingUtility.GetDiscountRate();
                s.ForceSupportAvailable = PurgeDirectiveRatingUtility.IsForceSupportAvailable();
                s.ForceSupportMax = PurgeDirectiveRatingUtility.EffectiveForceSupportMaxThreat();
                s.ClusterAvailable = PurgeDirectiveRatingUtility.IsClusterAvailable();
                s.ClusterMax = PurgeDirectiveRatingUtility.EffectiveClusterMaxThreat();
                s.EnvironmentAllowed = PurgeDirectiveRatingUtility.IsClusterEnvironmentAllowed();
                s.NextLevelNewPermissions = level >= 5
                    ? string.Empty
                    : NewPermissionsAtLevel(level + 1);
            }

            if (!takeover)
            {
                FillQuestInfo(s);
            }

            return s;
        }

        private static void FillQuestInfo(PurgeDirectiveRatingDisplay s)
        {
            PurgeDirectiveQuestPart? part = ActiveQuestPart();
            if (part == null)
            {
                s.CurrentQuestStage = "MAP_PurgeDirectiveRating.Quest.None".Translate();
                return;
            }

            s.CurrentQuestTargetLabel = part.targetWorldObject?.LabelCap ?? "?";
            if (part.IsOfferPending)
            {
                s.CurrentQuestStage = "MAP_PurgeDirectiveRating.Quest.OfferPending".Translate();
                s.OfferOrActionRemainingTicks = part.OfferRemainTicks();
            }
            else if (part.IsOperationActive)
            {
                s.CurrentQuestStage = "MAP_PurgeDirectiveRating.Quest.OperationActive".Translate();
                s.OfferOrActionRemainingTicks = part.ActionRemainTicks();
            }
            else
            {
                s.CurrentQuestStage = "MAP_PurgeDirectiveRating.Quest.None".Translate();
            }
        }

        public static PurgeDirectiveQuestPart? ActiveQuestPart()
        {
            QuestScriptDef? def = PurgeDirectiveQuestTargetUtility.ResolveQuestScriptDef();
            if (def == null) return null;
            foreach (Quest quest in Find.QuestManager.QuestsListForReading)
            {
                if (quest.root != def
                    || (quest.State != QuestState.Ongoing
                        && quest.State != QuestState.NotYetAccepted)) continue;
                foreach (QuestPart p in quest.PartsListForReading)
                {
                    if (p is PurgeDirectiveQuestPart purge && (purge.IsOfferPending || purge.IsOperationActive))
                    {
                        return purge;
                    }
                }
            }

            return null;
        }

        private static int MaxUnlockedMechWeightLevel(int level)
        {
            int max = 1;
            if (level >= PurgeDirectiveRatingUtility.Config.mechMediumRequiredLevel) max = 2;
            if (level >= PurgeDirectiveRatingUtility.Config.mechHeavyRequiredLevel) max = 3;
            if (level >= PurgeDirectiveRatingUtility.Config.mechUltraHeavyRequiredLevel) max = 4;
            return max;
        }

        /// <summary>等级 1～5 的显示名称，由翻译键映射为序列五～序列一。</summary>
        public static string RatingName(int level)
        {
            switch (level)
            {
                case 1: return "MAP_PurgeDirectiveRating.LevelName1".Translate();
                case 2: return "MAP_PurgeDirectiveRating.LevelName2".Translate();
                case 3: return "MAP_PurgeDirectiveRating.LevelName3".Translate();
                case 4: return "MAP_PurgeDirectiveRating.LevelName4".Translate();
                case 5: return "MAP_PurgeDirectiveRating.LevelName5".Translate();
                default: return "MAP_PurgeDirectiveRating.LevelName1".Translate();
            }
        }

        /// <summary>
        /// 「到达该等级时新增的权限」（相对上一等级）。所有描述由统一配置/查询推导，不另建权限表。
        /// </summary>
        public static string NewPermissionsAtLevel(int level)
        {
            List<string> items = new List<string>();
            PurgeDirectiveRatingConfigDef cfg = PurgeDirectiveRatingUtility.Config;

            switch (level)
            {
                case 1:
                    items.Add("MAP_PurgeDirectiveRating.Perm.MechLight".Translate());
                    items.Add("MAP_PurgeDirectiveRating.Perm.GoodsBasic".Translate());
                    break;
                case 2:
                    items.Add("MAP_PurgeDirectiveRating.Perm.MechMedium".Translate());
                    items.Add("MAP_PurgeDirectiveRating.Perm.GoodsStandard".Translate());
                    items.Add("MAP_PurgeDirectiveRating.Perm.ForceSupport".Translate()
                        + " ≤" + cfg.GetForceSupportMaxThreat(2));
                    break;
                case 3:
                    items.Add("MAP_PurgeDirectiveRating.Perm.MechHeavy".Translate());
                    items.Add("MAP_PurgeDirectiveRating.Perm.GoodsFull".Translate());
                    items.Add("MAP_PurgeDirectiveRating.Perm.ForceSupport".Translate()
                        + " ≤" + cfg.GetForceSupportMaxThreat(3));
                    break;
                case 4:
                    items.Add("MAP_PurgeDirectiveRating.Perm.MechUltraHeavy".Translate());
                    items.Add("MAP_PurgeDirectiveRating.Perm.ForceSupport".Translate()
                        + " ≤" + cfg.GetForceSupportMaxThreat(4));
                    items.Add("MAP_PurgeDirectiveRating.Perm.Cluster".Translate()
                        + " ≤" + cfg.GetClusterMaxThreat(4));
                    items.Add(string.Format(
                        "MAP_PurgeDirectiveRating.Perm.Discount".Translate(),
                        Mathf.RoundToInt(cfg.discountLevel4 * 100f)));
                    break;
                case 5:
                    items.Add("MAP_PurgeDirectiveRating.Perm.ForceUnlimited".Translate());
                    items.Add("MAP_PurgeDirectiveRating.Perm.Cluster".Translate()
                        + " ≤" + cfg.GetClusterMaxThreat(5));
                    items.Add("MAP_PurgeDirectiveRating.Perm.Environment".Translate());
                    items.Add(string.Format(
                        "MAP_PurgeDirectiveRating.Perm.Discount".Translate(),
                        Mathf.RoundToInt(cfg.discountLevel5 * 100f)));
                    break;
            }

            if (items.Count == 0)
            {
                return "MAP_PurgeDirectiveRating.Perm.None".Translate();
            }

            return string.Join("、", items);
        }

        /// <summary>汇总跨级变化中新增或失去的权限；lowerExclusive 不包含，upperInclusive 包含。</summary>
        public static string PermissionsBetween(int lowerExclusive, int upperInclusive)
        {
            List<string> segs = new List<string>();
            for (int level = Mathf.Max(1, lowerExclusive + 1);
                 level <= Mathf.Min(5, upperInclusive);
                 level++)
            {
                string permissions = NewPermissionsAtLevel(level);
                if (!string.IsNullOrEmpty(permissions))
                {
                    segs.Add(permissions);
                }
            }

            return segs.Count == 0
                ? "MAP_PurgeDirectiveRating.Perm.None".Translate()
                : string.Join("；", segs);
        }

        /// <summary>汇总某等级当前已开放的权限（1..level 各档新增权限的合并）。</summary>
        public static string PermissionSummaryForLevel(int level)
        {
            if (level <= 0) return "MAP_PurgeDirectiveRating.Perm.None".Translate();
            List<string> segs = new List<string>();
            for (int i = 1; i <= level; i++)
            {
                string seg = NewPermissionsAtLevel(i);
                if (!string.IsNullOrEmpty(seg)) segs.Add(seg);
            }

            if (segs.Count == 0) return "MAP_PurgeDirectiveRating.Perm.None".Translate();
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < segs.Count; i++)
            {
                if (i > 0) sb.Append("；");
                sb.Append(segs[i]);
            }

            return sb.ToString();
        }
    }
}

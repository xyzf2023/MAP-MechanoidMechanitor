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
        public int GoodsCatalogLevel { get; private set; }  // 普通物资自动分级：1=基础 2=标准 3=全部；指定物资另按名单评级开放
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

            // 从实际商品目录和评级名单读取，避免把芯片等具体物品写死在提示中。
            List<string> specifiedGoods = new List<string>();
            foreach (MechanoidOvermindThingCatalogEntry entry in MechanoidOvermindCatalogService.GetThingCatalog())
            {
                if (PurgeDirectiveGoodsRatingOverrideUtility.GetOverrideLevel(entry.Def) == level)
                {
                    specifiedGoods.Add(entry.Def.LabelCap.ToString());
                }
            }
            if (specifiedGoods.Count > 0)
            {
                items.Add("MAP_PurgeDirectiveRating.Perm.GoodsSpecified".Translate(
                    string.Join("、", specifiedGoods)));
            }

            if (items.Count == 0)
            {
                return "MAP_PurgeDirectiveRating.Perm.None".Translate();
            }

            return string.Join("、", items);
        }

        /// <summary>
        /// 信件专用的权限变化正文：比较前后评级，按类别合并跨级结果，不影响通讯界面的紧凑摘要。
        /// 仅查询配置和目录，不读取新评级下的当前权限代替旧评级，也不修改运行状态。
        /// </summary>
        public static string LetterPermissionChanges(int prevLevel, int newLevel)
        {
            prevLevel = Mathf.Clamp(prevLevel, 1, 5);
            newLevel = Mathf.Clamp(newLevel, 1, 5);
            if (prevLevel == newLevel)
                return "MAP_PurgeDirectiveRating.Letter.Change.None".Translate();

            PurgeDirectiveRatingConfigDef cfg = PurgeDirectiveRatingUtility.Config;
            bool upgrading = newLevel > prevLevel;
            List<string> sections = new List<string>();
            List<string> mechs = new List<string>();
            AddChangedMechWeight(mechs, prevLevel, newLevel, cfg.mechLightRequiredLevel,
                "MAP_PurgeDirectiveRating.Perm.MechLight");
            AddChangedMechWeight(mechs, prevLevel, newLevel, cfg.mechMediumRequiredLevel,
                "MAP_PurgeDirectiveRating.Perm.MechMedium");
            AddChangedMechWeight(mechs, prevLevel, newLevel, cfg.mechHeavyRequiredLevel,
                "MAP_PurgeDirectiveRating.Perm.MechHeavy");
            AddChangedMechWeight(mechs, prevLevel, newLevel, cfg.mechUltraHeavyRequiredLevel,
                "MAP_PurgeDirectiveRating.Perm.MechUltraHeavy");
            List<string> items = new List<string>();
            if (mechs.Count > 0)
                items.Add((upgrading
                    ? "MAP_PurgeDirectiveRating.Letter.Change.MechGranted"
                    : "MAP_PurgeDirectiveRating.Letter.Change.MechRevoked").Translate(string.Join("、", mechs)));
            AddLetterSection(sections, "MAP_PurgeDirectiveRating.Letter.Section.Mechs", items);

            items = new List<string>();
            int oldGoodsLevel = Mathf.Min(prevLevel, 3);
            int newGoodsLevel = Mathf.Min(newLevel, 3);
            if (oldGoodsLevel != newGoodsLevel)
            {
                string goodsKey = newGoodsLevel == 1 ? "MAP_PurgeDirectiveRating.Perm.GoodsBasic"
                    : newGoodsLevel == 2 ? "MAP_PurgeDirectiveRating.Perm.GoodsStandard"
                    : "MAP_PurgeDirectiveRating.Perm.GoodsFull";
                items.Add((upgrading
                    ? "MAP_PurgeDirectiveRating.Letter.Change.GoodsGranted"
                    : "MAP_PurgeDirectiveRating.Letter.Change.GoodsReduced").Translate(goodsKey.Translate()));
            }

            // 只列实际目录中前后可用状态改变的指定物资，黑名单优先，并按 Def 去重。
            List<string> specifiedGoods = new List<string>();
            HashSet<ThingDef> seen = new HashSet<ThingDef>();
            HashSet<ThingDef> blacklist = MechanoidOvermindCatalogService.GetMergedBlacklist();
            foreach (MechanoidOvermindThingCatalogEntry entry in MechanoidOvermindCatalogService.GetThingCatalog())
            {
                ThingDef thing = entry.Def;
                if (thing == null || blacklist.Contains(thing) || !seen.Add(thing)) continue;
                if (PurgeDirectiveGoodsRatingOverrideUtility.GetOverrideLevel(thing) <= 0) continue;
                int requiredLevel = PurgeDirectiveRatingUtility.RequiredLevelForThing(thing);
                if ((prevLevel >= requiredLevel) != (newLevel >= requiredLevel))
                    specifiedGoods.Add(thing.LabelCap.ToString());
            }
            specifiedGoods.Sort();
            if (specifiedGoods.Count > 0)
                items.Add((upgrading
                    ? "MAP_PurgeDirectiveRating.Letter.Change.GoodsSpecifiedGranted"
                    : "MAP_PurgeDirectiveRating.Letter.Change.GoodsSpecifiedRevoked")
                    .Translate(string.Join("、", specifiedGoods)));
            AddLetterSection(sections, "MAP_PurgeDirectiveRating.Letter.Section.Goods", items);

            items = new List<string>();
            AddProtocolChange(items, "MAP_PurgeDirectiveRating.Perm.ForceSupport",
                cfg.GetForceSupportMaxThreat(prevLevel), cfg.GetForceSupportMaxThreat(newLevel));
            AddProtocolChange(items, "MAP_PurgeDirectiveRating.Perm.Cluster",
                cfg.GetClusterMaxThreat(prevLevel), cfg.GetClusterMaxThreat(newLevel));
            bool oldEnvironment = PurgeDirectiveRatingUtility.ClusterEnvironmentAllowed(prevLevel);
            bool newEnvironment = PurgeDirectiveRatingUtility.ClusterEnvironmentAllowed(newLevel);
            if (oldEnvironment != newEnvironment)
                items.Add((newEnvironment
                    ? "MAP_PurgeDirectiveRating.Letter.Change.EnvironmentGranted"
                    : "MAP_PurgeDirectiveRating.Letter.Change.EnvironmentRevoked").Translate());
            AddLetterSection(sections, "MAP_PurgeDirectiveRating.Letter.Section.Protocols", items);

            items = new List<string>();
            float oldDiscount = cfg.GetDiscountRateForLevel(prevLevel);
            float newDiscount = cfg.GetDiscountRateForLevel(newLevel);
            if (oldDiscount != newDiscount)
                items.Add("MAP_PurgeDirectiveRating.Letter.Change.Discount".Translate(
                    Mathf.RoundToInt(oldDiscount * 100f), Mathf.RoundToInt(newDiscount * 100f)));
            AddLetterSection(sections, "MAP_PurgeDirectiveRating.Letter.Section.Discount", items);

            // 带宽单价独立于调拨折扣；描述评级配额，不把本期缓存单价误写为即时重算。
            if (ModsConfig.BiotechActive
                && cfg.bandwidthSupportBaseByLevel != null
                && cfg.bandwidthSupportCostByLevel != null
                && cfg.bandwidthSupportBaseByLevel.Count >= Mathf.Max(prevLevel, newLevel)
                && cfg.bandwidthSupportCostByLevel.Count >= Mathf.Max(prevLevel, newLevel)
                && cfg.bandwidthSupportUnit > 0 && cfg.bandwidthSupportPeriodDays > 0)
            {
                items = new List<string>();
                int oldBase = Mathf.Max(0, cfg.bandwidthSupportBaseByLevel[prevLevel - 1]);
                int newBase = Mathf.Max(0, cfg.bandwidthSupportBaseByLevel[newLevel - 1]);
                int oldCost = Mathf.Max(0, cfg.bandwidthSupportCostByLevel[prevLevel - 1]);
                int newCost = Mathf.Max(0, cfg.bandwidthSupportCostByLevel[newLevel - 1]);
                if (oldBase != newBase)
                    items.Add("MAP_PurgeDirectiveRating.Letter.Change.BandwidthBase".Translate(oldBase, newBase));
                if (oldCost != newCost)
                    items.Add("MAP_PurgeDirectiveRating.Letter.Change.BandwidthCost".Translate(
                        oldCost, newCost, cfg.bandwidthSupportUnit, cfg.bandwidthSupportPeriodDays));
                if (newBase < oldBase)
                    items.Add("MAP_PurgeDirectiveRating.Letter.Change.BandwidthBaseDeferred".Translate());
                if (oldCost != newCost)
                    items.Add("MAP_PurgeDirectiveRating.Letter.Change.BandwidthCostDeferred".Translate());
                AddLetterSection(sections, "MAP_PurgeDirectiveRating.Letter.Section.Bandwidth", items);
            }

            return sections.Count == 0
                ? "MAP_PurgeDirectiveRating.Letter.Change.None".Translate()
                : string.Join("\n\n", sections);
        }

        private static void AddChangedMechWeight(
            List<string> items, int prevLevel, int newLevel, int requiredLevel, string labelKey)
        {
            if ((prevLevel >= requiredLevel) != (newLevel >= requiredLevel))
                items.Add(labelKey.Translate());
        }

        private static void AddProtocolChange(List<string> items, string labelKey, int oldMax, int newMax)
        {
            if (oldMax == newMax) return;
            string label = labelKey.Translate();
            if (newMax <= 0)
                items.Add("MAP_PurgeDirectiveRating.Letter.Change.ProtocolRevoked".Translate(label));
            else if (oldMax <= 0)
                items.Add("MAP_PurgeDirectiveRating.Letter.Change.ProtocolGranted".Translate(
                    label, ProtocolLimitText(newMax)));
            else
                items.Add("MAP_PurgeDirectiveRating.Letter.Change.ProtocolLimit".Translate(
                    label, ProtocolLimitText(oldMax), ProtocolLimitText(newMax)));
        }

        private static string ProtocolLimitText(int value)
        {
            return value == int.MaxValue
                ? "MAP_PurgeDirectiveRating.Letter.Change.Unlimited".Translate()
                : value.ToString();
        }

        private static void AddLetterSection(List<string> sections, string titleKey, List<string> items)
        {
            if (items.Count > 0)
                sections.Add(titleKey.Translate() + "\n• " + string.Join("\n• ", items));
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

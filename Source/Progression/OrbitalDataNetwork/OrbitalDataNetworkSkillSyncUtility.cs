using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 轨道数据网络的技能备份与兴趣同步集中入口。
    ///
    /// 技能修改逻辑只允许出现在这里；信件、Pawn 生成与 Harmony 补丁都必须调用本类，
    /// 不得各自实现一份技能写入。
    ///
    /// 两个来源严格区分：
    /// - 历史备份来源：持久化注册表快照（允许死亡、尸体中、未生成、暂时离图）。
    /// - 存活判定来源：活跃注册表缓存，并再次验证 Pawn 状态。
    /// </summary>
    public static class OrbitalDataNetworkSkillSyncUtility
    {
        /// <summary>
        /// 全量收集并同步：轨道数据网络研究完成时，以及每次读档后的科研全量同步时调用。
        /// </summary>
        public static bool SyncAll()
        {
            if (!ResearchFeatureUnlockUtility.IsOrbitalDataNetworkUnlocked())
            {
                return true;
            }

            bool allSucceeded = CollectAndMergeBackupFromPersistentRecords();
            if (!DistributeBackupToRegisteredMechanitors(null))
            {
                allSucceeded = false;
            }

            return allSucceeded;
        }

        /// <summary>
        /// 新机械族机械师完成注册和初始化后调用：
        /// 其更高的技能会提高轨道备份并分发给其他人；较低的技能会被提升到备份值。
        /// </summary>
        public static bool SyncForNewMechanitor(Pawn? pawn)
        {
            if (pawn == null
                || !ResearchFeatureUnlockUtility.IsOrbitalDataNetworkUnlocked()
                || !GameComponent_MechanoidMechanitorRegistry.IsPawnAliveAndInitialized(pawn))
            {
                return true;
            }

            bool allSucceeded = true;

            try
            {
                if (!MergeSinglePawnSkillsIntoBackup(pawn))
                {
                    allSucceeded = false;
                }
            }
            catch (Exception ex)
            {
                allSucceeded = false;
                Log.Error(
                    "[MAP-机械族机械师] 轨道技能备份合并失败：" +
                    $"pawn={pawn.LabelShort}（{pawn.ThingID}）：{ex}");
            }

            try
            {
                if (!ApplyBackupToPawn(pawn))
                {
                    allSucceeded = false;
                }
            }
            catch (Exception ex)
            {
                allSucceeded = false;
                Log.Error(
                    "[MAP-机械族机械师] 轨道技能备份应用失败：" +
                    $"pawn={pawn.LabelShort}（{pawn.ThingID}）：{ex}");
            }

            // 新人带来的更高等级需要立即分发给其他活跃机械族机械师，不等到下次读档。
            if (!DistributeBackupToRegisteredMechanitors(pawn))
            {
                allSucceeded = false;
            }

            // 研究完成前处于死亡状态、没有获得“机械意识”的机械师，复活后在此补齐。
            GameComponent_MechanoidMechanitorRegistry
                .RequestMechanicalConsciousnessHediffSync();

            return allSucceeded;
        }

        /// <summary>备用机体生成前刷新一次备份，保证投放出去的机体带上最新数据。</summary>
        public static bool RefreshBackupBeforeDeployment()
        {
            if (!ResearchFeatureUnlockUtility.IsOrbitalDataNetworkUnlocked())
            {
                return true;
            }

            return CollectAndMergeBackupFromPersistentRecords();
        }

        /// <summary>将现有轨道备份应用到指定 Pawn（兴趣狂热 + 只提升不降低）。</summary>
        public static bool ApplyBackupToPawn(Pawn? pawn)
        {
            if (pawn == null)
            {
                return true;
            }

            if (!ResearchFeatureUnlockUtility.IsOrbitalDataNetworkUnlocked())
            {
                return true;
            }

            GameComponent_OrbitalDataNetworkState? state =
                GameComponent_OrbitalDataNetworkState.CurrentState;
            if (state == null)
            {
                Log.Error(
                    "[MAP-机械族机械师] 轨道技能备份应用失败：缺少 " +
                    "GameComponent_OrbitalDataNetworkState。");
                return false;
            }

            Pawn_SkillTracker? tracker = pawn.skills;
            if (tracker?.skills == null || tracker.skills.Count == 0)
            {
                // 没有有效技能追踪器：跳过，不算失败。
                return true;
            }

            bool allSucceeded = true;
            List<SkillDef> allSkills = DefDatabase<SkillDef>.AllDefsListForReading;
            for (int i = 0; i < allSkills.Count; i++)
            {
                SkillDef? def = allSkills[i];
                if (def == null)
                {
                    continue;
                }

                // 不重复创建 SkillRecord：缺失的技能记录保持原样。
                SkillRecord? record = FindRecordForDef(tracker, def);
                if (record == null)
                {
                    continue;
                }

                try
                {
                    // 兴趣统一提升至狂热。因背景暂时禁用的技能同样处理。
                    record.passion = Passion.Major;

                    if (!state.TryGetBackedUpLevel(def, out int backupLevel))
                    {
                        continue;
                    }

                    // 直接写 levelInt：只提升、不降低，且不引入 aptitude 等外部修正。
                    // 不清空 xpSinceLastLevel，不修改 xpSinceMidnight。
                    if (record.levelInt < backupLevel)
                    {
                        record.levelInt = backupLevel;
                    }
                }
                catch (Exception ex)
                {
                    allSucceeded = false;
                    Log.Error(
                        "[MAP-机械族机械师] 轨道技能写入失败：" +
                        $"pawn={pawn.LabelShort}（{pawn.ThingID}），" +
                        $"skill={def.defName}：{ex}");
                }
            }

            return allSucceeded;
        }

        /// <summary>
        /// 从持久化注册表快照收集历史最高基础等级。
        /// 允许从死亡但仍有持久化注册记录的机械族机械师身上读取技能。
        /// </summary>
        private static bool CollectAndMergeBackupFromPersistentRecords()
        {
            GameComponent_OrbitalDataNetworkState? state =
                GameComponent_OrbitalDataNetworkState.CurrentState;
            if (state == null)
            {
                Log.Error(
                    "[MAP-机械族机械师] 轨道技能备份失败：缺少 " +
                    "GameComponent_OrbitalDataNetworkState。");
                return false;
            }

            bool allSucceeded = true;
            IReadOnlyList<MechanoidMechanitorRegistrySnapshotEntry> snapshot =
                GameComponent_MechanoidMechanitorRegistry.GetPersistentRecordSnapshot();

            for (int i = 0; i < snapshot.Count; i++)
            {
                Pawn? pawn = snapshot[i].Pawn;
                if (pawn == null || pawn.Discarded)
                {
                    continue;
                }

                try
                {
                    if (!MergePawnSkillsIntoBackup(pawn, state))
                    {
                        allSucceeded = false;
                    }
                }
                catch (Exception ex)
                {
                    allSucceeded = false;
                    Log.Error(
                        "[MAP-机械族机械师] 轨道技能备份读取失败：" +
                        $"pawn={pawn.LabelShort}（{pawn.ThingID}）：{ex}");
                }
            }

            return allSucceeded;
        }

        private static bool MergeSinglePawnSkillsIntoBackup(Pawn pawn)
        {
            GameComponent_OrbitalDataNetworkState? state =
                GameComponent_OrbitalDataNetworkState.CurrentState;
            if (state == null)
            {
                Log.Error(
                    "[MAP-机械族机械师] 轨道技能备份失败：缺少 " +
                    "GameComponent_OrbitalDataNetworkState。");
                return false;
            }

            return MergePawnSkillsIntoBackup(pawn, state);
        }

        private static bool MergePawnSkillsIntoBackup(
            Pawn pawn,
            GameComponent_OrbitalDataNetworkState state)
        {
            Pawn_SkillTracker? tracker = pawn.skills;
            if (tracker?.skills == null)
            {
                // 没有有效技能追踪器：按规则排除，不算失败。
                return true;
            }

            bool allSucceeded = true;
            List<SkillDef> allSkills = DefDatabase<SkillDef>.AllDefsListForReading;
            for (int i = 0; i < allSkills.Count; i++)
            {
                SkillDef? def = allSkills[i];
                if (def == null)
                {
                    continue;
                }

                try
                {
                    SkillRecord? record = FindRecordForDef(tracker, def);
                    if (record == null)
                    {
                        continue;
                    }

                    // 使用 levelInt 而不是 Level：Level 可能包含 aptitude 等外部修正。
                    state.MergeSkillLevel(def, record.levelInt);
                }
                catch (Exception ex)
                {
                    allSucceeded = false;
                    Log.Error(
                        "[MAP-机械族机械师] 轨道技能备份合并单项失败：" +
                        $"pawn={pawn.LabelShort}（{pawn.ThingID}），" +
                        $"skill={def.defName}：{ex}");
                }
            }

            return allSucceeded;
        }

        /// <summary>
        /// 在技能追踪器中查找指定 SkillDef 的记录，找不到返回 null。
        /// 不使用 Pawn_SkillTracker.GetSkill，因为它在找不到时会记录错误并返回 skills[0]，
        /// 可能污染不相关的技能记录。
        /// </summary>
        private static SkillRecord? FindRecordForDef(Pawn_SkillTracker tracker, SkillDef def)
        {
            List<SkillRecord> skills = tracker.skills;
            for (int i = 0; i < skills.Count; i++)
            {
                if (skills[i].def == def)
                {
                    return skills[i];
                }
            }

            return null;
        }

        private static bool DistributeBackupToRegisteredMechanitors(Pawn? exclude)
        {
            bool allSucceeded = true;
            IReadOnlyList<Pawn> registered =
                GameComponent_MechanoidMechanitorRegistry.CurrentRegisteredMechanitors;

            for (int i = 0; i < registered.Count; i++)
            {
                Pawn pawn = registered[i];
                if (ReferenceEquals(pawn, exclude))
                {
                    continue;
                }

                if (!GameComponent_MechanoidMechanitorRegistry.IsPawnAliveAndInitialized(pawn))
                {
                    continue;
                }

                try
                {
                    if (!ApplyBackupToPawn(pawn))
                    {
                        allSucceeded = false;
                    }
                }
                catch (Exception ex)
                {
                    allSucceeded = false;
                    Log.Error(
                        "[MAP-机械族机械师] 轨道技能备份分发失败：" +
                        $"pawn={pawn.LabelShort}（{pawn.ThingID}）：{ex}");
                }
            }

            return allSucceeded;
        }
    }
}

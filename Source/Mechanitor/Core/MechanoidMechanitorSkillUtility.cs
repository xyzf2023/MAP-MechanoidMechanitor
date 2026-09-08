using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    internal static class MechanoidMechanitorSkillUtility
    {
        internal static bool TryGetPreferredSkillLevel(
            Pawn? pawn,
            SkillDef? skill,
            out int level)
        {
            level = 0;
            if (pawn == null
                || skill == null
                || !MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
            {
                return false;
            }

            Pawn_SkillTracker? tracker = pawn.skills;
            if (tracker == null)
            {
                return false;
            }

            SkillRecord? record = tracker.GetSkill(skill);
            if (record == null)
            {
                return false;
            }

            // 使用 Level 而不是 levelInt，保持原版技能禁用与 aptitude 等修正。
            level = record.Level;
            return true;
        }

        internal static int ResolveMechSkillLevel(
            int vanillaMechSkillLevel,
            Pawn? pawn,
            SkillDef? skill)
        {
            return TryGetPreferredSkillLevel(pawn, skill, out int preferredLevel)
                ? preferredLevel
                : vanillaMechSkillLevel;
        }

        internal static bool TryGetPreferredWorkTypeSkillLevel(
            Pawn? pawn,
            WorkTypeDef? workType,
            out int level)
        {
            level = 0;
            if (pawn == null
                || workType?.relevantSkills == null
                || workType.relevantSkills.Count == 0
                || !MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
            {
                return false;
            }

            int minimumLevel = int.MaxValue;
            for (int i = 0; i < workType.relevantSkills.Count; i++)
            {
                SkillDef? skill = workType.relevantSkills[i];
                if (!TryGetPreferredSkillLevel(pawn, skill, out int skillLevel))
                {
                    return false;
                }

                if (skillLevel < minimumLevel)
                {
                    minimumLevel = skillLevel;
                }
            }

            if (minimumLevel == int.MaxValue)
            {
                return false;
            }

            level = minimumLevel;
            return true;
        }

        internal static void PromoteNonePassionsToMinor(Pawn? pawn)
        {
            Pawn_SkillTracker? tracker = pawn?.skills;
            if (tracker?.skills == null)
            {
                return;
            }

            for (int i = 0; i < tracker.skills.Count; i++)
            {
                SkillRecord? record = tracker.skills[i];
                if (record != null && record.passion == Passion.None)
                {
                    record.passion = Passion.Minor;
                }
            }
        }

        /// <summary>
        /// “机械族机械师技能兴趣度最低为好奇”设置是否开启。
        /// MAPMechanitorMod.Settings 尚未初始化（为 null）时按默认开启处理。
        /// </summary>
        internal static bool IsMinimumMinorPassionSettingEnabled()
        {
            return MAPMechanitorMod.Settings?.ensureMechanoidMechanitorMinimumMinorPassion
                ?? true;
        }

        /// <summary>
        /// 对“真正首次完成技能初始化”的机械族机械师应用配置化兴趣下限。
        /// 设置开启时把所有 Passion.None 提升为 Passion.Minor；设置关闭时不修改任何兴趣。
        /// 调用方必须各自保证只在首次初始化执行一次（原生机械族机械师：
        /// CompCommanderSkills.skillsInitialized；后天机械族机械师：
        /// MechanoidMechanitorRecord.InitialPassionPolicyApplied），
        /// 严禁在修改设置、读档、Tick、Getter、UI 绘制或周期维护入口追溯修改既有 Pawn。
        /// </summary>
        internal static void ApplyConfiguredInitialPassionFloor(Pawn? pawn)
        {
            if (IsMinimumMinorPassionSettingEnabled())
            {
                PromoteNonePassionsToMinor(pawn);
            }
        }

        /// <summary>
        /// 解析心智映射导入后的单个技能兴趣。
        /// 优先级：轨道数据网络（狂热）&gt; 心智核心保存兴趣 &gt; “最低为好奇”设置。
        /// 轨道数据网络已解锁时返回 Passion.Major，不受此设置影响，心智核心中的
        /// None/Minor 不可能把网络狂热降级；
        /// 未解锁且设置开启时把无兴趣提升为好奇（None → Minor，Minor/Major 保持不变）；
        /// 未解锁且设置关闭时精确返回核心保存兴趣（允许把目标现有兴趣覆盖为
        /// Passion.None，也允许 Minor/Major 原样写入）。
        /// </summary>
        internal static Passion ResolveImportedPassion(
            Passion savedPassion,
            bool orbitalDataNetworkUnlocked)
        {
            if (orbitalDataNetworkUnlocked)
            {
                return Passion.Major;
            }

            if (!IsMinimumMinorPassionSettingEnabled())
            {
                return savedPassion;
            }

            return savedPassion == Passion.None ? Passion.Minor : savedPassion;
        }
    }
}

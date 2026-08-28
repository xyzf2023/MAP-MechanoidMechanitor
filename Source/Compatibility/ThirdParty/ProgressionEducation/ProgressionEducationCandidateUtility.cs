using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.ProgressionEducation
{
    /// <summary>
    /// Progression: Education 兼容专用候选枚举。
    /// 不再仅依赖 GameComponent_MechanoidMechanitorRegistry.CurrentRegisteredMechanitors，
    /// 因为 ClassroomTeaching 能力可能来自 Mechanitor identity 或真实 ThingComp。
    /// 改为按能力层扫描当前 Map 已生成 Pawn，允许低频 UI 操作（创建课程）进行全地图扫描。
    /// 仅回答“这个机械族 Pawn 是否允许进入 Education 的机械教学候选体系”；
    /// 真正课程资格（技能数值等）完全交给 Education 自己的 Role / SubjectLogic 判断。
    /// 不在 UI 层初始化任何生命周期状态；基础设施缺失时安全跳过。
    /// </summary>
    internal static class ProgressionEducationCandidateUtility
    {
        private const string LogPrefix =
            "[MAP-机械族机械师] Progression: Education 兼容：";

        private const int WarningKeyInfrastructureNull = unchecked((int)0x5046_0001);

        /// <summary>
        /// 枚举当前 Map 上具备 ClassroomTeaching 与 ColonistLikeTimetable 能力、
        /// 且教学基础设施完整的机械族 Pawn。
        /// </summary>
        public static List<Pawn> GetEligibleClassroomTeachers(Map map)
        {
            List<Pawn> result = new List<Pawn>();
            if (map == null || Current.Game == null)
            {
                return result;
            }

            IReadOnlyList<Pawn> spawned = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < spawned.Count; i++)
            {
                Pawn pawn = spawned[i];
                if (IsEligible(pawn, map))
                {
                    result.Add(pawn);
                }
            }

            return result;
        }

        private static bool IsEligible(Pawn pawn, Map map)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead)
            {
                return false;
            }

            if (!pawn.Spawned || pawn.Map != map)
            {
                return false;
            }

            if (pawn.Faction == null || !pawn.Faction.IsPlayerSafe())
            {
                return false;
            }

            if (pawn.RaceProps?.IsMechanoid != true)
            {
                return false;
            }

            if (!MechanoidMechanitorCapabilityUtility.HasCapability(
                    pawn,
                    MechanoidMechanitorCapability.ClassroomTeaching))
            {
                return false;
            }

            if (!MechanoidMechanitorCapabilityUtility.HasCapability(
                    pawn,
                    MechanoidMechanitorCapability.ColonistLikeTimetable))
            {
                return false;
            }

            if (pawn.timetable == null
                || pawn.skills == null
                || pawn.interactions == null
                || pawn.relations == null)
            {
                if (Prefs.DevMode)
                {
                    Log.WarningOnce(
                        LogPrefix
                        + "候选枚举跳过教学基础设施不完整（生命周期异常，不在 UI 层初始化）的"
                        + $"ColonistLikeTimetable 机械族：{pawn.LabelShort}（{pawn.ThingID}）。",
                        WarningKeyInfrastructureNull);
                }

                return false;
            }

            return true;
        }
    }
}

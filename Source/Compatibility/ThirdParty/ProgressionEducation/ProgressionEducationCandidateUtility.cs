using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.ProgressionEducation
{
    /// <summary>
    /// Progression: Education 兼容专用候选枚举。
    /// 权威来源：GameComponent_MechanoidMechanitorRegistry.CurrentRegisteredMechanitors。
    /// 不做全地图扫描寻找 RaceProps.IsMechanoid，也不在 UI 层初始化任何生命周期状态。
    /// 课程类型（Skill / Daycare / Proficiency）与教学资格由 Education 自己的
    /// Role / SubjectLogic 实时判断，本枚举阶段不检查任何技能数值。
    /// </summary>
    internal static class ProgressionEducationCandidateUtility
    {
        private const string LogPrefix =
            "[MAP-机械族机械师] Progression: Education 兼容：";

        private const int WarningKeyTimetableNull = unchecked((int)0x5046_0001);

        /// <summary>
        /// 枚举当前 Map 上合法的机械族机械师。
        /// timetable 为 null 的注册机械族机械师安全跳过（生命周期异常，不在 UI 层初始化）。
        /// </summary>
        public static List<Pawn> GetEligibleMechanoidMechanitors(Map map)
        {
            List<Pawn> result = new List<Pawn>();
            if (map == null || Current.Game == null)
            {
                return result;
            }

            IReadOnlyList<Pawn> registered =
                GameComponent_MechanoidMechanitorRegistry.CurrentRegisteredMechanitors;
            for (int i = 0; i < registered.Count; i++)
            {
                Pawn pawn = registered[i];
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

            if (!MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
            {
                return false;
            }

            if (pawn.timetable == null)
            {
                if (Prefs.DevMode)
                {
                    Log.WarningOnce(
                        LogPrefix
                        + "候选枚举跳过 timetable 为 null 的注册机械族机械师"
                        + "（生命周期异常，不在 UI 层初始化）："
                        + $"{pawn.LabelShort}（{pawn.ThingID}）。",
                        WarningKeyTimetableNull);
                }

                return false;
            }

            return true;
        }
    }
}

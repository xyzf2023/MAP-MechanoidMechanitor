using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 机械族通用 timetable 数据修复入口（能力层“ColonistLikeTimetable”的真实修复实现）。
    /// 不要求 Pawn 是机械族机械师，仅服务被调用方明确授权的机械族 Pawn；
    /// 不依赖 Progression: Education，不依赖具体 PawnDef，不访问任何第三方 MOD。
    /// 完全幂等：已有合法 24 格时不做任何修改，绝不覆盖玩家已设置的作息或第三方写入的 Assignment。
    /// </summary>
    public static class ColonistLikeMechTimetableUtility
    {
        public static void EnsureTimetableState(Pawn? pawn)
        {
            if (pawn == null)
            {
                return;
            }

            // 防御：仅处理机械族 Pawn。业务系统负责在调用前确认其具备合法身份或组件来源。
            if (pawn.RaceProps?.IsMechanoid != true)
            {
                return;
            }

            // 4.2 tracker 缺失：创建并设为 24 格 Anything。
            // 不使用原版构造器默认夜间 Sleep：机械族历史上没有 timetable，
            // 旧档升级或新生成时不能突然获得夜间 Sleep 行为。
            if (pawn.timetable == null)
            {
                pawn.timetable = new Pawn_TimetableTracker(pawn);
                for (int hour = 0; hour < 24; hour++)
                {
                    pawn.timetable.SetAssignment(hour, TimeAssignmentDefOf.Anything);
                }

                return;
            }

            // 4.3 已存在且完全合法：times 非 null、Count==24、无 null 元素 -> 完全不修改。
            List<TimeAssignmentDef>? times = pawn.timetable.times;
            if (times != null && times.Count == 24)
            {
                bool allValid = true;
                for (int hour = 0; hour < 24; hour++)
                {
                    if (times[hour] == null)
                    {
                        allValid = false;
                        break;
                    }
                }

                if (allValid)
                {
                    return;
                }
            }

            // 到达此处说明需要修复：仅修补异常结构，保留玩家已设置的合法作息。
            // 第三方自定义 Assignment（如 Education 的 PE_DynamicClass_*）原样保留，绝不改写。

            // 4.4 times == null：重建为 24 格 Anything。
            if (times == null)
            {
                times = new List<TimeAssignmentDef>(24);
                for (int hour = 0; hour < 24; hour++)
                {
                    times.Add(TimeAssignmentDefOf.Anything);
                }

                pawn.timetable.times = times;
                return;
            }

            // 4.6 times.Count > 24：截断尾部，仅保留前 24 格（不重置全部作息）。
            if (times.Count > 24)
            {
                times.RemoveRange(24, times.Count - 24);
            }

            // 4.5 / 4.6 补齐不足 24 格，并修复前 24 格中的 null 元素（仅替换 null 为 Anything）。
            for (int hour = 0; hour < 24; hour++)
            {
                if (hour >= times.Count)
                {
                    times.Add(TimeAssignmentDefOf.Anything);
                }
                else if (times[hour] == null)
                {
                    times[hour] = TimeAssignmentDefOf.Anything;
                }
            }
        }
    }
}

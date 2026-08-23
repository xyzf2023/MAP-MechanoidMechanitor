using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// MAP 派系前哨的守军 LordJob。
    /// 行为上基于原版守基地（LordJob_DefendBase），并额外支持在「联合军事行动」中
    /// 由 QuestPart 调用 <see cref="TryStartJointOperationAssault"/> 将守军转为对玩家殖民地的主动进攻。
    /// 该类型只负责“前哨守军”这一特定 LordJob；主动进攻通过新建 AssaultColony Lord 实现，
    /// 不修改本 LordJob 自身的防守状态机。
    /// </summary>
    public class LordJob_MAPFactionOutpostDefendBase : LordJob_DefendBase
    {
        public LordJob_MAPFactionOutpostDefendBase()
            : base()
        {
        }

        /// <summary>
        /// 将给定前哨守军 Lord 由防守转为对玩家殖民地的主动进攻。
        /// 会提取该 Lord 当前有效的守军 Pawn，迁移到一个新的 LordJob_AssaultColony Lord，
        /// 并在旧 Lord 不再拥有任何 Pawn 时移除之。
        /// 返回 true 表示成功触发进攻（已迁移为 AssaultColony Lord）。
        /// </summary>
        public static bool TryStartJointOperationAssault(Lord lord)
        {
            if (lord == null)
            {
                return false;
            }

            Map? map = lord.Map;
            Faction? faction = lord.faction;
            if (map == null || faction == null)
            {
                return false;
            }

            List<Pawn> validDefenders = new List<Pawn>();
            foreach (Pawn p in new List<Pawn>(lord.ownedPawns))
            {
                if (p != null && !p.Destroyed && p.Spawned && p.Map == map && p.Faction == faction)
                {
                    validDefenders.Add(p);
                }
            }

            if (validDefenders.Count == 0)
            {
                return false;
            }

            // 先从旧 Lord 摘出守军，避免迁移过程中状态不一致。
            foreach (Pawn p in validDefenders)
            {
                lord.RemovePawn(p);
            }

            Lord? newLord = null;
            try
            {
                newLord = LordMaker.MakeNewLord(
                    faction,
                    new LordJob_AssaultColony(
                        faction,
                        canKidnap: false,
                        canTimeoutOrFlee: false,
                        sappers: false,
                        useAvoidGridSmart: true,
                        canSteal: false,
                        breachers: false,
                        canPickUpOpportunisticWeapons: false),
                    map,
                    validDefenders);
            }
            catch (System.Exception ex)
            {
                Log.Error(
                    "[MAP-JointOperation] Event=OutpostDefendLordAssaultMigrateFailed"
                    + " | reason=MakeNewLordException"
                    + " | targetFaction=" + (faction?.GetUniqueLoadID() ?? "null")
                    + " | map=" + map.GetUniqueLoadID()
                    + " | ex=" + ex);
                newLord = null;
            }

            if (newLord == null)
            {
                // 迁移失败：尽量把守军放回旧 Lord，避免无主 Pawn。
                foreach (Pawn p in validDefenders)
                {
                    lord.AddPawn(p);
                }

                return false;
            }

            if (lord.ownedPawns.Count == 0)
            {
                try
                {
                    map.lordManager.RemoveLord(lord);
                }
                catch (System.Exception ex)
                {
                    Log.Warning(
                        "[MAP-JointOperation] Event=OutpostDefendLordRemoveFailed"
                        + " | map=" + map.GetUniqueLoadID()
                        + " | ex=" + ex);
                }
            }

            return true;
        }
    }
}

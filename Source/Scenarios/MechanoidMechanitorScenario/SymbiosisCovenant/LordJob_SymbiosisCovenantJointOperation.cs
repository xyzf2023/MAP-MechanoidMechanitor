using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 联合军事行动专用的领主任务（LordJob）。
    ///
    /// 设计要点（相对原版 LordJob_AssistColony）：
    /// 1. 使用真实参与派系（allyFaction）生成出的真实 Pawn。
    /// 2. 初始行为是协助玩家攻击敌对目标（LordToil_HuntEnemies）。
    /// 3. 不使用固定 25000 ticks 自动撤离（AssistColony 的 Trigger_TicksPassed(25000)）。
    /// 4. 只有收到本行动自己的专用撤离 memo（SendLeave）时才开始离图。
    /// 5. 保留原版危险温度、无法到达边缘等基本撤离兜底逻辑，不影响任何非本行动 Lord。
    /// 6. 可存档（ExposeData 保存 allyFaction / enemyFaction / fallbackLocation）。
    ///
    /// 追踪方式：每只参与派系援军在生成时都会通过 QuestUtility.AddQuestTag(lord, aidTag)
    /// 写入唯一 aidTag；QuestPart 只通过“本 LordJob 类型 + lord.questTags.Contains(aidTag)”
    /// 精确找到本行动援军，避免误伤/误控原版普通援军、任务援军或其他 MOD 援军。
    /// </summary>
    public class LordJob_SymbiosisCovenantJointOperation : LordJob
    {
        public Faction? allyFaction;
        public Faction? enemyFaction;
        public IntVec3 fallbackLocation;

        // 本行动专用撤离 memo：仅由 QuestPart 在成功/失败/无效结束或地图无敌对威胁时发出。
        public const string LeaveMemo = "MAP_SymbiosisCovenantJointOp_Leave";

        public LordJob_SymbiosisCovenantJointOperation()
        {
        }

        public LordJob_SymbiosisCovenantJointOperation(
            Faction allyFaction,
            Faction? enemyFaction,
            IntVec3 fallbackLocation)
        {
            this.allyFaction = allyFaction;
            this.enemyFaction = enemyFaction;
            this.fallbackLocation = fallbackLocation;
        }

        /// <summary>
        /// 向指定本行动 Lord 发送专用撤离 memo，使其转入离图状态。
        /// 不 Destroy / Kill / 强制删除任何 Pawn。
        /// </summary>
        public static void SendLeave(Lord lord)
        {
            if (lord != null && lord.LordJob is LordJob_SymbiosisCovenantJointOperation)
            {
                lord.ReceiveMemo(LeaveMemo);
            }
        }

        public override StateGraph CreateGraph()
        {
            StateGraph graph = new StateGraph();

            LordToil_HuntEnemies huntEnemies = new LordToil_HuntEnemies(fallbackLocation);
            graph.AddToil(huntEnemies);

            StateGraph travelGraph = new LordJob_Travel(IntVec3.Invalid).CreateGraph();
            LordToil travelStartingToil =
                graph.AttachSubgraph(travelGraph).StartingToil;

            LordToil_ExitMap exitMap = new LordToil_ExitMap();
            graph.AddToil(exitMap);

            LordToil_ExitMap exitMap2 =
                new LordToil_ExitMap(LocomotionUrgency.Jog, canDig: true);
            graph.AddToil(exitMap2);

            // 危险温度兜底（与原版 AssistColony 一致）。
            Transition transitionTemp = new Transition(huntEnemies, travelStartingToil);
            transitionTemp.AddPreAction(new TransitionAction_Message(
                "MessageVisitorsDangerousTemperature".Translate(
                    allyFaction?.def?.pawnsPlural.CapitalizeFirst() ?? "pawns",
                    allyFaction?.Name ?? "???")));
            transitionTemp.AddPreAction(new TransitionAction_EnsureHaveExitDestination());
            transitionTemp.AddTrigger(new Trigger_PawnExperiencingDangerousTemperatures());
            transitionTemp.AddPostAction(new TransitionAction_EndAllJobs());
            graph.AddTransition(transitionTemp);

            // 无法到达边缘兜底（与原版 AssistColony 一致）。
            Transition transitionNoEdge = new Transition(huntEnemies, exitMap2);
            transitionNoEdge.AddSource(exitMap);
            transitionNoEdge.AddSources(travelGraph.lordToils);
            transitionNoEdge.AddPreAction(new TransitionAction_Message(
                "MessageVisitorsTrappedLeaving".Translate(
                    allyFaction?.def?.pawnsPlural.CapitalizeFirst() ?? "pawns",
                    allyFaction?.Name ?? "???")));
            transitionNoEdge.AddTrigger(new Trigger_PawnCannotReachMapEdge());
            graph.AddTransition(transitionNoEdge);

            Transition transitionCanReach = new Transition(exitMap2, travelStartingToil);
            transitionCanReach.AddTrigger(new Trigger_PawnCanReachMapEdge());
            transitionCanReach.AddPreAction(new TransitionAction_EnsureHaveExitDestination());
            graph.AddTransition(transitionCanReach);

            // 自定义撤离：仅在本行动发出专用 memo 时离开，不依赖固定 25000 ticks。
            Transition transitionLeave = new Transition(huntEnemies, travelStartingToil);
            transitionLeave.AddPreAction(new TransitionAction_Message(
                "MAP_MechanoidMechanitor.Symbiosis.JointOp.AidLeaving".Translate(
                    allyFaction?.Name ?? "???")));
            transitionLeave.AddPreAction(new TransitionAction_EnsureHaveExitDestination());
            transitionLeave.AddTrigger(new Trigger_Memo(LeaveMemo));
            graph.AddTransition(transitionLeave);

            Transition transitionArrived = new Transition(travelStartingToil, exitMap);
            transitionArrived.AddTrigger(new Trigger_Memo("TravelArrived"));
            graph.AddTransition(transitionArrived);

            return graph;
        }

        public override void ExposeData()
        {
            Scribe_References.Look(ref allyFaction, "allyFaction");
            Scribe_References.Look(ref enemyFaction, "enemyFaction");
            Scribe_Values.Look(ref fallbackLocation, "fallbackLocation");
        }
    }
}

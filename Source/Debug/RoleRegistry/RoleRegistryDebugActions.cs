using System.Collections.Generic;
using LudeonTK;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 角色注册表开发者指令：打开管理窗口，以及地图点击注册 Pawn。
    /// </summary>
    public static class RoleRegistryDebugActions
    {
        [DebugAction("MAP-机械族机械师", "查看角色注册表",
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
        private static void OpenRoleRegistryDialog()
        {
            if (Current.Game == null)
            {
                Messages.Message(
                    "无法打开角色注册表：当前没有有效游戏。",
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
            }

            if (Current.Game.GetComponent<GameComponent_MechanoidMechanitorRegistry>() == null
                || Current.Game.GetComponent<GameComponent_SyntheticCompanionRegistry>() == null
                || Current.Game.GetComponent<GameComponent_MechanicalFlightRegistry>() == null
                || Current.Game.GetComponent<GameComponent_MechFusionRegistry>() == null
                || Current.Game.GetComponent<GameComponent_AutonomousMechRegistry>() == null)
            {
                Messages.Message(
                    "无法打开角色注册表：注册表组件不可用。",
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
            }

            Find.WindowStack.Add(new Dialog_RoleRegistryDebug());
        }

        [DebugAction("MAP-机械族机械师", "添加目标到角色注册表...",
            actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void AddPawnToRoleRegistry(Pawn clickedPawn)
        {
            if (clickedPawn == null
                || clickedPawn.Dead || clickedPawn.Destroyed || clickedPawn.Discarded)
            {
                Messages.Message(
                    "拒绝添加：目标已死亡、已销毁或已永久丢弃。",
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
            }

            List<FloatMenuOption> options = new List<FloatMenuOption>();

            if (clickedPawn.RaceProps?.IsMechanoid == true)
            {
                bool hasIndependentAutonomy =
                    GameComponent_AutonomousMechRegistry.TryGetRecord(clickedPawn,
                        out AutonomousMechAuthorizationRecord? autonomy)
                    && autonomy!.HasIndependentAuthorization;
                if (hasIndependentAutonomy)
                {
                    options.Add(new FloatMenuOption("授予独立自律资格（已经授权）", null));
                }
                else
                {
                    Pawn autonomyPawn = clickedPawn;
                    options.Add(new FloatMenuOption("授予独立自律资格", () =>
                    {
                        bool granted = GameComponent_AutonomousMechRegistry.TryAuthorize(autonomyPawn);
                        Messages.Message(granted
                                ? "已授予独立自律资格：" + autonomyPawn.LabelShortCap + "。"
                                : "无法授予：需要已初始化、存活且具有监管状态组件的机械体。",
                            granted ? MessageTypeDefOf.TaskCompletion : MessageTypeDefOf.RejectInput,
                            historical: false);
                    }));
                }

                bool hasMechanitorRecord =
                    GameComponent_MechanoidMechanitorRegistry.HasPersistentRecord(clickedPawn);
                if (hasMechanitorRecord)
                {
                    options.Add(new FloatMenuOption(
                        "加入机械族机械师注册表（已经注册）",
                        null));
                }
                else
                {
                    Pawn localPawn = clickedPawn;
                    options.Add(new FloatMenuOption(
                        "加入机械族机械师注册表",
                        () => TryRegisterMechanitor(localPawn)));
                }

                bool hasCompanionRecord =
                    GameComponent_SyntheticCompanionRegistry.HasAuthorizationRecord(clickedPawn);
                if (hasCompanionRecord)
                {
                    options.Add(new FloatMenuOption(
                        "加入仿生伴侣注册表（已经注册）",
                        null));
                }
                else
                {
                    Pawn localPawn = clickedPawn;
                    options.Add(new FloatMenuOption(
                        "加入仿生伴侣注册表",
                        () => TryAuthorizeCompanion(localPawn)));
                }

                bool hasFusionRecord =
                    GameComponent_MechFusionRegistry.HasEligibility(clickedPawn);
                if (hasFusionRecord)
                {
                    options.Add(new FloatMenuOption(
                        "加入合体资格注册表（已经注册）",
                        null));
                }
                else
                {
                    Pawn localPawn = clickedPawn;
                    options.Add(new FloatMenuOption(
                        "加入合体资格注册表",
                        () => TryRegisterMechFusion(localPawn)));
                }
            }

            bool hasFlightRecord =
                GameComponent_MechanicalFlightRegistry.HasAuthorizationRecord(clickedPawn);
            if (hasFlightRecord)
            {
                options.Add(new FloatMenuOption(
                    "加入飞行授权注册表（已经注册）",
                    null));
            }
            else
            {
                Pawn localPawn = clickedPawn;
                options.Add(new FloatMenuOption(
                    "加入飞行授权注册表",
                    () => TryAuthorizeFlight(localPawn)));
            }

            Find.WindowStack.Add(new FloatMenu(options, clickedPawn.LabelShortCap));
        }

        private static void TryRegisterMechanitor(Pawn pawn)
        {
            if (GameComponent_MechanoidMechanitorRegistry.TryRegisterFromDebug(pawn))
            {
                Messages.Message(
                    "已加入机械族机械师注册表：" + pawn.LabelShortCap + "。",
                    MessageTypeDefOf.TaskCompletion,
                    historical: false);
            }
            else
            {
                Messages.Message(
                    "加入机械族机械师注册表失败：" + pawn.LabelShortCap + "。",
                    MessageTypeDefOf.RejectInput,
                    historical: false);
            }
        }

        private static void TryAuthorizeCompanion(Pawn pawn)
        {
            if (GameComponent_SyntheticCompanionRegistry.TryAuthorize(pawn))
            {
                Messages.Message(
                    "已加入仿生伴侣注册表：" + pawn.LabelShortCap + "。",
                    MessageTypeDefOf.TaskCompletion,
                    historical: false);
            }
            else
            {
                Messages.Message(
                    "加入仿生伴侣注册表失败：" + pawn.LabelShortCap + "。",
                    MessageTypeDefOf.RejectInput,
                    historical: false);
            }
        }

        private static void TryAuthorizeFlight(Pawn pawn)
        {
            if (GameComponent_MechanicalFlightRegistry.TryAuthorize(
                    pawn,
                    source: MechanicalFlightAuthorizationSource.Debug))
            {
                Messages.Message(
                    "已加入飞行授权注册表：" + pawn.LabelShortCap + "。",
                    MessageTypeDefOf.TaskCompletion,
                    historical: false);
            }
            else
            {
                Messages.Message(
                    "加入飞行授权注册表失败：" + pawn.LabelShortCap + "。",
                    MessageTypeDefOf.RejectInput,
                    historical: false);
            }
        }

        private static void TryRegisterMechFusion(Pawn pawn)
        {
            if (GameComponent_MechFusionRegistry.TryRegisterFromDebug(pawn))
            {
                Messages.Message(
                    "已加入合体资格注册表：" + pawn.LabelShortCap + "。",
                    MessageTypeDefOf.TaskCompletion,
                    historical: false);
            }
            else
            {
                Messages.Message(
                    "加入合体资格注册表失败：" + pawn.LabelShortCap + "。",
                    MessageTypeDefOf.RejectInput,
                    historical: false);
            }
        }
    }
}

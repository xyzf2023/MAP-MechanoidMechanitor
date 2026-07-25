using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    // 原版 "DEV: Assign to overseer" 只扫描 FreeColonists，因此不会列出 MAP 机械体机械师节点。
    // 本补丁只新增一个 dev-only 按钮，不替换或修改原版按钮。
    // 按钮从当前地图 AllPawnsSpawned 中筛选 MAP mechanitor node controller。
    // 赋值过程尽量复用原版 overseer relation 流程（AddDirectRelation）。
    // 该按钮不属于正式游戏 UI，仅用于 DevMode / GodMode 调试便利。
    [HarmonyPatch(typeof(CompOverseerSubject), nameof(CompOverseerSubject.CompGetGizmosExtra))]
    public static class Patch_CompOverseerSubject_DevAssignMapOverseer
    {
        [HarmonyPostfix]
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, CompOverseerSubject __instance)
        {
            foreach (Gizmo gizmo in __result)
            {
                yield return gizmo;
            }

            if (!DebugSettings.ShowDevGizmos)
            {
                yield break;
            }

            Pawn subject = __instance.Parent;
            if (subject == null || subject.Map == null)
            {
                yield break;
            }

            yield return MakeAssignToMapOverseerCommand(subject);
        }

        private static Command_Action MakeAssignToMapOverseerCommand(Pawn subject)
        {
            Command_Action command = new Command_Action();
            command.defaultLabel = "DEV: Assign to MAP overseer";
            command.action = delegate
            {
                List<FloatMenuOption> options = BuildMapOverseerOptions(subject);
                if (options.Count > 0)
                {
                    Find.WindowStack.Add(new FloatMenu(options));
                }
            };
            return command;
        }

        private static List<FloatMenuOption> BuildMapOverseerOptions(Pawn subject)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            HashSet<Pawn> seen = new HashSet<Pawn>();
            IReadOnlyList<Pawn> allSpawned = subject.Map.mapPawns.AllPawnsSpawned;

            for (int i = 0; i < allSpawned.Count; i++)
            {
                Pawn candidate = allSpawned[i];
                if (!IsValidMapOverseerCandidate(candidate))
                {
                    continue;
                }

                if (!seen.Add(candidate))
                {
                    continue;
                }

                Pawn localPawn = candidate;
                options.Add(new FloatMenuOption(localPawn.LabelShortCap, delegate
                {
                    AssignSelectedMechsToMapOverseer(localPawn);
                }));
            }

            return options;
        }

        private static bool IsValidMapOverseerCandidate(Pawn? candidate)
        {
            if (candidate == null || !candidate.Spawned || candidate.Dead)
            {
                return false;
            }

            if (candidate.Faction == null || !candidate.Faction.IsPlayerSafe())
            {
                return false;
            }

            if (candidate.mechanitor == null)
            {
                return false;
            }

            if (!MAPMechanitorNodeUtility.IsMechanitorNodeController(candidate))
            {
                return false;
            }

            return MechanitorUtility.IsMechanitor(candidate);
        }

        private static void AssignSelectedMechsToMapOverseer(Pawn localPawn)
        {
            foreach (Pawn target in Find.Selector.SelectedPawns.Where(p => p.RaceProps.IsMechanoid))
            {
                if (target == localPawn)
                {
                    continue;
                }

                if (MAPMechanitorNodeUtility.HasNode(target)
                    && !MAPMechanitorNodeUtility.RequiresExternalOverseer(target))
                {
                    continue;
                }

                // 旧监管者移除交由统一工具处理，避免用 target.GetOverseer() 得到错误方向。
                target.SetFaction(Faction.OfPlayer);
                MAPOverseerAssignmentUtility.TryAssignActualOverseer(localPawn, target);
            }
        }
    }
}

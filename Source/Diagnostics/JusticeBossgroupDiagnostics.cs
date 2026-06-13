using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using MAP_MechanoidMechanitor;
using RimWorld;
using Verse;
using Verse.AI;

namespace MMT
{
    internal static class JusticeBossgroupDiagnostics
    {
        private const string LogPrefix = "[MAP Justice Bossgroup Diagnostic]";
        private const int ThrottleTicks = 2500;

        private static readonly FieldInfo MechanitorField =
            AccessTools.Field(typeof(Command_CallBossgroup), "mechanitor");

        private static readonly Dictionary<string, int> LastLogTickByKey = new Dictionary<string, int>();
        private static readonly Dictionary<int, bool?> LastGizmoDisabledByPawn = new Dictionary<int, bool?>();

        internal static bool ShouldDiagnose(Pawn? pawn)
        {
            return Prefs.DevMode
                && pawn != null
                && MAPMechanitorNodeUtility.HasNode(pawn)
                && !MAPMechanitorNodeUtility.RequiresExternalOverseer(pawn);
        }

        private static bool ShouldLog(string key, bool bypassThrottle = false)
        {
            if (!Prefs.DevMode)
            {
                return false;
            }

            int tick = Find.TickManager.TicksGame;
            if (!bypassThrottle
                && LastLogTickByKey.TryGetValue(key, out int lastTick)
                && tick - lastTick < ThrottleTicks)
            {
                return false;
            }

            LastLogTickByKey[key] = tick;
            return true;
        }

        private static string FormatAcceptanceReport(AcceptanceReport report)
        {
            return $"accepted={report.Accepted}, reason={(report.Reason.NullOrEmpty() ? "null" : report.Reason)}";
        }

        private static string FormatPawnState(Pawn pawn)
        {
            return $"def={pawn.def.defName}, label={pawn.LabelShort}, faction={pawn.Faction?.Name ?? "null"}, " +
                   $"Spawned={pawn.Spawned}, Downed={pawn.Downed}, Dead={pawn.Dead}, " +
                   $"mechanitor={(pawn.mechanitor != null)}, IsMechanitor={MechanitorUtility.IsMechanitor(pawn)}, " +
                   $"ShouldBeMechanitor={MechanitorUtility.ShouldBeMechanitor(pawn)}, " +
                   $"Manipulation={pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)}";
        }

        private static void AppendCallerDetails(StringBuilder sb, Pawn pawn, Thing caller, bool forced, bool useTouchReach = false)
        {
            sb.AppendLine(
                $"  caller {caller.def.defName}@{caller.Position}: Spawned={caller.Spawned}, " +
                $"Faction={caller.Faction?.Name ?? "null"}");

            PathEndMode reachMode = useTouchReach ? PathEndMode.Touch : PathEndMode.InteractionCell;
            sb.AppendLine(
                $"    CanReach({reachMode})={pawn.CanReach(caller, reachMode, Danger.Deadly)}, " +
                $"CanReserve={pawn.CanReserve(caller, 1, -1, null, forced)}");

            if (useTouchReach)
            {
                sb.AppendLine(
                    $"    CanReserveAndReach(Touch)={pawn.CanReserveAndReach(caller, PathEndMode.Touch, Danger.Deadly, 1, -1, null, forced)}");
            }

            CompPowerTrader? powerTrader = caller.TryGetComp<CompPowerTrader>();
            if (powerTrader != null)
            {
                sb.AppendLine($"    CompPowerTrader.PowerOn={powerTrader.PowerOn}");
            }
            else
            {
                sb.AppendLine("    CompPowerTrader=null");
            }

            CompUsable? usable = caller.TryGetComp<CompUsable>();
            if (usable != null)
            {
                AcceptanceReport usableReport = usable.CanBeUsedBy(pawn, forced);
                sb.AppendLine($"    CompUsable.CanBeUsedBy={FormatAcceptanceReport(usableReport)}");
            }
            else
            {
                sb.AppendLine("    CompUsable=null");
            }
        }

        private static void AppendBossgroupCallerSummary(StringBuilder sb, Pawn pawn, BossgroupDef def, bool forced, bool useTouchReach = false)
        {
            ThingDef? callerDef = CallBossgroupUtility.GetBossgroupCaller(def);
            if (callerDef == null)
            {
                sb.AppendLine("  GetBossgroupCaller=null");
                return;
            }

            sb.AppendLine($"  GetBossgroupCaller={callerDef.defName}");
            if (pawn.Map == null)
            {
                sb.AppendLine("  pawn.Map=null");
                return;
            }

            List<Thing> callers = pawn.Map.listerThings.ThingsOfDef(callerDef);
            sb.AppendLine($"  callerCount={callers.Count}");
            for (int i = 0; i < callers.Count; i++)
            {
                AppendCallerDetails(sb, pawn, callers[i], forced, useTouchReach);
            }
        }

        [HarmonyPatch(typeof(Command_CallBossgroup), nameof(Command_CallBossgroup.GizmoOnGUI))]
        public static class Command_CallBossgroup_GizmoOnGUI_DiagnosticsPatch
        {
            [HarmonyPostfix]
            public static void Postfix(Command_CallBossgroup __instance)
            {
                if (MechanitorField.GetValue(__instance) is not Pawn_MechanitorTracker mechanitorTracker)
                {
                    return;
                }

                Pawn pawn = mechanitorTracker.Pawn;
                if (!ShouldDiagnose(pawn))
                {
                    return;
                }

                bool disabled = __instance.Disabled;
                bool stateChanged = !LastGizmoDisabledByPawn.TryGetValue(pawn.thingIDNumber, out bool? lastDisabled)
                    || lastDisabled != disabled;
                LastGizmoDisabledByPawn[pawn.thingIDNumber] = disabled;

                string logKey = $"Gizmo:{pawn.thingIDNumber}:{__instance.defaultLabel}";
                if (!ShouldLog(logKey, bypassThrottle: stateChanged))
                {
                    return;
                }

                int lastBossgroupCalled = Find.BossgroupManager.lastBossgroupCalled;
                int ticksSinceLastCall = Find.TickManager.TicksGame - lastBossgroupCalled;
                PawnKindDef? pendingBossgroup = CallBossgroupUtility.GetPendingBossgroup();

                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"{LogPrefix} Command_CallBossgroup.GizmoOnGUI");
                sb.AppendLine($"  command.defaultLabel={__instance.defaultLabel}");
                sb.AppendLine($"  command.disabled={disabled}, command.disabledReason={__instance.disabledReason ?? "null"}");
                sb.AppendLine($"  mechanitor.pawn: {FormatPawnState(pawn)}");
                sb.AppendLine($"  lastBossgroupCalled={lastBossgroupCalled}, ticksGame={Find.TickManager.TicksGame}, ticksSinceLastCall={ticksSinceLastCall}");
                sb.AppendLine($"  GetPendingBossgroup={(pendingBossgroup?.defName ?? "null")}");
                sb.AppendLine($"  Faction.OfMechanoids null={Faction.OfMechanoids == null}, deactivated={Faction.OfMechanoids?.deactivated.ToString() ?? "n/a"}");

                bool allActionsNull = true;
                foreach (BossgroupDef bg in DefDatabase<BossgroupDef>.AllDefs)
                {
                    AcceptanceReport report = CallBossgroupUtility.BossgroupEverCallable(pawn, bg);
                    sb.AppendLine(
                        $"  BossgroupEverCallable({bg.defName}, boss={bg.boss.kindDef.defName}) => {FormatAcceptanceReport(report)}");
                    if (report)
                    {
                        allActionsNull = false;
                    }
                }

                sb.AppendLine(
                    $"  allFloatMenuActionsNull={allActionsNull} (IsDisabled reason-null path when true and earlier checks pass)");

                Log.Message(sb.ToString());
            }
        }

        [HarmonyPatch(typeof(CallBossgroupUtility), nameof(CallBossgroupUtility.BossgroupEverCallable))]
        public static class CallBossgroupUtility_BossgroupEverCallable_DiagnosticsPatch
        {
            [HarmonyPostfix]
            public static void Postfix(Pawn pawn, BossgroupDef def, bool forced, ref AcceptanceReport __result)
            {
                if (!ShouldDiagnose(pawn))
                {
                    return;
                }

                string logKey = $"BossgroupEverCallable:{pawn.thingIDNumber}:{def.defName}";
                if (!ShouldLog(logKey))
                {
                    return;
                }

                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"{LogPrefix} CallBossgroupUtility.BossgroupEverCallable");
                sb.AppendLine($"  bossgroupDef={def.defName}, boss={def.boss.kindDef.defName}");
                sb.AppendLine($"  result={FormatAcceptanceReport(__result)}");
                sb.AppendLine($"  pawn: {FormatPawnState(pawn)}");
                AppendBossgroupCallerSummary(sb, pawn, def, forced);

                Log.Message(sb.ToString());
            }
        }

        [HarmonyPatch(typeof(CompUseEffect_CallBossgroup), nameof(CompUseEffect_CallBossgroup.CanBeUsedBy))]
        public static class CompUseEffect_CallBossgroup_CanBeUsedBy_DiagnosticsPatch
        {
            [HarmonyPostfix]
            public static void Postfix(Pawn p, CompUseEffect_CallBossgroup __instance, ref AcceptanceReport __result)
            {
                if (!ShouldDiagnose(p))
                {
                    return;
                }

                Thing parent = __instance.parent;
                string logKey = $"CompUseEffect:{p.thingIDNumber}:{parent.thingIDNumber}";
                if (!ShouldLog(logKey))
                {
                    return;
                }

                AcceptanceReport canResolve = __instance.Props.bossgroupDef.Worker.CanResolve(p);

                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"{LogPrefix} CompUseEffect_CallBossgroup.CanBeUsedBy");
                sb.AppendLine(
                    $"  parent={parent.def.defName} ({parent.LabelCap}) @{parent.Position}");
                sb.AppendLine(
                    $"  Props.bossgroupDef={__instance.Props.bossgroupDef.defName}, boss={__instance.Props.bossgroupDef.boss.kindDef.defName}");
                sb.AppendLine($"  result={FormatAcceptanceReport(__result)}");
                sb.AppendLine($"  Faction.OfMechanoids null={Faction.OfMechanoids == null}, deactivated={Faction.OfMechanoids?.deactivated.ToString() ?? "n/a"}");
                sb.AppendLine($"  MechanitorUtility.IsMechanitor={MechanitorUtility.IsMechanitor(p)}, p.mechanitor={(p.mechanitor != null)}");
                sb.AppendLine(
                    $"  p.Spawned={p.Spawned}, Downed={p.Downed}, Dead={p.Dead}, " +
                    $"Manipulation={p.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)}");
                sb.AppendLine($"  Worker.CanResolve={FormatAcceptanceReport(canResolve)}");

                Log.Message(sb.ToString());
            }
        }

        [HarmonyPatch(typeof(CallBossgroupUtility), nameof(CallBossgroupUtility.TryStartSummonBossgroupJob))]
        public static class CallBossgroupUtility_TryStartSummonBossgroupJob_DiagnosticsPatch
        {
            [HarmonyPrefix]
            public static void Prefix(BossgroupDef def, Pawn pawn, ref Job? __state)
            {
                __state = null;
                if (!ShouldDiagnose(pawn))
                {
                    return;
                }

                __state = pawn.jobs.curJob;
            }

            [HarmonyPostfix]
            public static void Postfix(BossgroupDef def, Pawn pawn, bool forced, Job? __state)
            {
                if (!ShouldDiagnose(pawn))
                {
                    return;
                }

                string logKey = $"TryStartSummon:{pawn.thingIDNumber}:{def.defName}";
                if (!ShouldLog(logKey))
                {
                    return;
                }

                Job? curJob = pawn.jobs.curJob;
                bool jobChanged = __state != curJob;

                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"{LogPrefix} CallBossgroupUtility.TryStartSummonBossgroupJob");
                sb.AppendLine($"  entered=true, bossgroupDef={def.defName}, forced={forced}");
                sb.AppendLine($"  pawn: {FormatPawnState(pawn)}");
                AppendBossgroupCallerSummary(sb, pawn, def, forced, useTouchReach: true);
                sb.AppendLine($"  jobBefore={(FormatJob(__state))}");
                sb.AppendLine($"  jobAfter={(FormatJob(curJob))}");
                sb.AppendLine($"  jobChanged={jobChanged}");

                Log.Message(sb.ToString());
            }

            private static string FormatJob(Job? job)
            {
                if (job == null)
                {
                    return "null";
                }

                return $"{job.def.defName} targetA={job.targetA}, targetB={job.targetB}";
            }
        }
    }
}

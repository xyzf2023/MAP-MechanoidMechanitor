using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using UnlockGroup = Verse.Pair<RimWorld.ResearchPrerequisitesUtility.UnlockedHeader, System.Collections.Generic.List<Verse.Def>>;

namespace MAP_MechanoidMechanitor.GD5Expansion;

[StaticConstructorOnStartup]
internal static class SunEnergyCouplingBridge
{
    private const float PowerOutput = 100000f;
    private const string DefName = "MAP_SunEnergyCouplingBridge";
    private static readonly ThingDef Sun = DefDatabase<ThingDef>.GetNamed("MAP_Mech_Sun");
    private static readonly ThingDef Item = DefDatabase<ThingDef>.GetNamed(DefName);
    private static readonly HediffDef Implant = DefDatabase<HediffDef>.GetNamed(DefName);
    private static readonly ResearchProjectDef SunResearch =
        DefDatabase<ResearchProjectDef>.GetNamed("MAP_AntiparticleApplications");
    private static readonly ResearchProjectDef ModificationResearch =
        DefDatabase<ResearchProjectDef>.GetNamed("QCWF_MechanicalModification");

    static SunEnergyCouplingBridge()
    {
        Harmony harmony = new(ExpansionCompatibilityBootstrap.SourceId + ".sunenergy");
        harmony.Patch(AccessTools.Method(typeof(PowerNet), nameof(PowerNet.CurrentEnergyGainRate)),
            postfix: new HarmonyMethod(typeof(SunEnergyCouplingBridge), nameof(AddEnergy)));
        harmony.Patch(AccessTools.PropertyGetter(typeof(PowerNet), nameof(PowerNet.HasActivePowerSource)),
            postfix: new HarmonyMethod(typeof(SunEnergyCouplingBridge), nameof(HasPowerSource)));
        harmony.Patch(AccessTools.Method(typeof(MainTabWindow_Research), "UnlockedDefsGroupedByPrerequisites"),
            postfix: new HarmonyMethod(typeof(SunEnergyCouplingBridge), nameof(FilterResearchUnlocks)));
    }

    private static int CountProviders(PowerNet net)
    {
        Map? map = net.powerNetManager?.map;
        if (map == null) return 0;

        int count = 0;
        var pawns = map.mapPawns.AllPawnsSpawned;
        for (int i = 0; i < pawns.Count; i++)
        {
            Pawn pawn = pawns[i];
            // 仅要求太阳实际在图、存活并持有配件，不检查阵营或工作状态。
            if (pawn.def == Sun && pawn.Spawned && pawn.Map == map && !pawn.Dead
                && pawn.health.hediffSet.HasHediff(Implant))
                count++;
        }
        return count;
    }

    private static void AddEnergy(PowerNet __instance, ref float __result)
    {
        // 原版结算单位为瓦日每刻；每个独立电网分别获得完整输出。
        __result += CountProviders(__instance) * PowerOutput * CompPower.WattsToWattDaysPerTick;
    }

    private static void HasPowerSource(PowerNet __instance, ref bool __result)
    {
        if (!__result && CountProviders(__instance) > 0)
            __result = true;
    }

    private static void FilterResearchUnlocks(MainTabWindow_Research __instance,
        ResearchProjectDef project, ref List<UnlockGroup> __result)
    {
        if (project != ModificationResearch
            || (!SunResearch.IsHidden && __instance.VisibleResearchProjects.Contains(SunResearch)))
            return;

        // 只过滤本次显示和搜索结果，不改共享缓存，发现太阳科技后即可正常显示。
        List<UnlockGroup> visible = new(__result.Count);
        foreach (UnlockGroup group in __result)
        {
            List<Def> defs = group.Second.FindAll(def => def != Item);
            if (defs.Count > 0)
                visible.Add(new UnlockGroup(group.First, defs));
        }
        __result = visible;
    }
}
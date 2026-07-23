using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 机械集群建筑落地后的公共初始化，供集群部署与完整机械巢节点共用。
    /// 同一批建筑应只调用一次，避免重复初始化或重复安排生成时间。
    /// </summary>
    public static class MechClusterBuildingInitUtility
    {
        public const float InitiationChance = 0.6f;

        public static readonly FloatRange InitiationDelayDays = new FloatRange(0.1f, 15f);

        public static readonly FloatRange MechAssemblerInitialDelayDays =
            new FloatRange(0.5f, 1.5f);

        public readonly struct InitOptions
        {
            public readonly bool StartDormant;

            public readonly bool ImmediateAllConditionCausers;

            public readonly ThingDef? ImmediateConditionCauserDef;

            public readonly bool ApplyRandomInitiation;

            public readonly float RandomInitiationDays;

            public readonly int AssemblerDelayTicks;

            public InitOptions(
                bool startDormant,
                bool immediateAllConditionCausers,
                ThingDef? immediateConditionCauserDef,
                bool applyRandomInitiation,
                float randomInitiationDays,
                int assemblerDelayTicks)
            {
                StartDormant = startDormant;
                ImmediateAllConditionCausers = immediateAllConditionCausers;
                ImmediateConditionCauserDef = immediateConditionCauserDef;
                ApplyRandomInitiation = applyRandomInitiation;
                RandomInitiationDays = randomInitiationDays;
                AssemblerDelayTicks = assemblerDelayTicks;
            }

            /// <summary>完整节点：休眠，全部状态建筑立即生效，装配器使用正常首次生成延迟。</summary>
            public static InitOptions ForCompletedNode()
            {
                return new InitOptions(
                    startDormant: true,
                    immediateAllConditionCausers: true,
                    immediateConditionCauserDef: null,
                    applyRandomInitiation: false,
                    randomInitiationDays: 0f,
                    assemblerDelayTicks: (int)(MechAssemblerInitialDelayDays.RandomInRange * 60000f));
            }

            /// <summary>集群部署：按订单状态建筑立即生效，其余可随机延迟。</summary>
            public static InitOptions ForClusterDeployment(
                ThingDef? requestedConditionCauser,
                bool startDormant)
            {
                bool applyRandom = Rand.Chance(InitiationChance);
                return new InitOptions(
                    startDormant: startDormant,
                    immediateAllConditionCausers: false,
                    immediateConditionCauserDef: requestedConditionCauser,
                    applyRandomInitiation: applyRandom,
                    randomInitiationDays: InitiationDelayDays.RandomInRange,
                    assemblerDelayTicks: (int)(MechAssemblerInitialDelayDays.RandomInRange * 60000f));
            }
        }

        /// <summary>
        /// 对已落地建筑执行一次初始化：派系、休眠、护盾战败通知、状态建筑、CompSpawnerPawn、威胁建筑登记。
        /// </summary>
        public static void InitializeSpawnedBuildings(
            List<Thing> spawnedThings,
            Faction faction,
            LordJob_MechanoidDefendBase? lordJob,
            Lord? lord,
            InitOptions options)
        {
            if (spawnedThings == null || faction == null)
            {
                return;
            }

            for (int i = 0; i < spawnedThings.Count; i++)
            {
                Thing thing = spawnedThings[i];
                if (thing == null || thing.Destroyed)
                {
                    continue;
                }

                if (thing.def.CanHaveFaction)
                {
                    thing.SetFaction(faction);
                }

                if (options.StartDormant)
                {
                    thing.TryGetComp<CompCanBeDormant>()?.ToSleep();
                }

                thing.TryGetComp<CompSpawnerPawn>()
                    ?.CalculateNextPawnSpawnTick(options.AssemblerDelayTicks);

                if (lordJob != null && thing.TryGetComp<CompProjectileInterceptor>() != null)
                {
                    lordJob.AddThingToNotifyOnDefeat(thing);
                }

                CompInitiatable? initiatable = thing.TryGetComp<CompInitiatable>();
                if (initiatable != null)
                {
                    if (options.ImmediateAllConditionCausers
                        || (options.ImmediateConditionCauserDef != null
                            && thing.def == options.ImmediateConditionCauserDef))
                    {
                        initiatable.initiationDelayTicksOverride = 1;
                    }
                    else if (options.ApplyRandomInitiation)
                    {
                        initiatable.initiationDelayTicksOverride =
                            (int)(60000f * options.RandomInitiationDays);
                    }
                }

                if (lord != null
                    && thing is Building building
                    && MechClusterBuildingUtility.IsBuildingThreat(building))
                {
                    lord.AddBuilding(building);
                }
            }

            if (!options.StartDormant)
            {
                for (int i = 0; i < spawnedThings.Count; i++)
                {
                    spawnedThings[i]
                        ?.TryGetComp<CompWakeUpDormant>()
                        ?.Activate(null, sendSignal: true, silent: true);
                }
            }
        }
    }
}

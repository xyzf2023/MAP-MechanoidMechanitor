using System;
using System.Collections.Generic;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class CompProperties_DormantJustice : CompProperties
    {
        public CompProperties_DormantJustice()
        {
            compClass = typeof(CompDormantJustice);
        }
    }

    public sealed class CompDormantJustice : ThingComp
    {
        public const string BuildingDefName = "MAP_Building_JusticeDormant";
        public const int InitializationDurationTicks = 900;

        private const string ActivateLabelKey =
            "MAP_MechanoidMechanitor.DormantJustice.Activate.Label";
        private const string ActivateDescriptionKey =
            "MAP_MechanoidMechanitor.DormantJustice.Activate.Description";
        private const string InitializingInspectKey =
            "MAP_MechanoidMechanitor.DormantJustice.Initializing";
        private const string EmergencyCarryLabelKey =
            "MAP_MechanoidMechanitor.DormantJustice.EmergencyCarry.Label";
        private const string EmergencyCarryDescriptionKey =
            "MAP_MechanoidMechanitor.DormantJustice.EmergencyCarry.Description";

        private bool isInitializing;
        private int remainingInitTicks;
        private bool carriesEmergencyConsciousnessTransfer;
        private bool activationInProgress;

        private Effecter? progressBarEffecter;

        public bool IsInitializing => isInitializing;
        public bool CarriesEmergencyConsciousnessTransfer => carriesEmergencyConsciousnessTransfer;
        public bool IsActivationInProgress => activationInProgress;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref isInitializing, "isInitializing");
            Scribe_Values.Look(ref remainingInitTicks, "remainingInitTicks");
            Scribe_Values.Look(
                ref carriesEmergencyConsciousnessTransfer,
                "carriesEmergencyConsciousnessTransfer");
        }

        public override void CompTick()
        {
            base.CompTick();
            if (!isInitializing)
            {
                return;
            }

            if (!IsValidSpawnedPlayerBuilding())
            {
                CancelInitialization();
                return;
            }

            remainingInitTicks--;
            TickProgressBar();

            if (remainingInitTicks <= 0)
            {
                CompleteInitialization();
            }
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            CancelInitialization();
        }

        public override void PostDestroy(DestroyMode mode, Map prevMap)
        {
            base.PostDestroy(mode, prevMap);
            CancelInitialization();
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }

            if (parent is not Building building
                || building.Faction != Faction.OfPlayer)
            {
                yield break;
            }

            Command_Action activateCommand = new Command_Action
            {
                defaultLabel = ActivateLabelKey.Translate(),
                defaultDesc = ActivateDescriptionKey.Translate(),
                icon = TexCommand.DesirePower,
                action = BeginInitialization,
            };

            if (isInitializing || activationInProgress)
            {
                activateCommand.Disable(InitializingInspectKey.Translate());
            }

            yield return activateCommand;

            if (!ShouldShowEmergencyCarryToggle(building))
            {
                yield break;
            }

            yield return new Command_Toggle
            {
                defaultLabel = EmergencyCarryLabelKey.Translate(),
                defaultDesc = EmergencyCarryDescriptionKey.Translate(),
                icon = TexCommand.ForbidOff,
                isActive = () => carriesEmergencyConsciousnessTransfer,
                toggleAction = ToggleEmergencyCarry,
                activateIfAmbiguous = true,
            };
        }

        public override string CompInspectStringExtra()
        {
            if (!isInitializing)
            {
                return base.CompInspectStringExtra();
            }

            return InitializingInspectKey.Translate();
        }

        public static CompDormantJustice? FindDesignatedEmergencyCarrier()
        {
            ThingDef? def = DefDatabase<ThingDef>.GetNamedSilentFail(BuildingDefName);
            if (def == null || Current.Game == null)
            {
                return null;
            }

            List<CompDormantJustice> candidates = new List<CompDormantJustice>();
            IReadOnlyList<Map> maps = Find.Maps;
            for (int mapIndex = 0; mapIndex < maps.Count; mapIndex++)
            {
                Map? map = maps[mapIndex];
                if (map == null)
                {
                    continue;
                }

                List<Thing> things = map.listerThings.ThingsOfDef(def);
                for (int i = 0; i < things.Count; i++)
                {
                    if (things[i] is not Building building
                        || building.Destroyed
                        || !building.Spawned
                        || building.Faction == null
                        || !building.Faction.IsPlayerSafe())
                    {
                        continue;
                    }

                    CompDormantJustice? comp = building.GetComp<CompDormantJustice>();
                    if (comp != null && comp.carriesEmergencyConsciousnessTransfer)
                    {
                        candidates.Add(comp);
                    }
                }
            }

            if (candidates.Count == 0)
            {
                return null;
            }

            if (candidates.Count == 1)
            {
                return candidates[0];
            }

            Map? hostMap =
                GameComponent_MechanoidMechanitorRegistry.CurrentMechanicalConsciousnessHost?.MapHeld;
            candidates.Sort((a, b) => CompareEmergencyCarrierPriority(a, b, hostMap, maps));
            return candidates[0];
        }

        internal void PrepareForActivationAttempt()
        {
            CancelInitialization();
        }

        internal void BeginActivationAttempt()
        {
            activationInProgress = true;
        }

        internal void EndActivationAttempt()
        {
            activationInProgress = false;
        }

        private void BeginInitialization()
        {
            if (isInitializing || activationInProgress || !IsValidSpawnedPlayerBuilding())
            {
                return;
            }

            isInitializing = true;
            remainingInitTicks = InitializationDurationTicks;
        }

        private void CompleteInitialization()
        {
            isInitializing = false;
            remainingInitTicks = 0;
            CleanupProgressBar();

            if (!IsValidSpawnedPlayerBuilding())
            {
                return;
            }

            if (!DormantJusticeActivationUtility.TryActivate(this, out _))
            {
                Log.Error(
                    "[MAP-机械族机械师] 未启动正义协议初始化完成但启动失败：" +
                    $"building={parent.LabelShort}（{parent.ThingID}）。");
            }
        }

        private void CancelInitialization()
        {
            if (!isInitializing && progressBarEffecter == null)
            {
                return;
            }

            isInitializing = false;
            remainingInitTicks = 0;
            CleanupProgressBar();
        }

        private void TickProgressBar()
        {
            if (parent is not Building building || !building.Spawned || building.Map == null)
            {
                CancelInitialization();
                return;
            }

            if (progressBarEffecter == null)
            {
                progressBarEffecter = EffecterDefOf.ProgressBar.Spawn();
            }

            progressBarEffecter.EffectTick(building, TargetInfo.Invalid);
            MoteProgressBar? mote =
                ((SubEffecter_ProgressBar)progressBarEffecter.children[0]).mote;
            if (mote != null)
            {
                mote.progress = 1f - (float)remainingInitTicks / InitializationDurationTicks;
                mote.offsetZ = -0.5f;
            }
        }

        private void CleanupProgressBar()
        {
            progressBarEffecter?.Cleanup();
            progressBarEffecter = null;
        }

        private void ToggleEmergencyCarry()
        {
            carriesEmergencyConsciousnessTransfer = !carriesEmergencyConsciousnessTransfer;
        }

        private static int CompareEmergencyCarrierPriority(
            CompDormantJustice left,
            CompDormantJustice right,
            Map? hostMap,
            IReadOnlyList<Map> maps)
        {
            Building? leftBuilding = left.parent as Building;
            Building? rightBuilding = right.parent as Building;
            Map? leftMap = leftBuilding?.MapHeld;
            Map? rightMap = rightBuilding?.MapHeld;

            bool leftOnHostMap = hostMap != null && leftMap == hostMap;
            bool rightOnHostMap = hostMap != null && rightMap == hostMap;
            if (leftOnHostMap != rightOnHostMap)
            {
                return leftOnHostMap ? -1 : 1;
            }

            int leftMapIndex = GetMapIndex(leftMap, maps);
            int rightMapIndex = GetMapIndex(rightMap, maps);
            if (leftMapIndex != rightMapIndex)
            {
                return leftMapIndex.CompareTo(rightMapIndex);
            }

            string leftId = leftBuilding?.ThingID ?? string.Empty;
            string rightId = rightBuilding?.ThingID ?? string.Empty;
            return string.Compare(leftId, rightId, StringComparison.Ordinal);
        }

        private static int GetMapIndex(Map? map, IReadOnlyList<Map> maps)
        {
            if (map == null)
            {
                return int.MaxValue;
            }

            for (int i = 0; i < maps.Count; i++)
            {
                if (ReferenceEquals(maps[i], map))
                {
                    return i;
                }
            }

            return int.MaxValue;
        }

        private bool ShouldShowEmergencyCarryToggle(Building building)
        {
            return MechanoidMechanitorScenarioUtility.IsScenarioActive
                && building.Spawned
                && building.Map != null
                && building.Faction != null
                && building.Faction.IsPlayerSafe();
        }

        private bool IsValidSpawnedPlayerBuilding()
        {
            return parent is Building building
                && !building.Destroyed
                && building.Spawned
                && building.Map != null
                && building.Faction != null
                && building.Faction.IsPlayerSafe();
        }
    }
}

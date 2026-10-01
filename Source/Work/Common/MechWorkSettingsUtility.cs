using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class MechWorkSettingsUtility
    {
        /// <summary>
        /// 仅生命周期同步正式玩家机械师的通用工作。旧档保留原已开放类别的优先级，
        /// 新接入类别默认优先级 3；处理过的类别即使被玩家关闭也不重复启用。
        /// </summary>
        internal static void SynchronizeGeneralWorkSettings(Pawn? pawn)
        {
            if (pawn == null || pawn.Destroyed || pawn.Discarded || pawn.Dead
                || pawn.health == null || pawn.health.isBeingKilled
                || pawn.RaceProps?.IsMechanoid != true || pawn.kindDef == null
                || pawn.Faction?.IsPlayerSafe() != true
                || !GameComponent_MechanoidMechanitorRegistry.TryGetMechanitorRecord(
                    pawn, out MechanoidMechanitorRecord? record)
                || record == null)
            {
                return;
            }

            if (Scribe.mode != LoadSaveMode.Inactive
                || MechanoidMechanitorPostLoadSafetyCoordinator.ShouldDeferPositiveRestore)
            {
                GameComponent_MechanoidMechanitorRegistry.QueuePostSpawnInitialization(pawn);
                return;
            }

            // 身份可能刚从普通机械体变更，先清理其种族禁用缓存，再初始化工作设置。
            pawn.Notify_DisabledWorkTypesChanged();
            if (!TryEnsureWorkSettingsInitialized(pawn))
            {
                return;
            }

            RestrictToMechEnabledWorkTypes(pawn);
            List<WorkTypeDef> workTypes = MechanoidMechanitorRoleUtility.GetRoleWorkTypes();
            HashSet<string> known = record.KnownGeneralWorkTypeDefNames == null
                ? new HashSet<string>()
                : new HashSet<string>(record.KnownGeneralWorkTypeDefNames);

            if (record.KnownGeneralWorkTypeDefNames == null)
            {
                for (int i = 0; i < workTypes.Count; i++)
                {
                    WorkTypeDef workType = workTypes[i];
                    if (MechanoidMechanitorRoleUtility.IsLegacyRoleWorkType(workType))
                    {
                        known.Add(workType.defName);
                    }
                }
                record.KnownGeneralWorkTypeDefNames = new List<string>(known);
            }

            bool initializeAcquiredRole = record.Origin == MechanoidMechanitorOrigin.Acquired
                && !record.RoleWorkSettingsInitialized;
            for (int i = 0; i < workTypes.Count; i++)
            {
                WorkTypeDef workType = workTypes[i];
                bool newlyRecognized = !known.Contains(workType.defName);
                if ((newlyRecognized || initializeAcquiredRole)
                    && !pawn.WorkTypeIsDisabled(workType)
                    && pawn.workSettings!.GetPriority(workType) == 0)
                {
                    pawn.workSettings.SetPriority(workType, Pawn_WorkSettings.DefaultPriority);
                }

                // 真实禁用状态同样只处理一次，恢复资格后不擅自重新启用玩家工作。
                if (known.Add(workType.defName))
                {
                    record.KnownGeneralWorkTypeDefNames!.Add(workType.defName);
                }
            }

            if (record.Origin == MechanoidMechanitorOrigin.Acquired)
            {
                record.RoleWorkSettingsInitialized = true;
            }
        }

        public static bool TryEnsureWorkSettingsInitialized(Pawn? pawn)
        {
            if (pawn == null || pawn.Destroyed)
            {
                return false;
            }

            pawn.workSettings ??= new Pawn_WorkSettings(pawn);

            if (pawn.kindDef == null || pawn.def?.race == null)
            {
                return false;
            }

            pawn.workSettings.EnableAndInitializeIfNotAlreadyInitialized();
            return pawn.workSettings.Initialized;
        }

        public static void RestrictToMechEnabledWorkTypes(Pawn pawn)
        {
            if (pawn?.workSettings == null || !pawn.workSettings.EverWork)
            {
                return;
            }

            if (pawn.kindDef == null)
            {
                return;
            }

            if (!ModsConfig.BiotechActive || !pawn.RaceProps.IsMechanoid)
            {
                return;
            }

            HashSet<WorkTypeDef> allowed = BuildAllowedWorkTypes(pawn);
            if (allowed.Count == 0)
            {
                return;
            }

            List<WorkTypeDef> allWorkTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading;
            for (int i = 0; i < allWorkTypes.Count; i++)
            {
                WorkTypeDef workType = allWorkTypes[i];
                if (!allowed.Contains(workType))
                {
                    pawn.workSettings.Disable(workType);
                }
            }

            pawn.Notify_DisabledWorkTypesChanged();
        }

        private static HashSet<WorkTypeDef> BuildAllowedWorkTypes(Pawn pawn)
        {
            HashSet<WorkTypeDef> allowed = new HashSet<WorkTypeDef>();

            if (MechanoidMechanitorCapabilityUtility.HasCapability(pawn, MechanoidMechanitorCapability.GeneralMechWork))
            {
                List<WorkTypeDef> roleWorkTypes =
                    MechanoidMechanitorRoleUtility.GetRoleWorkTypes();
                for (int i = 0; i < roleWorkTypes.Count; i++)
                {
                    allowed.Add(roleWorkTypes[i]);
                }
            }
            else if (pawn.RaceProps.mechEnabledWorkTypes != null)
            {
                List<WorkTypeDef> mechEnabledWorkTypes = pawn.RaceProps.mechEnabledWorkTypes;
                for (int i = 0; i < mechEnabledWorkTypes.Count; i++)
                {
                    allowed.Add(mechEnabledWorkTypes[i]);
                }
            }

            if (WardenWorkUtility.IsAuthorized(pawn))
            {
                WorkTypeDef? warden = WardenWorkUtility.WardenWorkType;
                if (warden != null)
                {
                    allowed.Add(warden);
                }
            }

            if (AnimalHandlingWorkUtility.IsAuthorized(pawn))
            {
                WorkTypeDef? handling = AnimalHandlingWorkUtility.HandlingWorkType;
                if (handling != null)
                {
                    allowed.Add(handling);
                }
            }

            if (MechanicalChildcareUtility.IsAuthorized(pawn))
            {
                WorkTypeDef? childcare = MechanicalChildcareUtility.ChildcareWorkType;
                if (childcare != null)
                {
                    allowed.Add(childcare);
                }
            }

            return allowed;
        }
    }
}

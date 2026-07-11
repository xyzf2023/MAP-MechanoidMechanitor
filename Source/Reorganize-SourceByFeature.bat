@echo off
setlocal EnableExtensions EnableDelayedExpansion
chcp 65001 >nul

title MAP Mechanoid Mechanitor - Reorganize Source

rem Prefer the folder containing this script when it is placed in the project.
set "SOURCE_ROOT="
if exist "%~dp0MAP-MechanoidMechanitor.csproj" set "SOURCE_ROOT=%~dp0"
if not defined SOURCE_ROOT if exist "%~dp0Source\MAP-MechanoidMechanitor.csproj" set "SOURCE_ROOT=%~dp0Source"
if not defined SOURCE_ROOT set "SOURCE_ROOT=D:\Rimworld_MOD_dev\[MAP]机械族机械师\Source"
if "!SOURCE_ROOT:~-1!"=="\" set "SOURCE_ROOT=!SOURCE_ROOT:~0,-1!"

echo ============================================================
echo  MAP Mechanoid Mechanitor - Source folder reorganization
echo ============================================================
echo.
echo Source folder:
echo !SOURCE_ROOT!
echo.

if not exist "!SOURCE_ROOT!\MAP-MechanoidMechanitor.csproj" (
    echo [ERROR] MAP-MechanoidMechanitor.csproj was not found.
    echo Place this BAT in the project root or Source folder,
    echo or edit SOURCE_ROOT near the top of this script.
    echo.
    pause
    exit /b 1
)

set /a PLANNED=0
set /a ALREADY_DONE=0
set /a MISSING=0
set /a CONFLICTS=0
set /a FAILED=0
set "MODE=CHECK"

echo Checking all source and destination paths...
call :ProcessMappings

echo.
echo Check result:
echo   Ready to move : !PLANNED!
echo   Already moved : !ALREADY_DONE!
echo   Missing        : !MISSING!
echo   Conflicts      : !CONFLICTS!
echo.

if not "!CONFLICTS!"=="0" (
    echo [STOPPED] A source file and destination file both exist.
    echo Nothing was moved. Resolve the conflicts shown above first.
    echo.
    pause
    exit /b 2
)

if not "!MISSING!"=="0" (
    echo [STOPPED] Some files exist in neither their old nor new locations.
    echo This usually means the local Source differs from the reviewed version.
    echo Nothing was moved.
    echo.
    pause
    exit /b 3
)

if "!PLANNED!"=="0" (
    echo No files need to be moved. The reorganization is already complete.
    echo.
    pause
    exit /b 0
)

echo The script will only move files. It will not edit C# contents,
echo namespaces, class names, XML files, or the csproj file.
echo.
set /p "CONFIRM=Type Y and press Enter to begin: "
if /I not "!CONFIRM!"=="Y" (
    echo.
    echo Cancelled. Nothing was moved.
    pause
    exit /b 0
)

set "MODE=MOVE"
set /a MOVED=0
echo.
echo Moving files...
call :ProcessMappings

if not "!FAILED!"=="0" (
    echo.
    echo [WARNING] !FAILED! move operation(s) failed.
    echo Successful moves were not rolled back. Review the errors above.
    echo.
    pause
    exit /b 4
)

rem Remove directories that became empty. Non-empty folders are untouched.
for /f "delims=" %%D in ('dir "!SOURCE_ROOT!" /ad /b /s 2^>nul ^| sort /R') do rd "%%D" 2>nul

echo.
echo ============================================================
echo  Completed successfully. Moved !MOVED! file(s).
echo ============================================================
echo.
echo You can now inspect the new Source structure and Git changes.
echo No source file contents were changed by this script.
echo.
pause
exit /b 0


:ProcessMappings
rem Shared infrastructure
call :Handle "Capabilities\MechanoidMechanitorCapability.cs" "Core\Capabilities\MechanoidMechanitorCapability.cs"
call :Handle "Capabilities\MechanoidMechanitorCapabilityUtility.cs" "Core\Capabilities\MechanoidMechanitorCapabilityUtility.cs"

rem Mechanitor core identity and lifecycle
call :Handle "Comps\CompMAPMechanitorNode.cs" "Mechanitor\Core\CompMAPMechanitorNode.cs"
call :Handle "Comps\CompNativeMechanoidMechanitor.cs" "Mechanitor\Core\CompNativeMechanoidMechanitor.cs"
call :Handle "Scenarios\GameComponent_MechanoidMechanitorRegistry.cs" "Mechanitor\Core\GameComponent_MechanoidMechanitorRegistry.cs"
call :Handle "Scenarios\MechanoidMechanitorOrigin.cs" "Mechanitor\Core\MechanoidMechanitorOrigin.cs"
call :Handle "Scenarios\MechanoidMechanitorRecord.cs" "Mechanitor\Core\MechanoidMechanitorRecord.cs"
call :Handle "Patches\MechanitorIdentityPatches.cs" "Mechanitor\Core\MechanitorIdentityPatches.cs"
call :Handle "Patches\MechanitorInitializationPatches.cs" "Mechanitor\Core\MechanitorInitializationPatches.cs"
call :Handle "Patches\StatsPatches.cs" "Mechanitor\Core\StatsPatches.cs"
call :Handle "Utilities\MAPMechanitorInitializationUtility.cs" "Mechanitor\Core\MAPMechanitorInitializationUtility.cs"
call :Handle "Utilities\MAPMechanitorNodeLifecycleUtility.cs" "Mechanitor\Core\MAPMechanitorNodeLifecycleUtility.cs"
call :Handle "Utilities\MAPMechanitorNodeUtility.cs" "Mechanitor\Core\MAPMechanitorNodeUtility.cs"
call :Handle "Utilities\MechanoidMechanitorRoleUtility.cs" "Mechanitor\Core\MechanoidMechanitorRoleUtility.cs"

rem Mechanitor control network and UI
call :Handle "Enums\MAPMechanitorControlBackend.cs" "Mechanitor\ControlNetwork\MAPMechanitorControlBackend.cs"
call :Handle "Patches\CommandRangePatches.cs" "Mechanitor\ControlNetwork\CommandRangePatches.cs"
call :Handle "Patches\ControlMechPatches.cs" "Mechanitor\ControlNetwork\ControlMechPatches.cs"
call :Handle "Patches\DraftingPatches.cs" "Mechanitor\ControlNetwork\DraftingPatches.cs"
call :Handle "Patches\JusticeAllowedAreaColumnPatches.cs" "Mechanitor\ControlNetwork\UI\JusticeAllowedAreaColumnPatches.cs"
call :Handle "Patches\JusticeDevAssignMapOverseerPatches.cs" "Mechanitor\ControlNetwork\UI\JusticeDevAssignMapOverseerPatches.cs"
call :Handle "Patches\MAPMechMainButtonPatches.cs" "Mechanitor\ControlNetwork\UI\MAPMechMainButtonPatches.cs"
call :Handle "Patches\MAPMechanitorControlProtectionPatches.cs" "Mechanitor\ControlNetwork\MAPMechanitorControlProtectionPatches.cs"
call :Handle "Patches\MAPOverseerlessNodeRequirementPatches.cs" "Mechanitor\ControlNetwork\MAPOverseerlessNodeRequirementPatches.cs"
call :Handle "Patches\MAPOverseerlessNodeSubjectPatches.cs" "Mechanitor\ControlNetwork\MAPOverseerlessNodeSubjectPatches.cs"
call :Handle "Patches\MechCarrierCompatibilityPatches.cs" "Mechanitor\ControlNetwork\MechCarrierCompatibilityPatches.cs"
call :Handle "Patches\MechControlGroupDesignatorPatches.cs" "Mechanitor\ControlNetwork\MechControlGroupDesignatorPatches.cs"
call :Handle "Patches\MechanitorFloatMenuPatches.cs" "Mechanitor\ControlNetwork\UI\MechanitorFloatMenuPatches.cs"
call :Handle "Patches\MechanitorGizmoFilterPatches.cs" "Mechanitor\ControlNetwork\UI\MechanitorGizmoFilterPatches.cs"
call :Handle "Patches\OverseerQueryFilterPatches.cs" "Mechanitor\ControlNetwork\OverseerQueryFilterPatches.cs"
call :Handle "Patches\OverseerRelationGuardPatches.cs" "Mechanitor\ControlNetwork\OverseerRelationGuardPatches.cs"
call :Handle "Utilities\MAPMechanitorControlProtectionUtility.cs" "Mechanitor\ControlNetwork\MAPMechanitorControlProtectionUtility.cs"
call :Handle "Utilities\MAPOverseerlessNodeUtility.cs" "Mechanitor\ControlNetwork\MAPOverseerlessNodeUtility.cs"

rem Acquired mechanitor features
call :Handle "Comps\CompMechanoidMechanitorImplantMarker.cs" "Mechanitor\Acquired\CompMechanoidMechanitorImplantMarker.cs"
call :Handle "Jobs\FloatMenuOptionProvider_UseAutonomousDirectiveCore.cs" "Mechanitor\Acquired\AutonomousDirectiveCore\FloatMenuOptionProvider_UseAutonomousDirectiveCore.cs"
call :Handle "Jobs\JobDriver_UseAutonomousDirectiveCore.cs" "Mechanitor\Acquired\AutonomousDirectiveCore\JobDriver_UseAutonomousDirectiveCore.cs"
call :Handle "Patches\MechanoidMechanitorImplantPatches.cs" "Mechanitor\Acquired\Implants\MechanoidMechanitorImplantPatches.cs"
call :Handle "Settings\MechanoidMechanitorBrainImplantFeatureState.cs" "Mechanitor\Acquired\Implants\MechanoidMechanitorBrainImplantFeatureState.cs"
call :Handle "Utilities\AcquiredMechanitorStateUtility.cs" "Mechanitor\Acquired\AcquiredMechanitorStateUtility.cs"
call :Handle "Utilities\MechanoidMechanitorImplantUtility.cs" "Mechanitor\Acquired\Implants\MechanoidMechanitorImplantUtility.cs"
call :Handle "Utilities\MechanoidMechanitorRecipeImplantRegistrar.cs" "Mechanitor\Acquired\Implants\MechanoidMechanitorRecipeImplantRegistrar.cs"

rem Justice abilities
call :Handle "Abilities\MechHack\CompAbilityEffect_MechHack.cs" "Justice\Abilities\MechHack\CompAbilityEffect_MechHack.cs"
call :Handle "Abilities\MechHack\CompProperties_AbilityMechHack.cs" "Justice\Abilities\MechHack\CompProperties_AbilityMechHack.cs"
call :Handle "Abilities\MechHack\HediffComp_MechHackNoMove.cs" "Justice\Abilities\MechHack\HediffComp_MechHackNoMove.cs"
call :Handle "Abilities\MechHack\JobDriver_MechHack.cs" "Justice\Abilities\MechHack\JobDriver_MechHack.cs"
call :Handle "Abilities\MechRecode\CompAbilityEffect_MechRecode.cs" "Justice\Abilities\MechRecode\CompAbilityEffect_MechRecode.cs"
call :Handle "Abilities\MechRecode\CompProperties_AbilityMechRecode.cs" "Justice\Abilities\MechRecode\CompProperties_AbilityMechRecode.cs"
call :Handle "Abilities\MechRecode\JobDriver_MechRecode.cs" "Justice\Abilities\MechRecode\JobDriver_MechRecode.cs"
call :Handle "Abilities\MechReconstruction\CompAbilityEffect_MechReconstruction.cs" "Justice\Abilities\MechReconstruction\CompAbilityEffect_MechReconstruction.cs"
call :Handle "Abilities\MechReconstruction\CompProperties_AbilityMechReconstruction.cs" "Justice\Abilities\MechReconstruction\CompProperties_AbilityMechReconstruction.cs"
call :Handle "Abilities\MechReconstruction\JobDriver_MechReconstruction.cs" "Justice\Abilities\MechReconstruction\JobDriver_MechReconstruction.cs"

rem Justice profile, dormant activation, bandwidth and work modes
call :Handle "Buildings\CompDormantJustice.cs" "Justice\DormantActivation\CompDormantJustice.cs"
call :Handle "Buildings\DormantJusticeActivationUtility.cs" "Justice\DormantActivation\DormantJusticeActivationUtility.cs"
call :Handle "Comps\CompCommanderSkills.cs" "Justice\Profile\CompCommanderSkills.cs"
call :Handle "Comps\CompProperties_CommanderSkills.cs" "Justice\Profile\CompProperties_CommanderSkills.cs"
call :Handle "Jobs\FloatMenuOptionProvider_JusticeUseBossChipForBandwidth.cs" "Justice\BandwidthUpgrade\FloatMenuOptionProvider_JusticeUseBossChipForBandwidth.cs"
call :Handle "Jobs\JobDriver_JusticeUseBossChipForBandwidth.cs" "Justice\BandwidthUpgrade\JobDriver_JusticeUseBossChipForBandwidth.cs"
call :Handle "Utilities\JusticeBossChipBandwidthUtility.cs" "Justice\BandwidthUpgrade\JusticeBossChipBandwidthUtility.cs"
call :Handle "Comps\CompJusticeSelfWorkMode.cs" "Justice\WorkModes\SelfWorkMode\CompJusticeSelfWorkMode.cs"
call :Handle "Comps\HediffComp_JusticeModeRecovery.cs" "Justice\WorkModes\SelfWorkMode\HediffComp_JusticeModeRecovery.cs"
call :Handle "Comps\HediffComp_MobileCombatMarker.cs" "Justice\WorkModes\SelfWorkMode\HediffComp_MobileCombatMarker.cs"
call :Handle "Patches\JusticeSelfWorkModeColumnPatches.cs" "Justice\WorkModes\SelfWorkMode\JusticeSelfWorkModeColumnPatches.cs"
call :Handle "Stats\StatPart_JusticeSelfMechanitorOffset.cs" "Justice\WorkModes\SelfWorkMode\StatPart_JusticeSelfMechanitorOffset.cs"
call :Handle "Utilities\MechanoidMechanitorSelfWorkModeUtility.cs" "Justice\WorkModes\SelfWorkMode\MechanoidMechanitorSelfWorkModeUtility.cs"
call :Handle "Patches\WorkModePatches.cs" "Justice\WorkModes\ControlGroupWorkModes\WorkModePatches.cs"
call :Handle "Patches\WorkModeThinkTreePatches.cs" "Justice\WorkModes\ControlGroupWorkModes\WorkModeThinkTreePatches.cs"
call :Handle "Utilities\WorkModeUtility.cs" "Justice\WorkModes\ControlGroupWorkModes\WorkModeUtility.cs"

rem Justice special scenario
call :Handle "Scenarios\GameComponent_JusticeScenarioLogger.cs" "Justice\Scenario\GameComponent_JusticeScenarioLogger.cs"
call :Handle "Scenarios\GameComponent_JusticeScenarioState.cs" "Justice\Scenario\GameComponent_JusticeScenarioState.cs"
call :Handle "Scenarios\JusticeScenarioArrivalUtility.cs" "Justice\Scenario\JusticeScenarioArrivalUtility.cs"
call :Handle "Scenarios\JusticeScenarioFreeColonistUtility.cs" "Justice\Scenario\JusticeScenarioFreeColonistUtility.cs"
call :Handle "Scenarios\JusticeScenarioUtility.cs" "Justice\Scenario\JusticeScenarioUtility.cs"
call :Handle "Scenarios\Page_JusticeScenarioDescription.cs" "Justice\Scenario\Page_JusticeScenarioDescription.cs"
call :Handle "Scenarios\ScenPart_JusticeScenario.cs" "Justice\Scenario\ScenPart_JusticeScenario.cs"
call :Handle "Patches\JusticeScenarioAnomalyPatches.cs" "Justice\Scenario\Compatibility\JusticeScenarioAnomalyPatches.cs"
call :Handle "Patches\JusticeScenarioBillWorkerRestrictionPatch.cs" "Justice\Scenario\JusticeScenarioBillWorkerRestrictionPatch.cs"
call :Handle "Patches\JusticeScenarioDropPodPatch.cs" "Justice\Scenario\JusticeScenarioDropPodPatch.cs"
call :Handle "Patches\JusticeScenarioFreeColonistPatches.cs" "Justice\Scenario\Compatibility\JusticeScenarioFreeColonistPatches.cs"
call :Handle "Patches\JusticeScenarioGameEndPatches.cs" "Justice\Scenario\JusticeScenarioGameEndPatches.cs"
call :Handle "Patches\JusticeScenarioGravshipPatch.cs" "Justice\Scenario\Compatibility\JusticeScenarioGravshipPatch.cs"
call :Handle "Patches\JusticeScenarioHistoryPatches.cs" "Justice\Scenario\JusticeScenarioHistoryPatches.cs"
call :Handle "Patches\JusticeScenarioMeditationAlertPatch.cs" "Justice\Scenario\Compatibility\JusticeScenarioMeditationAlertPatch.cs"

rem Hermit vanilla relay
call :Handle "Patches\VanillaRelayControlGroupPatches.cs" "Hermit\VanillaRelay\VanillaRelayControlGroupPatches.cs"
call :Handle "Patches\VanillaRelayControlPatches.cs" "Hermit\VanillaRelay\VanillaRelayControlPatches.cs"
call :Handle "Patches\VanillaRelayFloatMenuPatches.cs" "Hermit\VanillaRelay\VanillaRelayFloatMenuPatches.cs"
call :Handle "Patches\VanillaRelayMechanitorPatches.cs" "Hermit\VanillaRelay\VanillaRelayMechanitorPatches.cs"
call :Handle "Utilities\VanillaRelayControlUtility.cs" "Hermit\VanillaRelay\VanillaRelayControlUtility.cs"
call :Handle "Utilities\VanillaRelayMechanitorUtility.cs" "Hermit\VanillaRelay\VanillaRelayMechanitorUtility.cs"

rem Data processing allocation
call :Handle "DataProcessing\DataProcessingAllocationPinRecord.cs" "DataProcessing\DataProcessingAllocationPinRecord.cs"
call :Handle "DataProcessing\DataProcessingAllocationRecord.cs" "DataProcessing\DataProcessingAllocationRecord.cs"
call :Handle "DataProcessing\Dialog_DataProcessingAllocation.cs" "DataProcessing\Dialog_DataProcessingAllocation.cs"
call :Handle "DataProcessing\GameComponent_DataProcessingAllocationRegistry.cs" "DataProcessing\GameComponent_DataProcessingAllocationRegistry.cs"
call :Handle "DataProcessing\Hediff_CommandFocus.cs" "DataProcessing\Hediff_CommandFocus.cs"
call :Handle "DataProcessing\Hediff_DataStreamDistribution.cs" "DataProcessing\Hediff_DataStreamDistribution.cs"
call :Handle "Patches\DataProcessingAllocationGizmoPatch.cs" "DataProcessing\DataProcessingAllocationGizmoPatch.cs"

rem Colonist-like profile and interaction
call :Handle "Comps\CompColonistLikeMechProfile.cs" "ColonistLike\Profile\CompColonistLikeMechProfile.cs"
call :Handle "Comps\CompEnsureEmptyGeneTracker.cs" "ColonistLike\Profile\CompEnsureEmptyGeneTracker.cs"
call :Handle "Utilities\MechanoidBackstoryUtility.cs" "ColonistLike\Profile\MechanoidBackstoryUtility.cs"
call :Handle "Comps\CompColonistLikeFloatMenuUser.cs" "ColonistLike\Interaction\CompColonistLikeFloatMenuUser.cs"
call :Handle "Comps\CompDirectedSocialInteractionUser.cs" "ColonistLike\Interaction\CompDirectedSocialInteractionUser.cs"
call :Handle "Comps\CompFreeColonistEquivalentUser.cs" "ColonistLike\Interaction\CompFreeColonistEquivalentUser.cs"
call :Handle "Patches\CharacterTabVisibilityPatches.cs" "ColonistLike\Interaction\CharacterTabVisibilityPatches.cs"
call :Handle "Patches\ColonistLikeCompUsablePatches.cs" "ColonistLike\Interaction\ColonistLikeCompUsablePatches.cs"
call :Handle "Patches\ColonistLikeFloatMenuPatches.cs" "ColonistLike\Interaction\ColonistLikeFloatMenuPatches.cs"

rem Human apparel and weapons
call :Handle "Comps\CompHumanApparelUser.cs" "ColonistLike\Apparel\CompHumanApparelUser.cs"
call :Handle "FloatMenu\FloatMenuOptionProvider_LoverWear.cs" "ColonistLike\Apparel\FloatMenuOptionProvider_LoverWear.cs"
call :Handle "Patches\HumanApparelGearTabPatches.cs" "ColonistLike\Apparel\HumanApparelGearTabPatches.cs"
call :Handle "Patches\HumanApparelGraphicPatches.cs" "ColonistLike\Apparel\HumanApparelGraphicPatches.cs"
call :Handle "Patches\HumanApparelVerbCommandPatches.cs" "ColonistLike\Apparel\HumanApparelVerbCommandPatches.cs"
call :Handle "Rendering\HumanApparelRenderNodeFactory.cs" "ColonistLike\Apparel\HumanApparelRenderNodeFactory.cs"
call :Handle "Utilities\HumanApparelUtility.cs" "ColonistLike\Apparel\HumanApparelUtility.cs"
call :Handle "Comps\CompHumanWeaponUser.cs" "ColonistLike\Weapons\CompHumanWeaponUser.cs"
call :Handle "Patches\HumanWeaponUserFloatMenuPatches.cs" "ColonistLike\Weapons\HumanWeaponUserFloatMenuPatches.cs"

rem Colonist bar integration
call :Handle "PawnColumns\PawnColumnWorker_ColonistBarPortrait.cs" "ColonistLike\ColonistBar\PawnColumnWorker_ColonistBarPortrait.cs"
call :Handle "Patches\JusticeScenarioColonistBarPatches.cs" "ColonistLike\ColonistBar\JusticeScenarioColonistBarPatches.cs"
call :Handle "Scenarios\JusticeScenarioColonistBarPortraitUtility.cs" "ColonistLike\ColonistBar\JusticeScenarioColonistBarPortraitUtility.cs"

rem Common mech work infrastructure
call :Handle "Comps\CompMechRestrictedWorkGiverUser.cs" "Work\Common\CompMechRestrictedWorkGiverUser.cs"
call :Handle "Patches\MechRepairSelfTargetPatches.cs" "Work\Common\MechRepairSelfTargetPatches.cs"
call :Handle "Patches\MechWorkGiverRestrictionPatches.cs" "Work\Common\MechWorkGiverRestrictionPatches.cs"
call :Handle "Patches\MechanoidMechanitorWorkTypePatches.cs" "Work\Common\MechanoidMechanitorWorkTypePatches.cs"
call :Handle "Utilities\MechWorkSettingsUtility.cs" "Work\Common\MechWorkSettingsUtility.cs"
call :Handle "Utilities\MechWorkTypeAuthorizationUtility.cs" "Work\Common\MechWorkTypeAuthorizationUtility.cs"

rem Animal handling work
call :Handle "Comps\CompAnimalHandlingWorkUser.cs" "Work\AnimalHandling\CompAnimalHandlingWorkUser.cs"
call :Handle "Scenarios\GameComponent_AnimalHandlingWorkRegistry.cs" "Work\AnimalHandling\GameComponent_AnimalHandlingWorkRegistry.cs"
call :Handle "Utilities\AnimalHandlingWorkUtility.cs" "Work\AnimalHandling\AnimalHandlingWorkUtility.cs"

rem Mechanical childcare work
call :Handle "Comps\CompMechanicalChildcareUser.cs" "Work\Childcare\CompMechanicalChildcareUser.cs"
call :Handle "Patches\BottleFeedBabyMoodPatches.cs" "Work\Childcare\BottleFeedBabyMoodPatches.cs"
call :Handle "Patches\MechanicalChildcareAutofeederPatches.cs" "Work\Childcare\MechanicalChildcareAutofeederPatches.cs"
call :Handle "Scenarios\GameComponent_MechanicalChildcareRegistry.cs" "Work\Childcare\GameComponent_MechanicalChildcareRegistry.cs"
call :Handle "Scenarios\MechanicalChildcareAuthorizationRecord.cs" "Work\Childcare\MechanicalChildcareAuthorizationRecord.cs"
call :Handle "Utilities\MechanicalChildcareUtility.cs" "Work\Childcare\MechanicalChildcareUtility.cs"

rem Warden work
call :Handle "Comps\CompWardenWorkUser.cs" "Work\Warden\CompWardenWorkUser.cs"
call :Handle "Scenarios\GameComponent_WardenWorkRegistry.cs" "Work\Warden\GameComponent_WardenWorkRegistry.cs"
call :Handle "Scenarios\WardenWorkAuthorizationRecord.cs" "Work\Warden\WardenWorkAuthorizationRecord.cs"
call :Handle "Utilities\WardenWorkUtility.cs" "Work\Warden\WardenWorkUtility.cs"

rem Work tab and mech gestation
call :Handle "Comps\CompWorkTabVisibleUser.cs" "Work\WorkTab\CompWorkTabVisibleUser.cs"
call :Handle "Patches\WorkTabPatches.cs" "Work\WorkTab\WorkTabPatches.cs"
call :Handle "Patches\JusticeMechGestatorBillPatches.cs" "Work\MechGestation\JusticeMechGestatorBillPatches.cs"
call :Handle "Utilities\MAPMechGestatorRecipeUtility.cs" "Work\MechGestation\MAPMechGestatorRecipeUtility.cs"

rem Travel core and caravans
call :Handle "Comps\CompMAPMechanitorTravelNode.cs" "Travel\Core\CompMAPMechanitorTravelNode.cs"
call :Handle "Travel\MAPMechanitorTravelUtility.cs" "Travel\Core\MAPMechanitorTravelUtility.cs"
call :Handle "Travel\MAPTravelUtility.cs" "Travel\Caravan\MAPTravelUtility.cs"
call :Handle "Patches\MAPCaravanItemReachabilityPatches.cs" "Travel\Caravan\MAPCaravanItemReachabilityPatches.cs"
call :Handle "Patches\MAPCaravanOwnerPatches.cs" "Travel\Caravan\MAPCaravanOwnerPatches.cs"
call :Handle "Patches\MAPMapRemovalPatches.cs" "Travel\Caravan\MAPMapRemovalPatches.cs"

rem Gravship and shuttle piloting
call :Handle "Comps\CompGravshipPilotUser.cs" "Travel\Gravship\CompGravshipPilotUser.cs"
call :Handle "Patches\GravshipLaunchBoardingPatches.cs" "Travel\Gravship\GravshipLaunchBoardingPatches.cs"
call :Handle "Patches\GravshipPilotConsolePatches.cs" "Travel\Gravship\GravshipPilotConsolePatches.cs"
call :Handle "Patches\GravshipRitualCandidatePatches.cs" "Travel\Gravship\Ritual\GravshipRitualCandidatePatches.cs"
call :Handle "Patches\GravshipRitualCrewPatches.cs" "Travel\Gravship\Ritual\GravshipRitualCrewPatches.cs"
call :Handle "Patches\GravshipRitualRolePatches.cs" "Travel\Gravship\Ritual\GravshipRitualRolePatches.cs"
call :Handle "Patches\GravshipRitualStartPatches.cs" "Travel\Gravship\Ritual\GravshipRitualStartPatches.cs"
call :Handle "FloatMenu\FloatMenuOptionProvider_MAPShuttleEnter.cs" "Travel\Shuttle\FloatMenuOptionProvider_MAPShuttleEnter.cs"
call :Handle "Patches\Odyssey\PilotingAbilityPatches.cs" "Travel\Shuttle\PilotingAbilityPatches.cs"
call :Handle "Patches\Odyssey\ShuttlePilotPatches.cs" "Travel\Shuttle\ShuttlePilotPatches.cs"
call :Handle "Utilities\MAPShuttlePilotUtility.cs" "Travel\Shuttle\MAPShuttlePilotUtility.cs"

rem Psychic rituals
call :Handle "Comps\CompPsychicRitualParticipantUser.cs" "PsychicRitual\CompPsychicRitualParticipantUser.cs"
call :Handle "Patches\PsychicRitualCandidatePoolPatches.cs" "PsychicRitual\PsychicRitualCandidatePoolPatches.cs"
call :Handle "Patches\PsychicRitualRoleDefPatches.cs" "PsychicRitual\PsychicRitualRoleDefPatches.cs"
call :Handle "Utilities\MAPPsychicRitualUtility.cs" "PsychicRitual\MAPPsychicRitualUtility.cs"

rem Mechanical consciousness transfer
call :Handle "Jobs\JobDriver_TransferMechanicalConsciousness.cs" "MechanicalConsciousnessTransfer\JobDriver_TransferMechanicalConsciousness.cs"
call :Handle "Patches\EmergencyMechanicalConsciousnessTransferPatches.cs" "MechanicalConsciousnessTransfer\EmergencyMechanicalConsciousnessTransferPatches.cs"
call :Handle "Patches\MechanicalConsciousnessTransferGizmoPatch.cs" "MechanicalConsciousnessTransfer\MechanicalConsciousnessTransferGizmoPatch.cs"
call :Handle "Scenarios\EmergencyMechanicalConsciousnessTransferUtility.cs" "MechanicalConsciousnessTransfer\EmergencyMechanicalConsciousnessTransferUtility.cs"
exit /b 0


:Handle
set "REL_SOURCE=%~1"
set "REL_DEST=%~2"
set "FULL_SOURCE=!SOURCE_ROOT!\!REL_SOURCE!"
set "FULL_DEST=!SOURCE_ROOT!\!REL_DEST!"

rem Entries whose old and new paths are identical are intentionally retained.
if /I "!FULL_SOURCE!"=="!FULL_DEST!" exit /b 0

if /I "!MODE!"=="CHECK" (
    if exist "!FULL_SOURCE!" (
        if exist "!FULL_DEST!" (
            echo [CONFLICT] !REL_SOURCE!
            echo            Destination already exists: !REL_DEST!
            set /a CONFLICTS+=1
        ) else (
            set /a PLANNED+=1
        )
    ) else (
        if exist "!FULL_DEST!" (
            set /a ALREADY_DONE+=1
        ) else (
            echo [MISSING]  !REL_SOURCE!
            set /a MISSING+=1
        )
    )
    exit /b 0
)

if not exist "!FULL_SOURCE!" exit /b 0
for %%D in ("!FULL_DEST!") do if not exist "%%~dpD" mkdir "%%~dpD" >nul 2>&1
move /Y "!FULL_SOURCE!" "!FULL_DEST!" >nul
if errorlevel 1 (
    echo [FAILED] !REL_SOURCE!  --^>  !REL_DEST!
    set /a FAILED+=1
) else (
    echo [MOVED]  !REL_SOURCE!  --^>  !REL_DEST!
    set /a MOVED+=1
)
exit /b 0

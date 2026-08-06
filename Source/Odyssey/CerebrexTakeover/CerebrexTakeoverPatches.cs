using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch]
    public static class CompCerebrexCore_OpenDialog_TakeoverPatch
    {
        private static readonly MethodInfo StartCoreDeactivation =
            AccessTools.Method(typeof(CompCerebrexCore), "StartCoreDeactivation");

        private static readonly AccessTools.FieldRef<CompCerebrexCore, Pawn>
            InteractedPawn = AccessTools.FieldRefAccess<CompCerebrexCore, Pawn>("interactedPawn");

        public static bool Prepare() => ModsConfig.OdysseyActive;

        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(CompCerebrexCore), "OpenDialog");
        }

        public static bool Prefix(CompCerebrexCore __instance)
        {
            Pawn? pawn = InteractedPawn(__instance);
            if (pawn == null)
            {
                return true;
            }

            DiaNode node = new DiaNode("CerebrexCoreDialog".Translate(pawn.Named("PAWN")));

            if (!GameComponent_CerebrexTakeoverState.IsActive)
            {
                node.options.Add(new DiaOption("MAP_CerebrexTakeover.Option".Translate())
                {
                    action = () => CerebrexTakeoverCoreUtility.BeginTakeover(__instance, pawn),
                    resolveTree = true
                });
            }

            node.options.Add(new DiaOption("CerebrexCoreOption_Destroy".Translate())
            {
                action = () => StartCoreDeactivation.Invoke(__instance, new object[] { false }),
                resolveTree = true
            });
            node.options.Add(new DiaOption("CerebrexCoreOption_Scavenge".Translate())
            {
                action = () => StartCoreDeactivation.Invoke(__instance, new object[] { true }),
                resolveTree = true
            });
            node.options.Add(new DiaOption("CerebrexCoreOption_Postpone".Translate())
            {
                resolveTree = true
            });

            Dialog_NodeTree dialog = new Dialog_NodeTree(node)
            {
                forcePause = true
            };
            Find.WindowStack.Add(dialog);
            return false;
        }
    }

    internal static class CerebrexTakeoverCoreUtility
    {
        private static readonly AccessTools.FieldRef<CompCerebrexCore, bool>
            Deactivated = AccessTools.FieldRefAccess<CompCerebrexCore, bool>("deactivated");

        private static readonly AccessTools.FieldRef<CompCerebrexCore, bool>
            Scavenging = AccessTools.FieldRefAccess<CompCerebrexCore, bool>("scavenging");

        private static readonly AccessTools.FieldRef<CompCerebrexCore, int>
            CloseTick = AccessTools.FieldRefAccess<CompCerebrexCore, int>("closeTick");

        public static void BeginTakeover(CompCerebrexCore core, Pawn pawn)
        {
            GameComponent_CerebrexTakeoverState? state =
                GameComponent_CerebrexTakeoverState.Current;
            if (state == null || !state.TryBeginTakeover(core.parent, pawn))
            {
                Messages.Message(
                    "MAP_CerebrexTakeover.BeginFailed".Translate(),
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
            }

            Deactivated(core) = true;
            Scavenging(core) = false;
            CloseTick(core) = Find.TickManager.TicksGame + 300;
            core.parent.TryGetComp<CompCerebrexBossController>()
                ?.Notify_CoreDeactivationStarted();

            Find.TickManager.slower.SignalForceNormalSpeed();
            Find.MusicManagerPlay.ForceFadeoutAndSilenceFor(999f, 3f);
            SoundDefOf.CerebrexCore_Pain.PlayOneShotOnCamera();
            CameraJumper.TryJump(core.parent, CameraJumper.MovementMode.Cut);
            ScreenFader.StartFade(Color.white, 4.9166665f);
        }

        public static void MarkCompleted(CompCerebrexCore core)
        {
            CloseTick(core) = -99999;
        }
    }

    [HarmonyPatch]
    public static class CompCerebrexCore_DeactivateCore_TakeoverPatch
    {
        private static readonly AccessTools.FieldRef<CompCerebrexCore, Pawn>
            InteractedPawn = AccessTools.FieldRefAccess<CompCerebrexCore, Pawn>("interactedPawn");

        public static bool Prepare() => ModsConfig.OdysseyActive;

        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(CompCerebrexCore), "DeactivateCore");
        }

        public static bool Prefix(CompCerebrexCore __instance)
        {
            GameComponent_CerebrexTakeoverState? state =
                GameComponent_CerebrexTakeoverState.Current;
            if (state == null || !state.IsPendingFor(__instance))
            {
                return true;
            }

            Pawn? pawn = InteractedPawn(__instance) ?? state.PendingPawn;
            NamedArgument pawnArgument = pawn != null
                ? pawn.Named("PAWN")
                : "unknown".Named("PAWN");
            CerebrexTakeoverCoreUtility.MarkCompleted(__instance);
            __instance.parent.TryGetComp<CompCerebrexBossController>()
                ?.Notify_CoreDeactivationStarted();
            Find.TickManager.Pause();

            try
            {
                QuestUtility.SendQuestTargetSignals(
                    __instance.parent.questTags,
                    "CoreDefeated",
                    __instance.parent.Named("SUBJECT"));
            }
            catch (Exception ex)
            {
                Log.Error("[MAP-机械族机械师] 主脑接管时发送 CoreDefeated 信号失败。\n" + ex);
            }

            try
            {
                (Find.Scenario.AllParts.FirstOrDefault(
                    part => part is ScenPart_PursuingMechanoids) as ScenPart_PursuingMechanoids)
                    ?.Notify_QuestCompleted();
            }
            catch (Exception ex)
            {
                Log.Error("[MAP-机械族机械师] 主脑接管时结束追击机械族剧本部件失败。\n" + ex);
            }

            state.CompleteTakeover(__instance, pawn);

            Find.LetterStack.ReceiveLetter(
                "MAP_CerebrexTakeover.Letter.Label".Translate(),
                "MAP_CerebrexTakeover.Letter.Text".Translate(pawnArgument),
                LetterDefOf.PositiveEvent,
                __instance.parent);

            GameVictoryUtility.ShowCredits(
                "MAP_CerebrexTakeover.Credits".Translate(pawnArgument),
                SongDefOf.OdysseyCreditsSong,
                exitToMainMenu: false,
                0f);
            state.ClearPendingTakeover();
            return false;
        }
    }

    [HarmonyPatch(typeof(GameComponent_MechanoidMechanitorStoryState),
        nameof(GameComponent_MechanoidMechanitorStoryState.GetPurgeDirectiveRewardPoints))]
    public static class PurgeCredits_Get_TakeoverPatch
    {
        public static bool Prefix(ref int __result)
        {
            GameComponent_CerebrexTakeoverState? state =
                GameComponent_CerebrexTakeoverState.Current;
            if (!GameComponent_CerebrexTakeoverState.IsActive || state == null)
            {
                return true;
            }

            __result = state.ResourceCredits;
            return false;
        }
    }

    [HarmonyPatch(typeof(GameComponent_MechanoidMechanitorStoryState),
        nameof(GameComponent_MechanoidMechanitorStoryState.TryAddPurgeDirectiveRewardPoints))]
    public static class PurgeCredits_Add_TakeoverPatch
    {
        public static bool Prefix(int amount, ref bool __result)
        {
            GameComponent_CerebrexTakeoverState? state =
                GameComponent_CerebrexTakeoverState.Current;
            if (!GameComponent_CerebrexTakeoverState.IsActive || state == null)
            {
                return true;
            }

            __result = CerebrexTakeoverNodeScope.Active && state.TryAddCredits(amount);
            return false;
        }
    }

    [HarmonyPatch]
    public static class PurgeCredits_StaticSpend_TakeoverPatch
    {
        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(GameComponent_MechanoidMechanitorStoryState),
                nameof(GameComponent_MechanoidMechanitorStoryState.TrySpendPurgeDirectiveCredits),
                new[] { typeof(int), typeof(bool) });
        }

        public static bool Prefix(int amount, bool forCurrentSave, ref bool __result)
        {
            GameComponent_CerebrexTakeoverState? state =
                GameComponent_CerebrexTakeoverState.Current;
            if (!GameComponent_CerebrexTakeoverState.IsActive || state == null)
            {
                return true;
            }

            __result = forCurrentSave && state.TrySpendCredits(amount);
            return false;
        }
    }

    [HarmonyPatch]
    public static class PurgeCredits_InstanceSpend_TakeoverPatch
    {
        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(GameComponent_MechanoidMechanitorStoryState),
                nameof(GameComponent_MechanoidMechanitorStoryState.TrySpendPurgeDirectiveCredits),
                new[] { typeof(int) });
        }

        public static bool Prefix(int amount, ref bool __result)
        {
            GameComponent_CerebrexTakeoverState? state =
                GameComponent_CerebrexTakeoverState.Current;
            if (!GameComponent_CerebrexTakeoverState.IsActive || state == null)
            {
                return true;
            }

            __result = state.TrySpendCredits(amount);
            return false;
        }
    }

    [HarmonyPatch]
    public static class PurgeCredits_StaticRefund_TakeoverPatch
    {
        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(GameComponent_MechanoidMechanitorStoryState),
                nameof(GameComponent_MechanoidMechanitorStoryState.RefundPurgeDirectiveCredits),
                new[] { typeof(int), typeof(bool) });
        }

        public static bool Prefix(int amount, bool forCurrentSave, ref bool __result)
        {
            GameComponent_CerebrexTakeoverState? state =
                GameComponent_CerebrexTakeoverState.Current;
            if (!GameComponent_CerebrexTakeoverState.IsActive || state == null)
            {
                return true;
            }

            __result = forCurrentSave && state.RefundCredits(amount);
            return false;
        }
    }

    [HarmonyPatch]
    public static class PurgeCredits_InstanceRefund_TakeoverPatch
    {
        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(GameComponent_MechanoidMechanitorStoryState),
                nameof(GameComponent_MechanoidMechanitorStoryState.RefundPurgeDirectiveCredits),
                new[] { typeof(int) });
        }

        public static bool Prefix(int amount, ref bool __result)
        {
            GameComponent_CerebrexTakeoverState? state =
                GameComponent_CerebrexTakeoverState.Current;
            if (!GameComponent_CerebrexTakeoverState.IsActive || state == null)
            {
                return true;
            }

            __result = state.RefundCredits(amount);
            return false;
        }
    }

    [HarmonyPatch(typeof(GameComponent_MechanoidMechanitorStoryState),
        nameof(GameComponent_MechanoidMechanitorStoryState.IsPurgeDirectiveActive),
        MethodType.Getter)]
    public static class PurgeDirectiveActive_TakeoverPatch
    {
        public static void Postfix(ref bool __result)
        {
            if (GameComponent_CerebrexTakeoverState.IsActive)
            {
                __result = CerebrexTakeoverNodeScope.Active;
            }
        }
    }

    [HarmonyPatch(typeof(MechanoidMechanitorPurgeDirectiveUtility),
        nameof(MechanoidMechanitorPurgeDirectiveUtility.Tick))]
    public static class PurgeDirectiveTick_TakeoverPatch
    {
        public static bool Prefix()
        {
            return !GameComponent_CerebrexTakeoverState.IsActive;
        }
    }

    [HarmonyPatch]
    public static class PurgeDirectiveForceCheck_TakeoverPatch
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            return AccessTools.GetDeclaredMethods(typeof(MechanoidMechanitorPurgeDirectiveUtility))
                .Where(method => method.Name == nameof(
                    MechanoidMechanitorPurgeDirectiveUtility.ForceProtocolCheckNow));
        }

        public static bool Prefix()
        {
            return !GameComponent_CerebrexTakeoverState.IsActive;
        }
    }

    [HarmonyPatch(typeof(MechanoidMechanitorMechHiveCommunicationUtility),
        nameof(MechanoidMechanitorMechHiveCommunicationUtility.TryGetContactableMechHive))]
    public static class ContactableMechHive_TakeoverPatch
    {
        public static bool Prefix(ref Faction mechHive, ref bool __result)
        {
            if (!GameComponent_CerebrexTakeoverState.IsActive)
            {
                return true;
            }

            mechHive = Faction.OfMechanoids;
            __result = ModsConfig.OdysseyActive
                && Current.Game != null
                && mechHive != null
                && Faction.OfPlayerSilentFail != null
                && CerebrexTakeoverRelationUtility.AreMutualAllies();
            return false;
        }
    }

    [HarmonyPatch]
    public static class LockedMechHiveRelation_TakeoverPatch
    {
        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(GameComponent_MechanoidMechanitorStoryState),
                nameof(GameComponent_MechanoidMechanitorStoryState.TryGetLockedMechHiveRelation),
                new[]
                {
                    typeof(Faction),
                    typeof(Faction),
                    typeof(Faction).MakeByRefType(),
                    typeof(FactionRelationKind).MakeByRefType()
                });
        }

        public static bool Prefix(
            Faction a,
            Faction b,
            ref Faction mechHive,
            ref FactionRelationKind relationKind,
            ref bool __result)
        {
            if (CerebrexTakeoverRelationUtility.IsApplying)
            {
                __result = false;
                return false;
            }

            if (!GameComponent_CerebrexTakeoverState.IsActive
                || !CerebrexTakeoverRelationUtility.IsPlayerAndMechHivePair(a, b))
            {
                return true;
            }

            mechHive = Faction.OfMechanoids;
            relationKind = FactionRelationKind.Ally;
            __result = mechHive != null;
            return false;
        }
    }

    [HarmonyPatch(typeof(MechanoidMechanitorMechHiveRelationApplier),
        nameof(MechanoidMechanitorMechHiveRelationApplier.ApplyExactMechHiveRelation))]
    public static class ApplyMechHiveRelation_TakeoverPatch
    {
        public static bool Prefix(
            Faction mechHive,
            ref FactionRelationKind relationKind,
            ref bool hostileOnHarmByPlayer,
            ref bool __result)
        {
            if (!GameComponent_CerebrexTakeoverState.IsActive
                || mechHive == null
                || mechHive != Faction.OfMechanoids)
            {
                return true;
            }

            relationKind = FactionRelationKind.Ally;
            hostileOnHarmByPlayer = false;
            __result = CerebrexTakeoverRelationUtility.EnsureMutualAllies();
            return false;
        }
    }

    [HarmonyPatch(typeof(MechHiveNodeDeliveryUtility),
        nameof(MechHiveNodeDeliveryUtility.TryAddPurgeQuota))]
    public static class MechHiveNodeQuota_TakeoverPatch
    {
        public static bool Prefix(float totalValue, float multiplier)
        {
            if (!GameComponent_CerebrexTakeoverState.IsActive)
            {
                return true;
            }

            if (totalValue > 0f && multiplier > 0f)
            {
                int amount = Mathf.FloorToInt(totalValue * multiplier);
                if (amount > 0)
                {
                    GameComponent_CerebrexTakeoverState.Current?.TryAddCredits(amount);
                }
            }

            return false;
        }
    }

    public sealed class CerebrexTakeoverScopeState
    {
        public bool Entered;
        public bool Exited;
    }

    [HarmonyPatch(typeof(MechHiveNodeTransportInteraction),
        nameof(MechHiveNodeTransportInteraction.SettleArrival))]
    public static class MechHiveNodeSettle_TakeoverScopePatch
    {
        public static void Prefix(ref CerebrexTakeoverScopeState __state)
        {
            __state = new CerebrexTakeoverScopeState
            {
                Entered = GameComponent_CerebrexTakeoverState.IsActive
            };
            if (__state.Entered)
            {
                CerebrexTakeoverNodeScope.Enter();
            }
        }

        public static void Postfix(CerebrexTakeoverScopeState __state)
        {
            if (__state?.Entered == true && !__state.Exited)
            {
                CerebrexTakeoverNodeScope.Exit();
                __state.Exited = true;
            }
        }

        public static Exception? Finalizer(
            Exception? __exception,
            CerebrexTakeoverScopeState __state)
        {
            if (__state?.Entered == true && !__state.Exited)
            {
                CerebrexTakeoverNodeScope.Exit();
                __state.Exited = true;
            }

            return __exception;
        }
    }

    [HarmonyPatch]
    public static class MechHiveNodeTransportMenu_TakeoverScopePatch
    {
        public static MethodBase TargetMethod()
        {
            Type? iteratorType = typeof(MechHiveNodeTransportInteraction)
                .GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public)
                .FirstOrDefault(type =>
                    type.Name.Contains("GetFloatMenuOptions")
                    && typeof(IEnumerator).IsAssignableFrom(type));
            return AccessTools.Method(iteratorType, "MoveNext");
        }

        public static void Prefix(ref CerebrexTakeoverScopeState __state)
        {
            __state = new CerebrexTakeoverScopeState
            {
                Entered = GameComponent_CerebrexTakeoverState.IsActive
            };
            if (__state.Entered)
            {
                CerebrexTakeoverNodeScope.Enter();
            }
        }

        public static void Postfix(CerebrexTakeoverScopeState __state)
        {
            if (__state?.Entered == true && !__state.Exited)
            {
                CerebrexTakeoverNodeScope.Exit();
                __state.Exited = true;
            }
        }

        public static Exception? Finalizer(
            Exception? __exception,
            CerebrexTakeoverScopeState __state)
        {
            if (__state?.Entered == true && !__state.Exited)
            {
                CerebrexTakeoverNodeScope.Exit();
                __state.Exited = true;
            }

            return __exception;
        }
    }

    [HarmonyPatch]
    public static class OvermindCoreDisplay_TakeoverPatch
    {
        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(Dialog_MechanoidOvermindCommunication),
                "DrawCoreDisplay");
        }

        public static bool Prefix(Rect frameRect)
        {
            if (!GameComponent_CerebrexTakeoverState.IsActive)
            {
                return true;
            }

            MechanoidOvermindUiStyle.DrawCoreFrame(frameRect);
            Rect inner = frameRect.ContractedBy(Mathf.Max(10f, frameRect.height * 0.06f));
            Pawn? host = GameComponent_MechanoidMechanitorScenarioState.IsEnabled
                ? GameComponent_MechanoidMechanitorRegistry.CurrentMechanicalConsciousnessHost
                : null;

            if (host != null
                && !host.Dead
                && !host.Destroyed
                && !host.Discarded)
            {
                float size = Mathf.Min(inner.width, inner.height);
                Rect iconRect = new Rect(
                    inner.x + (inner.width - size) * 0.5f,
                    inner.y + (inner.height - size) * 0.5f,
                    size,
                    size);
                Widgets.ThingIcon(iconRect, host, 1f);
            }
            else
            {
                GameFont oldFont = Text.Font;
                TextAnchor oldAnchor = Text.Anchor;
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(inner, "no data");
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
            }

            return false;
        }
    }

    internal static class CerebrexTakeoverDialogueScope
    {
        [ThreadStatic]
        public static bool PurgeCreditsResponse;
    }

    [HarmonyPatch]
    public static class OvermindDialogueText_TakeoverPatch
    {
        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(Dialog_MechanoidOvermindCommunication),
                "PlayDialogueText");
        }

        public static void Prefix(ref string __0)
        {
            if (!GameComponent_CerebrexTakeoverState.IsActive)
            {
                return;
            }

            __0 = CerebrexTakeoverDialogueScope.PurgeCreditsResponse
                ? "MAP_CerebrexTakeover.PurgeCreditsResponse".Translate().RawText
                : "…";
        }
    }

    [HarmonyPatch]
    public static class OvermindPurgeCreditsResponse_TakeoverPatch
    {
        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(Dialog_MechanoidOvermindCommunication),
                "PlayPurgeCreditsQueryResponse");
        }

        public static void Prefix(ref bool __state)
        {
            __state = GameComponent_CerebrexTakeoverState.IsActive;
            if (__state)
            {
                CerebrexTakeoverDialogueScope.PurgeCreditsResponse = true;
            }
        }

        public static void Postfix(bool __state)
        {
            if (__state)
            {
                CerebrexTakeoverDialogueScope.PurgeCreditsResponse = false;
            }
        }

        public static Exception? Finalizer(Exception? __exception, bool __state)
        {
            if (__state)
            {
                CerebrexTakeoverDialogueScope.PurgeCreditsResponse = false;
            }

            return __exception;
        }
    }

    [HarmonyPatch]
    public static class OvermindMojibake_TakeoverPatch
    {
        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(Dialog_MechanoidOvermindCommunication),
                "ShouldPlayMojibakeEasterEgg");
        }

        public static void Postfix(ref bool __result)
        {
            if (GameComponent_CerebrexTakeoverState.IsActive)
            {
                __result = false;
            }
        }
    }
}

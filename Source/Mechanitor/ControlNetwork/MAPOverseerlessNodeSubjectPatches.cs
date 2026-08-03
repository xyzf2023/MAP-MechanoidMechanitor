using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// CompOverseerSubject.State 的安全接管。
    /// 普通机械体仅经过一次按 ThingDef 缓存的节点配置查询；
    /// 只有 MAP 原版控制节点才跳过原版 getter。
    /// </summary>
    [HarmonyPatch(typeof(CompOverseerSubject), nameof(CompOverseerSubject.State), MethodType.Getter)]
    public static class MAPOverseerlessNodeSubjectPatches_State
    {
        [HarmonyPrefix]
        public static bool Prefix(
            CompOverseerSubject __instance,
            ref OverseerSubjectState __result)
        {
            Pawn? subject = __instance?.Parent;
            if (subject == null
                || !MAPMechanitorNodeUtility.TryGetVanillaControlNodeProfile(
                    subject,
                    out bool requiresExternalOverseer))
            {
                return true;
            }

            // 无需外部监管者的节点，例如正义或后天机械族机械师，始终视为受控。
            if (!requiresExternalOverseer)
            {
                __result = OverseerSubjectState.Overseen;
                return false;
            }

            // 需要外部监管者的节点，例如隐者。控制组决定方向，ControlledPawns 决定带宽状态。
            Pawn? overseer = MAPOverseerRelationDirectionUtility.FindActualOverseer(subject);
            if (overseer == null || overseer.Destroyed || overseer.Dead)
            {
                __result = OverseerSubjectState.RequiresOverseer;
                return false;
            }

            Pawn_MechanitorTracker? tracker = overseer.mechanitor;
            if (tracker == null)
            {
                __result = OverseerSubjectState.RequiresOverseer;
                return false;
            }

            List<Pawn>? controlledPawns = tracker.ControlledPawns;
            __result = controlledPawns != null && controlledPawns.Contains(subject)
                ? OverseerSubjectState.Overseen
                : OverseerSubjectState.RequiresBandwidth;
            return false;
        }
    }

    /// <summary>
    /// MAP 节点的检查文本不能继续使用原版“第一条 Overseer 关系”，
    /// 否则隐者可能把下属显示成自己的监管者。
    /// </summary>
    [HarmonyPatch(typeof(CompOverseerSubject), nameof(CompOverseerSubject.CompInspectStringExtra))]
    public static class MAPOverseerlessNodeSubjectPatches_InspectString
    {
        [HarmonyPrefix]
		public static bool Prefix(
			CompOverseerSubject __instance,
			ref string? __result,
			int ___delayUntilFeralCheck)
		{
			if (__instance == null)
			{
				return true;
			}

			Pawn? subject = __instance.Parent;
            if (subject == null
                || !MAPMechanitorNodeUtility.TryGetVanillaControlNodeProfile(
                    subject,
                    out bool requiresExternalOverseer))
            {
                return true;
            }

            if (subject.Faction != Faction.OfPlayer || !requiresExternalOverseer)
            {
                __result = null;
                return false;
            }

            Pawn? overseer = MAPOverseerRelationDirectionUtility.FindActualOverseer(subject);
            StringBuilder builder = new StringBuilder();
            TaggedString overseerText = "Overseer".Translate();
            if (overseer?.mechanitor != null)
            {
                overseerText += ": " + overseer.LabelShort;
                List<Pawn>? controlledPawns = overseer.mechanitor.ControlledPawns;
                if (controlledPawns == null || !controlledPawns.Contains(subject))
                {
                    overseerText += " (" + "InsufficientBandwidth".Translate() + ")";
                }
            }
            else
            {
                overseerText += ": " + "OverseerNone".Translate();
            }

            builder.Append(overseerText);
            if (__instance.State != OverseerSubjectState.Overseen)
            {
                TaggedString warning = ___delayUntilFeralCheck > 0
                    ? "Uncontrolled".Translate()
                        + " ("
                        + (__instance.Props.delayUntilFeralCheck - ___delayUntilFeralCheck)
                            .ToStringTicksToPeriod(allowSeconds: true, shortForm: true)
                        + ")"
                    : "Danger".Translate() + ": " + "MayGoFeral".Translate();
                builder.AppendInNewLine(warning.Colorize(ColorLibrary.RedReadable));
            }

            __result = builder.ToString();
            return false;
        }
    }

    /// <summary>
    /// 失控时只移除实际上级方向，不得误删 MAP 节点对其下属的合法监管关系。
    /// </summary>
    [HarmonyPatch(typeof(CompOverseerSubject), "ForceFeral")]
    public static class MAPOverseerlessNodeSubjectPatches_ForceFeral
    {
        [HarmonyPrefix]
        public static bool Prefix(CompOverseerSubject __instance)
        {
            Pawn? subject = __instance?.Parent;
            if (subject == null
                || !MAPMechanitorNodeUtility.TryGetVanillaControlNodeProfile(
                    subject,
                    out bool requiresExternalOverseer))
            {
                return true;
            }

            Pawn? overseer = requiresExternalOverseer
                ? MAPOverseerRelationDirectionUtility.FindActualOverseer(subject)
                : null;
            if (overseer != null)
            {
                Messages.Message(
                    "MessageMechanitorLostControlOfMech".Translate(overseer, subject)
                        + ": "
                        + subject.LabelShortCap,
                    new LookTargets(new Pawn[2] { subject, overseer }),
                    MessageTypeDefOf.NeutralEvent);
                subject.relations?.TryRemoveDirectRelation(
                    PawnRelationDefOf.Overseer,
                    overseer);
            }

            subject.SetFaction(Faction.OfMechanoids);
            return false;
        }
    }

    /// <summary>
    /// 遗弃 MAP 节点时只断开实际外部监管者；ThingComp 的基类实现为空，
    /// 因而可安全跳过原版中方向不明确的私有 Overseer 查询。
    /// </summary>
    [HarmonyPatch(
        typeof(CompOverseerSubject),
        nameof(CompOverseerSubject.Notify_AbandonedAtTile))]
    public static class MAPOverseerlessNodeSubjectPatches_AbandonedAtTile
    {
        [HarmonyPrefix]
        public static bool Prefix(CompOverseerSubject __instance, PlanetTile tile)
        {
            Pawn? subject = __instance?.Parent;
            if (subject == null
                || !MAPMechanitorNodeUtility.TryGetVanillaControlNodeProfile(
                    subject,
                    out bool requiresExternalOverseer))
            {
                return true;
            }

            if (requiresExternalOverseer)
            {
                Pawn? overseer =
                    MAPOverseerRelationDirectionUtility.FindActualOverseer(subject);
                if (overseer != null)
                {
                    subject.relations?.TryRemoveDirectRelation(
                        PawnRelationDefOf.Overseer,
                        overseer);
                }
            }

            return false;
        }
    }
}

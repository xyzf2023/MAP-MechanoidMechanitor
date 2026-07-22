using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 仪式/传教结果写入心情时的空引用安全处理。
    /// 作用范围严格收紧：仅当本 MOD 的 Full 模式正式机械族机械师实际参与当前仪式（或担任当前处理角色）
    /// 且确实缺少 needs.mood 时，才接管对应原版方法并做安全重放；机械族仍保留在 totalPresence 中计入质量。
    /// Disabled/Basic/Partial 以及第三方普通无心情 Pawn 不会触发这些安全补丁；普通 Pawn 的心情正常获得。
    /// </summary>
    public static class MechanoidMechanitorIdeologyMoodSafetyPatches
    {
        private static readonly MethodInfo ApplyAttachableOutcomeMethod =
            AccessTools.Method(
                typeof(RitualOutcomeEffectWorker_FromQuality),
                "ApplyAttachableOutcome");

        private static readonly MethodInfo ApplyDevelopmentPointsMethod =
            AccessTools.Method(
                typeof(RitualOutcomeEffectWorker_FromQuality),
                "ApplyDevelopmentPoints");

        private static readonly MethodInfo GetQualityMethod =
            AccessTools.Method(
                typeof(RitualOutcomeEffectWorker_FromQuality),
                "GetQuality",
                new[] { typeof(LordJob_Ritual), typeof(float) });

        private static readonly AccessTools.FieldRef<LordJob_Ritual, Dictionary<Pawn, int>>
            TotalPresenceTmpField =
                AccessTools.FieldRefAccess<LordJob_Ritual, Dictionary<Pawn, int>>(
                    "totalPresenceTmp");

        public static bool HasMood(Pawn? pawn) => pawn?.needs?.mood != null;

        /// <summary>是否为 Full 模式正式机械族机械师且缺少心情 Tracker。</summary>
        private static bool IsFullMechanitorWithoutMood(Pawn? pawn)
        {
            return pawn != null
                && !HasMood(pawn)
                && MechanoidMechanitorIdeologyAdaptationUtility
                    .AllowsIdeologyFullParticipation(pawn);
        }

        /// <summary>
        /// 集合中是否存在“Full 模式正式机械族机械师且无心情”的成员——只有这种情形才需要本 MOD 接管。
        /// </summary>
        public static bool InvolvesFullMechanitorWithoutMood(Dictionary<Pawn, int> pawns)
        {
            if (pawns == null
                || !MechanoidMechanitorIdeologyAdaptationUtility.IsAtLeast(
                    MechanoidMechanitorIdeologyAdaptationLevel.Full))
            {
                return false;
            }

            foreach (Pawn pawn in pawns.Keys)
            {
                if (IsFullMechanitorWithoutMood(pawn))
                {
                    return true;
                }
            }

            return false;
        }

        public static void TryGainMemorySafe(Pawn? pawn, Thought_Memory? thought, Pawn? otherPawn = null)
        {
            if (!HasMood(pawn) || thought == null)
            {
                return;
            }

            pawn!.needs.mood.thoughts.memories.TryGainMemory(thought, otherPawn);
        }

        public static void TryGainMemorySafe(Pawn? pawn, ThoughtDef? def, Pawn? otherPawn = null)
        {
            if (!HasMood(pawn) || def == null)
            {
                return;
            }

            pawn!.needs.mood.thoughts.memories.TryGainMemory(def, otherPawn);
        }

        private static float InvokeGetQuality(
            RitualOutcomeEffectWorker_FromQuality worker,
            LordJob_Ritual jobRitual,
            float progress)
        {
            return (float)(GetQualityMethod.Invoke(worker, new object[] { jobRitual, progress })
                ?? 0f);
        }

        private static void InvokeApplyAttachableOutcome(
            RitualOutcomeEffectWorker_FromQuality worker,
            Dictionary<Pawn, int> totalPresence,
            LordJob_Ritual jobRitual,
            RitualOutcomePossibility outcome,
            out string? extraLetterText,
            ref LookTargets letterLookTargets)
        {
            object[] args =
            {
                totalPresence,
                jobRitual,
                outcome,
                null!,
                letterLookTargets
            };
            ApplyAttachableOutcomeMethod.Invoke(worker, args);
            extraLetterText = args[3] as string;
            letterLookTargets = (LookTargets)args[4];
        }

        private static void InvokeApplyDevelopmentPoints(
            RitualOutcomeEffectWorker_FromQuality worker,
            Precept_Ritual? ritual,
            RitualOutcomePossibility outcome,
            out string? extraOutcomeDesc)
        {
            object[] args = { ritual!, outcome, null! };
            ApplyDevelopmentPointsMethod.Invoke(worker, args);
            extraOutcomeDesc = args[2] as string;
        }

        [HarmonyPatch(
            typeof(RitualOutcomeEffectWorker_GiveMemoryAttended),
            nameof(RitualOutcomeEffectWorker_GiveMemoryAttended.Apply))]
        public static class Patch_GiveMemoryAttended
        {
            [HarmonyPrefix]
            public static bool Prefix(
                RitualOutcomeEffectWorker_GiveMemoryAttended __instance,
                float progress,
                Dictionary<Pawn, int> totalPresence,
                LordJob_Ritual jobRitual)
            {
                if (!ModsConfig.IdeologyActive
                    || progress < 1f
                    || !InvolvesFullMechanitorWithoutMood(totalPresence))
                {
                    return true;
                }

                foreach (KeyValuePair<Pawn, int> pair in totalPresence)
                {
                    TryGainMemorySafe(
                        pair.Key,
                        __instance.MakeMemory(pair.Key, jobRitual));
                }

                return false;
            }
        }

        // 说明：RitualOutcomeEffectWorker_GiveMemoryBelievers 原版遍历的是
        // AllMapsCaravansAndTravellingTransporters_Alive_Colonists（自由殖民者，不含机械族机械师），
        // 原版成员列表本就不会包含本 MOD 的机械族机械师，不存在无心情 NRE 风险，故不再 Patch。

        [HarmonyPatch(
            typeof(RitualOutcomeEffectWorker_Speech),
            nameof(RitualOutcomeEffectWorker_Speech.Apply))]
        public static class Patch_Speech
        {
            private static readonly float InspirationChance = 0.05f;
            private static readonly float ConversionChance = 0.02f;

            [HarmonyPrefix]
            public static bool Prefix(
                RitualOutcomeEffectWorker_Speech __instance,
                float progress,
                Dictionary<Pawn, int> totalPresence,
                LordJob_Ritual jobRitual)
            {
                if (!ModsConfig.IdeologyActive
                    || !InvolvesFullMechanitorWithoutMood(totalPresence))
                {
                    return true;
                }

                Pawn organizer = jobRitual.Organizer;
                float quality = InvokeGetQuality(__instance, jobRitual, progress);
                RitualOutcomePossibility outcome = __instance.GetOutcome(quality, jobRitual);
                ThoughtDef? memory = outcome.memory;
                LookTargets letterLookTargets = organizer;
                string? extraLetterText = null;
                if (jobRitual.Ritual != null)
                {
                    InvokeApplyAttachableOutcome(
                        __instance,
                        totalPresence,
                        jobRitual,
                        outcome,
                        out extraLetterText,
                        ref letterLookTargets);
                }

                string inspired = string.Empty;
                string converted = string.Empty;
                foreach (KeyValuePair<Pawn, int> item in totalPresence)
                {
                    Pawn key = item.Key;
                    if (key == organizer
                        || !organizer.Position.InHorDistOf(key.Position, 18f)
                        || memory == null)
                    {
                        continue;
                    }

                    if (HasMood(key))
                    {
                        Thought_Memory thought = __instance.MakeMemory(key, jobRitual, memory);
                        thought.otherPawn = organizer;
                        thought.moodPowerFactor = key.Ideo == organizer.Ideo ? 1f : 0.5f;
                        TryGainMemorySafe(key, thought);
                    }

                    if (memory != ThoughtDefOf.InspirationalSpeech)
                    {
                        continue;
                    }

                    if (Rand.Chance(InspirationChance))
                    {
                        InspirationHandler? inspirationHandler =
                            key.mindState?.inspirationHandler;
                        InspirationDef? insp =
                            inspirationHandler?.GetRandomAvailableInspirationDef();
                        if (insp != null
                            && inspirationHandler != null
                            && inspirationHandler.TryStartInspiration(
                                insp,
                                "LetterSpeechInspiration".Translate(
                                    key.Named("PAWN"),
                                    organizer.Named("SPEAKER"))))
                        {
                            inspired += "  - " + key.NameShortColored.Resolve() + "\n";
                        }
                    }

                    // 机械族机械师意识形态锁定 100%，不作为演讲转换对象，也不列入被转换名单。
                    if (key.Ideo != organizer.Ideo
                        && Rand.Chance(ConversionChance)
                        && !MechanoidMechanitorIdeologyAdaptationUtility
                            .AllowsIdeologyMembership(key)
                        && key.ideo != null)
                    {
                        key.ideo.SetIdeo(organizer.Ideo);
                        converted += "  - " + key.NameShortColored.Resolve() + "\n";
                    }
                }

                TaggedString letterText =
                    "LetterFinishedSpeech".Translate(organizer.Named("ORGANIZER")).CapitalizeFirst()
                    + " "
                    + ("Letter" + memory!.defName).Translate()
                    + "\n\n"
                    + __instance.OutcomeQualityBreakdownDesc(quality, progress, jobRitual);
                if (!converted.NullOrEmpty())
                {
                    letterText += "\n\n"
                        + "LetterSpeechConvertedListeners".Translate(
                            organizer.Named("PAWN"),
                            organizer.Ideo.Named("IDEO")).CapitalizeFirst()
                        + ":\n\n"
                        + converted.TrimEndNewlines();
                }

                if (!inspired.NullOrEmpty())
                {
                    letterText += "\n\n"
                        + "LetterSpeechInspiredListeners".Translate()
                        + "\n\n"
                        + inspired.TrimEndNewlines();
                }

                if (progress < 1f)
                {
                    letterText += "\n\n"
                        + "LetterSpeechInterrupted".Translate(
                            progress.ToStringPercent(),
                            organizer.Named("ORGANIZER"));
                }

                if (extraLetterText != null)
                {
                    letterText += "\n\n" + extraLetterText;
                }

                InvokeApplyDevelopmentPoints(
                    __instance,
                    jobRitual.Ritual,
                    outcome,
                    out string? extraOutcomeDesc);
                if (extraOutcomeDesc != null)
                {
                    letterText += "\n\n" + extraOutcomeDesc;
                }

                bool positive = memory == ThoughtDefOf.EncouragingSpeech
                    || memory == ThoughtDefOf.InspirationalSpeech;
                Find.LetterStack.ReceiveLetter(
                    "OutcomeLetterLabel".Translate(
                        outcome.label.Named("OUTCOMELABEL"),
                        jobRitual.Ritual!.Label.Named("RITUALLABEL")),
                    letterText,
                    positive
                        ? LetterDefOf.RitualOutcomePositive
                        : LetterDefOf.RitualOutcomeNegative,
                    letterLookTargets);

                if (jobRitual.Ritual.def == PreceptDefOf.ThroneSpeech)
                {
                    Ability? ability = organizer.abilities?.GetAbility(
                        AbilityDefOf.Speech,
                        includeTemporary: true);
                    RoyalTitle? title = organizer.royalty?.MostSeniorTitle;
                    if (ability != null && title != null)
                    {
                        ability.StartCooldown(title.def.speechCooldown.RandomInRange);
                    }
                }

                return false;
            }
        }

        [HarmonyPatch(
            typeof(RitualOutcomeEffectWorker_Conversion),
            nameof(RitualOutcomeEffectWorker_Conversion.Apply))]
        public static class Patch_Conversion
        {
            [HarmonyPrefix]
            public static bool Prefix(
                RitualOutcomeEffectWorker_Conversion __instance,
                float progress,
                Dictionary<Pawn, int> totalPresence,
                LordJob_Ritual jobRitual)
            {
                if (!ModsConfig.IdeologyActive
                    || !InvolvesFullMechanitorWithoutMood(totalPresence))
                {
                    return true;
                }

                float quality = InvokeGetQuality(__instance, jobRitual, progress);
                RitualOutcomePossibility outcome = __instance.GetOutcome(quality, jobRitual);
                LookTargets letterLookTargets = jobRitual.selectedTarget;
                string? extraLetterText = null;
                if (jobRitual.Ritual != null)
                {
                    InvokeApplyAttachableOutcome(
                        __instance,
                        totalPresence,
                        jobRitual,
                        outcome,
                        out extraLetterText,
                        ref letterLookTargets);
                }

                Pawn? moralist = jobRitual.PawnWithRole("moralist");
                Pawn? convertee = jobRitual.PawnWithRole("convertee");
                // 转换目标的确定度/意识形态由确定度补丁统一保护：若目标是机械族机械师，
                // SetIdeo/OffsetCertainty 会被拦截并强制 100%；普通目标按原版正常转换。
                if (convertee?.ideo != null)
                {
                    float offset = outcome.ideoCertaintyOffset;
                    if (offset <= -1f && moralist != null)
                    {
                        convertee.ideo.SetIdeo(moralist.Ideo);
                    }
                    else
                    {
                        convertee.ideo.OffsetCertainty(offset);
                    }
                }

                foreach (Pawn key in totalPresence.Keys)
                {
                    if (key == moralist || key == convertee || outcome.memory == null
                        || !HasMood(key))
                    {
                        continue;
                    }

                    TryGainMemorySafe(
                        key,
                        (Thought_AttendedRitual)__instance.MakeMemory(
                            key,
                            jobRitual,
                            outcome.memory));
                }

                TaggedString text = outcome.description.Formatted(jobRitual.Ritual!.Label)
                    .CapitalizeFirst();
                string moodBreakdown = __instance.def.OutcomeMoodBreakdown(outcome);
                if (!moodBreakdown.NullOrEmpty())
                {
                    text += "\n\n" + moodBreakdown;
                }

                if (extraLetterText != null)
                {
                    text += "\n\n" + extraLetterText;
                }

                text += "\n\n"
                    + __instance.OutcomeQualityBreakdownDesc(quality, progress, jobRitual);
                InvokeApplyDevelopmentPoints(
                    __instance,
                    jobRitual.Ritual,
                    outcome,
                    out string? extraOutcomeDesc);
                if (extraOutcomeDesc != null)
                {
                    text += "\n\n" + extraOutcomeDesc;
                }

                Find.LetterStack.ReceiveLetter(
                    "OutcomeLetterLabel".Translate(
                        outcome.label.Named("OUTCOMELABEL"),
                        jobRitual.Ritual.Label.Named("RITUALLABEL")),
                    text,
                    outcome.Positive
                        ? LetterDefOf.RitualOutcomePositive
                        : LetterDefOf.RitualOutcomeNegative,
                    letterLookTargets);
                return false;
            }
        }

        [HarmonyPatch(
            typeof(RitualOutcomeEffectWorker_Trial),
            nameof(RitualOutcomeEffectWorker_Trial.Apply))]
        public static class Patch_Trial
        {
            [HarmonyPrefix]
            public static bool Prefix(
                RitualOutcomeEffectWorker_Trial __instance,
                float progress,
                Dictionary<Pawn, int> totalPresence,
                LordJob_Ritual jobRitual)
            {
                if (!ModsConfig.IdeologyActive)
                {
                    return true;
                }

                Pawn? leader = jobRitual.PawnWithRole("leader");
                Pawn? convict = jobRitual.PawnWithRole("convict");
                if (!IsFullMechanitorWithoutMood(leader)
                    && !IsFullMechanitorWithoutMood(convict))
                {
                    return true;
                }

                float quality = InvokeGetQuality(__instance, jobRitual, progress);
                RitualOutcomePossibility outcome = __instance.GetOutcome(quality, jobRitual);
                LookTargets letterLookTargets = convict;
                string? extraLetterText = null;
                if (jobRitual.Ritual != null)
                {
                    InvokeApplyAttachableOutcome(
                        __instance,
                        totalPresence,
                        jobRitual,
                        outcome,
                        out extraLetterText,
                        ref letterLookTargets);
                }

                string title = convict!.LabelShort + " " + outcome.label;
                TaggedString body = outcome.description.Formatted(
                    convict.Named("PAWN"),
                    leader.Named("PROSECUTOR"));
                string moodBreakdown = __instance.def.OutcomeMoodBreakdown(outcome);
                if (!moodBreakdown.NullOrEmpty())
                {
                    body += "\n\n" + moodBreakdown;
                }

                body += "\n\n"
                    + __instance.OutcomeQualityBreakdownDesc(quality, progress, jobRitual);
                if (extraLetterText != null)
                {
                    body += "\n\n" + extraLetterText;
                }

                if (outcome.Positive)
                {
                    Find.LetterStack.ReceiveLetter(
                        LetterMaker.MakeLetter(
                            title,
                            body,
                            LetterDefOf.RitualOutcomePositive,
                            letterLookTargets));
                    convict.guilt?.Notify_Guilty(900000);
                    TryGainMemorySafe(convict, ThoughtDefOf.TrialConvicted);
                }
                else
                {
                    Find.LetterStack.ReceiveLetter(
                        title,
                        body,
                        LetterDefOf.RitualOutcomeNegative,
                        letterLookTargets);
                    TryGainMemorySafe(leader, ThoughtDefOf.TrialFailed);
                    TryGainMemorySafe(convict, ThoughtDefOf.TrialExonerated);
                }

                return false;
            }
        }

        [HarmonyPatch(
            typeof(RitualOutcomeEffectWorker_Bestowing),
            nameof(RitualOutcomeEffectWorker_Bestowing.Apply))]
        public static class Patch_Bestowing
        {
            [HarmonyPrefix]
            public static bool Prefix(
                RitualOutcomeEffectWorker_Bestowing __instance,
                float progress,
                Dictionary<Pawn, int> totalPresence,
                LordJob_Ritual jobRitual)
            {
                if (!ModsConfig.IdeologyActive
                    || !InvolvesFullMechanitorWithoutMood(totalPresence))
                {
                    return true;
                }

                LordJob_BestowingCeremony ceremony = (LordJob_BestowingCeremony)jobRitual;
                Pawn target = ceremony.target;
                Pawn bestower = ceremony.bestower;
                Hediff_Psylink? mainPsylinkSource = target.GetMainPsylinkSource();
                float quality = InvokeGetQuality(__instance, jobRitual, progress);
                RitualOutcomePossibility outcome = __instance.GetOutcome(quality, jobRitual);
                LookTargets letterLookTargets = target;
                string? extraLetterText = null;
                if (jobRitual.Ritual != null)
                {
                    InvokeApplyAttachableOutcome(
                        __instance,
                        totalPresence,
                        jobRitual,
                        outcome,
                        out extraLetterText,
                        ref letterLookTargets);
                }

                RoyalTitleDef? currentTitle = target.royalty.GetCurrentTitle(bestower.Faction);
                RoyalTitleDef? titleAwardedWhenUpdating = target.royalty.GetTitleAwardedWhenUpdating(
                    bestower.Faction,
                    target.royalty.GetFavor(bestower.Faction));
                Pawn_RoyaltyTracker.MakeLetterTextForTitleChange(
                    target,
                    bestower.Faction,
                    currentTitle,
                    titleAwardedWhenUpdating,
                    out string headline,
                    out string body);
                target.royalty?.TryUpdateTitle(
                    bestower.Faction,
                    sendLetter: false,
                    titleAwardedWhenUpdating);

                List<AbilityDef> abilitiesPreUpdate = mainPsylinkSource == null
                    ? new List<AbilityDef>()
                    : target.abilities.abilities.ConvertAll(a => a.def);
                ThingOwner<Thing> innerContainer = bestower.inventory.innerContainer;
                Thing? amplifier = null;
                for (int i = 0; i < innerContainer.Count; i++)
                {
                    if (innerContainer[i].def == ThingDefOf.PsychicAmplifier)
                    {
                        amplifier = innerContainer[i];
                        break;
                    }
                }

                if (amplifier != null)
                {
                    innerContainer.Remove(amplifier);
                    amplifier.Destroy();
                }

                for (int level = target.GetPsylinkLevel();
                    level < target.GetMaxPsylinkLevelByTitle();
                    level++)
                {
                    target.ChangePsylinkLevel(1, sendLetter: false);
                    Find.History.Notify_PsylinkAvailable();
                }

                foreach (KeyValuePair<Pawn, int> item in totalPresence)
                {
                    if (item.Key != target)
                    {
                        TryGainMemorySafe(item.Key, outcome.memory);
                    }
                }

                int honor = 0;
                for (int i = __instance.def.honorFromQuality.PointsCount - 1; i >= 0; i--)
                {
                    if (quality >= __instance.def.honorFromQuality[i].x)
                    {
                        honor = (int)__instance.def.honorFromQuality[i].y;
                        break;
                    }
                }

                if (honor > 0 && target.royalty != null)
                {
                    target.royalty.GainFavor(bestower.Faction, honor);
                }

                List<AbilityDef> newAbilities = mainPsylinkSource == null
                    ? new List<AbilityDef>()
                    : target.abilities.abilities
                        .ConvertAll(a => a.def)
                        .FindAll(def => !abilitiesPreUpdate.Contains(def));
                string text = headline;
                text = text
                    + "\n\n"
                    + Hediff_Psylink.MakeLetterTextNewPsylinkLevel(
                        ceremony.target,
                        target.GetPsylinkLevel(),
                        newAbilities);
                text = text + "\n\n" + body;
                if (extraLetterText != null)
                {
                    text = text + "\n\n" + extraLetterText;
                }

                Find.LetterStack.ReceiveLetter(
                    "LetterLabelGainedRoyalTitle".Translate(
                        titleAwardedWhenUpdating.GetLabelCapFor(target).Named("TITLE"),
                        target.Named("PAWN")),
                    text,
                    LetterDefOf.RitualOutcomePositive,
                    letterLookTargets,
                    ceremony.bestower.Faction);

                MethodInfo? outcomeDesc = AccessTools.Method(
                    typeof(RitualOutcomeEffectWorker_Bestowing),
                    "OutcomeDesc");
                string text2 = (string)outcomeDesc.Invoke(
                    __instance,
                    new object[]
                    {
                        quality,
                        progress,
                        ceremony,
                        honor,
                        totalPresence.Count
                    });
                Find.LetterStack.ReceiveLetter(
                    "OutcomeLetterLabel".Translate(
                        outcome.label.Named("OUTCOMELABEL"),
                        "RitualBestowingCeremony".Translate().Named("RITUALLABEL")),
                    text2,
                    outcome.Positive
                        ? LetterDefOf.RitualOutcomePositive
                        : LetterDefOf.RitualOutcomeNegative,
                    target);
                return false;
            }
        }

        [HarmonyPatch(typeof(RoleEffect_GiveThoughtOnTend), nameof(RoleEffect_GiveThoughtOnTend.Notify_Tended))]
        public static class Patch_GiveThoughtOnTend
        {
            [HarmonyPrefix]
            public static bool Prefix(Pawn doctor, Pawn patient)
            {
                if (!ModsConfig.IdeologyActive)
                {
                    return true;
                }

                // 仅当 patient 是 Full 模式机械族机械师且无心情时跳过心情写入；基类 Notify_Tended 为空实现，
                // 跳过不丢失其他副作用。普通无心情 Pawn 交由原版处理。
                if (doctor != patient
                    && doctor.Ideo == patient.Ideo
                    && IsFullMechanitorWithoutMood(patient))
                {
                    return false;
                }

                return true;
            }
        }

        [HarmonyPatch]
        public static class Patch_AddRelicInRoomThought
        {
            private static MethodBase? cachedTarget;

            private static bool Prepare() => TargetMethod() != null;

            private static MethodBase? TargetMethod() =>
                cachedTarget ??= AccessTools.Method(typeof(LordJob_Ritual), "AddRelicInRoomThought");

            [HarmonyPrefix]
            public static bool Prefix(LordJob_Ritual __instance)
            {
                if (!ModsConfig.IdeologyActive)
                {
                    return true;
                }

                Dictionary<Pawn, int>? presence = TotalPresenceTmpField(__instance);
                if (presence == null || !InvolvesFullMechanitorWithoutMood(presence))
                {
                    return true;
                }

                Precept_Ritual? ritual = __instance.Ritual;
                if (ritual?.ideo == null || __instance.selectedTarget.Map == null)
                {
                    return false;
                }

                Room? room = __instance.selectedTarget.Cell.GetRoom(__instance.selectedTarget.Map);
                if (room == null || room.TouchesMapEdge)
                {
                    return false;
                }

                int num = 0;
                string relicName = string.Empty;
                foreach (Thing item in room.ContainedThings(ThingDefOf.Reliquary))
                {
                    CompRelicContainer? container = item.TryGetComp<CompRelicContainer>();
                    if (container == null)
                    {
                        continue;
                    }

                    Precept_ThingStyle? style =
                        (container.ContainedThing as ThingWithComps)?.compStyleable?.SourcePrecept;
                    if (style == null || style.ideo != ritual.ideo)
                    {
                        continue;
                    }

                    if (num == 0)
                    {
                        relicName = container.ContainedThing.Label;
                    }

                    num++;
                }

                if (num <= 0)
                {
                    return false;
                }

                foreach (KeyValuePair<Pawn, int> pair in presence)
                {
                    if (pair.Key.Ideo != ritual.ideo || !HasMood(pair.Key))
                    {
                        continue;
                    }

                    Thought_RelicAtRitual thought =
                        (Thought_RelicAtRitual)ThoughtMaker.MakeThought(
                            ThoughtDefOf.RelicAtRitual,
                            Mathf.Min(num, ThoughtDefOf.RelicAtRitual.stages.Count) - 1);
                    thought.relicName = Find.ActiveLanguageWorker.WithDefiniteArticle(
                        relicName,
                        Gender.None);
                    TryGainMemorySafe(pair.Key, thought);
                }

                return false;
            }
        }

        [HarmonyPatch(
            typeof(InteractionWorker_ConvertIdeoAttempt),
            nameof(InteractionWorker_ConvertIdeoAttempt.Interacted))]
        public static class Patch_ConvertIdeoAttempt_Interacted
        {
            [HarmonyPrefix]
            public static bool Prefix(Pawn recipient)
            {
                if (!ModsConfig.IdeologyActive)
                {
                    return true;
                }

                // 机械族机械师意识形态锁定 100%，不可作为被传教/转换目标。
                if (MechanoidMechanitorIdeologyAdaptationUtility.AllowsIdeologyMembership(recipient))
                {
                    return false;
                }

                return true;
            }
        }
    }
}

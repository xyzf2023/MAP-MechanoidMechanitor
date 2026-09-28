using System;
using System.Linq;
using GD3;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace MAP_MechanoidMechanitor.GD5
{
    internal static class GD5BlackHiveEndingService
    {
        internal const string VisitorDefName = "MAP_GD5_BlackHiveVisitor";
        internal static PawnKindDef VisitorKind => DefDatabase<PawnKindDef>.GetNamed(VisitorDefName);
        internal static Faction? Hive => Find.FactionManager?.FirstFactionOfDef(GDDefOf.BlackMechanoid);
        internal static bool IsFriendly => Hive != null && Faction.OfPlayerSilentFail != null
            && !Hive.HostileTo(Faction.OfPlayer) && !Faction.OfPlayer.HostileTo(Hive);

        internal static bool IsUnlocked
        {
            get
            {
                if (!GD5BlackHiveEndingBootstrap.IsReady || !GD5StoryFlowService.IsEnabled) return false;
                MissionComponent? mission = Find.World?.GetComponent<MissionComponent>();
                return mission?.scriptEnded == true && mission.script_Finished?.Contains(1200) == true
                    && !mission.apocritonDead && !Find.QuestManager.QuestsListForReading.Any(q =>
                        q.root?.defName == "GD_Quest_BlackApocriton" && q.State == QuestState.EndedSuccess);
            }
        }

        internal static string? ContactDisabledReason(Map? map)
        {
            if (!IsUnlocked) return "MAP_GD5.Ending.Unavailable".Translate();
            if (!IsFriendly) return "MAP_GD5.Ending.Hostile".Translate();
            if (map == null || !Find.Maps.Contains(map) || GameComponent_GD5StoryState.Current?.HasVisit == true)
                return "MAP_GD5.Ending.Unavailable".Translate();
            if (MechanoidStoryDepartureUtility.GetCandidates(map).Count == 0)
                return "MAP_GD5.Ending.NoCandidates".Translate();
            return null;
        }

        internal static void OpenContact(Map map, Pawn speaker)
        {
            if (ContactDisabledReason(map) != null) return;
            Find.WindowStack.Add(new GD5StoryDialog(GD5BlackHiveEndingBootstrap.Contact,
                new GD5StoryContext(map, speaker)));
        }

        internal static bool CanSpeak(Pawn? pawn) => pawn?.Spawned == true && !pawn.Dead
            && !pawn.Downed && !pawn.InMentalState && pawn.Faction == Faction.OfPlayerSilentFail
            && pawn.CanTakeOrder && (pawn.IsPlayerControlled || AutonomousMechUtility.IsAutonomousMech(pawn))
            && pawn.health?.capacities.CapableOf(PawnCapacityDefOf.Talking) == true;

        internal static void OpenConversation(Pawn speaker, Pawn visitor)
        {
            if (!CanSpeak(speaker) || speaker.Map != visitor.Map
                || GameComponent_GD5StoryState.Current?.CanTalkTo(visitor) != true
                || Find.WindowStack.Windows.OfType<GD5StoryDialog>().Any(w => w.IsVisitorConversation(visitor))) return;
            visitor.jobs?.EndCurrentJob(JobCondition.InterruptForced);
            Find.WindowStack.Add(new GD5StoryDialog(GD5BlackHiveEndingBootstrap.Departure,
                new GD5StoryContext(visitor.Map, speaker, visitor)));
        }

        internal static string? DepartureDisabledReason(GD5StoryContext context)
        {
            if (context.Visitor == null || GameComponent_GD5StoryState.Current?.CanTalkTo(context.Visitor) != true)
                return "MAP_GD5.Ending.Unavailable".Translate();
            if (context.Map == null || MechanoidStoryDepartureUtility.GetCandidates(context.Map).Count == 0)
                return "MAP_GD5.Ending.NoCandidates".Translate();
            return null;
        }

        internal static string? ReturnDisabledReason(GD5StoryContext context, bool speakerOnly)
        {
            var state = GameComponent_GD5StoryState.Current;
            bool available = speakerOnly
                ? state?.CanReturnSpeaker(context.Visitor, context.Speaker) == true
                : state?.CanReturnFromDrysea(context.Visitor) == true;
            return available ? null : "MAP_GD5.Ending.Unavailable".Translate().ToString();
        }

        internal static void DismissPawn(Pawn pawn)
        {
            // 访客销毁不走 Kill，也不触发原 Boss 的死亡或剧情状态。
            if (pawn.Spawned) PlaySkip(pawn.Map, pawn.Position, arriving: false);
            foreach (var window in Find.WindowStack.Windows.OfType<GD5StoryDialog>()
                .Where(w => w.IsVisitorConversation(pawn)).ToList()) window.Close();
            pawn.Destroy(DestroyMode.Vanish);
            // Pawn.Destroy 会暂存世界引用；接人访客不是可再次出现的剧情同行者。
            if (Find.WorldPawns.Contains(pawn)) Find.WorldPawns.RemovePawn(pawn);
        }

        internal static void PlaySkip(Map map, IntVec3 cell, bool arriving)
        {
            if (map == null || !cell.InBounds(map)) return;
            try
            {
                // 原版折跃含延迟 5～32 tick 的子效果，必须交给地图持续更新。
                Effecter effecter = (arriving ? EffecterDefOf.Skip_ExitNoDelay : EffecterDefOf.Skip_EntryNoDelay)
                    .Spawn(cell, map);
                map.effecterMaintainer.AddEffecterToMaintain(effecter, cell, 60);
                if (!arriving) SoundDefOf.Psycast_Skip_Entry?.PlayOneShot(new TargetInfo(cell, map));
                else if (ModsConfig.RoyaltyActive)
                    SoundDefOf.Psycast_Skip_Exit?.PlayOneShot(new TargetInfo(cell, map));
            }
            catch (Exception ex)
            {
                // 视觉失败不应阻断角色归属的正式结算。
                Log.ErrorOnce("[MAP-GD5] 折跃效果播放失败：" + ex, 0x47503521);
            }
        }

    }
}

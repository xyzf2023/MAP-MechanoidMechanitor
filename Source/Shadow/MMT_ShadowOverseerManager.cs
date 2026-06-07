using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MMT
{
    public class MMT_ShadowOverseerManager : GameComponent
    {
        private List<Pawn> subjects = new List<Pawn>();
        private List<Pawn> controllers = new List<Pawn>();

        public MMT_ShadowOverseerManager(Game game)
        {
        }

        public static MMT_ShadowOverseerManager? Current
        {
            get
            {
                Game? game = Verse.Current.Game;
                return game?.GetComponent<MMT_ShadowOverseerManager>();
            }
        }

        public static MMT_ShadowOverseerManager? EnsureInstance()
        {
            Game? game = Verse.Current.Game;
            if (game == null)
            {
                return null;
            }

            MMT_ShadowOverseerManager? manager = game.GetComponent<MMT_ShadowOverseerManager>();
            if (manager != null)
            {
                return manager;
            }

            manager = new MMT_ShadowOverseerManager(game);
            game.components.Add(manager);
            return manager;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref subjects, "subjects", LookMode.Reference);
            Scribe_Collections.Look(ref controllers, "controllers", LookMode.Reference);
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            PostLoadInit();
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            PostLoadInit();
        }

        private void PostLoadInit()
        {
            subjects ??= new List<Pawn>();
            controllers ??= new List<Pawn>();

            while (controllers.Count > subjects.Count)
            {
                controllers.RemoveAt(controllers.Count - 1);
            }

            while (controllers.Count < subjects.Count)
            {
                subjects.RemoveAt(subjects.Count - 1);
            }

            CleanInvalidEntries();

            for (int i = 0; i < subjects.Count; i++)
            {
                Pawn subject = subjects[i];
                Pawn? controller = controllers[i];
                if (!IsValidSubject(subject) || !IsValidController(controller))
                {
                    continue;
                }

                EnsureSubjectInControllerGroup(subject, controller);

                if (Prefs.DevMode)
                {
                    bool groupAssigned = controller!.mechanitor?.GetControlGroup(subject) != null;
                    Log.Message(
                        $"[MMT] Shadow overseer restored after load: subject={subject.LabelShort}, " +
                        $"controller={controller.LabelShort}, groupAssigned={groupAssigned}");
                }
            }
        }

        public void SetShadowOverseer(Pawn subject, Pawn controller)
        {
            if (subject == null
                || controller == null
                || subject == controller
                || !ModsConfig.BiotechActive
                || !OverseerlessMechanitorUtility.IsNode(controller)
                || OverseerlessMechanitorUtility.IsNode(subject)
                || controller.Faction == null
                || !controller.Faction.IsPlayerSafe()
                || subject.Faction == null
                || !subject.Faction.IsPlayerSafe())
            {
                return;
            }

            int index = subjects.IndexOf(subject);
            if (index >= 0)
            {
                controllers[index] = controller;
            }
            else
            {
                subjects.Add(subject);
                controllers.Add(controller);
            }

            if (Prefs.DevMode)
            {
                Log.Message(
                    $"[MMT] Shadow overseer set: subject={subject.LabelShort}, controller={controller.LabelShort}");
            }

            EnsureSubjectInControllerGroup(subject, controller);
        }

        private static void EnsureSubjectInControllerGroup(Pawn subject, Pawn controller)
        {
            if (subject == null || controller == null)
            {
                return;
            }

            if (!OverseerlessMechanitorUtility.IsNode(controller))
            {
                return;
            }

            if (controller.mechanitor == null)
            {
                OverseerlessMechanitorUtility.EnsureBasicTrackers(controller);
            }

            if (controller.mechanitor == null)
            {
                return;
            }

            if (controller.mechanitor.controlGroups == null || controller.mechanitor.controlGroups.Count == 0)
            {
                controller.mechanitor.Notify_PawnSpawned(true);
            }

            if (controller.mechanitor.GetControlGroup(subject) == null)
            {
                controller.mechanitor.AssignPawnControlGroup(subject);
            }

            if (Prefs.DevMode)
            {
                bool groupAssigned = controller.mechanitor.GetControlGroup(subject) != null;
                Log.Message(
                    $"[MMT] Shadow subject assigned to control group: subject={subject.LabelShort}, " +
                    $"controller={controller.LabelShort}, groupAssigned={groupAssigned}");
            }
        }

        public Pawn? GetShadowOverseer(Pawn subject)
        {
            if (subject == null || OverseerlessMechanitorUtility.IsNode(subject))
            {
                return null;
            }

            if (subject.relations?.GetFirstDirectRelationPawn(PawnRelationDefOf.Overseer) != null)
            {
                RemoveShadowOverseer(subject);
                return null;
            }

            int index = subjects.IndexOf(subject);
            if (index < 0)
            {
                return null;
            }

            Pawn? controller = controllers[index];
            if (!IsValidSubject(subject) || !IsValidController(controller))
            {
                RemoveAt(index);
                return null;
            }

            return controller;
        }

        public bool HasShadowOverseer(Pawn subject)
        {
            return GetShadowOverseer(subject) != null;
        }

        public void RemoveShadowOverseer(Pawn subject)
        {
            if (subject == null)
            {
                return;
            }

            int index = subjects.IndexOf(subject);
            if (index < 0)
            {
                return;
            }

            Pawn? controller = index < controllers.Count ? controllers[index] : null;
            RemoveAt(index);
            controller?.mechanitor?.UnassignPawnFromAnyControlGroup(subject);
            controller?.mechanitor?.Notify_BandwidthChanged();

            if (Prefs.DevMode)
            {
                Log.Message($"[MMT] Shadow overseer removed: subject={subject.LabelShort}");
            }
        }

        public bool IsShadowControlledBy(Pawn subject, Pawn controller)
        {
            return GetShadowOverseer(subject) == controller;
        }

        public List<Pawn> GetShadowSubjectsFor(Pawn controller)
        {
            List<Pawn> result = new List<Pawn>();
            if (controller == null || !OverseerlessMechanitorUtility.IsNode(controller))
            {
                return result;
            }

            for (int i = subjects.Count - 1; i >= 0; i--)
            {
                Pawn subject = subjects[i];
                Pawn? recordController = i < controllers.Count ? controllers[i] : null;

                if (!IsValidSubject(subject) || !IsValidController(recordController))
                {
                    RemoveAt(i);
                    continue;
                }

                if (subject.relations?.GetFirstDirectRelationPawn(PawnRelationDefOf.Overseer) != null)
                {
                    RemoveShadowOverseer(subject);
                    continue;
                }

                if (recordController != controller)
                {
                    continue;
                }

                result.Add(subject);
            }

            return result;
        }

        private void CleanInvalidEntries()
        {
            bool removedAny = false;
            for (int i = subjects.Count - 1; i >= 0; i--)
            {
                Pawn subject = subjects[i];
                Pawn? controller = i < controllers.Count ? controllers[i] : null;
                if (!IsValidSubject(subject) || !IsValidController(controller))
                {
                    RemoveAt(i);
                    removedAny = true;
                }
            }

            if (removedAny && Prefs.DevMode)
            {
                Log.Message("[MMT] Removed invalid shadow overseer record during cleanup.");
            }
        }

        private static bool IsValidSubject(Pawn? subject)
        {
            return subject != null
                && !subject.Destroyed
                && !subject.Dead
                && !OverseerlessMechanitorUtility.IsNode(subject)
                && subject.Faction != null
                && subject.Faction.IsPlayerSafe();
        }

        private static bool IsValidController(Pawn? controller)
        {
            return controller != null
                && !controller.Destroyed
                && !controller.Dead
                && ModsConfig.BiotechActive
                && OverseerlessMechanitorUtility.IsNode(controller)
                && controller.Faction != null
                && controller.Faction.IsPlayerSafe();
        }

        private void RemoveAt(int index)
        {
            subjects.RemoveAt(index);
            if (index < controllers.Count)
            {
                controllers.RemoveAt(index);
            }
        }
    }
}

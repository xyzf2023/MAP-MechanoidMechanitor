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
        }

        public Pawn? GetShadowOverseer(Pawn subject)
        {
            if (subject == null || OverseerlessMechanitorUtility.IsNode(subject))
            {
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

            RemoveAt(index);

            if (Prefs.DevMode)
            {
                Log.Message($"[MMT] Shadow overseer removed: subject={subject.LabelShort}");
            }
        }

        public bool IsShadowControlledBy(Pawn subject, Pawn controller)
        {
            return GetShadowOverseer(subject) == controller;
        }

        private void CleanInvalidEntries()
        {
            for (int i = subjects.Count - 1; i >= 0; i--)
            {
                Pawn subject = subjects[i];
                Pawn? controller = i < controllers.Count ? controllers[i] : null;
                if (!IsValidSubject(subject) || !IsValidController(controller))
                {
                    RemoveAt(i);
                }
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

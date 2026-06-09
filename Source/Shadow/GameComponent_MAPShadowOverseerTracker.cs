using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class GameComponent_MAPShadowOverseerTracker : GameComponent
    {
        private Dictionary<Pawn, Pawn> shadowOverseerByMech = new Dictionary<Pawn, Pawn>();

        public GameComponent_MAPShadowOverseerTracker(Game game)
        {
        }

        public static GameComponent_MAPShadowOverseerTracker? Current
        {
            get
            {
                Game? game = Verse.Current.Game;
                return game?.GetComponent<GameComponent_MAPShadowOverseerTracker>();
            }
        }

        public static GameComponent_MAPShadowOverseerTracker? EnsureInstance()
        {
            Game? game = Verse.Current.Game;
            if (game == null)
            {
                return null;
            }

            GameComponent_MAPShadowOverseerTracker? tracker = game.GetComponent<GameComponent_MAPShadowOverseerTracker>();
            if (tracker != null)
            {
                return tracker;
            }

            tracker = new GameComponent_MAPShadowOverseerTracker(game);
            game.components.Add(tracker);
            return tracker;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(
                ref shadowOverseerByMech,
                "shadowOverseerByMech",
                LookMode.Reference,
                LookMode.Reference);
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
            shadowOverseerByMech ??= new Dictionary<Pawn, Pawn>();
            CleanInvalidEntries();
        }

        public Pawn? GetShadowOverseer(Pawn? mech)
        {
            if (mech == null || shadowOverseerByMech == null)
            {
                return null;
            }

            if (mech.GetOverseer() != null)
            {
                RemoveShadowOverseer(mech);
                return null;
            }

            if (!shadowOverseerByMech.TryGetValue(mech, out Pawn controller))
            {
                return null;
            }

            if (!IsValidStoredPair(mech, controller))
            {
                shadowOverseerByMech.Remove(mech);
                return null;
            }

            return controller;
        }

        public bool HasShadowOverseer(Pawn? mech)
        {
            return GetShadowOverseer(mech) != null;
        }

        public bool IsShadowOverseenBy(Pawn? mech, Pawn? controller)
        {
            if (mech == null || controller == null)
            {
                return false;
            }

            return GetShadowOverseer(mech) == controller;
        }

        public void SetShadowOverseer(Pawn mech, Pawn controller)
        {
            shadowOverseerByMech ??= new Dictionary<Pawn, Pawn>();
            shadowOverseerByMech[mech] = controller;
        }

        public void RemoveShadowOverseer(Pawn? mech)
        {
            if (mech == null || shadowOverseerByMech == null)
            {
                return;
            }

            shadowOverseerByMech.Remove(mech);
        }

        public void RemoveAllForController(Pawn? controller)
        {
            if (controller == null || shadowOverseerByMech == null || shadowOverseerByMech.Count == 0)
            {
                return;
            }

            List<Pawn>? toRemove = null;
            foreach (KeyValuePair<Pawn, Pawn> pair in shadowOverseerByMech)
            {
                if (pair.Value == controller)
                {
                    toRemove ??= new List<Pawn>();
                    toRemove.Add(pair.Key);
                }
            }

            if (toRemove == null)
            {
                return;
            }

            for (int i = 0; i < toRemove.Count; i++)
            {
                shadowOverseerByMech.Remove(toRemove[i]);
            }
        }

        private void CleanInvalidEntries()
        {
            if (shadowOverseerByMech == null || shadowOverseerByMech.Count == 0)
            {
                return;
            }

            List<Pawn>? toRemove = null;
            foreach (KeyValuePair<Pawn, Pawn> pair in shadowOverseerByMech)
            {
                if (!IsValidStoredPair(pair.Key, pair.Value))
                {
                    toRemove ??= new List<Pawn>();
                    toRemove.Add(pair.Key);
                }
            }

            if (toRemove == null)
            {
                return;
            }

            for (int i = 0; i < toRemove.Count; i++)
            {
                shadowOverseerByMech.Remove(toRemove[i]);
            }
        }

        private static bool IsValidStoredPair(Pawn mech, Pawn controller)
        {
            if (mech == null
                || controller == null
                || mech.Destroyed
                || controller.Destroyed
                || mech.Dead
                || controller.Dead)
            {
                return false;
            }

            if (!ModsConfig.BiotechActive)
            {
                return false;
            }

            if (!MAPMechanitorNodeUtility.IsShadowController(controller))
            {
                return false;
            }

            if (MAPMechanitorControlProtectionUtility.IsProtectedMechanitorTarget(mech))
            {
                return false;
            }

            if (mech.Faction == null || !mech.Faction.IsPlayerSafe())
            {
                return false;
            }

            return true;
        }
    }
}

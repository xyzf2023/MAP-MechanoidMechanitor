using System;
using System.Collections.Generic;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class SymbiosisCovenantOptionDef : Def
    {
        public Type? workerClass;
        public int displayOrder;
        public int minimumTrust;
        public bool requiresFaction = true;

        private SymbiosisCovenantOptionWorker? worker;

        public SymbiosisCovenantOptionWorker Worker
        {
            get
            {
                if (worker == null)
                {
                    worker = (SymbiosisCovenantOptionWorker)Activator.CreateInstance(
                        workerClass ?? typeof(SymbiosisCovenantOptionWorker_Unavailable));
                    worker.def = this;
                }

                return worker;
            }
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }

            if (workerClass == null)
            {
                yield return defName + " 缺少 workerClass。";
            }
            else if (!typeof(SymbiosisCovenantOptionWorker).IsAssignableFrom(workerClass))
            {
                yield return defName + " 的 workerClass 必须继承 SymbiosisCovenantOptionWorker。";
            }

            if (minimumTrust < 0 || minimumTrust > 100)
            {
                yield return defName + " 的 minimumTrust 必须位于 0 到 100 之间。";
            }
        }
    }

    public abstract class SymbiosisCovenantOptionWorker
    {
        public SymbiosisCovenantOptionDef def = null!;

        public virtual bool ShouldShow(
            GameComponent_SymbiosisCovenantState state,
            SymbiosisCovenantFactionRecord? record)
        {
            return !def.requiresFaction || record?.Faction != null;
        }

        public virtual bool CanExecute(
            GameComponent_SymbiosisCovenantState state,
            SymbiosisCovenantFactionRecord? record,
            out string? disabledReason)
        {
            if (def.requiresFaction && record?.Faction == null)
            {
                disabledReason =
                    "MAP_MechanoidMechanitor.Symbiosis.Option.NoFaction".Translate();
                return false;
            }

            if (record != null && record.Trust < def.minimumTrust)
            {
                disabledReason =
                    "MAP_MechanoidMechanitor.Symbiosis.Option.TrustRequired".Translate(
                        def.minimumTrust);
                return false;
            }

            disabledReason = null;
            return true;
        }

        public abstract void Execute(
            GameComponent_SymbiosisCovenantState state,
            SymbiosisCovenantFactionRecord? record);
    }

    public sealed class SymbiosisCovenantOptionWorker_PublicDeclaration
        : SymbiosisCovenantOptionWorker
    {
        public override bool CanExecute(
            GameComponent_SymbiosisCovenantState state,
            SymbiosisCovenantFactionRecord? record,
            out string? disabledReason)
        {
            if (!base.CanExecute(state, record, out disabledReason))
            {
                return false;
            }

            if (state.PublicDeclarationBroadcast)
            {
                disabledReason =
                    "MAP_MechanoidMechanitor.Symbiosis.Option.AlreadyExecuted".Translate();
                return false;
            }

            return true;
        }

        public override void Execute(
            GameComponent_SymbiosisCovenantState state,
            SymbiosisCovenantFactionRecord? record)
        {
            state.TryBroadcastPublicDeclaration();
        }
    }

    public sealed class SymbiosisCovenantOptionWorker_SignCovenant
        : SymbiosisCovenantOptionWorker
    {
        public override bool CanExecute(
            GameComponent_SymbiosisCovenantState state,
            SymbiosisCovenantFactionRecord? record,
            out string? disabledReason)
        {
            if (!base.CanExecute(state, record, out disabledReason))
            {
                return false;
            }

            if (record!.CovenantMember)
            {
                disabledReason =
                    "MAP_MechanoidMechanitor.Symbiosis.Option.AlreadyExecuted".Translate();
                return false;
            }

            return true;
        }

        public override void Execute(
            GameComponent_SymbiosisCovenantState state,
            SymbiosisCovenantFactionRecord? record)
        {
            state.TrySignCovenant(record);
        }
    }

    public sealed class SymbiosisCovenantOptionWorker_Unavailable
        : SymbiosisCovenantOptionWorker
    {
        public override bool CanExecute(
            GameComponent_SymbiosisCovenantState state,
            SymbiosisCovenantFactionRecord? record,
            out string? disabledReason)
        {
            disabledReason =
                "MAP_MechanoidMechanitor.Symbiosis.Option.Unavailable".Translate();
            return false;
        }

        public override void Execute(
            GameComponent_SymbiosisCovenantState state,
            SymbiosisCovenantFactionRecord? record)
        {
        }
    }
}

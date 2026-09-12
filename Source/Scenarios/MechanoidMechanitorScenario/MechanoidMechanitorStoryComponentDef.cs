using System;
using System.Collections.Generic;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class MechanoidMechanitorStoryComponentDef : Def
    {
        public int displayOrder;

        public Type? workerClass;

        [Unsaved(false)]
        private MechanoidMechanitorStoryComponentWorker? workerInt;

        [Unsaved(false)]
        private bool workerCreationFailed;

        public MechanoidMechanitorStoryComponentWorker? Worker
        {
            get
            {
                if (workerInt != null || workerCreationFailed)
                {
                    return workerInt;
                }

                string? error = GetWorkerClassError(workerClass);
                if (error != null)
                {
                    workerCreationFailed = true;
                    Log.Error(
                        $"[MAP-机械族机械师] {defName}: cannot create story component worker. {error}");
                    return null;
                }

                try
                {
                    object? instance = Activator.CreateInstance(workerClass!);
                    if (instance is MechanoidMechanitorStoryComponentWorker worker)
                    {
                        worker.def = this;
                        workerInt = worker;
                    }
                    else
                    {
                        workerCreationFailed = true;
                        Log.Error(
                            $"[MAP-机械族机械师] {defName}: workerClass '{workerClass}' did not produce a {nameof(MechanoidMechanitorStoryComponentWorker)} instance.");
                    }
                }
                catch (Exception ex)
                {
                    workerCreationFailed = true;
                    Log.Error(
                        $"[MAP-机械族机械师] {defName}: failed to create story component worker '{workerClass}': {ex}");
                }

                return workerInt;
            }
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }

            string? workerError = GetWorkerClassError(workerClass);
            if (workerError != null)
            {
                yield return $"{defName}: {workerError}";
            }
        }

        private static string? GetWorkerClassError(Type? workerClass)
        {
            if (workerClass == null)
            {
                return "workerClass is not configured; expected a concrete MechanoidMechanitorStoryComponentWorker subclass.";
            }

            if (!typeof(MechanoidMechanitorStoryComponentWorker).IsAssignableFrom(workerClass))
            {
                return $"workerClass '{workerClass.FullName}' does not derive from {nameof(MechanoidMechanitorStoryComponentWorker)}.";
            }

            if (workerClass.IsAbstract)
            {
                return $"workerClass '{workerClass.FullName}' is abstract and cannot be instantiated.";
            }

            if (workerClass.GetConstructor(Type.EmptyTypes) == null)
            {
                return $"workerClass '{workerClass.FullName}' has no public parameterless constructor and cannot be instantiated by Activator.CreateInstance.";
            }

            return null;
        }
    }
}

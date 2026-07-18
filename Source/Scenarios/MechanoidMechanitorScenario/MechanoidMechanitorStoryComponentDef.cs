using System;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class MechanoidMechanitorStoryComponentDef : Def
    {
        public int displayOrder;

        public Type workerClass = typeof(MechanoidMechanitorStoryComponentWorker);

        [Unsaved(false)]
        private MechanoidMechanitorStoryComponentWorker? workerInt;

        public MechanoidMechanitorStoryComponentWorker Worker
        {
            get
            {
                if (workerInt == null)
                {
                    workerInt = (MechanoidMechanitorStoryComponentWorker)Activator
                        .CreateInstance(workerClass);
                    workerInt.def = this;
                }

                return workerInt;
            }
        }
    }
}

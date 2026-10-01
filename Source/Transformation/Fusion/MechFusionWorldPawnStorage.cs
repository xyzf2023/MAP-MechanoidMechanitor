using System;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>合体会话校验后复用公共形态暂存，阵营仍以会话快照为准。</summary>
    internal static class MechFusionWorldPawnStorage
    {
        internal static void EnsureStored(MechFusionSession session, Pawn source)
        {
            if (!ReferenceEquals(session.SourcePawn, source) || source.Spawned)
                throw new InvalidOperationException("合体暂存需要会话中的已离图源 Pawn。");

            MechTransformationWorldPawnStorage.EnsureStored(source, session.OriginalSourceFaction);
        }
    }
}

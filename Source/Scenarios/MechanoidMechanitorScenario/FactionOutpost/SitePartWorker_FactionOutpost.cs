using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class SitePartWorker_FactionOutpost : SitePartWorker
    {
        public override void PostMapGenerate(Map map)
        {
            base.PostMapGenerate(map);
            if (map?.Parent is MAPFactionOutpost outpost)
            {
                FactionOutpostMapGenerator.Generate(map, outpost);
            }
        }
    }
}

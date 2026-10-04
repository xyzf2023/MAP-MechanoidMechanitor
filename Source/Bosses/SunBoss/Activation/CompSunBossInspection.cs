using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class CompProperties_SunBossInspection : CompProperties_Interactable
    {
        public CompProperties_SunBossInspection()
        {
            compClass = typeof(CompSunBossInspection);
            // 不使用 onlyTargetControlledPawns：原版只认殖民者，会排除机械族机械师。
            targetingParameters = new TargetingParameters
            {
                canTargetPawns = true,
                canTargetBuildings = false,
                canTargetAnimals = false,
                canTargetMechs = true,
                validator = target => target.Thing is Pawn pawn
                    && CompSunBossInspection.IsEligibleInspector(pawn)
            };
        }
    }

    /// <summary>复用原版检查工作；距离苏醒优先，成功检查只补发一次苏醒请求。</summary>
    public sealed class CompSunBossInspection : CompInteractable
    {
        public override bool Active => false;
        public override string ExposeKey => "sunBossInspection";

        internal static bool IsEligibleInspector(Pawn pawn)
        {
            return pawn != null && pawn.Spawned && !pawn.Destroyed && !pawn.Dead
                && pawn.Faction == Faction.OfPlayer && pawn.HostFaction == null
                && pawn.MentalStateDef == null && !pawn.Deathresting
                && !pawn.IsSelfShutdown() && !pawn.IsDeactivated() && pawn.jobs != null
                && (pawn.IsColonistPlayerControlled || (pawn.RaceProps.IsMechanoid
                    && MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn)));
        }

        public override AcceptanceReport CanInteract(Pawn? activateBy = null, bool checkOptionalItems = true)
        {
            // 行走和检查中的原版 FailOn 也会走这里，在苏醒时结束工作。
            if (!(parent is Building core) || core.Destroyed || !core.Spawned
                || !core.Map.GetComponent<MapComponent_SunBossArena>().CanStartActivation(core))
                return "MAP_SunBoss_InspectionUnavailable".Translate();

            if (activateBy != null && (!IsEligibleInspector(activateBy) || activateBy.Map != core.Map))
                return "MAP_SunBoss_InspectionInspectorRequired".Translate();

            return base.CanInteract(activateBy, checkOptionalItems);
        }

        protected override void OnInteracted(Pawn caster)
        {
            // 仅成功完成检查才调用；取消、失败或距离苏醒后的中断不会进入此处。
            if (parent is Building core && !core.Destroyed && core.Spawned)
                core.Map.GetComponent<MapComponent_SunBossArena>().TryStartActivation(core);
        }
    }
}

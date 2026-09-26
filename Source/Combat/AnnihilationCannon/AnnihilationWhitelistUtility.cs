using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>合并所有 XML 白名单；静态配置不写入存档，修改后重启游戏生效。</summary>
    [StaticConstructorOnStartup]
    internal static class AnnihilationWhitelistUtility
    {
        private static readonly HashSet<ThingDef> thingDefs = new HashSet<ThingDef>();
        private static readonly HashSet<PawnKindDef> pawnKinds = new HashSet<PawnKindDef>();

        internal static bool HasEntries => thingDefs.Count != 0 || pawnKinds.Count != 0;

        static AnnihilationWhitelistUtility()
        {
            foreach (AnnihilationWhitelistDef whitelist in DefDatabase<AnnihilationWhitelistDef>.AllDefsListForReading)
            {
                if (whitelist.thingDefs != null)
                    foreach (ThingDef def in whitelist.thingDefs)
                        if (def != null) thingDefs.Add(def);
                if (whitelist.pawnKinds != null)
                    foreach (PawnKindDef def in whitelist.pawnKinds)
                        if (def != null) pawnKinds.Add(def);
            }
        }

        internal static bool IsProtected(Thing? thing)
        {
            return HasEntries && thing != null && (IsListed(thing) || IsProtectedByHolder(thing));
        }

        private static bool IsListed(Thing thing)
        {
            if (thingDefs.Contains(thing.def)) return true;
            if (thing is Pawn pawn) return pawn.kindDef != null && pawnKinds.Contains(pawn.kindDef);
            // 正常爆炸仍能杀死白名单 Pawn；尸体继续受保护，不能被后续清理补删。
            return thing is Corpse corpse && corpse.InnerPawn != null && IsListed(corpse.InnerPawn);
        }

        internal static bool IsProtectedByHolder(Thing thing)
        {
            for (IThingHolder? holder = thing.ParentHolder; holder != null; holder = holder.ParentHolder)
            {
                // 部分建筑由组件持有内容，组件的 ParentHolder 会直接跳过其所属建筑。
                Thing? owner = holder as Thing ?? (holder as ThingComp)?.parent;
                if (owner != null && IsListed(owner)) return true;
            }
            return false;
        }
    }
}

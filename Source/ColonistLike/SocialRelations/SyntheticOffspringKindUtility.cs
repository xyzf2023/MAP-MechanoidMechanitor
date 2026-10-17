using MAP_MechanoidMechanitor.Compatibility.ThirdParty.HumanoidAlienRaces;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>受孕解析与旧孕期迁移共用入口；只读 Def，绝不创建或临时修改共享 kind。</summary>
    internal static class SyntheticOffspringKindUtility
    {
        internal static bool TryResolveAtConception(
            Pawn source, out PawnKindDef? kind, out ThingDef? race, out string detail)
        {
            kind = source.kindDef;
            race = null;
            detail = string.Empty;
            if (HarSyntheticOffspringCompatibility.IsAlienRace(source.def))
            {
                if (!HarSyntheticOffspringCompatibility.TryReadChildKind(source.def, out PawnKindDef? configured, out detail))
                    return false;

                if (configured != null)
                {
                    kind = configured;
                    if (kind.race != source.def)
                        detail = $"HAR 显式 childKindDef={kind.defName} 指向种族 {kind.race?.defName ?? "<null>"}，"
                            + $"与单一遗传来源种族 {source.def.defName} 不同；尊重上游显式配置。";
                }
                else if (kind?.race != source.def)
                {
                    detail = $"HAR 遗传来源种族={source.def.defName}，kind={kind?.defName ?? "<null>"}，"
                        + $"kind.race={kind?.race?.defName ?? "<null>"}；未配置 childKindDef，无法安全选择出生类型。";
                    return false;
                }
            }

            if (!TryValidateKind(kind, out string detailIfInvalid))
            {
                detail = detailIfInvalid;
                return false;
            }

            race = kind!.race;
            return true;
        }

        internal static bool TryResolveForBirth(
            Pawn? source, PawnKindDef? savedKind, string? savedRaceName,
            out PawnKindDef? kind, out ThingDef? race, out string detail)
        {
            kind = savedKind;
            race = null;
            detail = string.Empty;
            if (!TryValidateKind(kind, out detail))
                return false;

            if (!string.IsNullOrEmpty(savedRaceName))
            {
                race = DefDatabase<ThingDef>.GetNamedSilentFail(savedRaceName!);
                if (race == null || kind!.race != race)
                {
                    detail = $"出生种族快照 {savedRaceName} 缺失或与已保存 kind={kind!.defName} 的 race 不符；拒绝重算快照。";
                    return false;
                }
                return true;
            }

            // 旧存档没有 race 快照。缺少原遗传来源时无法证明来源不是 HAR，保留孕期等待恢复。
            if (source?.def == null)
            {
                detail = "旧孕期缺少种族快照且原 geneticParent 不可用，无法安全回填；孕期保留。";
                return false;
            }

            if (HarSyntheticOffspringCompatibility.IsAlienRace(source.def) && kind!.race != source.def)
            {
                // 仅当旧 kind 仍是原遗传来源的 kind 时，才有证据迁移旧实现保存的错误种族。
                if (source.kindDef != savedKind)
                {
                    detail = "旧孕期 kind 与原遗传来源当前 kind 不同，无法证明旧快照应被修正；孕期保留。";
                    return false;
                }
                return TryResolveAtConception(source, out kind, out race, out detail);
            }

            // 已与 HAR 来源实际种族一致的旧快照，以及非 HAR 旧孕期，均保留原 kind 语义。
            race = kind!.race;
            return true;
        }

        private static bool TryValidateKind(PawnKindDef? kind, out string failure)
        {
            failure = string.Empty;
            if (kind?.race?.race == null
                || DefDatabase<PawnKindDef>.GetNamedSilentFail(kind.defName) != kind
                || DefDatabase<ThingDef>.GetNamedSilentFail(kind.race.defName) != kind.race)
            {
                failure = $"出生 PawnKindDef {kind?.defName ?? "<null>"} 或对应种族缺失/未注册，孕期不会被移除。";
                return false;
            }
            return true;
        }
    }
}

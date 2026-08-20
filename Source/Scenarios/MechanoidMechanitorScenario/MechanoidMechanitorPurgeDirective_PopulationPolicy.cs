using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 肃清指令“禁止正常途径新增血肉自由殖民者”的统一策略入口。
    /// 所有补丁都只询问这个类，不重复判断生效条件与种族。
    /// </summary>
    public static class MechanoidMechanitorPurgeDirectivePopulationPolicy
    {
        /// <summary>
        /// 人口限制是否生效。
        /// 必须同时满足：
        /// 1. 当前是机械族机械师专用剧本（GeneralScenario 普通剧本即使开启肃清也不启用人口限制）；
        /// 2. 机械巢与玩家仍非敌对（复用关系工具的既有判定）。
        /// 注意：不修改 ShouldApplyNonHostileMechHiveRestrictions 本身，
        /// 它仍可能被其他肃清逻辑复用；这里只是收窄 PopulationPolicy 的作用域。
        /// </summary>
        public static bool RestrictionActive =>
            GameComponent_MechanoidMechanitorStoryState
                .IsMechanoidMechanitorScenarioStoryConfiguration
            && MechanoidMechanitorPurgeDirectiveRelationUtility
                .ShouldApplyNonHostileMechHiveRestrictions();

        public static TaggedString BlockReason =>
            "MAP_MechanoidMechanitor.PurgeDirective.Population.DisabledReason"
                .Translate();

        public static bool IsFleshHumanlike(Pawn? pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            RaceProperties? race = pawn.RaceProps;

            return race != null
                && race.Humanlike
                && race.IsFlesh;
        }

        public static bool IsFleshHumanlike(PawnKindDef? pawnKind)
        {
            RaceProperties? race =
                pawnKind?.race?.race;

            return race != null
                && race.Humanlike
                && race.IsFlesh;
        }

        /// <summary>
        /// 本次操作是否会新增一个被禁止的玩家自由血肉殖民者。
        /// 已经作为玩家自由成员存在的 Pawn 重复 SetFaction 不算新增。
        /// 但囚犯/奴隶被招募为自由殖民者属于新增，应继续拦截。
        /// </summary>
        public static bool WouldAddForbiddenFreeColonist(
            Pawn? pawn,
            Faction? targetFaction)
        {
            if (!RestrictionActive
                || pawn == null
                || targetFaction == null)
            {
                return false;
            }

            Faction? player =
                Faction.OfPlayerSilentFail;

            if (player == null
                || targetFaction != player)
            {
                return false;
            }

            if (!IsFleshHumanlike(pawn))
            {
                return false;
            }

            // 已经是玩家自由成员时，重复 SetFaction 不属于“新增人口”。
            // 但玩家囚犯和玩家奴隶被 Recruit 成自由殖民者，
            // 属于新增自由血肉殖民者，应继续拦截。
            if (pawn.Faction == player
                && !pawn.IsPrisoner
                && !pawn.IsSlave)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 这个胚胎如果从指定 Faction 的分娩者/成长槽出生，是否会产生被肃清政策禁止的
        /// “新增玩家自由血肉智慧人口”。
        /// 仅当下列全部成立时才返回 true：
        /// 1. RestrictionActive 生效；
        /// 2. Faction.OfPlayerSilentFail 存在（玩家派系存在）；
        /// 3. 出生派系 birthFaction 正是玩家派系（只有“出生归入玩家”的胚胎才属于本限制范围，
        ///    把血肉胚胎植入一个仍属于其他派系的囚犯不应被肃清政策禁止）；
        /// 4. 根据 geneticMother 推断的 PawnKind 是血肉 Humanlike（非血肉 Humanlike 不受限）。
        /// geneticMother 用于判断生成 PawnKind（为 null 时回退 Colonist）；
        /// birthFaction 用于判断出生后是否属于玩家派系。
        /// 注意：HumanEmbryo 是 Biotech DLC 类型，本项目编译期不可见，
        /// 因此由 Biotech 补丁通过反射取出基因母亲后调用本方法，避免直接依赖 DLC 程序集。
        /// </summary>
        public static bool WouldCreateForbiddenFleshFromEmbryo(
            Pawn? geneticMother,
            Faction? birthFaction)
        {
            if (!RestrictionActive)
            {
                return false;
            }

            Faction? player = Faction.OfPlayerSilentFail;
            if (player == null
                || birthFaction != player)
            {
                return false;
            }

            // 出生时 child kind 以 geneticMother.kindDef 为主要来源。
            PawnKindDef kind =
                geneticMother?.kindDef
                ?? PawnKindDefOf.Colonist;

            return IsFleshHumanlike(kind);
        }
    }
}

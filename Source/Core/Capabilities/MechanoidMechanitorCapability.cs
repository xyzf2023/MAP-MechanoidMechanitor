using System;

namespace MAP_MechanoidMechanitor
{
    public enum MechanoidMechanitorCapability
    {
        None = 0,

        TravelLeadCaravan = 1 << 0,
        TravelCollectItems = 1 << 1,
        TravelRefreshTrackers = 1 << 2,

        /// <summary>
        /// 专属剧本中当前机械意识宿主的全局自由殖民者替代资格。
        /// </summary>
        FreeColonistEquivalent = 1 << 3,
        ColonistLikeFloatMenu = 1 << 4,
        HumanWeapons = 1 << 5,
        GravshipPilot = 1 << 6,
        WorkTab = 1 << 7,
        PsychicRituals = 1 << 8,
        SelfWorkMode = 1 << 9,
        ImplantInstallation = 1 << 10,
        ShuttlePilot = 1 << 11,

        ColonistLikeSocialTab = 1 << 12,
        SyntheticSpouseInteraction = 1 << 13,
        SyntheticPregnancy = 1 << 14,

        IdeologyMembership = 1 << 15,
        IdeologyFullParticipation = 1 << 16,
        Psycasting = 1 << 17,
        Royalty = 1 << 18,

        // 以下两项为本次“恋人/非机械师机械族”能力层化重构新增，
        // 必须保持在 Royalty 之后且不改变任何已有 bit。
        // ColonistLikeTimetable：允许拥有并使用原版 Pawn_TimetableTracker 数据层与真实 CurrentAssignment。
        // ClassroomTeaching：允许作为第三方课堂教育系统的教师候选（Skill / Daycare）。
        ColonistLikeTimetable = 1 << 19,
        ClassroomTeaching = 1 << 20,

        // 通用机械飞行资格仅来自独立飞行授权注册表；不改变既有能力位。
        Flight = 1 << 21,

        // 原版机控中枢或合体期间“机控同调”提供的机械师控制系统接入。
        MechanitorControl = 1 << 22,

        // 合体只能是先天能力，唯一事实来源是 CompMechFusionInnate 标记与合体资格注册表。
        // 保留原 bit 值，避免改变既有能力位的数值。
        Fusion = 1 << 23
    }
}

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 恋人专用生育方式。独立于原版 PregnancyApproach。
    /// 数值从 1 起，避免旧存档中已废弃的 0 被误映射为有效选项。
    /// </summary>
    public enum LoverPregnancyApproach : byte
    {
        AvoidPregnancy = 1,
        TryForBaby = 2,
        TryForBabyMale = 3,
        TryForBabyFemale = 4,
    }
}

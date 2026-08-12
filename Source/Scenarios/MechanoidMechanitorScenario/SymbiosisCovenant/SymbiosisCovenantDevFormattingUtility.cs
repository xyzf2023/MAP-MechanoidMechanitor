global using static MAP_MechanoidMechanitor.Scenarios.SymbiosisCovenantDevFormattingUtility;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 共生盟约 DEV 界面共用的轻量格式化入口。
    /// 使用 global using static 是为了让同一源码文件中的两个独立 Window 类
    /// 都能解析 Percent(...)，同时不扩大 Dialog_SymbiosisCovenant 私有方法的可见性。
    /// </summary>
    internal static class SymbiosisCovenantDevFormattingUtility
    {
        public static string Percent(float value)
        {
            return UnityEngine.Mathf.RoundToInt(value * 100f).ToString() + "%";
        }
    }
}

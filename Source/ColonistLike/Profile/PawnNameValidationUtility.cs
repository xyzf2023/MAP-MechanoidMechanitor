using System;
using System.Text.RegularExpressions;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 扩展原版 Pawn 名称输入规则，使名称可以包含中文直角引号「」。
    /// </summary>
    public static class PawnNameValidationUtility
    {
        /// <summary>
        /// 原版允许字符的基础上额外允许「和」。
        /// 保持首尾锚点，避免正则只匹配名称的一部分。
        /// </summary>
        private const string ExtendedPawnNamePattern =
            @"^[\p{L}0-9 '\-.「」]*$";

        /// <summary>
        /// 用于判断当前正则是否已经支持本次所需字符。
        /// </summary>
        private const string ValidationProbe = "「正义」 1";

        private static bool initialized;

        /// <summary>
        /// 保留当前名称正则已有规则，并追加对「」的支持。
        /// 此方法应只在 MOD 初始化期间调用一次。
        /// </summary>
        public static void ExtendPawnNameRegex()
        {
            if (initialized)
            {
                return;
            }

            initialized = true;

            try
            {
                Regex currentRegex = CharacterCardUtility.ValidNameRegex;

                // 当前规则已经支持目标名称时，不重复构造正则。
                // 这也兼容其他 MOD 已经完成同类扩展的情况。
                if (currentRegex != null && currentRegex.IsMatch(ValidationProbe))
                {
                    return;
                }

                // 正常情况下原版字段不会为 null。
                // 若字段被其他 MOD 异常清空，则使用扩展后的原版规则作为兜底。
                if (currentRegex == null)
                {
                    CharacterCardUtility.ValidNameRegex =
                        new Regex(ExtendedPawnNamePattern);

                    Log.Warning(
                        "[MAP-机械族机械师] CharacterCardUtility.ValidNameRegex 为 null，" +
                        "已使用包含中文直角引号的默认名称验证规则。");
                    return;
                }

                // 不能直接用固定规则覆盖当前正则。
                // 当前正则可能已经被其他 MOD 扩展，因此需要使用“当前规则 或 本 MOD 规则”的并集。
                string combinedPattern =
                    "(?:" + currentRegex + ")|(?:" + ExtendedPawnNamePattern + ")";

                Regex combinedRegex = new Regex(
                    combinedPattern,
                    currentRegex.Options);

                // 在真正替换原版字段之前进行一次结果验证。
                if (!combinedRegex.IsMatch(ValidationProbe))
                {
                    Log.Warning(
                        "[MAP-机械族机械师] 扩展 Pawn 名称验证规则失败，" +
                        "生成后的正则仍不接受测试名称：" + ValidationProbe);
                    return;
                }

                CharacterCardUtility.ValidNameRegex = combinedRegex;
            }
            catch (Exception ex)
            {
                // 名称规则扩展失败不应阻止整个 MOD 加载。
                // 保留原有规则并只记录一次警告。
                Log.Warning(
                    "[MAP-机械族机械师] 扩展 Pawn 名称验证规则时发生异常：" +
                    ex);
            }
        }
    }
}

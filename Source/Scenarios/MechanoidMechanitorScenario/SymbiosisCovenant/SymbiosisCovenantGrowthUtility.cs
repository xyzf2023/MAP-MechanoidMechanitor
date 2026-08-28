using UnityEngine;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 共生盟约 Trust / Unity「成长倍率」的唯一计算入口。
    /// 所有 Harmony Patch、QuestPart 与奖励调用点都必须通过这里换算，
    /// 禁止在业务代码里散写 amount * setting，避免倍率语义漂移与多套舍入规则。
    ///
    /// 本工具只作用于「变化 delta」。以下绝对状态设置一律不得经过这里：
    /// 初始化 Trust、声明前 Trust 锁定、SetTrustDirect、严重背叛直接设 -100、
    /// DEV 直接设置 Trust / Unity、盟约解散 Unity 归零、旧存档绝对值校准。
    /// 成长倍率也绝不影响任何军事援军点数。
    /// </summary>
    public static class SymbiosisCovenantGrowthUtility
    {
        /// <summary>倍率档位下限（tenths）：5 表示 ×0.5。</summary>
        public const int MinMultiplierTenths = 5;

        /// <summary>倍率档位默认值（tenths）：10 表示 ×1.0。</summary>
        public const int DefaultMultiplierTenths = 10;

        /// <summary>倍率档位上限（tenths）：20 表示 ×2.0。</summary>
        public const int MaxMultiplierTenths = 20;

        /// <summary>
        /// 当前生效的倍率档位。设置对象缺失（尚未初始化 / 异常配置）时按默认 ×1.0 处理，
        /// 保证任何调用点都不会因为拿不到设置而放大或吞掉奖励。
        /// </summary>
        public static int CurrentMultiplierTenths
        {
            get
            {
                MAPMechanitorModSettings? settings = MAPMechanitorMod.Settings;
                if (settings == null)
                {
                    return DefaultMultiplierTenths;
                }

                return Mathf.Clamp(
                    settings.symbiosisCovenantGrowthMultiplierTenths,
                    MinMultiplierTenths,
                    MaxMultiplierTenths);
            }
        }

        /// <summary>当前实际倍率：tenths / 10。</summary>
        public static float CurrentMultiplier => CurrentMultiplierTenths / 10f;

        /// <summary>
        /// 对带符号的整数基础变化统一应用成长倍率，并按「变化幅度」向上取整。
        /// +7 ×0.5 = +3.5 → +4；-7 ×0.5 = -3.5 → -4。
        /// </summary>
        public static int ScaleDelta(int baseDelta)
        {
            if (baseDelta == 0)
            {
                return 0;
            }

            int magnitude = CeilMagnitude(Mathf.Abs((float)baseDelta) * CurrentMultiplier);
            return baseDelta > 0 ? magnitude : -magnitude;
        }

        /// <summary>
        /// 浮点版，供每日团结度结算使用：舍入同样只发生在最终变化层级，
        /// 避免成员越多时逐成员取整产生额外重复收益。
        /// </summary>
        public static int ScaleDelta(float baseDelta)
        {
            if (Mathf.Approximately(baseDelta, 0f))
            {
                return 0;
            }

            int magnitude = CeilMagnitude(Mathf.Abs(baseDelta) * CurrentMultiplier);
            return baseDelta > 0f ? magnitude : -magnitude;
        }

        /// <summary>
        /// 来源窗口上限的倍率换算。窗口基础上限本身就是「×1.0 下的值」，
        /// 只在这里换算一次，调用方严禁对结果二次乘倍率。
        /// </summary>
        public static int ScaleWindowCap(int baseCap)
        {
            if (baseCap <= 0)
            {
                return 0;
            }

            return CeilMagnitude(baseCap * CurrentMultiplier);
        }

        /// <summary>
        /// 变化幅度向上取整。必须先取绝对值再取整：
        /// 直接 Mathf.Ceiling(-3.5) 得到 -3，会朝零方向取整而额外减轻负面效果。
        /// 输入恒为非负；极小的正幅度也至少保留 1 点，避免低倍率把有效奖励完全抹平。
        /// </summary>
        private static int CeilMagnitude(float rawMagnitude)
        {
            return Mathf.Max(1, Mathf.CeilToInt(rawMagnitude));
        }
    }
}

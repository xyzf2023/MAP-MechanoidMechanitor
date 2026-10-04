using System.Collections.Generic;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.YetAnotherOptimizer
{
    internal static class YetAnotherOptimizerColonistBarPatch
    {
        internal static bool Enabled { get; set; }

        // 按参数位置绑定，避免依赖上游参数名。
        public static void Postfix(MapPawns __0, ref List<Pawn> __result)
        {
            if (!Enabled || __0 == null || __result == null)
            {
                return;
            }

            // 复用剧本开关、正式身份、地图归属与去重规则。
            // requireSpawned: false 与原版头像栏读取 FreeColonists 的规则一致，
            // 可保留地图内被携带或位于容器中的合格机械师。
            // BuildMapResult 复制到本 MOD 的缓冲区，不修改 YAO 借出的池化列表。
            __result = MechanoidMechanitorScenarioFreeColonistResultUtility.BuildMapResult(
                __0, __result, requireSpawned: false);
        }
    }
}

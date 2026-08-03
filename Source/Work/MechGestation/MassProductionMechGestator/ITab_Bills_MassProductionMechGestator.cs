using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 量产型机械培育仓专用账单标签页。
    ///
    /// 原版 ITab_Bills 会将 RecipeDef.displayPriority 取负后传给 FloatMenu，
    /// FloatMenu 再按 orderInPriority 从大到小排序，因此 displayPriority
    /// 数值越小，最终菜单位置越靠前。
    ///
    /// 本标签页只在原版菜单同步构建期间临时降低复活配方的 displayPriority，
    /// 使所有复活配方形成连续的置顶区块；菜单构建结束后立即恢复全局 Def。
    /// </summary>
    public class ITab_Bills_MassProductionMechGestator : ITab_Bills
    {
        /// <summary>
        /// 必须足够小，以确保复活配方排在普通培育配方之前。
        ///
        /// 不得使用 int.MinValue，因为 ITab_Bills 会计算 -displayPriority，
        /// 对 int.MinValue 取负会产生整数溢出风险。
        /// </summary>
        private const int ResurrectionMenuDisplayPriority = -1000000000;

        protected override void FillTab()
        {
            Building_WorkTable table = SelTable;

            // 理论上该标签页只会挂载到量产型机械培育仓。
            // 保留类型检查，避免 XML 被其他 MOD 复用时影响未知建筑。
            if (table is not Building_MassProductionMechGestator)
            {
                base.FillTab();
                return;
            }

            List<RecipeDef> recipes = table.def.AllRecipes;
            if (recipes == null || recipes.Count == 0)
            {
                base.FillTab();
                return;
            }

            // AllRecipes 理论上不应包含重复 RecipeDef，
            // 但当前量产仓同时支持 XML 配方、recipeUsers 和动态追加。
            // 使用 Dictionary 按 RecipeDef 去重，保证每个配方只保存和恢复一次。
            Dictionary<RecipeDef, int> originalPriorities =
                new Dictionary<RecipeDef, int>();

            try
            {
                for (int i = 0; i < recipes.Count; i++)
                {
                    RecipeDef recipe = recipes[i];
                    if (recipe == null
                        || !recipe.mechResurrection
                        || originalPriorities.ContainsKey(recipe))
                    {
                        continue;
                    }

                    originalPriorities.Add(recipe, recipe.displayPriority);
                    recipe.displayPriority = ResurrectionMenuDisplayPriority;
                }

                // 必须继续调用原版账单标签页。
                // 不复制原版 FillTab，以保留剪贴板、账单上限、科研检查、
                // 技能提示、机械师检查、信息卡、教程事件及其他 MOD 兼容。
                base.FillTab();
            }
            finally
            {
                // RecipeDef 是全局 Def。
                // 无论 base.FillTab 是否正常返回或抛出异常，都必须恢复原值。
                foreach (KeyValuePair<RecipeDef, int> pair in originalPriorities)
                {
                    pair.Key.displayPriority = pair.Value;
                }
            }
        }
    }
}

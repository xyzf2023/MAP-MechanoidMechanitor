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
    /// 本标签页只在原版菜单同步构建期间临时调整极端优先级：
    ///
    /// 1. 所有机械族复活配方使用最大的安全菜单排序值；
    /// 2. 占用同一保留值或 int.MinValue 的普通配方临时向后移动；
    /// 3. 菜单构建结束后立即恢复所有全局 RecipeDef。
    ///
    /// 最终保证所有 mechResurrection 配方形成连续的置顶区块，
    /// 同时不永久改变原版或其他 MOD 的配方定义。
    /// </summary>
    public class ITab_Bills_MassProductionMechGestator : ITab_Bills
    {
        /// <summary>
        /// 原版会计算 -displayPriority。
        ///
        /// 使用 int.MinValue + 1 后，取负结果恰好是 int.MaxValue，
        /// 即 FloatMenu 的最大安全 orderInPriority。
        ///
        /// 不得直接使用 int.MinValue，因为对 int.MinValue 取负存在溢出风险。
        /// </summary>
        private const int ResurrectionMenuDisplayPriority = int.MinValue + 1;

        /// <summary>
        /// 非复活配方若使用 int.MinValue 或 int.MinValue + 1，
        /// 会产生取负溢出边界或与复活配方并列最高优先级。
        ///
        /// 菜单构建期间将这些极端普通配方临时调整到 int.MinValue + 2，
        /// 使其最终 orderInPriority 为 int.MaxValue - 1，
        /// 严格低于复活配方，但仍高于绝大多数普通配方。
        /// </summary>
        private const int ExtremeNormalMenuDisplayPriority = int.MinValue + 2;

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

            // AllRecipes 可能同时包含 XML 配方、recipeUsers 配方和动态追加配方。
            // 使用 Dictionary 按 RecipeDef 去重，确保每个全局 Def 只保存和恢复一次。
            Dictionary<RecipeDef, int> originalPriorities =
                new Dictionary<RecipeDef, int>();

            try
            {
                for (int i = 0; i < recipes.Count; i++)
                {
                    RecipeDef recipe = recipes[i];
                    if (recipe == null || originalPriorities.ContainsKey(recipe))
                    {
                        continue;
                    }

                    if (recipe.mechResurrection)
                    {
                        originalPriorities.Add(recipe, recipe.displayPriority);
                        recipe.displayPriority = ResurrectionMenuDisplayPriority;
                        continue;
                    }

                    // int.MinValue：
                    // 原版执行一元取负时存在整数溢出边界。
                    //
                    // int.MinValue + 1：
                    // 取负后会与复活配方同为 int.MaxValue，无法保证复活区块严格置顶。
                    //
                    // 因此只临时规范化这两个极端非复活值。
                    if (recipe.displayPriority <= ResurrectionMenuDisplayPriority)
                    {
                        originalPriorities.Add(recipe, recipe.displayPriority);
                        recipe.displayPriority = ExtremeNormalMenuDisplayPriority;
                    }
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

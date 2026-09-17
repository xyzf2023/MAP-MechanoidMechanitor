using System.Collections.Generic;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// XML 配置的动态说明来源。实例由 Def 共享，不得保存 Pawn 或查询结果，
    /// 不得在读取说明时修改业务状态或回调父 Hediff 的提示入口。
    /// </summary>
    public abstract class HediffExtraDescriptionProvider
    {
        public abstract IEnumerable<HediffExtraDescriptionEntry> GetEntries(Hediff hediff);
    }
}

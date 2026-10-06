using System.Collections.Generic;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.FamilyRelationsAdoption
{
    internal static class FamilyRelationsAdoptionParentQueryPatch
    {
        internal static bool Enabled;

        public static void Postfix(Pawn? __0, ref List<Pawn>? __result)
        {
            // 按参数位置绑定；仅保护本 MOD 开放社交查询的非血肉角色。
            if (!Enabled || __result != null || __0 == null || __0.relations == null
                || __0.RaceProps?.IsFlesh != false
                || !ColonistLikeSocialTabUtility.HasSocialTab(__0))
                return;

            // 上游合并亲生父母时会 AddRange 修改该列表，不能复用共享空列表。
            // 不改变领养资格、不补建 Tracker，也不在只读查询中写入关系或授权。
            __result = new List<Pawn>();
        }
    }
}

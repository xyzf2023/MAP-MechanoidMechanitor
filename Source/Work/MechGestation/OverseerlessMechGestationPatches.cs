using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 原版 Bill_ProductionMech.CreateProducts 会无条件建立 Overseer 关系，
    /// 并立即将产物加入执行者控制组。对 requiresExternalOverseer=false 的机械师节点，
    /// 需在生成后仅清除 producer↔product 这一对关系。
    /// </summary>
    [HarmonyPatch(
        typeof(Bill_ProductionMech),
        nameof(Bill_ProductionMech.CreateProducts))]
    public static class Patch_Bill_ProductionMech_CreateProducts_OverseerlessNode
    {
        [HarmonyPostfix]
        public static void Postfix(
            Bill_ProductionMech __instance,
            Thing __result)
        {
            if (__result is not Pawn product)
            {
                return;
            }

            GameComponent_AutonomousMechRegistry.SynchronizeAutomaticSources(product);
            if (!AutonomousMechUtility.IsAutonomousMech(product))
            {
                return;
            }

            Pawn? producer = __instance.BoundPawn;
            if (producer?.relations != null)
            {
                // 不可用 product.GetOverseer()：无监管者节点的查询补丁会将其过滤为 null。
                producer.relations.TryRemoveDirectRelation(
                    PawnRelationDefOf.Overseer,
                    product);

                // TryRemoveDirectRelation 会触发 OnRelationRemoved → Unassign；
                // 仅在仍残留于控制组时兜底。
                if (producer.mechanitor?.GetControlGroup(product) != null)
                {
                    producer.mechanitor.UnassignPawnFromAnyControlGroup(product);
                }

                producer.mechanitor?.Notify_BandwidthChanged();
            }

            // CompNativeMechanoidMechanitor.PostPostMake 已登记先天身份；
            // EnsureRoleState 仅对已是机械族机械师的产物幂等补齐状态。
            if (MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(product))
            {
                MechanoidMechanitorRoleUtility.EnsureRoleState(product);
                MAPMechanitorNodeLifecycleUtility.EnsureBasicTrackers(product);
            }
            GameComponent_AutonomousMechRegistry.NotifyPawnLifecycle(product);
        }
    }
}

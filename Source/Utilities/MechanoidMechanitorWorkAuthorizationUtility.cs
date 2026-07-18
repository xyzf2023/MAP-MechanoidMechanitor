using System;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 由机械族机械师身份模块统一授予/撤销 Warden、AnimalHandling、Mechanical Childcare 与通用背景故事。
    /// </summary>
    public static class MechanoidMechanitorWorkAuthorizationUtility
    {
        public static void GrantAndEnsureInfrastructure(Pawn? pawn)
        {
            if (pawn == null || pawn.Destroyed)
            {
                return;
            }

            MechanoidBackstoryUtility.EnsureGenericBackstories(pawn);
            WardenWorkUtility.GrantAndEnsureInfrastructure(pawn);

            if (pawn.GetComp<CompAnimalHandlingWorkUser>() != null)
            {
                AnimalHandlingWorkUtility.EnsureInfrastructure(pawn);
            }
            else
            {
                AnimalHandlingWorkUtility.GrantAndEnsureInfrastructure(pawn, 3);
            }

            if (pawn.GetComp<CompMechanicalChildcareUser>() != null)
            {
                MechanicalChildcareUtility.EnsureInfrastructure(pawn);
            }
            else
            {
                MechanicalChildcareUtility.GrantAndEnsureInfrastructure(pawn, 3);
            }
        }

        /// <summary>
        /// 统一撤销由机械族机械师身份授予的动态工作授权。
        /// 允许死亡或尸体中的 Pawn 清理持久记录；仅空引用与 Discarded 直接结束。
        /// </summary>
        public static void RevokeGrantedAuthorizations(Pawn? pawn)
        {
            if (pawn == null || pawn.Discarded)
            {
                return;
            }

            try
            {
                GameComponent_WardenWorkRegistry.Revoke(pawn);
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] 撤销监管工作授权异常：" +
                    $"pawn={pawn.LabelShort}（{pawn.ThingID}）：{ex}");
            }

            try
            {
                GameComponent_AnimalHandlingWorkRegistry.Revoke(pawn);
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] 撤销驯兽工作授权异常：" +
                    $"pawn={pawn.LabelShort}（{pawn.ThingID}）：{ex}");
            }

            try
            {
                GameComponent_MechanicalChildcareRegistry.Revoke(pawn);
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] 撤销保育工作授权异常：" +
                    $"pawn={pawn.LabelShort}（{pawn.ThingID}）：{ex}");
            }
        }
    }
}

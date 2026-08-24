using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class ProxySubchainUtility
    {
        private static readonly Dictionary<Pawn, int> supportedHolderTransitionDepth =
            new Dictionary<Pawn, int>();

        public static bool HasImplant(Pawn? pawn)
        {
            return ImplantEffectUtility.HasHediff(
                pawn,
                MAPMechanitor_HediffDefOf.MAP_ProxySubchain);
        }

        public static bool CanMaintainControl(Pawn? pawn)
        {
            if (!IsEligibleHost(pawn))
            {
                return false;
            }

            if (pawn!.Spawned)
            {
                return true;
            }

            if (pawn.MapHeld == null)
            {
                return false;
            }

            if (pawn.ParentHolder is Pawn_CarryTracker carryTracker)
            {
                return carryTracker.pawn != null && carryTracker.pawn.Spawned;
            }

            if (pawn.ParentHolder is Building_CryptosleepCasket casket && casket.Spawned)
            {
                return true;
            }

            // 文化 DLC 塑形仓（CompBiosculpterPod）内的机械师可继续控制。
            if (IsSupportedBiosculpterPodHolder(pawn))
            {
                return true;
            }

            // 原版空投舱发射器（TransportPod）内、且尚未发射离图的机械师可继续控制。
            if (pawn.ParentHolder is CompTransporter transporter
                && IsOriginalTransportPodTransporter(transporter))
            {
                return true;
            }

            return false;
        }

        // 是否为有效代理子链机械师（供各 Harmony 补丁类的临时例外判断复用）。
        public static bool IsValidProxySubchainMechanitor(Pawn? pawn)
        {
            return IsEligibleHost(pawn);
        }

        // 是否为原版空投舱发射器（TransportPod）的 CompTransporter，且其父建筑仍在当前地图。
        // 仅放行原版空投舱发射器，不覆盖穿梭机、运输车队、传送门或其他 Mod 的任意运输容器。
        public static bool IsOriginalTransportPodTransporter(CompTransporter? comp)
        {
            if (comp == null)
            {
                return false;
            }

            Thing? parentBuilding = comp.parent;
            return parentBuilding != null
                && parentBuilding.def == ThingDefOf.TransportPod
                && parentBuilding.Spawned;
        }

        // 文化 DLC 未加载时直接返回 false，避免对类型或 Def 的引用引发错误。
        private static bool IsSupportedBiosculpterPodHolder(Pawn pawn)
        {
            if (!ModsConfig.IdeologyActive)
            {
                return false;
            }

            if (pawn.ParentHolder is not CompBiosculpterPod pod)
            {
                return false;
            }

            Thing? parentBuilding = pod.parent;
            return parentBuilding != null
                && parentBuilding.Spawned
                && parentBuilding.Map == pawn.MapHeld;
        }

        // 收集本次发射组中、位于原版空投舱发射器内、且为有效代理子链机械师的 Pawn。
        // 在 CompLaunchable.TryLaunch 真正执行前调用，以在原容器被销毁/转移前锁定目标。
        public static List<Pawn> CollectProxySubchainMechanitorsInOriginalTransportPods(
            CompLaunchable launchable)
        {
            List<Pawn> result = new List<Pawn>();
            if (launchable == null)
            {
                return result;
            }

            ThingWithComps? parent = launchable.parent;
            if (parent == null)
            {
                return result;
            }

            Map? map = parent.Map;
            if (map == null)
            {
                return result;
            }

            CompTransporter? ownTransporter = parent.TryGetComp<CompTransporter>();
            if (ownTransporter == null)
            {
                return result;
            }

            // TransportersInGroup 返回原版内部共享的临时列表，复制一份以避免遍历期间被复用污染。
            List<CompTransporter> group = new List<CompTransporter>(
                ownTransporter.TransportersInGroup(map));
            foreach (CompTransporter comp in group)
            {
                if (!IsOriginalTransportPodTransporter(comp))
                {
                    continue;
                }

                foreach (Thing thing in comp.GetDirectlyHeldThings())
                {
                    if (thing is Pawn p
                        && IsValidProxySubchainMechanitor(p)
                        && p.mechanitor != null)
                    {
                        result.Add(p);
                    }
                }
            }

            return result;
        }

        // 对发射前采集的机械师，仅当发射后确实已离开“地图内空投舱发射器”白名单状态时，
        // 才解除其下属机械族征召；发射被取消、或仍停留在地图内发射器中的 Pawn 不受影响。
        public static void UndraftProxySubchainMechsIfLeftSupportedHolders(List<Pawn>? pawns)
        {
            if (pawns == null)
            {
                return;
            }

            foreach (Pawn pawn in pawns)
            {
                if (pawn == null || pawn.Destroyed)
                {
                    continue;
                }

                if (pawn.mechanitor == null)
                {
                    continue;
                }

                if (!CanMaintainControl(pawn))
                {
                    pawn.mechanitor.UndraftAllMechs();
                }
            }
        }

        public static Pawn? BeginSupportedHolderTransition(Thing? thing)
        {
            if (thing is not Pawn pawn || !IsEligibleHost(pawn))
            {
                return null;
            }

            supportedHolderTransitionDepth.TryGetValue(pawn, out int depth);
            supportedHolderTransitionDepth[pawn] = depth + 1;
            return pawn;
        }

        public static void EndSupportedHolderTransition(Pawn? pawn)
        {
            if (pawn == null || !supportedHolderTransitionDepth.TryGetValue(pawn, out int depth))
            {
                return;
            }

            if (depth <= 1)
            {
                supportedHolderTransitionDepth.Remove(pawn);
                return;
            }

            supportedHolderTransitionDepth[pawn] = depth - 1;
        }

        public static bool ShouldPreserveControlDuringCurrentHolderTransition(Pawn? pawn)
        {
            return IsEligibleHost(pawn)
                && pawn != null
                && supportedHolderTransitionDepth.ContainsKey(pawn);
        }

        public static bool TryGetHeldCommandOrigin(
            Pawn? mech,
            out Map? map,
            out IntVec3 origin)
        {
            map = null;
            origin = IntVec3.Invalid;

            Pawn? overseer = mech == null
                ? null
                : MAPOverseerRelationDirectionUtility.FindActualOverseer(mech);
            if (overseer == null
                || overseer.Spawned
                || !CanMaintainControl(overseer)
                || overseer.mechanitor?.ControlledPawns?.Contains(mech!) != true)
            {
                return false;
            }

            map = overseer.MapHeld;
            origin = overseer.PositionHeld;
            return map != null && origin.IsValid;
        }

        private static bool IsEligibleHost(Pawn? pawn)
        {
            return ModsConfig.BiotechActive
                && pawn != null
                && !pawn.Destroyed
                && !pawn.Dead
                && !pawn.IsPrisoner
                && pawn.Faction != null
                && pawn.Faction.IsPlayerSafe()
                && pawn.mechanitor != null
                && MechanitorUtility.IsMechanitor(pawn)
                && HasImplant(pawn);
        }
    }
}

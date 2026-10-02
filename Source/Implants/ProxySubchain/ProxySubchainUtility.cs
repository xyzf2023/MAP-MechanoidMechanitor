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

        public static bool HasEffect(Pawn? pawn)
        {
            return HasImplant(pawn)
                || MechFusionMechanitorSynchronizationService
                    .GrantsProxySubchainEffect(pawn);
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

            Map? map = pawn.MapHeld;
            if (map == null)
            {
                return false;
            }

            IThingHolder? holder = pawn.ParentHolder;
            // 搬运者也可能进入穿梭机等容器；沿搬运链寻找地图上的有效指挥位置。
            while (holder is Pawn_CarryTracker carryTracker)
            {
                Pawn? carrier = carryTracker.pawn;
                if (carrier == null || carrier.Destroyed || carrier.Dead || carrier == pawn)
                {
                    return false;
                }

                if (carrier.Spawned)
                {
                    return carrier.Map == map;
                }

                holder = carrier.ParentHolder;
            }

            return IsSupportedHolder(holder, map);
        }

        // 仅认可仍在地图上的原版容器，不将飞行运输舱、世界对象或任意 ThingOwner 放行。
        public static bool IsSupportedHolder(IThingHolder? holder, Map? expectedMap)
        {
            switch (holder)
            {
                case Building_CryptosleepCasket casket:
                    return IsSpawnedOnMap(casket, expectedMap);
                case Building_Enterable building:
                    // 基因提取器、成长舱及软/高阶子核心扫描仪共享此原版基类。
                    return IsSpawnedOnMap(building, expectedMap);
                case Building_HoldingPlatform platform:
                    return ModsConfig.AnomalyActive && IsSpawnedOnMap(platform, expectedMap);
                case PawnFlyer flyer:
                    return IsSpawnedOnMap(flyer, expectedMap);
                case CompDevourer devourer:
                    return ModsConfig.AnomalyActive && IsSpawnedOnMap(devourer.parent, expectedMap);
                case CompBiosculpterPod pod:
                    return IsSupportedBiosculpterPod(pod, expectedMap);
                case CompTransporter transporter:
                    return IsSupportedTransporter(transporter, expectedMap);
                default:
                    return false;
            }
        }

        private static bool IsSpawnedOnMap(Thing? thing, Map? expectedMap)
        {
            return thing != null && !thing.Destroyed && thing.Spawned && thing.Map != null
                && (expectedMap == null || thing.Map == expectedMap);
        }

        // 空投舱及带有原版穿梭机组件的运输器均可在登舱后、起飞前维持控制。
        public static bool IsSupportedTransporter(CompTransporter? comp, Map? expectedMap = null)
        {
            ThingWithComps? parent = comp?.parent;
            return IsSpawnedOnMap(parent, expectedMap)
                && (parent!.def == ThingDefOf.TransportPod || parent.GetComp<CompShuttle>() != null);
        }

        // 是否为有效代理子链机械师（供各 Harmony 补丁类的临时例外判断复用）。
        public static bool IsValidProxySubchainMechanitor(Pawn? pawn)
        {
            return IsEligibleHost(pawn);
        }

        // 统一判断：指定塑形仓是否为代理子链可认可的受支持容器。
        // 仅当文化 DLC 已启用、组件与父建筑有效、父建筑已生成且与期望地图一致时才返回 true。
        public static bool IsSupportedBiosculpterPod(CompBiosculpterPod? pod, Map? expectedMap)
        {
            if (!ModsConfig.IdeologyActive)
            {
                return false;
            }

            if (pod == null)
            {
                return false;
            }

            return IsSpawnedOnMap(pod.parent, expectedMap);
        }

        // 收集本次发射组内受支持容器中的机械师，包括搬运者携带的机械师。
        // 在 CompLaunchable.TryLaunch 真正执行前调用，以在原容器被销毁/转移前锁定目标。
        public static List<Pawn> CollectProxySubchainMechanitorsInSupportedTransporters(
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

            // 原版 TransportersInGroup 在运输组尚未建立、发射条件不成立等拒绝发射路径中可能返回 null。
            // 代理子链 Prefix 先于原版 CompLaunchable.TryLaunch 执行，必须先判空，
            // 避免让原版本来只会拒绝发射的操作变成空引用报错。
            List<CompTransporter>? originalGroup = ownTransporter.TransportersInGroup(map);
            if (originalGroup == null)
            {
                return result;
            }

            // 复制原版内部共享的临时列表后再遍历，避免遍历期间被复用污染。
            List<CompTransporter> group = new List<CompTransporter>(originalGroup);
            foreach (CompTransporter comp in group)
            {
                if (!IsSupportedTransporter(comp, map))
                {
                    continue;
                }

                CollectHeldMechanitors(comp, result);
            }

            return result;
        }

        // 任务穿梭机不通过 CompLaunchable 起飞，直接在其搬运容器转移前采集。
        public static List<Pawn> CollectProxySubchainMechanitorsInTransporter(CompTransporter? comp)
        {
            List<Pawn> result = new List<Pawn>();
            if (IsSupportedTransporter(comp))
            {
                CollectHeldMechanitors(comp!, result);
            }
            return result;
        }

        private static void CollectHeldMechanitors(IThingHolder holder, List<Pawn> result)
        {
            foreach (Thing thing in ThingOwnerUtility.GetAllThingsRecursively(holder))
            {
                if (thing is Pawn pawn && CanMaintainControl(pawn) && !result.Contains(pawn))
                {
                    result.Add(pawn);
                }
            }
        }

        // 对起飞前采集的机械师，仅当起飞后确实已离开地图内受支持容器时，
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

        // 容器入口整体执行完后再校验，异常或加入失败不得留下临时控制例外。
        public static void CompleteSupportedHolderTransition(Pawn? pawn)
        {
            EndSupportedHolderTransition(pawn);
            if (pawn != null && !supportedHolderTransitionDepth.ContainsKey(pawn)
                && !pawn.Destroyed && pawn.mechanitor != null && !CanMaintainControl(pawn))
            {
                pawn.mechanitor.UndraftAllMechs();
            }
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
                && HasEffect(pawn);
        }
    }
}

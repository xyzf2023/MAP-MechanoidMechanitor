using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 机械意识全局唯一，因此紧急转移也必须是全局单事务。
    /// 事务存在时允许其他 Pawn 正常死亡，但禁止其再次发起嵌套紧急转移。
    /// </summary>
    internal static class EmergencyMechanicalConsciousnessTransferGlobalGuard
    {
        private static readonly HashSet<Pawn> Participants =
            new HashSet<Pawn>(ReferencePawnComparer.Instance);

        private static Pawn? rootSource;
        private static Pawn? currentTarget;

        internal static bool Active => rootSource != null;

        internal static bool TryBegin(Pawn? source)
        {
            if (source == null || Active)
            {
                return false;
            }

            rootSource = source;
            currentTarget = null;
            Participants.Clear();
            Participants.Add(source);
            return true;
        }

        internal static bool TryRegisterTarget(Pawn? target)
        {
            if (!Active
                || target == null
                || target.Dead
                || target.Destroyed
                || target.Discarded
                || target.health?.isBeingKilled == true
                || ReferenceEquals(target, rootSource))
            {
                return false;
            }

            if (ReferenceEquals(target, currentTarget))
            {
                return true;
            }

            if (Participants.Contains(target))
            {
                return false;
            }

            currentTarget = target;
            Participants.Add(target);
            return true;
        }

        internal static bool IsParticipant(Pawn? pawn)
        {
            return pawn != null && Participants.Contains(pawn);
        }

        internal static bool IsRootSource(Pawn? pawn)
        {
            return pawn != null && ReferenceEquals(rootSource, pawn);
        }

        internal static void MarkCurrentTargetFailed(Pawn? target)
        {
            if (ReferenceEquals(currentTarget, target))
            {
                currentTarget = null;
            }
        }

        internal static void Complete(Pawn? source)
        {
            if (!ReferenceEquals(rootSource, source))
            {
                return;
            }

            rootSource = null;
            currentTarget = null;
            Participants.Clear();
        }

        private sealed class ReferencePawnComparer : IEqualityComparer<Pawn>
        {
            internal static readonly ReferencePawnComparer Instance =
                new ReferencePawnComparer();

            public bool Equals(Pawn? x, Pawn? y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(Pawn obj)
            {
                return obj == null ? 0 : obj.thingIDNumber;
            }
        }
    }

    [HarmonyPatch(
        typeof(EmergencyMechanicalConsciousnessTransferUtility),
        nameof(EmergencyMechanicalConsciousnessTransferUtility.TryBeginEmergencyTransferAttempt))]
    internal static class EmergencyTransferGlobalBeginGuardPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Pawn? pawn, ref bool __result)
        {
            if (MechanoidMechanitorPostLoadSafetyCoordinator.LoadInProgress
                || EmergencyMechanicalConsciousnessTransferGlobalGuard.Active)
            {
                __result = false;
                return false;
            }

            return true;
        }

        [HarmonyPostfix]
        private static void Postfix(Pawn? pawn, ref bool __result)
        {
            if (__result
                && !EmergencyMechanicalConsciousnessTransferGlobalGuard.TryBegin(pawn))
            {
                EmergencyMechanicalConsciousnessTransferUtility.ClearAttemptGuard(pawn);
                __result = false;
            }
        }
    }

    /// <summary>
    /// 旧局部 guard 不能再让非根目标的 Pawn.Kill Prefix 返回 false，从而吞掉原版死亡。
    /// 根来源的递归 Kill 仍由旧 guard 阻止，其他参与者则只跳过再次转移、继续原版 Kill。
    /// </summary>
    [HarmonyPatch(
        typeof(EmergencyMechanicalConsciousnessTransferUtility),
        "IsAttemptInProgress")]
    internal static class EmergencyTransferDoNotSuppressOriginalKillPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Pawn? pawn, ref bool __result)
        {
            if (!EmergencyMechanicalConsciousnessTransferGlobalGuard.Active
                || EmergencyMechanicalConsciousnessTransferGlobalGuard
                    .IsRootSource(pawn))
            {
                return true;
            }

            __result = false;
            return false;
        }
    }

    [HarmonyPatch(
        typeof(EmergencyMechanicalConsciousnessTransferUtility),
        nameof(EmergencyMechanicalConsciousnessTransferUtility.ClearAttemptGuard))]
    internal static class EmergencyTransferGlobalGuardCleanupPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Pawn? pawn)
        {
            EmergencyMechanicalConsciousnessTransferGlobalGuard.Complete(pawn);
        }
    }

    [HarmonyPatch(
        typeof(EmergencyMechanicalConsciousnessTransferUtility),
        nameof(EmergencyMechanicalConsciousnessTransferUtility.SelectEmergencyTransferTarget))]
    internal static class EmergencyTransferSafeTargetSelectionPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(
            Pawn source,
            Pawn? excludePawn,
            ref Pawn? __result)
        {
            Pawn? firstOtherMechanitor = null;
            HashSet<Pawn> seen = new HashSet<Pawn>();

            foreach (Pawn candidate in GameComponent_MechanoidMechanitorRegistry
                         .GetMechanicalConsciousnessCandidates())
            {
                if (candidate == null
                    || candidate.Dead
                    || candidate.Destroyed
                    || candidate.Discarded
                    || candidate.health?.isBeingKilled == true
                    || !seen.Add(candidate)
                    || ReferenceEquals(candidate, source)
                    || ReferenceEquals(candidate, excludePawn)
                    || EmergencyMechanicalConsciousnessTransferGlobalGuard
                        .IsParticipant(candidate))
                {
                    continue;
                }

                if (!MechanicalConsciousnessTransferUtility
                        .CanTransferMechanicalConsciousness(source, candidate))
                {
                    continue;
                }

                if (JusticePawnUtility.IsJustice(candidate))
                {
                    __result =
                        EmergencyMechanicalConsciousnessTransferGlobalGuard
                            .TryRegisterTarget(candidate)
                            ? candidate
                            : null;
                    return false;
                }

                firstOtherMechanitor ??= candidate;
            }

            if (firstOtherMechanitor != null
                && EmergencyMechanicalConsciousnessTransferGlobalGuard
                    .TryRegisterTarget(firstOtherMechanitor))
            {
                __result = firstOtherMechanitor;
            }
            else
            {
                __result = null;
            }

            return false;
        }
    }

    /// <summary>
    /// 轨道数据网络完成后的“紧急控制权交接”目标选择必须套用同一全局事务 guard：
    /// 跳过正在死亡、已被 guard 注册参与的目标，并把选定目标注册为参与方，
    /// 防止交接事务期间出现嵌套紧急转移或吞掉原版 Kill。
    /// 该方法业务原体与紧急控制权交接共用 MechanicalControlHandoffUtility.CanTransferControl。
    /// </summary>
    [HarmonyPatch(
        typeof(EmergencyMechanicalConsciousnessTransferUtility),
        nameof(EmergencyMechanicalConsciousnessTransferUtility.SelectEmergencyControlHandoffTarget))]
    internal static class EmergencyControlHandoffSafeTargetSelectionPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(
            Pawn source,
            Pawn? excludePawn,
            ref Pawn? __result)
        {
            Pawn? firstOtherMechanitor = null;
            HashSet<Pawn> seen = new HashSet<Pawn>();

            foreach (Pawn candidate in GameComponent_MechanoidMechanitorRegistry
                         .GetMechanicalConsciousnessCandidates())
            {
                if (candidate == null
                    || candidate.Dead
                    || candidate.Destroyed
                    || candidate.Discarded
                    || candidate.health?.isBeingKilled == true
                    || !seen.Add(candidate)
                    || ReferenceEquals(candidate, source)
                    || ReferenceEquals(candidate, excludePawn)
                    || EmergencyMechanicalConsciousnessTransferGlobalGuard
                        .IsParticipant(candidate))
                {
                    continue;
                }

                if (!MechanicalControlHandoffUtility.CanTransferControl(
                        source,
                        candidate))
                {
                    continue;
                }

                if (JusticePawnUtility.IsJustice(candidate))
                {
                    __result =
                        EmergencyMechanicalConsciousnessTransferGlobalGuard
                            .TryRegisterTarget(candidate)
                            ? candidate
                            : null;
                    return false;
                }

                firstOtherMechanitor ??= candidate;
            }

            if (firstOtherMechanitor != null
                && EmergencyMechanicalConsciousnessTransferGlobalGuard
                    .TryRegisterTarget(firstOtherMechanitor))
            {
                __result = firstOtherMechanitor;
            }
            else
            {
                __result = null;
            }

            return false;
        }
    }

    [HarmonyPatch(
        typeof(MechanicalConsciousnessTransferUtility),
        "TryTransferMechanicalConsciousness",
        new[]
        {
            typeof(Pawn),
            typeof(Pawn),
            typeof(MechanicalConsciousnessTransferContext)
        })]
    internal static class EmergencyTransferParticipantValidationPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(
            Pawn? target,
            MechanicalConsciousnessTransferContext context,
            ref bool __result)
        {
            if (context != MechanicalConsciousnessTransferContext.Emergency)
            {
                return true;
            }

            if (!EmergencyMechanicalConsciousnessTransferGlobalGuard.Active
                || !EmergencyMechanicalConsciousnessTransferGlobalGuard
                    .TryRegisterTarget(target))
            {
                __result = false;
                return false;
            }

            return true;
        }

        [HarmonyPostfix]
        private static void Postfix(
            Pawn? target,
            MechanicalConsciousnessTransferContext context,
            ref bool __result)
        {
            if (context == MechanicalConsciousnessTransferContext.Emergency
                && !__result)
            {
                EmergencyMechanicalConsciousnessTransferGlobalGuard
                    .MarkCurrentTargetFailed(target);
            }
        }
    }

    /// <summary>
    /// 宿主替换采用“先确保新宿主正面 Hediff，再提交字段，再清理旧宿主”的顺序。
    /// 新宿主在添加 Hediff 时死亡、进入死亡流程或最终验证失败，均回滚宿主字段。
    /// </summary>
    [HarmonyPatch(
        typeof(GameComponent_MechanoidMechanitorRegistry),
        "TryReplaceMechanicalConsciousnessHost")]
    internal static class MechanicalConsciousnessHostTransactionalReplacePatch
    {
        private const string ConsciousnessHediffDefName =
            "MAP_MechanicalConsciousness";

        private static readonly FieldInfo? HostField =
            AccessTools.Field(
                typeof(GameComponent_MechanoidMechanitorRegistry),
                "mechanicalConsciousnessHost");

        private static readonly MethodInfo? SynchronizeHediffMethod =
            AccessTools.Method(
                typeof(GameComponent_MechanoidMechanitorRegistry),
                "SynchronizeMechanicalConsciousnessHediff");

        private static readonly MethodInfo? NotifyColonistDisplaysMethod =
            AccessTools.Method(
                typeof(GameComponent_MechanoidMechanitorRegistry),
                "NotifyScenarioColonistDisplaysIfNeeded");

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(
            Pawn expectedCurrentHost,
            Pawn newHost,
            ref bool __result)
        {
            __result = TryReplaceSafely(expectedCurrentHost, newHost);
            return false;
        }

        private static bool TryReplaceSafely(
            Pawn expectedCurrentHost,
            Pawn newHost)
        {
            GameComponent_MechanoidMechanitorRegistry? registry =
                Current.Game?.GetComponent<GameComponent_MechanoidMechanitorRegistry>();
            HediffDef? consciousnessDef =
                DefDatabase<HediffDef>.GetNamedSilentFail(
                    ConsciousnessHediffDefName);

            if (registry == null
                || HostField == null
                || SynchronizeHediffMethod == null
                || consciousnessDef == null
                || expectedCurrentHost == null
                || newHost == null
                || ReferenceEquals(expectedCurrentHost, newHost)
                || newHost.Dead
                || newHost.Destroyed
                || newHost.Discarded
                || newHost.health?.isBeingKilled == true
                || !ReferenceEquals(
                    GameComponent_MechanoidMechanitorRegistry
                        .CurrentMechanicalConsciousnessHost,
                    expectedCurrentHost)
                || !GameComponent_MechanoidMechanitorRegistry
                    .CanHostMechanicalConsciousness(newHost))
            {
                return false;
            }

            bool targetHadHediff = HasHediff(newHost, consciousnessDef);
            bool sourceHadHediff = HasHediff(expectedCurrentHost, consciousnessDef);
            bool fieldCommitted = false;

            try
            {
                MechanoidMechanitorRoleUtility.EnsureRoleState(newHost);
                if (!MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(newHost)
                    || newHost.Dead
                    || newHost.Destroyed
                    || newHost.Discarded
                    || newHost.health?.isBeingKilled == true)
                {
                    return false;
                }

                MAPOverseerlessNodeUtility.ClearExternalOverseerIfNode(newHost);

                if (!targetHadHediff)
                {
                    newHost.health?.AddHediff(consciousnessDef);
                }

                if (!HasHediff(newHost, consciousnessDef)
                    || newHost.Dead
                    || newHost.Destroyed
                    || newHost.Discarded
                    || newHost.health?.isBeingKilled == true
                    || !ReferenceEquals(
                        GameComponent_MechanoidMechanitorRegistry
                            .CurrentMechanicalConsciousnessHost,
                        expectedCurrentHost))
                {
                    RemoveAddedTargetHediffIfNeeded(
                        newHost,
                        consciousnessDef,
                        targetHadHediff);
                    return false;
                }

                HostField.SetValue(registry, newHost);
                fieldCommitted = true;
                SynchronizeHediffMethod.Invoke(registry, Array.Empty<object>());

                bool valid =
                    ReferenceEquals(
                        GameComponent_MechanoidMechanitorRegistry
                            .CurrentMechanicalConsciousnessHost,
                        newHost)
                    && !newHost.Dead
                    && !newHost.Destroyed
                    && !newHost.Discarded
                    && newHost.health?.isBeingKilled != true
                    && HasHediff(newHost, consciousnessDef)
                    // 轨道数据网络完成后，所有机械族机械师都会保留“机械意识”，
                    // 因此不能再把“旧载体已无机械意识”作为替换成功的必要条件，
                    // 否则最终同步会立即把旧载体的健康状态补回并导致事务回滚。
                    && (ResearchFeatureUnlockUtility.IsOrbitalDataNetworkUnlocked()
                        || !HasHediff(expectedCurrentHost, consciousnessDef));

                if (!valid)
                {
                    Rollback(
                        registry,
                        expectedCurrentHost,
                        newHost,
                        consciousnessDef,
                        sourceHadHediff,
                        targetHadHediff);
                    return false;
                }

                NotifyColonistDisplaysMethod?.Invoke(null, Array.Empty<object>());
                return true;
            }
            catch (TargetInvocationException ex)
            {
                if (fieldCommitted)
                {
                    Rollback(
                        registry,
                        expectedCurrentHost,
                        newHost,
                        consciousnessDef,
                        sourceHadHediff,
                        targetHadHediff);
                }
                else
                {
                    RemoveAddedTargetHediffIfNeeded(
                        newHost,
                        consciousnessDef,
                        targetHadHediff);
                }

                Log.Error(
                    "[MAP-机械族机械师] 机械意识宿主事务替换失败：" +
                    (ex.InnerException ?? ex));
                return false;
            }
            catch (Exception ex)
            {
                if (fieldCommitted)
                {
                    Rollback(
                        registry,
                        expectedCurrentHost,
                        newHost,
                        consciousnessDef,
                        sourceHadHediff,
                        targetHadHediff);
                }
                else
                {
                    RemoveAddedTargetHediffIfNeeded(
                        newHost,
                        consciousnessDef,
                        targetHadHediff);
                }

                Log.Error(
                    "[MAP-机械族机械师] 机械意识宿主事务替换异常：" + ex);
                return false;
            }
        }

        private static void Rollback(
            GameComponent_MechanoidMechanitorRegistry registry,
            Pawn source,
            Pawn target,
            HediffDef consciousnessDef,
            bool sourceHadHediff,
            bool targetHadHediff)
        {
            try
            {
                HostField?.SetValue(registry, source);

                if (sourceHadHediff && !HasHediff(source, consciousnessDef))
                {
                    source.health?.AddHediff(consciousnessDef);
                }

                RemoveAddedTargetHediffIfNeeded(
                    target,
                    consciousnessDef,
                    targetHadHediff);

                SynchronizeHediffMethod?.Invoke(registry, Array.Empty<object>());
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] 机械意识宿主事务回滚失败：" + ex);
            }
        }

        private static void RemoveAddedTargetHediffIfNeeded(
            Pawn target,
            HediffDef def,
            bool targetHadHediff)
        {
            if (targetHadHediff || target.health?.hediffSet?.hediffs == null)
            {
                return;
            }

            List<Hediff> snapshot =
                new List<Hediff>(target.health.hediffSet.hediffs);
            for (int i = snapshot.Count - 1; i >= 0; i--)
            {
                Hediff? hediff = snapshot[i];
                if (hediff?.def == def)
                {
                    target.health.RemoveHediff(hediff);
                }
            }
        }

        private static bool HasHediff(Pawn? pawn, HediffDef def)
        {
            return pawn?.health?.hediffSet?.HasHediff(def) == true;
        }
    }

    /// <summary>
    /// 精确选择 DefMap 的 PawnCapacityDef 索引器，避免宽泛 GetProperty("Item")
    /// 在运行时命中多个索引器并抛出 AmbiguousMatchException。
    /// </summary>
    [HarmonyPatch(
        typeof(LoadDeathDiagnosticUtility),
        "TryGetCachedConsciousness")]
    internal static class LoadDeathDiagnosticExactCapacityCachePatch
    {
        private static readonly FieldInfo? CapacityCacheField =
            typeof(PawnCapacitiesHandler).GetField(
                "cachedCapacityLevels",
                BindingFlags.Instance
                | BindingFlags.NonPublic
                | BindingFlags.DeclaredOnly);

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(
            Pawn? pawn,
            ref float value,
            ref string status,
            ref bool __result)
        {
            value = 0f;
            status = "unavailable";
            __result = false;

            try
            {
                PawnCapacitiesHandler? capacities = pawn?.health?.capacities;
                if (capacities == null)
                {
                    status = "capacities-null";
                    return false;
                }

                object? cacheMap = CapacityCacheField?.GetValue(capacities);
                if (cacheMap == null)
                {
                    status = CapacityCacheField == null
                        ? "cache-field-unavailable"
                        : "cache-map-null";
                    return false;
                }

                PropertyInfo? exactIndexer = null;
                PropertyInfo[] properties = cacheMap.GetType().GetProperties(
                    BindingFlags.Instance
                    | BindingFlags.Public
                    | BindingFlags.NonPublic);
                for (int i = 0; i < properties.Length; i++)
                {
                    PropertyInfo property = properties[i];
                    ParameterInfo[] indexParameters = property.GetIndexParameters();
                    if (property.Name == "Item"
                        && indexParameters.Length == 1
                        && indexParameters[0].ParameterType == typeof(PawnCapacityDef))
                    {
                        exactIndexer = property;
                        break;
                    }
                }

                if (exactIndexer == null)
                {
                    status = "exact-indexer-unavailable";
                    return false;
                }

                object? element = exactIndexer.GetValue(
                    cacheMap,
                    new object[] { PawnCapacityDefOf.Consciousness });
                if (element == null)
                {
                    status = "cache-element-null";
                    return false;
                }

                Type elementType = element.GetType();
                FieldInfo? statusField = elementType.GetField(
                    "status",
                    BindingFlags.Instance
                    | BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly);
                FieldInfo? valueField = elementType.GetField(
                    "value",
                    BindingFlags.Instance
                    | BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly);

                status = statusField?.GetValue(element)?.ToString()
                    ?? "status-field-unavailable";
                if (!string.Equals(status, "Cached", StringComparison.Ordinal))
                {
                    return false;
                }

                if (valueField?.GetValue(element) is float cached)
                {
                    value = cached;
                    __result = true;
                    return false;
                }

                status = "cached-value-unavailable";
                return false;
            }
            catch (Exception ex)
            {
                status = "error:" + ex.GetType().Name;
                return false;
            }
        }
    }
}

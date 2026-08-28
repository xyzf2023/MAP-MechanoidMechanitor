using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 独立的「存档加载期死亡保护」机制。
    ///
    /// 在旧存档加载期间，凡在存档数据中原本存活、且可确认为「机械族机械师」的 Pawn，
    /// 会被登记到受保护集合。若加载期间该 Pawn 以 dinfo == null 进入
    /// Pawn.Kill(DamageInfo? dinfo, Hediff exactCulprit)，则阻止该次 Kill 真正执行。
    /// 加载结束并完成现有 MechanoidMechanitorPostLoadSafetyCoordinator 收尾流程后，
    /// 立即清空所有临时保护记录。
    ///
    /// 本机制：
    /// - 不依赖 Hediff；
    /// - 不改变意识值；
    /// - 不赋予真正的“不死”状态；
    /// - 不修改存档中的 Pawn 数据；
    /// - 不主动复活已经死亡的 Pawn；
    /// - 不影响正常游戏；
    /// - 不阻止带有真实 DamageInfo 的死亡；
    /// - 仅作为旧存档加载期间的防误杀保险层。
    /// </summary>
    internal static class MechanoidMechanitorLoadDeathGuard
    {
        private const string LogPrefix = "[MAP-LOAD-DEATH-GUARD]";
        private const int LogKeyBase = 0x4D415047; // "MAPG"

        private static readonly HashSet<Pawn> protectedPawns =
            new HashSet<Pawn>(ReferencePawnComparer.Instance);

        private static readonly FieldInfo? HealthPawnField =
            AccessTools.Field(typeof(Pawn_HealthTracker), "pawn");

        private static Game? activeGame;
        private static bool active;

        internal static bool Active => active;

        private static bool SettingEnabled =>
            MAPMechanitorMod.Settings?.preventMechanoidMechanitorDeathDuringLoad ?? true;

        internal static void BeginLoad(Game? game)
        {
            activeGame = game;
            protectedPawns.Clear();
            active = SettingEnabled;
        }

        internal static void EndLoad()
        {
            active = false;
            protectedPawns.Clear();
            activeGame = null;
        }

        internal static void AbortLoad()
        {
            EndLoad();
        }

        internal static void Reset(Game? game)
        {
            active = false;
            protectedPawns.Clear();
            activeGame = game;
        }

        /// <summary>
        /// 登记一个“加载期原本存活的机械族机械师”。只有满足全部条件的 Pawn 才会被登记：
        /// 未被销毁/丢弃、健康存在且当前未死亡、是机械族、且可确认为机械族机械师。
        /// 幂等：重复登记同一 Pawn 不会改变任何状态。
        /// </summary>
        internal static void RegisterLoadedMechanitor(Pawn? pawn)
        {
            try
            {
                if (!active || !SettingEnabled || pawn == null)
                {
                    return;
                }

                if (pawn.Destroyed || pawn.Discarded)
                {
                    return;
                }

                if (pawn.health == null
                    || pawn.Dead
                    || pawn.health.State == PawnHealthState.Dead)
                {
                    return;
                }

                if (pawn.RaceProps == null || !pawn.RaceProps.IsMechanoid)
                {
                    return;
                }

                if (!IsAnyMechanoidMechanitor(pawn))
                {
                    return;
                }

                protectedPawns.Add(pawn);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce(
                    LogPrefix + " 登记加载期受保护机械族机械师时异常：" + ex,
                    LogKeyBase + 1);
            }
        }

        /// <summary>
        /// 判断本次 Pawn.Kill 是否应被阻止。仅在保护激活、设置开启、
        /// 加载仍属于同一局游戏、dinfo 为 null、且目标 Pawn 已被登记时返回 true。
        /// 任何不确定情况一律返回 false（交回原版死亡逻辑）。
        /// </summary>
        internal static bool ShouldBlockKill(Pawn? pawn, DamageInfo? dinfo)
        {
            // 双门控：Guard 自身 active 与 SafetyCoordinator 的 LoadInProgress
            // 必须同时为 true，才允许进入阻止死亡逻辑；任一为 false 都交回原版死亡。
            if (!active
                || !SettingEnabled
                || !MechanoidMechanitorPostLoadSafetyCoordinator.LoadInProgress)
            {
                return false;
            }

            // 真实伤害来源（dinfo 有值）一律放行，绝不保护。
            if (dinfo != null)
            {
                return false;
            }

            if (pawn == null)
            {
                return false;
            }

            // 执行 Kill Prefix 时再次确认 Pawn 当前并未进入死亡状态；
            // 若已通过其他合法流程死亡，不应干扰其死亡收尾。
            if (pawn.health == null
                || pawn.Dead
                || pawn.health.State == PawnHealthState.Dead)
            {
                return false;
            }

            if (activeGame != null
                && Current.Game != null
                && !ReferenceEquals(activeGame, Current.Game))
            {
                return false;
            }

            return protectedPawns.Contains(pawn);
        }

        private static bool IsAnyMechanoidMechanitor(Pawn pawn)
        {
            if (MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
            {
                return true;
            }

            if (MechanoidMechanitorRoleUtility.HasNativeMechanitorMarker(pawn))
            {
                return true;
            }

            if (MechanoidMechanitorRoleUtility.HasAcquiredMechanitorHediff(pawn))
            {
                return true;
            }

            if (MechanoidMechanitorRoleUtility.IsMechanicalConsciousnessHost(pawn))
            {
                return true;
            }

            if (GameComponent_MechanoidMechanitorRegistry.HasPersistentRecord(pawn))
            {
                return true;
            }

            return false;
        }

        private static Pawn? GetPawnFromHealthTracker(Pawn_HealthTracker? tracker)
        {
            if (tracker == null)
            {
                return null;
            }

            try
            {
                return HealthPawnField?.GetValue(tracker) as Pawn;
            }
            catch
            {
                return null;
            }
        }

        private static void LogBlockedKill(Pawn pawn, DamageInfo? dinfo, Hediff? exactCulprit)
        {
            // 每次真正阻止一次加载期误杀都独立输出一条 Warning（不再按 Pawn 去重），
            // 以保留完整的触发链诊断信息。
            string culprit = exactCulprit?.def?.defName ?? "null";
            Log.Warning(
                LogPrefix + " 已阻止加载期间机械族机械师意外死亡：" +
                $"Pawn={pawn.LabelShort}（{pawn.ThingID ?? "unknown"}）" +
                $"，Def={pawn.def?.defName ?? "unknown"}" +
                $"，DamageInfo={(dinfo.HasValue ? dinfo.Value.ToString() : "null")}" +
                $"，ExactCulprit={culprit}" +
                $"，SCRIBE={Scribe.mode}" +
                $"，GUARD_ACTIVE={active}" +
                $"，LOAD_IN_PROGRESS={MechanoidMechanitorPostLoadSafetyCoordinator.LoadInProgress}" +
                $"，PROGRAM={Current.ProgramState}");

            try
            {
                if (LoadDeathDiagnosticUtility.Active)
                {
                    LoadDeathDiagnosticUtility.Write(
                        "LoadDeathGuard.BlockedKill",
                        pawn,
                        "DAMAGE=" + (dinfo.HasValue ? dinfo.Value.ToString() : "null")
                        + " EXACT_CULPRIT=" + culprit,
                        false);
                }
            }
            catch
            {
                // 忽略诊断写入失败，不影响保护本身。
            }
        }

        [HarmonyPatch(
            typeof(Pawn_HealthTracker),
            nameof(Pawn_HealthTracker.ExposeData))]
        private static class HealthExposeDataPatch
        {
            private static void Postfix(Pawn_HealthTracker __instance)
            {
                // 仅在 LoadingVars 阶段登记：这是获取“存档数据中原本存活”事实的
                // 主要入口。ResolvingCrossRefs / PostLoadInit 已属后续加载阶段，
                // 若 Pawn 在那时才变为 Alive，不代表其存档中原本存活，禁止重新登记。
                if (Scribe.mode != LoadSaveMode.LoadingVars)
                {
                    return;
                }

                Pawn? pawn = GetPawnFromHealthTracker(__instance);
                RegisterLoadedMechanitor(pawn);
            }
        }

        [HarmonyPatch(
            typeof(Pawn),
            nameof(Pawn.SpawnSetup),
            new[] { typeof(Map), typeof(bool) })]
        private static class SpawnSetupPatch
        {
            private static void Prefix(Pawn __instance, Map map, bool respawningAfterLoad)
            {
                if (!respawningAfterLoad)
                {
                    return;
                }

                RegisterLoadedMechanitor(__instance);
            }
        }

        [HarmonyPatch(
            typeof(Pawn),
            nameof(Pawn.Kill),
            new[] { typeof(DamageInfo?), typeof(Hediff) })]
        private static class KillPatch
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(Pawn __instance, DamageInfo? dinfo, Hediff exactCulprit)
            {
                if (ShouldBlockKill(__instance, dinfo))
                {
                    LogBlockedKill(__instance, dinfo, exactCulprit);
                    return false;
                }

                return true;
            }
        }

        private sealed class ReferencePawnComparer : IEqualityComparer<Pawn>
        {
            internal static readonly ReferencePawnComparer Instance =
                new ReferencePawnComparer();

            public bool Equals(Pawn? x, Pawn? y) => ReferenceEquals(x, y);

            public int GetHashCode(Pawn obj) => obj?.thingIDNumber ?? 0;
        }
    }
}

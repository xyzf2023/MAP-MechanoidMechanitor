using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.VanillaPsycastsExpanded
{
    /// <summary>
    /// 原版灵能拓展（VPE）兼容运行时。
    /// 仅在 VanillaPsycastsExpandedCompatibility.Apply 完成全部反射目标解析后，由 Configure 配置；
    /// 未配置或兼容未成功应用时，所有入口安全 no-op，不会产生任何 VPE 相关副作用。
    /// 职责：对机械族机械师 Pawn 进行幂等的 VPE 灵能状态补齐：
    /// - 已有 VPE_PsycastAbilityImplant 的 Pawn 绝不触碰（不删除、不重建、不重新初始化）；
    /// - 缺失时才在最高级启灵神经所在部位新增 Hediff，并仅对该新增实例调用一次 InitializeFromPsylink；
    /// - 不修改 experience / points / level / statPoints / unlockedPaths / psysets / 已学技能，
    /// - 不删除 Pawn_AbilityTracker 中的原版 Psycast，不修改启灵神经等级，不添加额外启灵神经。
    /// </summary>
    internal static class VanillaPsycastsExpandedCompatibilityRuntime
    {
        private const string LogPrefix = "[MAP-机械族机械师] 原版灵能拓展兼容：";

        private const int ErrorKeyConfigureNull = unchecked((int)0x5650_0001);
        private const int ErrorKeyAddHediff = unchecked((int)0x5650_0002);
        private const int ErrorKeyTypeMismatch = unchecked((int)0x5650_0003);
        private const int ErrorKeyInitialize = unchecked((int)0x5650_0004);
        private const int ErrorKeyRollback = unchecked((int)0x5650_0005);
        private const int ErrorKeyBatch = unchecked((int)0x5650_0006);

        private static Type? compAbilitiesType;
        private static Type? hediffPsycastAbilitiesType;
        private static HediffDef? vpeHediffDef;
        private static MethodInfo? initializeFromPsylinkMethod;
        private static bool configured;

        public static bool IsConfigured => configured;

        /// <summary>
        /// 仅由兼容模块在全部反射目标确认后调用。配置完成后 Runtime 才可被安全使用。
        /// </summary>
        public static void Configure(
            Type compAbilitiesTypeArg,
            Type hediffPsycastAbilitiesTypeArg,
            HediffDef vpeHediffDefArg,
            MethodInfo initializeFromPsylinkMethodArg)
        {
            if (configured)
            {
                return;
            }

            if (compAbilitiesTypeArg == null
                || hediffPsycastAbilitiesTypeArg == null
                || vpeHediffDefArg == null
                || initializeFromPsylinkMethodArg == null)
            {
                Log.ErrorOnce(
                    LogPrefix + "Configure 收到 null 反射目标，本模块保持未初始化，"
                    + "所有运行时入口将安全跳过。",
                    ErrorKeyConfigureNull);
                return;
            }

            compAbilitiesType = compAbilitiesTypeArg;
            hediffPsycastAbilitiesType = hediffPsycastAbilitiesTypeArg;
            vpeHediffDef = vpeHediffDefArg;
            initializeFromPsylinkMethod = initializeFromPsylinkMethodArg;
            configured = true;
        }

        /// <summary>
        /// 兼容应用失败时清除配置，避免留下可被调用的半初始化缓存。
        /// </summary>
        public static void Clear()
        {
            configured = false;
            compAbilitiesType = null;
            hediffPsycastAbilitiesType = null;
            vpeHediffDef = null;
            initializeFromPsylinkMethod = null;
        }

        /// <summary>
        /// 幂等的单 Pawn VPE 灵能状态补齐入口。
        /// 仅当全部前置条件满足（机械族机械师、携带 CompAbilities、至少一个启灵神经、
        /// 缺少 VPE 状态）时，才在最高级启灵神经所在部位新增 VPE_PsycastAbilityImplant，
        /// 并仅对该新增 Hediff 调用一次 InitializeFromPsylink。
        /// 任何失败都只回滚本次新创建的 Hediff，绝不触碰既有状态，绝不把异常抛给调用方。
        /// </summary>
        public static void EnsurePawnState(Pawn? pawn)
        {
            if (!configured)
            {
                return;
            }

            if (!ModsConfig.RoyaltyActive)
            {
                return;
            }

            if (pawn == null || pawn.Dead || pawn.Destroyed || pawn.Discarded)
            {
                return;
            }

            if (pawn.health == null || pawn.health.hediffSet == null)
            {
                return;
            }

            if (!pawn.RaceProps.IsMechanoid)
            {
                return;
            }

            if (!MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
            {
                return;
            }

            List<ThingComp>? allComps = pawn.AllComps;
            bool hasAbilitiesComp = false;
            if (allComps != null)
            {
                for (int i = 0; i < allComps.Count; i++)
                {
                    ThingComp? comp = allComps[i];
                    if (comp != null && compAbilitiesType!.IsInstanceOfType(comp))
                    {
                        hasAbilitiesComp = true;
                        break;
                    }
                }
            }

            if (!hasAbilitiesComp)
            {
                return;
            }

            Hediff_Psylink? bestPsylink = null;
            int bestLevel = int.MinValue;
            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                if (hediffs[i] is Hediff_Psylink psylink && psylink.level > bestLevel)
                {
                    bestLevel = psylink.level;
                    bestPsylink = psylink;
                }
            }

            if (bestPsylink == null)
            {
                return;
            }

            // 已拥有 VPE 灵能状态：立即返回，不做任何修改，绝不重置或重建。
            if (pawn.health.hediffSet.GetFirstHediffOfDef(vpeHediffDef) != null)
            {
                return;
            }

            Hediff? added = null;
            try
            {
                added = pawn.health.AddHediff(vpeHediffDef, bestPsylink.Part);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce(
                    LogPrefix + $"为机械族机械师 {FormatPawn(pawn)} 添加 VPE 灵能状态失败，"
                    + "已保持原状，不影响其他功能：" + ex,
                    ErrorKeyAddHediff);
                return;
            }

            if (added == null)
            {
                Log.ErrorOnce(
                    LogPrefix + $"为机械族机械师 {FormatPawn(pawn)} 添加 VPE 灵能状态返回空"
                    + " Hediff，已保持原状。",
                    ErrorKeyAddHediff);
                return;
            }

            if (!hediffPsycastAbilitiesType!.IsInstanceOfType(added))
            {
                // 类型不符：只回滚本次刚刚创建、尚未成功初始化的 Hediff。
                RemoveNewlyAddedHediff(pawn, added);
                Log.ErrorOnce(
                    LogPrefix + $"为机械族机械师 {FormatPawn(pawn)} 新增的 VPE 灵能状态类型不符"
                    + $"（实际 {added.GetType().FullName}），已回滚本次新增。",
                    ErrorKeyTypeMismatch);
                return;
            }

            try
            {
                initializeFromPsylinkMethod!.Invoke(
                    added,
                    new object[] { bestPsylink });
            }
            catch (Exception ex)
            {
                // 初始化失败：只回滚本次新增，绝不触碰进入方法前已存在的任何 VPE Hediff。
                RemoveNewlyAddedHediff(pawn, added);
                Log.ErrorOnce(
                    LogPrefix + $"初始化机械族机械师 {FormatPawn(pawn)} 的新增 VPE 灵能状态失败，"
                    + "已回滚本次新增：" + ex,
                    ErrorKeyInitialize);
            }
        }

        /// <summary>
        /// 旧档机械族机械师批量修复入口。
        /// 只遍历正式机械族机械师注册表持久化记录（GameComponent_MechanoidMechanitorRegistry.
        /// GetPersistentRecordSnapshot），不自行从地图 / 世界重新拼装机械师列表。
        /// 逐 Pawn 调用同一 EnsurePawnState，内部已完成全部前置校验与幂等处理；
        /// 自身错误不向外传播，不会让 TryRestorePositiveSources 失败。
        /// </summary>
        public static void EnsureAllRegisteredMechanitors()
        {
            if (!configured)
            {
                return;
            }

            try
            {
                IReadOnlyList<MechanoidMechanitorRegistrySnapshotEntry> snapshot =
                    GameComponent_MechanoidMechanitorRegistry.GetPersistentRecordSnapshot();
                for (int i = 0; i < snapshot.Count; i++)
                {
                    EnsurePawnState(snapshot[i].Pawn);
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce(
                    LogPrefix + "旧档机械族机械师 VPE 状态批量修复失败：" + ex,
                    ErrorKeyBatch);
            }
        }

        private static void RemoveNewlyAddedHediff(Pawn pawn, Hediff added)
        {
            try
            {
                if (pawn.health?.hediffSet != null
                    && pawn.health.hediffSet.hediffs.Contains(added))
                {
                    pawn.health.RemoveHediff(added);
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce(
                    LogPrefix + "回滚本次新增 VPE 灵能状态时发生异常：" + ex,
                    ErrorKeyRollback);
            }
        }

        private static string FormatPawn(Pawn pawn)
        {
            return $"{pawn.LabelShort}（{pawn.ThingID}）";
        }
    }
}

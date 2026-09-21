using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>一次伤害的保护快照；转移只写伤口，不重新进入护甲或结构结算。</summary>
    internal sealed class SunBossDamageContext
    {
        [ThreadStatic] private static List<SunBossDamageContext>? active;
        private static readonly MethodInfo WriteInjury = AccessTools.Method(typeof(DamageWorker_AddInjury),
            "FinalizeAndAddInjury", new[] { typeof(Pawn), typeof(Hediff_Injury), typeof(DamageInfo), typeof(DamageWorker.DamageResult) });
        internal static readonly MethodInfo SpecialEffectsMethod = AccessTools.Method(typeof(DamageWorker_AddInjury),
            "ApplySpecialEffectsToPart");
        private static readonly Action<DamageWorker_AddInjury, Pawn, float, DamageInfo, DamageWorker.DamageResult> ApplySpecialEffects
            = AccessTools.MethodDelegate<Action<DamageWorker_AddInjury, Pawn, float, DamageInfo, DamageWorker.DamageResult>>(SpecialEffectsMethod);
        internal readonly CompSunBossState State;
        internal readonly bool ProtectCore, InternalBurn;
        internal readonly int Stabilizers;
        internal readonly float Factor;
        internal int Writing;
        private int preparedDepth;
        private readonly DamageInfo originalDamage;
        private bool ended;

        private SunBossDamageContext(CompSunBossState state, DamageInfo damage, bool internalBurn)
        {
            State = state;
            ProtectCore = Find(state.Boss)?.ProtectCore ?? (state.Structure > 0f);
            Stabilizers = state.Stabilizers;
            Factor = SunBossStage.For(Stabilizers).DamageFactor;
            InternalBurn = internalBurn;
            originalDamage = damage;
        }

        internal static SunBossDamageContext? Find(Pawn pawn)
        {
            if (active == null) return null;
            for (int i = active.Count - 1; i >= 0; i--)
                if (active[i].State.Boss == pawn) return active[i];
            return null;
        }

        internal static SunBossDamageContext? Begin(Thing thing, DamageInfo damage, bool internalBurn = false)
        {
            if (!(thing is Pawn pawn) || pawn.Dead || pawn.Destroyed) return null;
            CompSunBossState? state = pawn.GetComp<CompSunBossState>();
            if (state == null) return null;
            var context = new SunBossDamageContext(state, damage, internalBurn);
            (active ??= new List<SunBossDamageContext>()).Add(context);
            return context;
        }

        internal void End(bool recheck = true)
        {
            if (ended) return;
            ended = true;
            active?.Remove(this);
            if (active?.Count == 0) active = null;
            Pawn pawn = State.Boss;
            if (pawn.Dead || pawn.Destroyed) return;
            State.DetectCoolingFailure();
            // 同 Pawn 的外层伤害仍可能在写伤口，必须等最外层保护快照释放。
            if (recheck && ProtectCore && State.Structure <= 0f && Find(pawn) == null)
                pawn.health.CheckForStateChange(originalDamage, null);
        }

        internal static float RawHealth(Pawn pawn, BodyPartRecord part)
        {
            if (pawn.health.hediffSet.PartIsMissing(part)) return 0f;
            float health = part.def.GetMaxHealth(pawn);
            foreach (Hediff_Injury injury in pawn.health.hediffSet.hediffs.OfType<Hediff_Injury>())
                if (injury.Part == part)
                    health = injury.destroysBodyParts ? health - injury.Severity : Mathf.Max(health - injury.Severity, 1f);
            return Mathf.Max(0f, health);
        }

        internal static BodyPartRecord? RandomPart(Pawn pawn, Predicate<BodyPartRecord> allowed)
        {
            List<BodyPartRecord> parts = pawn.health.hediffSet.GetNotMissingParts()
                .Where(p => p.coverageAbs > 0f && RawHealth(pawn, p) > 0f && allowed(p)).ToList();
            return parts.Count == 0 ? null : parts.RandomElementByWeight(p => p.coverageAbs);
        }

        internal static Hediff_Injury CopyInjury(Hediff_Injury template, Pawn pawn, BodyPartRecord part, float amount)
        {
            var injury = (Hediff_Injury)HediffMaker.MakeHediff(template.def, pawn, part);
            injury.sourceDef = template.sourceDef;
            injury.sourceLabel = template.sourceLabel;
            injury.sourceBodyPartGroup = template.sourceBodyPartGroup;
            injury.sourceHediffDef = template.sourceHediffDef;
            injury.sourceToolLabel = template.sourceToolLabel;
            injury.destroysBodyParts = template.destroysBodyParts;
            injury.Severity = amount;
            return injury;
        }

        private void NotifyFullShieldAbsorption(float postArmorAmount)
        {
            if (InternalBurn || originalDamage.Instigator == null || Stabilizers != 6 || Factor != 0f
                || !(postArmorAmount > 0f) || float.IsInfinity(postArmorAmount)) return;
            CurrentGameComponentCache<GameComponent_SunBossNotifications>.Get()
                ?.NotifyShieldAbsorbed(State.Boss);
        }

        internal float Route(DamageWorker_AddInjury worker, Hediff_Injury injury, DamageInfo damage,
            DamageWorker.DamageResult result)
        {
            Pawn pawn = State.Boss;
            BodyPartRecord? part = injury.Part;
            if (part == null || pawn.health.hediffSet.PartIsMissing(part)) return 0f;
            if (preparedDepth == 0) NotifyFullShieldAbsorption(injury.Severity);
            float amount = injury.Severity * (InternalBurn || preparedDepth > 0 ? 1f : Factor);
            if (!(amount > 0f) || float.IsInfinity(amount))
            {
                if (!InternalBurn && Factor == 0f) { result.deflected = true; result.AddPart(pawn, part); }
                return 0f;
            }
            if (preparedDepth == 0) State.ConsumeStructure(amount);
            float before = result.totalDamageDealt;
            bool transfer = State.Protects(part);
            var visited = new HashSet<BodyPartRecord>();
            while (part != null && amount > 0f && !pawn.Dead && !pawn.Destroyed && visited.Add(part))
            {
                float health = RawHealth(pawn, part);
                float permitted = Mathf.Max(0f, health - (State.Protects(part) ? 1f : 0f));
                float applied = Mathf.Min(amount, permitted);
                if (applied > 0f)
                {
                    Hediff_Injury next = part == injury.Part ? injury : CopyInjury(injury, pawn, part, applied);
                    next.Severity = applied;
                    DamageInfo routed = damage;
                    routed.SetHitPart(part);
                    routed.SetAmount(applied);
                    routed.SetIgnoreArmor(true);
                    routed.SetAllowDamagePropagation(false);
                    Writing++;
                    try { WriteInjury.Invoke(worker, new object[] { pawn, next, routed, result }); }
                    finally { Writing--; }
                }
                amount -= applied;
                // 普通部位命中的超杀不凭空变成全身伤害；只有保护产生的溢出才转移。
                if (!transfer || amount <= 0f) break;
                part = RandomPart(pawn, p => !visited.Contains(p) && !State.Protects(p));
            }
            return result.totalDamageDealt - before;
        }

        /// <summary>原版护甲后、特殊传播前的边界；保留钝击眩晕等副作用，不按复制伤口重复扣条。</summary>
        internal static void ApplyPostArmorSpecial(DamageWorker_AddInjury worker, Pawn pawn, float amount,
            DamageInfo damage, DamageWorker.DamageResult result)
        {
            if (pawn.GetComp<CompSunBossState>() == null)
            {
                ApplySpecialEffects(worker, pawn, amount, damage, result);
                return;
            }
            SunBossDamageContext? context = Find(pawn);
            bool owns = context == null;
            context ??= Begin(pawn, damage);
            if (context == null) return;
            bool completed = false;
            try
            {
                context.NotifyFullShieldAbsorption(amount);
                amount *= context.InternalBurn ? 1f : context.Factor;
                if (!(amount > 0f) || float.IsInfinity(amount))
                {
                    result.deflected = true;
                    result.AddPart(pawn, damage.HitPart);
                    completed = true;
                    return;
                }
                context.State.ConsumeStructure(amount);
                context.preparedDepth++;
                try { ApplySpecialEffects(worker, pawn, amount, damage, result); }
                finally { context.preparedDepth--; }
                completed = true;
            }
            finally { if (owns) context.End(completed); }
        }

        internal static void ApplyInternalBurn(CompSunBossState state, float amount, BodyPartRecord part)
        {
            Pawn pawn = state.Boss;
            var damage = new DamageInfo(DamageDefOf.Burn, amount, instigator: pawn, hitPart: part);
            damage.SetIgnoreArmor(true);
            damage.SetAllowDamagePropagation(false);
            SunBossDamageContext? context = Begin(pawn, damage, true);
            if (context == null) return;
            bool completed = false;
            try
            {
                // 从真实灼烧伤口入口写入，避开外部伤害的护甲、难度及 IncomingDamageFactor。
                var injury = (Hediff_Injury)HediffMaker.MakeHediff(
                    HealthUtility.GetHediffDefFromDamage(DamageDefOf.Burn, pawn, part), pawn, part);
                injury.Severity = amount;
                context.Route((DamageWorker_AddInjury)DamageDefOf.Burn.Worker, injury, damage, new DamageWorker.DamageResult());
                completed = true;
            }
            finally { context.End(completed); }
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    internal static class SunBossDamageTransactionPatch
    {
        public static void Prefix(Thing __instance, DamageInfo dinfo, out SunBossDamageContext? __state)
            => __state = SunBossDamageContext.Begin(__instance, dinfo);
        public static void Postfix(SunBossDamageContext? __state) => __state?.End();
        public static Exception? Finalizer(Exception? __exception, SunBossDamageContext? __state)
        {
            __state?.End(__exception == null);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(DamageWorker_AddInjury), "ApplyToPawn")]
    internal static class SunBossApplyAllDamagePatch
    {
        public static void Prefix(Pawn pawn, ref DamageInfo dinfo, out SunBossDamageContext? __state)
        {
            // 兼容直接调用 DamageWorker 的入口；普通 TakeDamage 已有外层事务，不重复创建。
            __state = SunBossDamageContext.Find(pawn) == null ? SunBossDamageContext.Begin(pawn, dinfo) : null;
            if (pawn.GetComp<CompSunBossState>() == null || !dinfo.ApplyAllDamage) return;
            // 原版按实际部位掉血循环补伤，遇保护会重复消耗结构。保留分片，取消补伤循环。
            bool propagate = dinfo.AllowDamagePropagation;
            dinfo.SetApplyAllDamage(false);
            dinfo.SetAllowDamagePropagation(propagate);
        }
        public static void Postfix(SunBossDamageContext? __state) => __state?.End();
        public static Exception? Finalizer(Exception? __exception, SunBossDamageContext? __state)
        {
            __state?.End(__exception == null);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(DamageWorker_AddInjury), "ApplyDamageToPart")]
    internal static class SunBossDamagePartPatch
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo replacement = AccessTools.Method(typeof(SunBossDamageContext), nameof(SunBossDamageContext.ApplyPostArmorSpecial));
            int replaced = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (!instruction.Calls(SunBossDamageContext.SpecialEffectsMethod)) { yield return instruction; continue; }
                CodeInstruction call = new CodeInstruction(instruction) { opcode = OpCodes.Call, operand = replacement };
                replaced++;
                yield return call;
            }
            if (replaced != 1)
                throw new InvalidOperationException("[MAP] 太阳伤害结算未找到唯一的护甲后传播入口，拒绝加载不完整保护。");
        }
    }

    [HarmonyPatch(typeof(DamageWorker_AddInjury), "ApplySmallPawnDamagePropagation")]
    internal static class SunBossSmallPawnPropagationPatch
    {
        public static bool Prefix(Pawn pawn) => pawn.GetComp<CompSunBossState>() == null;
    }

    [HarmonyPatch(typeof(DamageWorker_AddInjury), "ReduceDamageToPreserveOutsideParts")]
    internal static class SunBossProtectedPartOverkillPatch
    {
        public static bool Prefix(Pawn pawn, float postArmorDamage, DamageInfo dinfo, ref float __result)
        {
            if (dinfo.HitPart == null || pawn.GetComp<CompSunBossState>()?.Protects(dinfo.HitPart) != true) return true;
            // 受保护武器的超杀必须完整到达溢出路由，不能先被原版随机保肢截掉。
            __result = postArmorDamage;
            return false;
        }
    }

    [HarmonyPatch(typeof(DamageWorker_AddInjury), "FinalizeAndAddInjury",
        new[] { typeof(Pawn), typeof(Hediff_Injury), typeof(DamageInfo), typeof(DamageWorker.DamageResult) })]
    internal static class SunBossFinalInjuryPatch
    {
        public static bool Prefix(DamageWorker_AddInjury __instance, Pawn pawn, Hediff_Injury injury,
            DamageInfo dinfo, DamageWorker.DamageResult result, ref float __result)
        {
            if (pawn.GetComp<CompSunBossState>() == null || pawn.Dead) return true;
            SunBossDamageContext? context = SunBossDamageContext.Find(pawn);
            if (context?.Writing > 0) return true;
            bool owns = context == null;
            context ??= SunBossDamageContext.Begin(pawn, dinfo);
            if (context == null) return true;
            bool completed = false;
            try { __result = context.Route(__instance, injury, dinfo, result); completed = true; }
            finally { if (owns) context.End(completed); }
            return false;
        }
    }

    /// <summary>覆盖直接加伤口/缺失部件的旁路；真实伤害路由内的写入不再倍率或扣结构。</summary>
    [HarmonyPatch(typeof(HediffSet), nameof(HediffSet.AddDirect))]
    internal static class SunBossDirectInjuryPatch
    {
        public static bool Prefix(HediffSet __instance, Hediff hediff, DamageInfo? dinfo,
            DamageWorker.DamageResult damageResult)
        {
            Pawn pawn = __instance.pawn;
            CompSunBossState? state = pawn.GetComp<CompSunBossState>();
            if (state == null || pawn.Dead || Scribe.mode != LoadSaveMode.Inactive || hediff.Part == null) return true;
            SunBossDamageContext? context = SunBossDamageContext.Find(pawn);
            if (hediff is Hediff_MissingPart)
                return (context?.InternalBurn == true || (context?.Factor ?? state.Stage.DamageFactor) > 0f)
                    && !state.Protects(hediff.Part);
            if (context?.Writing > 0) return true;
            if (!(hediff is Hediff_Injury injury)) return true;
            DamageInfo damage = dinfo ?? new DamageInfo(DamageDefOf.Burn, injury.Severity, hitPart: injury.Part);
            bool owns = context == null;
            context ??= SunBossDamageContext.Begin(pawn, damage);
            if (context == null) return true;
            bool completed = false;
            try
            {
                context.Route(damage.Def?.Worker as DamageWorker_AddInjury ?? (DamageWorker_AddInjury)DamageDefOf.Burn.Worker,
                    injury, damage, damageResult ?? new DamageWorker.DamageResult());
                completed = true;
            }
            finally { if (owns) context.End(completed); }
            return false;
        }
    }

    [HarmonyPatch(typeof(Hediff), nameof(Hediff.Severity), MethodType.Setter)]
    internal static class SunBossExistingInjuryPatch
    {
        public static bool Prefix(Hediff __instance, float value)
        {
            if (!(__instance is Hediff_Injury injury) || !(value > injury.Severity)
                || injury.pawn?.health?.hediffSet == null || injury.Part == null
                || Scribe.mode != LoadSaveMode.Inactive || injury.pawn.Dead
                || injury.pawn.GetComp<CompSunBossState>() == null
                || SunBossDamageContext.Find(injury.pawn)?.Writing > 0
                || !injury.pawn.health.hediffSet.hediffs.Contains(injury)) return true;
            // 新伤口尚未入表时不干预；已有伤口的直接加重作为独立增量结算。
            Hediff_Injury delta = SunBossDamageContext.CopyInjury(injury, injury.pawn, injury.Part, value - injury.Severity);
            injury.pawn.health.AddHediff(delta);
            return false;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>只在原版非征召战斗决策期间提供激光射程；不把索敌 Verb 暴露给实际射击或存档。</summary>
    internal sealed class HighEnergyLaserBeamCombatContext : IDisposable
    {
        [ThreadStatic] private static HighEnergyLaserBeamCombatContext? current;
        private readonly HighEnergyLaserBeamCombatContext? previous;
        private readonly Pawn pawn;
        private readonly CompHighEnergyLaserBeam laser;
        private readonly bool allowTurrets;
        private bool usedLaserVerb;
        private bool disposed;

        private HighEnergyLaserBeamCombatContext(Pawn pawn, CompHighEnergyLaserBeam laser, bool allowTurrets)
        {
            this.pawn = pawn;
            this.laser = laser;
            this.allowTurrets = allowTurrets;
            previous = current;
            current = this;
        }

        internal static HighEnergyLaserBeamCombatContext? Begin(Pawn pawn, bool allowTurrets = false)
        {
            CompHighEnergyLaserBeam? laser = pawn.GetComp<CompHighEnergyLaserBeam>();
            if (laser?.AutoFireEnabled != true || pawn.Drafted
                || PawnUtility.PlayerForcedJobNowOrSoon(pawn) || !laser.CanStartAutoFire) return null;
            return new HighEnergyLaserBeamCombatContext(pawn, laser, allowTurrets);
        }

        internal static void SelectVerb(Pawn actor, Thing? target, ref Verb? verb)
        {
            HighEnergyLaserBeamCombatContext? context = current;
            if (context == null || context.pawn != actor || verb?.IsMeleeAttack == false) return;
            // 有原版远程攻击就完整保留其索敌、射程和射击位置逻辑；激光在任务返回后补位。
            // 无远程攻击时让原版识别激光，不按目标距离退回近战攻击。
            Verb? targetingVerb = context.laser.AutoAttackVerb;
            if (targetingVerb == null) return;
            verb = targetingVerb;
            context.usedLaserVerb = true;
        }

        internal void CompleteJob(ref Job? job, bool hostilityResponse)
        {
            // 先撤销临时 Verb，再检查真实武器；返回的 Job 不依赖当前调用上下文。
            Dispose();
            if (job == null || job.playerForced || job == pawn.CurJob || !laser.CanStartAutoFire) return;
            // 已准备待战时交给原版等待 Driver：贴身接敌在近战入口优先尝试激光，其他攻击仍由待战检查处理。
            if (!hostilityResponse && job.def == JobDefOf.Wait_Combat) return;
            bool attackJob = job.def == JobDefOf.AttackMelee || job.def == JobDefOf.AttackStatic;
            if (!attackJob && (hostilityResponse
                || (job.def != JobDefOf.Wait_Combat && job.def != JobDefOf.Goto))) return;
            Thing? target = attackJob ? job.targetA.Thing : pawn.mindState?.enemyTarget;
            if (target == null || target.Destroyed || !target.Spawned || target.Map != pawn.Map) return;

            Verb? ordinaryVerb = pawn.TryGetAttackVerb(target, !pawn.IsColonist, allowTurrets);
            if (ordinaryVerb?.IsMeleeAttack == false && ordinaryVerb.CanHitTarget(target)) return;
            if (!hostilityResponse)
            {
                if (!laser.CanAutoFireAt(target)) return;
                JobMaker.ReturnToPool(job);
                // 当前普通武器无法射击，而激光可以时，先进入原版待战检查，
                // 保留原版贴身威胁选择；真正施放复用近战优先入口或等待 Driver 的补位入口。
                job = JobMaker.MakeJob(JobDefOf.Wait_Combat,
                    JobGiver_AIFightEnemy.ExpiryInterval_ShooterSucceeded.RandomInRange,
                    checkOverrideOnExpiry: true);
                return;
            }
            Job? cast = laser.TryMakeAutoFireJob(target);
            if (cast != null)
            {
                JobMaker.ReturnToPool(job);
                job = cast;
            }
            else if (hostilityResponse && usedLaserVerb && job.def == JobDefOf.AttackStatic
                && ordinaryVerb?.IsMeleeAttack != false)
            {
                // 反击先根据有效 Verb 判定远程，再选目标。若选中无法自动施放的目标，
                // 不能留下一个依赖临时激光 Verb、实际无法射击的 AttackStatic。
                JobMaker.ReturnToPool(job);
                job = ordinaryVerb == null ? null : JobMaker.MakeJob(JobDefOf.AttackMelee, target);
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            current = previous;
            disposed = true;
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.TryGetAttackVerb))]
    internal static class HighEnergyLaserBeamCombatVerbPatch
    {
        private static void Postfix(Pawn __instance, Thing? target, ref Verb? __result) =>
            HighEnergyLaserBeamCombatContext.SelectVerb(__instance, target, ref __result);
    }

    [HarmonyPatch(typeof(JobGiver_AIFightEnemy), "TryGiveJob")]
    internal static class HighEnergyLaserBeamWorkCombatPatch
    {
        private static void Prefix(JobGiver_AIFightEnemy __instance, Pawn pawn, bool ___allowTurrets,
            out HighEnergyLaserBeamCombatContext? __state)
        {
            // 工作模式使用此子类；守卫、袭击等其他战斗 JobGiver 不扩大接入范围。
            __state = __instance is JobGiver_AIFightEnemies
                ? HighEnergyLaserBeamCombatContext.Begin(pawn, ___allowTurrets) : null;
        }

        [HarmonyPriority(Priority.First)]
        private static void Postfix(ref Job? __result, HighEnergyLaserBeamCombatContext? __state) =>
            __state?.CompleteJob(ref __result, hostilityResponse: false);

        private static void Finalizer(HighEnergyLaserBeamCombatContext? __state) => __state?.Dispose();
    }

    [HarmonyPatch(typeof(JobGiver_ConfigurableHostilityResponse), "TryGetAttackNearbyEnemyJob")]
    internal static class HighEnergyLaserBeamHostilityResponsePatch
    {
        private static bool Prefix(Pawn pawn, ref Job? __result,
            out HighEnergyLaserBeamCombatContext? __state)
        {
            __state = null;
            CompHighEnergyLaserBeam? laser = pawn.GetComp<CompHighEnergyLaserBeam>();
            // 常驻思维树每 30 tick 可强制切换 Job；整个技能期间停止重新分配反击任务，
            // 由当前技能 Driver 处理关闭开关、失能和目标失效，恢复阶段也不会被抢占。
            if (laser?.SupportsFireAtWill == true && laser.IsUsingSunSkill)
            {
                __result = null;
                return false;
            }
            __state = HighEnergyLaserBeamCombatContext.Begin(pawn);
            return true;
        }

        [HarmonyPriority(Priority.First)]
        private static void Postfix(ref Job? __result, HighEnergyLaserBeamCombatContext? __state) =>
            __state?.CompleteJob(ref __result, hostilityResponse: true);

        private static void Finalizer(HighEnergyLaserBeamCombatContext? __state) => __state?.Dispose();
    }

    [HarmonyPatch(typeof(Pawn_DraftController), nameof(Pawn_DraftController.Drafted), MethodType.Setter)]
    internal static class HighEnergyLaserBeamPreserveFireAtWillPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            var field = AccessTools.Field(typeof(Pawn_DraftController), "fireAtWillInt");
            List<CodeInstruction> stores = codes.FindAll(code => code.opcode == OpCodes.Stfld
                && Equals(code.operand, field));
            if (field == null || stores.Count != 1)
            {
                Log.Error("[MAP-机械族机械师] 未找到唯一的征召自由开火重置入口，太阳开关保留补丁未应用。");
                return codes;
            }
            // 原版随后会结束当前 Job 并立即重选任务，必须在原赋值处保留状态，
            // 不能等到 Setter 的 Postfix 才恢复，否则期间可能按开启状态自动攻击。
            stores[0].opcode = OpCodes.Call;
            stores[0].operand = AccessTools.Method(typeof(HighEnergyLaserBeamPreserveFireAtWillPatch),
                nameof(ResetUnlessLaserUser));
            return codes;
        }

        private static void ResetUnlessLaserUser(Pawn_DraftController controller, bool value)
        {
            if (controller.pawn.GetComp<CompHighEnergyLaserBeam>()?.SupportsFireAtWill != true)
                controller.FireAtWill = value;
        }
    }
}

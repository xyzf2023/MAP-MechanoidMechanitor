using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.MilianModification
{
    internal static class MilianModificationCompatibilityPatches
    {
        private static bool transitionEnabled;
        private static bool selfInstallationEnabled;
        private static bool abilityCooldownEnabled;
        private static bool transitionPredicateIsStatic;
        private static Dictionary<MethodBase, FieldInfo> driverCaptures = new Dictionary<MethodBase, FieldInfo>();
        private static MethodInfo? waitWithMethod;
        private static ModContentPack? miliraAbilityOwner;
        private static ModContentPack? modificationAbilityOwner;

        internal static void SetEnabled(MilianModificationCompatibility.Feature feature, bool enabled)
        {
            switch (feature)
            {
                case MilianModificationCompatibility.Feature.TransitionAnchor:
                    transitionEnabled = enabled;
                    break;
                case MilianModificationCompatibility.Feature.SelfInstallation:
                    selfInstallationEnabled = enabled;
                    break;
                case MilianModificationCompatibility.Feature.AbilityCooldown:
                    abilityCooldownEnabled = enabled;
                    break;
            }
        }

        private static bool CanApply(bool enabled) => enabled
            && Scribe.mode == LoadSaveMode.Inactive
            && !MechanoidMechanitorPostLoadSafetyCoordinator.ShouldDeferPositiveRestore;

        internal static void ConfigureTransition(bool predicateIsStatic)
        {
            transitionPredicateIsStatic = predicateIsStatic;
        }

        internal static void ConfigureSelfWait(Dictionary<MethodBase, FieldInfo> captures, MethodInfo waitWith)
        {
            driverCaptures = new Dictionary<MethodBase, FieldInfo>(captures);
            waitWithMethod = waitWith;
        }

        internal static void ConfigureAbilityOwners(ModContentPack milira, ModContentPack modification)
        {
            miliraAbilityOwner = milira;
            modificationAbilityOwner = modification;
        }

        internal static IEnumerable<CodeInstruction> TranspilerTransitionAnchor(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            MethodInfo? nonHumanlike = AccessTools.DeclaredMethod(typeof(WildManUtility),
                nameof(WildManUtility.NonHumanlikeOrWildMan), new[] { typeof(Pawn) });
            MethodInfo? controlled = AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.IsColonyMechPlayerControlled));
            MethodInfo? homeFaction = AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.HomeFaction));
            MethodInfo helper = ResolveHelper(nameof(IsNonHumanlikeOrWildManUnlessMechanitor),
                typeof(bool), typeof(Pawn));
            int index = RequireUniqueCall(codes, nonHumanlike, "跃迁锚点的非人类判断");
            RequireUniqueCall(codes, controlled, "跃迁锚点的玩家机械族受控判断");
            RequireUniqueCall(codes, homeFaction, "跃迁锚点的归属阵营判断");
            int pawnLoad = index - 1;
            while (pawnLoad >= 0 && codes[pawnLoad].opcode == OpCodes.Nop) pawnLoad--;
            OpCode expectedLoad = transitionPredicateIsStatic ? OpCodes.Ldarg_0 : OpCodes.Ldarg_1;
            if (pawnLoad < 0 || codes[pawnLoad].opcode != expectedLoad)
                throw new InvalidOperationException("跃迁锚点判断不再直接读取谓词的 Pawn 参数。");

            // 同栈替换唯一的种族门槛，原来的受控、阵营和排除施法者条件继续执行。
            codes[index].opcode = OpCodes.Call;
            codes[index].operand = helper;
            return codes;
        }

        internal static bool IsNonHumanlikeOrWildManUnlessMechanitor(Pawn pawn)
        {
            bool original = pawn.NonHumanlikeOrWildMan();
            return original && !(CanApply(transitionEnabled)
                && pawn.Spawned && !pawn.Destroyed && !pawn.Dead && !pawn.Discarded
                && pawn.Faction == Faction.OfPlayer && pawn.HostFaction == null
                && !pawn.IsPrisoner && !pawn.IsSlave
                && MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn));
        }

        internal static IEnumerable<CodeInstruction> TranspilerSelfWait(
            IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            if (!driverCaptures.TryGetValue(__originalMethod, out FieldInfo capture))
                throw new InvalidOperationException("未配置模块安装或拆卸状态机的 JobDriver 捕获字段。");
            MethodInfo helper = ResolveHelper(nameof(CreateSelfInstallationWait), typeof(Toil),
                typeof(TargetIndex), typeof(int), typeof(bool), typeof(bool), typeof(bool),
                typeof(TargetIndex), typeof(PathEndMode), typeof(JobDriver));
            int index = RequireUniqueCall(codes, waitWithMethod, "模块安装或拆卸的 WaitWith");
            CodeInstruction call = codes[index];
            CodeInstruction loadThis = new CodeInstruction(OpCodes.Ldarg_0);
            loadThis.labels.AddRange(call.labels);
            call.labels.Clear();
            codes.Insert(index, loadThis);
            codes.Insert(index + 1, new CodeInstruction(OpCodes.Ldfld, capture));
            call.opcode = OpCodes.Call;
            call.operand = helper;
            return codes;
        }

        internal static Toil CreateSelfInstallationWait(TargetIndex targetInd, int ticks, bool useProgressBar,
            bool maintainPosture, bool maintainSleep, TargetIndex face, PathEndMode pathEndMode, JobDriver driver)
        {
            if (CanApply(selfInstallationEnabled) && driver?.pawn is Pawn pawn && driver.job != null
                && driver.job.GetTarget(targetInd).Thing == pawn
                && MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
            {
                // 自身无需 ForceWait；上游附加的维修效果和后续装卸步骤继续使用这个 Toil。
                Toil toil = Toils_General.Wait(ticks, face);
                toil.FailOnDespawnedOrNull(targetInd);
                toil.FailOnCannotTouch(targetInd, pathEndMode);
                if (useProgressBar) toil.WithProgressBarToilDelay(targetInd);
                return toil;
            }

            return Toils_General.WaitWith(targetInd, ticks, useProgressBar, maintainPosture,
                maintainSleep, face, pathEndMode);
        }

        internal static IEnumerable<CodeInstruction> TranspilerAbilityCooldown(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            MethodInfo? getter = AccessTools.PropertyGetter(typeof(Pawn_AbilityTracker),
                nameof(Pawn_AbilityTracker.AllAbilitiesForReading));
            MethodInfo helper = ResolveHelper(nameof(GetAbilitiesToReset), typeof(List<Ability>),
                typeof(Pawn_AbilityTracker), typeof(ThingComp));
            int index = RequireUniqueCall(codes, getter, "安装完成后的技能枚举");
            // 确认上游仍通过这一枚举处理次数与冷却，不跳过整个安装完成通知。
            RequireUniqueCall(codes, AccessTools.PropertySetter(typeof(Ability), nameof(Ability.RemainingCharges)),
                "安装完成后的技能次数重置");
            RequireUniqueCall(codes, AccessTools.DeclaredMethod(typeof(Ability), nameof(Ability.StartCooldown),
                new[] { typeof(int) }), "安装完成后的技能冷却重置");

            CodeInstruction call = codes[index];
            CodeInstruction loadComp = new CodeInstruction(OpCodes.Ldarg_0);
            loadComp.labels.AddRange(call.labels);
            call.labels.Clear();
            codes.Insert(index, loadComp);
            call.opcode = OpCodes.Call;
            call.operand = helper;
            return codes;
        }

        internal static List<Ability> GetAbilitiesToReset(Pawn_AbilityTracker tracker, ThingComp modification)
        {
            List<Ability> abilities = tracker.AllAbilitiesForReading;
            if (!CanApply(abilityCooldownEnabled) || miliraAbilityOwner == null || modificationAbilityOwner == null
                || !(modification.parent is Pawn pawn)
                || !MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
                return abilities;

            // 只过滤此次上游循环的输入，不修改 Tracker 的缓存、技能列表或任何存档数据。
            List<Ability> result = new List<Ability>();
            foreach (Ability ability in abilities)
            {
                ModContentPack? owner = ability?.def?.modContentPack;
                if (owner == miliraAbilityOwner || owner == modificationAbilityOwner)
                    result.Add(ability!);
            }
            return result;
        }

        private static MethodInfo ResolveHelper(string name, Type returnType, params Type[] parameters)
        {
            MethodInfo? method = AccessTools.DeclaredMethod(typeof(MilianModificationCompatibilityPatches),
                name, parameters);
            if (method == null || !method.IsStatic || method.ReturnType != returnType)
                throw new InvalidOperationException("无法精确解析米莉安模块兼容 helper：" + name);
            return method;
        }

        private static int RequireUniqueCall(List<CodeInstruction> codes, MethodInfo? target, string description)
        {
            if (target == null)
                throw new InvalidOperationException("无法解析" + description + "的目标签名。");
            int index = -1;
            int count = 0;
            for (int i = 0; i < codes.Count; i++)
            {
                if (!codes[i].Calls(target)) continue;
                index = i;
                count++;
            }
            if (count != 1)
                throw new InvalidOperationException($"{description}调用预期 1 处，实际 {count} 处。");
            if (codes[index].blocks.Count > 0)
                throw new InvalidOperationException(description + "位于异常块边界，无法安全改写。");
            return index;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.RPGStyleInventory
{
    internal enum RPGInventoryPatchKind { Control, Row, OriginalSlot, Icons, OriginalLayout }

    /// <summary>只改写已校验的局部资格、读取与按钮条件，不接管上游绘制或任务。</summary>
    internal static class RPGInventoryPatches
    {
        private static readonly Dictionary<MethodBase, RPGInventoryPatchKind> Kinds = new Dictionary<MethodBase, RPGInventoryPatchKind>();
        internal static void Configure(MethodInfo target, RPGInventoryPatchKind kind) => Kinds[target] = kind;
        private static MethodInfo Helper(string name) => AccessTools.DeclaredMethod(typeof(RPGInventoryPatches), name)
            ?? AccessTools.DeclaredMethod(typeof(RPGInventoryRuntime), name)
            ?? throw new MissingMethodException(name);

        private static void Require(bool condition, string detail)
        {
            if (!condition) throw new InvalidOperationException("RPG 装备栏结构变化：" + detail);
        }

        internal static void ValidateMenu(MethodInfo target)
        {
            List<CodeInstruction> codes = PatchProcessor.GetOriginalInstructions(target).ToList();
            int info = codes.FindIndex(code => code.opcode == OpCodes.Ldstr && Equals(code.operand, "DefInfoTip"));
            int control = codes.FindIndex(code => code.Calls(RPGInventoryRuntime.For(target.DeclaringType!)!.CanControlColonist));
            Require(info >= 0 && control > info, "槽位菜单不再先创建信息卡再检查操作资格。");
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            List<CodeInstruction> codes = instructions.ToList();
            RPGInventoryContext context = RPGInventoryRuntime.For(__originalMethod.DeclaringType!)!;
            RPGInventoryPatchKind kind = Kinds[__originalMethod];
            if (kind == RPGInventoryPatchKind.Control)
            {
                MethodInfo colonist = AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.IsColonistPlayerControlled));
                Require(codes.Count(code => code.Calls(colonist)) == 1, "殖民者资格调用不唯一。");
                int index = codes.FindIndex(code => code.Calls(colonist));
                codes[index].opcode = OpCodes.Ldarg_0;
                codes[index].operand = null;
                codes.Insert(index + 1, new CodeInstruction(OpCodes.Call, Helper(nameof(CountsAsControlled))));
            }
            else if (kind == RPGInventoryPatchKind.Row || kind == RPGInventoryPatchKind.OriginalSlot)
            {
                int thingArgument = kind == RPGInventoryPatchKind.Row ? 3 : 2;
                int inventoryArgument = thingArgument + 1;
                int controlCount = codes.Count(code => code.Calls(context.CanControl));
                Require(controlCount == 1, "物品绘制的 CanControl 调用不唯一。");
                int index = codes.FindIndex(code => code.Calls(context.CanControl));
                // 栈上原有 tab；原位改成加载 Thing，保留分支标签及异常块。
                codes[index].opcode = OpCodes.Ldarg;
                codes[index].operand = (short)thingArgument;
                codes.InsertRange(index + 1, new[]
                {
                    new CodeInstruction(OpCodes.Ldarg, (short)inventoryArgument),
                    new CodeInstruction(OpCodes.Call, Helper(nameof(CanControlThing)))
                });
                if (kind == RPGInventoryPatchKind.Row)
                {
                    int[] colonistCalls = codes.Select((code, i) => new { code, i })
                        .Where(item => item.code.Calls(context.CanControlColonist)).Select(item => item.i).ToArray();
                    Require(colonistCalls.Length == 2, "列表应分别检查装备和服用资格。");
                    SetCall(codes[colonistCalls[1]], Helper(nameof(CanConsume)));
                }
                if (!context.Revamped)
                {
                    ReplacePair(codes, AccessTools.Field(typeof(Pawn), nameof(Pawn.kindDef)),
                        AccessTools.Field(typeof(PawnKindDef), nameof(PawnKindDef.destroyGearOnDrop)),
                        Helper(nameof(LockGear)), 1, thingArgument, appendInstance: true);
                    MethodInfo confirm = AccessTools.DeclaredMethod(typeof(MechanitorUtility),
                        nameof(MechanitorUtility.TryConfirmBandwidthLossFromDroppingThing),
                        new[] { typeof(Pawn), typeof(Thing), typeof(Action) });
                    Require(codes.Count(code => code.Calls(confirm)) == 1, "原版带宽确认调用不唯一。");
                    int confirmIndex = codes.FindIndex(code => code.Calls(confirm));
                    codes[confirmIndex].opcode = OpCodes.Ldarg_0;
                    codes[confirmIndex].operand = null;
                    codes.Insert(confirmIndex + 1, new CodeInstruction(OpCodes.Call, Helper(nameof(ConfirmOriginalDrop))));
                }
                ProtectForcedReads(codes, 1);
            }
            else if (kind == RPGInventoryPatchKind.Icons)
            {
                ProtectTraitReads(codes, 1);
                ProtectForcedReads(codes, 1);
            }
            else if (kind == RPGInventoryPatchKind.OriginalLayout)
            {
                MethodInfo race = AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.RaceProps));
                MethodInfo humanlike = AccessTools.PropertyGetter(typeof(RaceProperties), nameof(RaceProperties.Humanlike));
                int count = 0;
                for (int i = 0; i + 1 < codes.Count; i++)
                {
                    if (!codes[i].Calls(race) || !codes[i + 1].Calls(humanlike)) continue;
                    codes[i].opcode = OpCodes.Nop;
                    codes[i].operand = null;
                    codes[i + 1].opcode = OpCodes.Ldarg_0;
                    codes[i + 1].operand = null;
                    codes.Insert(i + 2, new CodeInstruction(OpCodes.Call, Helper(nameof(UseOriginalSlots))));
                    i += 2;
                    count++;
                }
                Require(count == 6, "原版槽位布局的 Humanlike 判断预期 6 处，实际 " + count + " 处。");
                ProtectTraitReads(codes, 2);
            }
            return codes;
        }

        private static void SetCall(CodeInstruction code, MethodInfo method)
        {
            code.opcode = OpCodes.Call;
            code.operand = method;
        }

        private static void ReplaceCalls(List<CodeInstruction> codes, MethodInfo original, MethodInfo helper, int expected)
        {
            Require(original != null && codes.Count(code => code.Calls(original)) == expected,
                original?.Name + " 的调用数不符。");
            foreach (CodeInstruction code in codes)
                if (code.Calls(original)) SetCall(code, helper);
        }

        private static void ReplacePair(List<CodeInstruction> codes, FieldInfo first, FieldInfo second,
            MethodInfo helper, int expected, int argument = -1, bool appendInstance = false)
        {
            Require(first != null && second != null, "原版字段无法解析。");
            int count = 0;
            for (int i = 0; i + 1 < codes.Count; i++)
            {
                if (!codes[i].LoadsField(first) || !codes[i + 1].LoadsField(second)) continue;
                codes[i].opcode = argument < 0 ? OpCodes.Nop : OpCodes.Ldarg;
                codes[i].operand = argument < 0 ? null : (object)(short)argument;
                if (appendInstance)
                {
                    codes[i + 1].opcode = OpCodes.Ldarg_0;
                    codes[i + 1].operand = null;
                    codes.Insert(i + 2, new CodeInstruction(OpCodes.Call, helper));
                    i += 2;
                }
                else SetCall(codes[i + 1], helper);
                count++;
            }
            Require(count == expected, first!.Name + "/" + second!.Name + " 字段链数量不符。");
        }

        private static void ProtectTraitReads(List<CodeInstruction> codes, int expected)
        {
            ReplacePair(codes, AccessTools.Field(typeof(Pawn), nameof(Pawn.story)),
                AccessTools.Field(typeof(Pawn_StoryTracker), nameof(Pawn_StoryTracker.traits)),
                Helper(nameof(RPGInventoryRuntime.ReadTraits)), expected);
            ReplaceCalls(codes, AccessTools.DeclaredMethod(typeof(TraitSet), nameof(TraitSet.HasTrait), new[] { typeof(TraitDef) }),
                Helper(nameof(RPGInventoryRuntime.HasTrait)), expected);
        }

        private static void ProtectForcedReads(List<CodeInstruction> codes, int expected)
        {
            ReplacePair(codes, AccessTools.Field(typeof(Pawn), nameof(Pawn.outfits)),
                AccessTools.Field(typeof(Pawn_OutfitTracker), nameof(Pawn_OutfitTracker.forcedHandler)),
                Helper(nameof(RPGInventoryRuntime.ReadForcedHandler)), expected);
            ReplaceCalls(codes, AccessTools.DeclaredMethod(typeof(OutfitForcedHandler), nameof(OutfitForcedHandler.IsForced), new[] { typeof(Apparel) }),
                Helper(nameof(RPGInventoryRuntime.IsForced)), expected);
        }

        internal static bool CountsAsControlled(Pawn pawn, object tab)
        {
            if (pawn.IsColonistPlayerControlled) return true;
            return RPGInventoryRuntime.For(tab)?.Enabled == true
                && ReferenceEquals(RPGInventoryRuntime.CurrentPawn(tab), pawn)
                && GearInteractionUtility.CanOperate(pawn)
                && HumanApparelGearTabPatches.GearTabCountsAsColonistPlayerControlled(pawn);
        }

        internal static bool CanControlThing(object tab, Thing thing, bool inventory)
        {
            RPGInventoryContext context = RPGInventoryRuntime.For(tab)!;
            bool original = (bool)RPGInventoryRuntime.Invoke(context.CanControl, tab)!;
            Pawn? pawn = RPGInventoryRuntime.CurrentPawn(tab);
            if (!context.Enabled || pawn == null || !RPGInventoryRuntime.ShouldProtect(pawn, thing)) return original;
            return original && RPGInventoryRuntime.CanDrop(tab, pawn, thing);
        }

        internal static bool CanConsume(object tab)
        {
            RPGInventoryContext context = RPGInventoryRuntime.For(tab)!;
            if (!context.Enabled) return (bool)RPGInventoryRuntime.Invoke(context.CanControlColonist, tab)!;
            return RPGInventoryRuntime.CurrentPawn(tab)?.IsColonistPlayerControlled == true
                && (bool)RPGInventoryRuntime.Invoke(context.CanControl, tab)!;
        }

        internal static bool LockGear(Pawn pawn, Thing thing, object tab) =>
            RPGInventoryRuntime.For(tab)?.Enabled == true
                ? GearInteractionUtility.IsGearLocked(pawn, thing) : pawn.kindDef.destroyGearOnDrop;

        internal static bool ConfirmOriginalDrop(Pawn pawn, Thing thing, Action action, object tab)
        {
            RPGInventoryContext context = RPGInventoryRuntime.For(tab)!;
            if (!context.Enabled)
                return MechanitorUtility.TryConfirmBandwidthLossFromDroppingThing(pawn, thing, action);
            if (thing is not Apparel apparel || pawn.apparel?.WornApparel.Contains(apparel) != true)
                return false;
            bool executed = false;
            return MechanitorUtility.TryConfirmBandwidthLossFromDroppingThing(pawn, thing, () =>
            {
                if (executed || !context.Enabled || !RPGInventoryRuntime.CanDrop(tab, pawn, thing)) return;
                executed = true;
                action();
            });
        }

        public static void CanControlPostfix(object __instance, ref bool __result)
        {
            if (__result && RPGInventoryRuntime.For(__instance)?.Enabled == true
                && RPGInventoryRuntime.CurrentPawn(__instance) is Pawn pawn
                && GearInteractionUtility.HasGearCapability(pawn))
                __result = GearInteractionUtility.CanOperate(pawn);
        }

        public static bool DropPrefix(object __instance, Thing t)
        {
            RPGInventoryContext? context = RPGInventoryRuntime.For(__instance);
            if (context?.Enabled != true) return true;
            Pawn? pawn = RPGInventoryRuntime.CurrentPawn(__instance);
            if (pawn == null || !RPGInventoryRuntime.CanDrop(__instance, pawn, t)) return false;
            if (!context.Revamped || RPGInventoryRuntime.DropIsApproved(__instance, t)) return true;
            RPGInventoryRuntime.RequestDrop(__instance, pawn, t);
            return false;
        }

        public static void PopupPostfix(object __instance, Pawn pawn, Thing thing, List<FloatMenuOption> __result)
        {
            if (RPGInventoryRuntime.For(__instance)?.CEEnabled != true)
                RPGInventoryRuntime.ProtectMenu(__instance, pawn, thing, __result);
        }

        public static bool InventoryPrefix(Pawn p, ref bool __result)
        {
            if (p.inventory != null) return true;
            __result = false;
            return false;
        }

        public static void SlotsPostfix(object __instance, ref bool __result)
        {
            if (!__result && RPGInventoryRuntime.For(__instance)?.LayoutEnabled == true)
                __result = RPGInventoryRuntime.CanDisplay(RPGInventoryRuntime.CurrentPawn(__instance));
        }

        public static void ApparelPostfix(object __instance, Pawn p, ref bool __result)
        {
            if (!__result && RPGInventoryRuntime.For(__instance)?.LayoutEnabled == true)
                __result = RPGInventoryRuntime.CanDisplayApparel(p);
        }

        internal static bool UseOriginalSlots(Pawn pawn, object tab) => pawn.RaceProps.Humanlike
            || (RPGInventoryRuntime.For(tab)?.LayoutEnabled == true
                && RPGInventoryRuntime.CanDisplay(pawn));
    }
}

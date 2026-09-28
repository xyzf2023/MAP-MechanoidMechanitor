using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using GD3;
using HarmonyLib;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.GD5
{
    [StaticConstructorOnStartup]
    internal static class GD5BlackHiveEndingBootstrap
    {
        private const string HarmonyId = "xyzf.mechanoidmechanitor.gd5.blackhiveending";
        internal static bool IsReady { get; private set; }
        internal static GD5DialogueDef Contact = null!;
        internal static GD5DialogueDef Departure = null!;

        static GD5BlackHiveEndingBootstrap()
        {
            var harmony = new Harmony(HarmonyId);
            try
            {
                Contact = DefDatabase<GD5DialogueDef>.GetNamed("MAP_GD5_BlackHiveContact");
                Departure = DefDatabase<GD5DialogueDef>.GetNamed("MAP_GD5_BlackHiveDeparture");
                string[] errors = Contact.Validate().Concat(Departure.Validate()).ToArray();
                if (errors.Length > 0) throw new InvalidOperationException(string.Join("; ", errors));
                var race = DefDatabase<ThingDef>.GetNamed(GD5BlackHiveEndingService.VisitorDefName);
                if (race.thingClass != typeof(Pawn_BlackHiveVisitor)
                    || race.GetCompProperties<CompProperties_BlackHiveVisitor>() == null
                    || GD5BlackHiveEndingService.VisitorKind.race != race)
                    throw new InvalidOperationException("接人毒蜂 Def 不符合预期。");
                if (GDDefOf.Drysea?.pocketMapProperties == null)
                    throw new InvalidOperationException("枯海口袋地图生成器未正确加载。");
                var target = AccessTools.DeclaredMethod(typeof(TradeWindow_BlackMech),
                    nameof(TradeWindow_BlackMech.DoWindowContents), new[] { typeof(Rect) });
                if (target == null || target.ReturnType != typeof(void)
                    || AccessTools.Field(typeof(TradeWindow_BlackMech), "map")?.FieldType != typeof(Map)
                    || AccessTools.Field(typeof(TradeWindow_BlackMech), "pawn")?.FieldType != typeof(Pawn)
                    || AccessTools.Field(typeof(TradeWindow_BlackMech), "options")?.FieldType != typeof(string[]))
                    throw new MissingMemberException("闪毁5黑衣通讯窗口结构已变化。");
                harmony.Patch(target,
                    prefix: new HarmonyMethod(typeof(GD5BlackHiveEndingBootstrap), nameof(DrawPrefix)),
                    postfix: new HarmonyMethod(typeof(GD5BlackHiveEndingBootstrap), nameof(DrawPostfix)),
                    transpiler: new HarmonyMethod(typeof(GD5BlackHiveEndingBootstrap), nameof(TradeScrollTranspiler)));
                // TakeDamage 会在 PreApplyDamage 前忽略零数值事件；EMP/眩晕也必须触发撤离。
                harmony.Patch(AccessTools.Method(typeof(Thing), nameof(Thing.TakeDamage), new[] { typeof(DamageInfo) }),
                    prefix: new HarmonyMethod(typeof(GD5BlackHiveEndingBootstrap), nameof(DamagePrefix))
                        { priority = Priority.First });
                harmony.Patch(AccessTools.DeclaredMethod(typeof(MapParent), nameof(MapParent.CheckRemoveMapNow)),
                    prefix: new HarmonyMethod(typeof(GD5BlackHiveEndingBootstrap), nameof(RemoveMapPrefix)));
                IsReady = true;
                Log.Message("[MAP-GD5] 黑衣机械巢接人结局已加载。");
            }
            catch (Exception ex)
            {
                harmony.UnpatchAll(HarmonyId);
                Log.Error("[MAP-GD5] 黑衣接人结局初始化失败，已撤销本模块补丁。\n" + ex);
            }
        }

        private static bool DamagePrefix(Thing __instance, DamageInfo dinfo, ref DamageWorker.DamageResult __result)
        {
            if (!(__instance is Pawn_BlackHiveVisitor visitor) || !visitor.TryWithdrawForDamage(dinfo)) return true;
            __result = new DamageWorker.DamageResult();
            return false;
        }

        private static bool RemoveMapPrefix(MapParent __instance) =>
            GameComponent_GD5StoryState.Current?.KeepsTravelMap(__instance.Map) != true;

        private static void DrawPrefix(Rect inRect, out Rect __state)
        {
            __state = Rect.zero;
            if (!GD5BlackHiveEndingService.IsUnlocked) return;
            __state = inRect;
        }

        private static void DrawPostfix(TradeWindow_BlackMech __instance, Map ___map, Pawn ___pawn,
            string[] ___options, Rect __state)
        {
            if (__state.height <= 0f || !Find.WindowStack.Windows.Contains(__instance)) return;
            Rect content = __state.ContractedBy(10f);
            // 原有选项坐标不动，联络入口置于第一项（已完成）上一行。
            Rect row = new Rect(content.x, __state.height - 25f - ___options.Length * Text.LineHeight,
                content.width, Text.LineHeight);
            string? reason = GD5BlackHiveEndingService.ContactDisabledReason(___map);
            string label = "MAP_GD5.Ending.Contact".Translate();
            Color oldColor = GUI.color;
            if (reason == null) Widgets.DrawHighlightIfMouseover(row);
            else GUI.color = Color.gray;
            Widgets.Label(row, label);
            GUI.color = oldColor;
            if (reason != null) TooltipHandler.TipRegion(row, reason);
            else if (Widgets.ButtonInvisible(row))
            {
                GD5BlackHiveEndingService.OpenContact(___map, ___pawn);
                __instance.Close();
            }
        }

        private static void BeginTradeScrollView(Rect outRect, ref Vector2 scrollPosition,
            Rect viewRect, bool showScrollbars)
        {
            // 只缩短交易列表，给上方插入的新选项让出空间，不挪动原选项或改变其点击行为。
            if (GD5BlackHiveEndingService.IsUnlocked)
                outRect.height = Mathf.Max(0f, outRect.height - Text.LineHeight);
            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect, showScrollbars);
        }

        private static IEnumerable<CodeInstruction> TradeScrollTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = instructions.ToList();
            var original = AccessTools.Method(typeof(Widgets), nameof(Widgets.BeginScrollView),
                new[] { typeof(Rect), typeof(Vector2).MakeByRefType(), typeof(Rect), typeof(bool) });
            var replacement = AccessTools.DeclaredMethod(typeof(GD5BlackHiveEndingBootstrap), nameof(BeginTradeScrollView));
            if (original == null || replacement == null || codes.Count(c => c.Calls(original)) != 1)
                throw new InvalidOperationException("闪毁5黑衣通讯交易滚动区结构已变化。");
            foreach (CodeInstruction code in codes)
                yield return code.Calls(original)
                    ? new CodeInstruction(code) { opcode = OpCodes.Call, operand = replacement }
                    : code;
        }
    }
}

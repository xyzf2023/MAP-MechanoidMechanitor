using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor
{
    // 仅拦截远行队新生成地点地图后的标准抵达信；入场与已有地图通知仍走原版流程。
    [HarmonyPatch(typeof(CaravanArrivalAction_VisitSite), "DoEnter",
        new[] { typeof(Caravan), typeof(Site) })]
    internal static class SunBossCaravanArrivalLetterPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            MethodInfo original = AccessTools.Method(typeof(LetterStack), nameof(LetterStack.ReceiveLetter),
                new[] { typeof(Letter), typeof(string), typeof(int), typeof(bool) });
            MethodInfo replacement = AccessTools.Method(typeof(SunBossCaravanArrivalLetterPatch),
                nameof(ReceiveNewMapArrivalLetter));
            // 新地图分支传入已构造的 Letter；已有地图分支使用 TaggedString 重载，不匹配此处。
            if (original == null || replacement == null || codes.FindAll(code => code.Calls(original)).Count != 1)
            {
                Log.Error("[MAP-机械族机械师] 太阳设施远行队抵达信补丁：原版发送结构不符，保留原方法。");
                return codes;
            }

            List<CodeInstruction> patched = new List<CodeInstruction>();
            foreach (CodeInstruction code in codes)
            {
                if (code.Calls(original))
                {
                    // 保留接收者及原参数，追加 DoEnter 的 Site 参数供定点过滤。
                    CodeInstruction loadSite = new CodeInstruction(OpCodes.Ldarg_2);
                    loadSite.labels.AddRange(code.labels);
                    code.labels.Clear();
                    for (int i = code.blocks.Count - 1; i >= 0; i--)
                        if (code.blocks[i].blockType != ExceptionBlockType.EndExceptionBlock)
                        {
                            loadSite.blocks.Insert(0, code.blocks[i]);
                            code.blocks.RemoveAt(i);
                        }
                    patched.Add(loadSite);
                    code.opcode = OpCodes.Call;
                    code.operand = replacement;
                }
                patched.Add(code);
            }
            return patched;
        }

        private static void ReceiveNewMapArrivalLetter(LetterStack stack, Letter letter,
            string debugInfo, int delayTicks, bool playSound, Site site)
        {
            // 自定义信件由 Site.PostMapGenerate 的生成完成回调发送，此处不重复发送。
            if (site.def.defName == "MAP_SunBossFacility") return;
            stack.ReceiveLetter(letter, debugInfo, delayTicks, playSound);
        }
    }
}

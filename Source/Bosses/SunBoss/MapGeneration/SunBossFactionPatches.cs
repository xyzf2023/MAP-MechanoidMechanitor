using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    // 原版房间部件忽略布局传入的派系；在其公共守军生成入口替换机械巢归属。
    [HarmonyPatch(typeof(RoomGenUtility), nameof(RoomGenUtility.SpawnDormantThreatCluster),
        new[] { typeof(LayoutRoom), typeof(Map), typeof(Faction), typeof(Func<PawnKindDef, bool>),
            typeof(float), typeof(string) })]
    internal static class SunBossDormantGuardFactionPatch
    {
        private static void Prefix(Map map, ref Faction faction)
        {
            Faction? facilityFaction = map.GetComponent<MapComponent_SunBossArena>().FacilityFaction;
            if (facilityFaction != null && faction == Faction.OfMechanoids)
                faction = facilityFaction;
        }
    }

    // 保留原版出仓位置、空仓状态、眩晕和战斗组分配，只替换其写死的机械巢查询。
    // 自动靠近触发、摧毁出仓和 BOSS 主动释放都会走 Trigger。
    [HarmonyPatch(typeof(CompMechGestatorTank), "Trigger", new[] { typeof(Map) })]
    internal static class SunBossGestatorFactionPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            MethodInfo original = AccessTools.PropertyGetter(typeof(Faction), nameof(Faction.OfMechanoids));
            MethodInfo replacement = AccessTools.Method(typeof(SunBossFactionUtility),
                nameof(SunBossFactionUtility.GetMechFactionForMap));
            // 当前原版包含 Pawn 生成、Lord 查询、Lord 创建及 LordJob 构造四处查询。
            if (codes.FindAll(code => code.Calls(original)).Count != 4)
            {
                Log.Error("[MAP-机械族机械师] 太阳设施培养舱派系补丁：原版出仓结构不符，保留原方法。");
                return codes;
            }

            List<CodeInstruction> patched = new List<CodeInstruction>();
            foreach (CodeInstruction code in codes)
            {
                if (code.Calls(original))
                {
                    // 分支目标先加载 Trigger 的地图参数，再按本地图读取派系。
                    CodeInstruction loadMap = new CodeInstruction(OpCodes.Ldarg_1);
                    loadMap.labels.AddRange(code.labels);
                    code.labels.Clear();
                    // 起始异常块随参数移动；结束异常块仍留在实际调用上。
                    for (int i = code.blocks.Count - 1; i >= 0; i--)
                        if (code.blocks[i].blockType != ExceptionBlockType.EndExceptionBlock)
                        {
                            loadMap.blocks.Insert(0, code.blocks[i]);
                            code.blocks.RemoveAt(i);
                        }
                    patched.Add(loadMap);
                    code.opcode = OpCodes.Call;
                    code.operand = replacement;
                }
                patched.Add(code);
            }
            return patched;
        }
    }
}

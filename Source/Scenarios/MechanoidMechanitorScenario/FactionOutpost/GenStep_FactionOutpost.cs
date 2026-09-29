using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 每个步骤生成一个建筑群。尺寸按前哨创建时保存的档位读取，0 表示该档位没有此建筑群。
    /// 原版生成器使用独立实例，避免修改共享 GenStepDef；建筑群仍通过 UsedRects 避让。
    /// </summary>
    public sealed class GenStep_FactionOutpost : GenStep
    {
        // XML 顺序固定为 Baseline、Expanded、Large、Fortress。
        public List<int> sizesByTier = new List<int>();

        public override int SeedPart => 398638181;

        public override void Generate(Map map, GenStepParams parms)
        {
            if (!(map?.Parent is MAPFactionOutpost outpost))
            {
                Log.Error("[MAP-机械族机械师] 前哨建筑生成步骤缺少对应的前哨世界对象。");
                return;
            }

            int tier = (int)outpost.LayoutTier;
            if (sizesByTier == null || sizesByTier.Count != 4
                || tier < 0 || tier >= sizesByTier.Count || sizesByTier[tier] < 0)
            {
                Log.Error("[MAP-机械族机械师] 前哨建筑生成步骤的四档尺寸配置或已保存布局档位无效。");
                return;
            }

            int size = sizesByTier[tier];
            if (size == 0)
            {
                return;
            }

            new GenStep_Outpost
            {
                size = size,
                allowGeneratingThronerooms = false,
                settlementDontGeneratePawns = true,
                allowGeneratingFarms = false,
                generateLoot = false
            }.Generate(map, parms);
        }
    }
}

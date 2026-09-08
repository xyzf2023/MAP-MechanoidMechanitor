using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class CompProperties_MindMappingAutonomousDirectiveCore : CompProperties
    {
        public CompProperties_MindMappingAutonomousDirectiveCore()
            : base(typeof(CompMindMappingAutonomousDirectiveCore))
        {
        }
    }

    public sealed class CompMindMappingAutonomousDirectiveCore : ThingComp
    {
        private MindMappingData? data;

        public bool IsBlank => data == null;
        public MindMappingData? Data => data;

        public void Store(MindMappingData newData)
        {
            data = newData;
        }

        public void Clear()
        {
            data = null;
        }

        public override string TransformLabel(string label)
        {
            return data == null
                ? "MAP_MindMapping.Core.BlankLabel".Translate()
                : "MAP_MindMapping.Core.LoadedLabel".Translate(data.ShortName);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (!DebugSettings.ShowDevGizmos)
            {
                yield break;
            }

            if (IsBlank)
            {
                yield return new Command_Action
                {
                    defaultLabel = "DEV：随机生成",
                    action = DevGenerateRandomData
                };
                yield break;
            }

            yield return new Command_Action
            {
                defaultLabel = "DEV：清空数据",
                action = Clear
            };
        }

        private void DevGenerateRandomData()
        {
            // 点击时再次确认当前仍为空白，避免 Gizmo 创建后状态变化时覆盖已有数据。
            if (!DebugSettings.ShowDevGizmos || !IsBlank)
            {
                return;
            }

            List<Pawn> candidates = new List<Pawn>();
            List<Pawn> allPawns = PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead;
            for (int i = 0; i < allPawns.Count; i++)
            {
                Pawn pawn = allPawns[i];
                if (pawn != null
                    && pawn.RaceProps.Humanlike
                    && pawn.Name != null
                    && pawn.story != null
                    && pawn.skills != null)
                {
                    candidates.Add(pawn);
                }
            }

            Pawn? source = candidates.RandomElementWithFallback();
            if (source == null)
            {
                Messages.Message(
                    "DEV：未找到可复制的人格数据",
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
            }

            Store(MindMappingData.Capture(source));
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Deep.Look(ref data, "mindMappingData");
        }
    }
}

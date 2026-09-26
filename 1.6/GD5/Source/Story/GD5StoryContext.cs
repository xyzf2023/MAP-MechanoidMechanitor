using Verse;

namespace MAP_MechanoidMechanitor.GD5
{
    // 同一次通讯共用快照，翻页不会更换通讯者或关系分支。
    internal sealed class GD5StoryContext
    {
        internal readonly Map? Map;
        internal readonly Pawn? Speaker;
        internal readonly GD5DialogueCondition Relation;
        private readonly bool speakerIsJustice;

        internal GD5StoryContext(Map? map, Pawn? speaker)
        {
            Map = map;
            Speaker = speaker;
            Relation = GD5StoryFlowService.GetHiveRelation();
            speakerIsJustice = JusticePawnUtility.IsJustice(speaker);
        }

        internal bool Matches(GD5DialogueCondition condition)
        {
            if (condition == GD5DialogueCondition.Always) return true;
            if (condition == GD5DialogueCondition.SpeakerJustice) return speakerIsJustice;
            return condition == Relation;
        }

        internal string Translate(string key) => Speaker == null
            ? key.Translate().ToString()
            : key.Translate(Speaker.NameShortColored).ToString();
    }
}

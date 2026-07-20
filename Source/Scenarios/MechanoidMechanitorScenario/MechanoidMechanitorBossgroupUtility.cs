using RimWorld;
using RimWorld.QuestGen;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public enum MechanoidMechanitorBossgroupSource
    {
        Auto,
        MechHive,
        Other
    }

    public sealed class MechanoidMechanitorBossgroupExtension : DefModExtension
    {
        public MechanoidMechanitorBossgroupSource source =
            MechanoidMechanitorBossgroupSource.Auto;
    }

    public static class MechanoidMechanitorBossgroupUtility
    {
        private const string DisabledReasonKey =
            "MAP_MechanoidMechanitor.PurgeDirective.Bossgroup.DisabledReason";

        public static string DisabledReason => DisabledReasonKey.Translate();

        public static bool ShouldBlockPlayerSummon(BossgroupDef? bossgroupDef)
        {
            return MechanoidMechanitorPurgeDirectiveRelationUtility
                    .ShouldApplyNonHostileMechHiveRestrictions()
                && IsMechHiveBossgroup(bossgroupDef);
        }

        public static bool HasAnyBlockedMechHiveBossgroup()
        {
            if (!MechanoidMechanitorPurgeDirectiveRelationUtility
                    .ShouldApplyNonHostileMechHiveRestrictions())
            {
                return false;
            }

            foreach (BossgroupDef bossgroupDef in DefDatabase<BossgroupDef>.AllDefs)
            {
                if (IsMechHiveBossgroup(bossgroupDef))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsMechHiveBossgroup(BossgroupDef? bossgroupDef)
        {
            if (bossgroupDef == null)
            {
                return false;
            }

            MechanoidMechanitorBossgroupExtension? extension =
                bossgroupDef.GetModExtension<MechanoidMechanitorBossgroupExtension>();
            if (extension != null)
            {
                switch (extension.source)
                {
                    case MechanoidMechanitorBossgroupSource.MechHive:
                        return true;
                    case MechanoidMechanitorBossgroupSource.Other:
                        return false;
                }
            }

            QuestScriptDef? quest = bossgroupDef.quest;
            return quest?.root != null
                && quest.root.GetType() == typeof(QuestNode_Root_Bossgroup);
        }
    }
}

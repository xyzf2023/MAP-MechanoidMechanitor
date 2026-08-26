using System;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class CompAbilityEffect_MechHack : CompAbilityEffect
    {
        private const string DiabolusPawnKindDefName = "Mech_Diabolus";
        private const string WarqueenPawnKindDefName = "Mech_Warqueen";

        public new CompProperties_AbilityMechHack Props =>
            (CompProperties_AbilityMechHack)props;

        public override bool CanApplyOn(LocalTargetInfo target, LocalTargetInfo dest)
        {
            return base.CanApplyOn(target, dest)
                && target.HasThing
                && target.Thing is Pawn targetPawn
                && CanHack(targetPawn, parent.pawn);
        }

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            if (!base.Valid(target, throwMessages))
            {
                return false;
            }

            if (target.HasThing
                && target.Thing is Pawn targetPawn
                && CanHack(targetPawn, parent.pawn))
            {
                return true;
            }

            if (throwMessages)
            {
                // 仅当目标满足全部旧版规则、唯一失败原因就是新增 BOSS 限制时，
                // 才显示 BOSS 专属提示；否则继续沿用普通无效提示。
                bool showBossRestrictedMessage = target.HasThing
                    && target.Thing is Pawn targetPawnForMessage
                    && CanHackUnderOriginalRules(targetPawnForMessage, parent.pawn)
                    && IsBossBlockedByHackRestriction(targetPawnForMessage);

                Messages.Message(
                    (showBossRestrictedMessage
                        ? Props.bossRestrictedTargetMessageKey
                        : Props.invalidTargetMessageKey).Translate(),
                    parent.pawn,
                    MessageTypeDefOf.RejectInput,
                    historical: false);
            }

            return false;
        }

        public override Window ConfirmationDialog(
            LocalTargetInfo target,
            Action confirmAction)
        {
            Pawn? targetPawn = target.Pawn;
            Faction? targetFaction = targetPawn?.Faction;

            if (targetFaction != null
                && targetFaction != Faction.OfPlayer
                && !targetFaction.HostileTo(Faction.OfPlayer))
            {
                return Dialog_MessageBox.CreateConfirmation(
                    Props.nonHostileConfirmMessageKey.Translate(),
                    confirmAction);
            }

            return null!;
        }

        /// <summary>
        /// 仅包含“新增 BOSS 限制出现之前”的全部实际骇入条件。
        /// 不含 IsBossBlockedByHackRestriction；包含 JusticePawnUtility.IsBossJustice 旧有永久规则。
        /// </summary>
        private bool CanHackUnderOriginalRules(Pawn? targetPawn, Pawn? caster)
        {
            if (targetPawn == null || caster == null || caster.Map == null)
            {
                return false;
            }

            return targetPawn.Spawned
                && !targetPawn.Dead
                && targetPawn.Map == caster.Map
                && targetPawn.RaceProps.IsMechanoid
                && targetPawn.Faction != Faction.OfPlayer
                && targetPawn.OverseerSubject != null
                && !JusticePawnUtility.IsBossJustice(targetPawn);
        }

        public bool CanHack(Pawn? targetPawn, Pawn? caster)
        {
            if (!CanHackUnderOriginalRules(targetPawn, caster))
            {
                return false;
            }

            // CanHackUnderOriginalRules 已保证 targetPawn 非空。
            return !IsBossBlockedByHackRestriction(targetPawn!);
        }

        /// <summary>
        /// 仅代表“是否被新增的 BOSS 限制拦截”，与「正义」-重装指挥单元的永久禁止规则相互独立。
        /// 「正义」BOSS 由 JusticePawnUtility.IsBossJustice 永久拦截，不受此设置影响。
        /// </summary>
        private bool IsBossBlockedByHackRestriction(Pawn targetPawn)
        {
            // 设置实例不存在时不意外阻止目标；只有明确开启限制时才拦截。
            if (MAPMechanitorMod.Settings?.restrictMechHackBossTargets != true)
            {
                return false;
            }

            PawnKindDef? targetKind = targetPawn.kindDef;
            if (targetKind == null)
            {
                return false;
            }

            if (!targetKind.isBoss)
            {
                return false;
            }

            // 炼狱魔王与战争女皇为明确允许的两个例外。
            if (targetKind.defName == DiabolusPawnKindDefName)
            {
                return false;
            }

            if (targetKind.defName == WarqueenPawnKindDefName)
            {
                return false;
            }

            return true;
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
        }
    }
}

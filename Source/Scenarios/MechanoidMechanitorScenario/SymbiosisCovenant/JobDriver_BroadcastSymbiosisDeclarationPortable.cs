using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class JobDriver_BroadcastSymbiosisDeclarationPortable
        : JobDriver
    {
        // 该 Job 没有实体目标，不需要 Reserve 任何 Thing / 地块 / 假通讯台。
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // 持续 900 ticks 广播。不创建假 Target 用于进度条。
            Toil wait = Toils_General.Wait(
                SymbiosisCovenantCommunicationUtility.DeclarationDurationTicks);

            wait.FailOn(
                () =>
                {
                    Pawn? p = pawn;
                    if (p == null || p.Dead || p.Destroyed)
                    {
                        return true;
                    }

                    if (!p.Spawned)
                    {
                        return true;
                    }

                    Map? map = p.Map;
                    if (map == null)
                    {
                        return true;
                    }

                    if (p.Faction == null || !p.Faction.IsPlayerSafe())
                    {
                        return true;
                    }

                    // 广播过程中拆除植入体 -> 立刻失败。
                    if (!PortableCommsUtility.HasMicroCommunicator(p))
                    {
                        return true;
                    }

                    // 广播过程中太阳耀斑 -> 立刻失败。
                    if (map.gameConditionManager.ElectricityDisabled(map))
                    {
                        return true;
                    }

                    // 其他逻辑已提前完成 -> 不重复广播。
                    if (!SymbiosisCovenantCommunicationUtility
                            .CanBroadcastDeclaration())
                    {
                        return true;
                    }

                    return false;
                });

            yield return wait;

            Toil broadcast =
                ToilMaker.MakeToil("BroadcastSymbiosisDeclarationPortable");
            broadcast.initAction = delegate
            {
                Pawn? actor = broadcast.actor;
                if (actor == null)
                {
                    return;
                }

                if (!PortableCommsUtility.CanUsePortableComms(actor, out _)
                    || !SymbiosisCovenantCommunicationUtility
                        .CanBroadcastDeclaration())
                {
                    return;
                }

                // 复用现有核心业务，不复制内部逻辑。
                GameComponent_SymbiosisCovenantState.CurrentComponent
                    ?.TryBroadcastPublicDeclaration();
            };
            yield return broadcast;
        }
    }
}

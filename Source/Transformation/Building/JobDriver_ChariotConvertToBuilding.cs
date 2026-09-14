using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 战车原地转换读条。读条被其他命令打断时不会发生形态变化；
    /// 完成后只提交到下一 tick 的安全转换队列。
    /// </summary>
    public sealed class JobDriver_ChariotConvertToBuilding : JobDriver
    {
        public const int ConversionDurationTicks = 180;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddFailCondition(
                () => !MechBuildingConversionService.CanConvert(pawn, out _));

            yield return Toils_General.StopDead();

            Toil wait = Toils_General.Wait(
                ConversionDurationTicks,
                TargetIndex.A);
            wait.WithProgressBarToilDelay(TargetIndex.A);
            yield return wait;

            yield return Toils_General.Do(
                () =>
                {
                    if (GameComponent_MechBuildingConversionQueue
                        .TryQueueConversion(pawn, out string? failureReason))
                    {
                        return;
                    }

                    Messages.Message(
                        failureReason
                            ?? "MAP_MechanoidMechanitor.Transformation.Building.Unavailable"
                                .Translate(),
                        pawn,
                        MessageTypeDefOf.RejectInput,
                        historical: false);
                });
        }
    }
}

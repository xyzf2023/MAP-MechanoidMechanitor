using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 机械体原地转换为建筑的读条。读条被其他命令打断时不会发生形态变化；
    /// 完成后只提交到下一 tick 的安全转换队列。
    /// </summary>
    public class JobDriver_MechConvertToBuilding : JobDriver
    {
        public const int ConversionDurationTicks = 180;
        private bool failureReported;

        // 沿用原版已存档的 Toil 进度；不增加计时器，也不改变旧存档的步骤编号。
        internal float ConversionProgress => CurToilIndex < 1 ? 0f
            : CurToilIndex == 1 ? Mathf.Clamp01(1f - (float)ticksLeftThisToil / ConversionDurationTicks) : 1f;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddFailCondition(() =>
            {
                if (MechBuildingConversionService.CanConvert(pawn, out string? failureReason))
                {
                    return false;
                }

                if (!failureReported)
                {
                    failureReported = true;
                    MechBuildingConversionService.Reject(pawn, failureReason, sendFailureMessage: true);
                }

                return true;
            });

            yield return Toils_General.StopDead();

            Toil wait = Toils_General.Wait(ConversionDurationTicks);
            // 转换读条自行控制朝向，不再由原版等待步骤转向目标。
            wait.handlingFacing = true;
            wait.AddPreInitAction(() => pawn.Rotation = Rot4.South);
            wait.tickAction = () => pawn.Rotation = Rot4.South;
            wait.WithProgressBarToilDelay(TargetIndex.A);
            yield return wait;

            yield return Toils_General.Do(
                () =>
                {
                    pawn.Rotation = Rot4.South;
                    if (GameComponent_MechBuildingConversionQueue
                        .TryQueueConversion(pawn, out string? failureReason))
                    {
                        return;
                    }

                    MechBuildingConversionService.Reject(
                        pawn, failureReason, sendFailureMessage: true);
                });
        }
    }

    /// <summary>
    /// 旧存档的 curDriver 按完整类型名保存；保留无独立逻辑的兼容类型，
    /// 已开始的转换可继续读条，新 JobDef 的 driverClass 只使用通用类型。
    /// </summary>
    [System.Obsolete("仅用于读取旧存档，请使用 JobDriver_MechConvertToBuilding。")]
    public sealed class JobDriver_ChariotConvertToBuilding : JobDriver_MechConvertToBuilding
    {
    }
}

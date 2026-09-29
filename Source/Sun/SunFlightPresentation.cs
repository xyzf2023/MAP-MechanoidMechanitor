using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>太阳的飞行姿态；复用直线飞行数据，只缓存表现，不改变实际移动。</summary>
    internal static class SunFlightPresentation
    {
        private const float CoreTilt = 10f;
        private const float ArmorTilt = 16f;
        private const float CoreResponseSeconds = 0.10f;
        private const float ArmorResponseSeconds = 0.18f;
        private const int StaleAfterTicks = 30;

        internal readonly struct Pose
        {
            internal readonly float BodyAngle;
            internal readonly float ArmorAngle;
            private readonly Vector2 armorMotion;
            private readonly Vector2 inertia;
            private readonly float movement;

            internal Pose(Vector2 core, Vector2 armor, float maximumTilt)
            {
                BodyAngle = Mathf.Clamp(core.x, -1f, 1f) * Mathf.Min(CoreTilt, maximumTilt);
                ArmorAngle = Mathf.Clamp(armor.x, -1f, 1f) * Mathf.Min(ArmorTilt, maximumTilt);
                armorMotion = Vector2.ClampMagnitude(armor, 1f);
                movement = Mathf.Clamp01(Mathf.Max(core.magnitude, armor.magnitude));
                // 加速时甲片落后，制动时沿原方向越过平衡点；仅一次收稳，不持续摆动。
                // 基准绘制尺寸下最多偏移 0.045 格；当前太阳缩放后约为 0.077 格。
                inertia = Vector2.ClampMagnitude((armor - core) * 0.18f, 0.045f);
            }

            internal float FloatFactor => Mathf.Lerp(1f, 0.3f, movement);
            internal float LayoutAngle(float open) => Mathf.Lerp(BodyAngle, ArmorAngle, open);

            // 输入已经旋转到地图 X/Z 方向的甲片偏移；核心与灯罩仍使用原绘制锚点。
            internal Vector2 ApplyToOffset(Vector2 offset, float open)
            {
                float forward = Vector2.Dot(offset.normalized, armorMotion);
                offset *= 1f - open * (0.05f * movement + 0.03f * forward);
                offset -= armorMotion * (Vector2.Dot(offset, armorMotion) * 0.04f * open);
                return offset + inertia * open;
            }
        }

        private sealed class MotionState
        {
            internal Map Map = null!;
            internal int LastTick;
            internal Vector2 Core, CoreVelocity, Armor, ArmorVelocity;
            internal Pose Pose;
        }

        private static readonly Dictionary<int, MotionState> States = new();

        // GetDrawParms 可能由并行预绘制调用；读取端绝不推进动画或修改缓存。
        internal static Pose Current(Pawn pawn) => pawn.Spawned
            && States.TryGetValue(pawn.thingIDNumber, out var state) && state.Map == pawn.Map
                ? state.Pose : default;

        internal static void BeginFlight(Pawn pawn)
        {
            if (!SunArmorPresentation.IsSun(pawn) || !pawn.Spawned) return;
            States[pawn.thingIDNumber] = new MotionState
            {
                Map = pawn.Map,
                LastTick = Find.TickManager.TicksGame
            };
        }

        internal static void Forget(Pawn pawn) => States.Remove(pawn.thingIDNumber);
        internal static void ClearAllRuntimeState() => States.Clear();

        // 仅从游戏 tick、主线程装甲准备及武器发射点查询进入。
        internal static void Prepare(Pawn pawn)
        {
            if (!SunArmorPresentation.IsSun(pawn)) return;
            if (pawn.Dead || GravityDisorderPresentation.ShouldDraw(pawn)
                || !MechanicalFlightUtility.TryGetHoverVisualState(pawn, out var record, out var profile)
                || record == null || profile == null || !profile.allowTilt
                || record.Purpose == MechanicalFlightPurpose.FusionRelocation)
            {
                Forget(pawn);
                return;
            }

            int now = Find.TickManager.TicksGame;
            bool existing = States.TryGetValue(pawn.thingIDNumber, out MotionState? state)
                && state != null && state.Map == pawn.Map;
            if (existing && state!.LastTick == now) return;

            Vector2 motion = record.UsesAerialMovement && !SunArmorPresentation.HasSkillAnimation(pawn)
                ? TravelMotion(pawn, profile) : Vector2.zero;
            if (!existing)
            {
                state = new MotionState { Map = pawn.Map, LastTick = now, Core = motion, Armor = motion };
                States[pawn.thingIDNumber] = state;
            }
            else
            {
                int elapsed = now - state!.LastTick;
                state.LastTick = now;
                if (elapsed < 0 || elapsed > StaleAfterTicks)
                {
                    // 游戏刻回退或长时间未更新后恢复当前姿态，不补演过期的制动动作。
                    state.Core = state.Armor = motion;
                    state.CoreVelocity = state.ArmorVelocity = Vector2.zero;
                }
                else
                {
                    // 游戏刻决定进度：暂停不推进，倍速一致，同 tick 多次绘制/发射不重复积分。
                    float seconds = elapsed / 60f;
                    state.Core = Vector2.SmoothDamp(state.Core, motion, ref state.CoreVelocity,
                        CoreResponseSeconds, float.PositiveInfinity, seconds);
                    state.Armor = Vector2.SmoothDamp(state.Armor, motion, ref state.ArmorVelocity,
                        ArmorResponseSeconds, float.PositiveInfinity, seconds);
                }
            }
            state!.Pose = new Pose(state.Core, state.Armor, Mathf.Max(0f, profile.maximumTiltAngle));
        }

        private static Vector2 TravelMotion(Pawn pawn, MechanicalFlightProfileDef profile)
        {
            // 群体飞行乘员读取提供者的直线运动，独立降落时读取自己的路径。
            var member = GroupFlightUtility.Member(pawn);
            Pawn source = member != null && GroupFlightUtility.IsAttached(member)
                ? member.Session?.Provider ?? pawn : pawn;
            if (source.Downed || source.stances?.FullBodyBusy == true
                || !MechanicalFlightStraightPathPatch.TryGetDirectPathDrawData(
                    source, out Vector3 current, out Vector3 destination)) return Vector2.zero;

            Vector2 remaining = new Vector2(destination.x - current.x, destination.z - current.z);
            float distance = remaining.magnitude;
            if (distance <= 0.0001f) return Vector2.zero;
            // 当前直线移动以配置速度匀速前进，最后一 tick 的位移不超过剩余距离。
            float speed = Mathf.Max(0.01f, profile.flightCellsPerSecond);
            return remaining / distance * Mathf.Clamp01(distance * 60f / speed);
        }
    }
}

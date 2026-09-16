using System;
using System.Runtime.CompilerServices;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    internal static class GravityDisorderPresentation
    {
        internal const int RecoveryTicks = 30;
        private const int EntryTicks = RecoveryTicks;
        private const int StruggleStopRemainingTicks = 180;
        private const int StruggleCycleTicks = 240;
        private const float StruggleAngleAmplitude = 18f;
        private const float ShadowRoundSize = 0.75f;
        private const float ShadowLongSize = 1.25f;
        private const float ShadowShortSize = 0.58f;
        private const float ShadowStruggleWeight = 0.25f;

        // 提前驱散后的短暂视觉尾段不延长控制，也不写入存档。
        // 弱键避免离图、切换游戏后静态表现缓存持有旧 Pawn。
        private sealed class RecoveryState
        {
            internal Map Map = null!;
            internal int StartTick;
            internal float StartProgress;
            internal float EntryProgress;
        }

        private static readonly ConditionalWeakTable<Pawn, RecoveryState> Recoveries = new();

        // 原版预绘制可能并行执行，不能用全局 bool 临时伪装所有 Pawn 的姿态。
        [ThreadStatic] internal static Pawn? RenderingPawn;
        [ThreadStatic] private static Pawn? renderingSubject;
        [ThreadStatic] private static int normalRenderDepth;

        internal static Pawn? RenderingSubject => renderingSubject;

        // 查询正常绘制目标时暂时关闭本效果；保留原版及现有飞行/合体补丁。
        internal readonly struct NormalRenderScope : IDisposable
        {
            public void Dispose() => normalRenderDepth--;
        }

        internal static NormalRenderScope EnterNormalRender()
        {
            normalRenderDepth++;
            return new NormalRenderScope();
        }

        internal static float RecoveryProgress(Hediff_GravityDisorder effect)
        {
            var disappears = effect.TryGetComp<HediffComp_Disappears>();
            return disappears == null ? 0f
                : Mathf.Clamp01(1f - disappears.EffectiveTicksToDisappear / (float)RecoveryTicks);
        }

        internal static float RecoveryProgress(Pawn pawn)
        {
            var effect = GravityDisorderUtility.GetEffect(pawn);
            if (effect != null)
                return RecoveryProgress(effect);
            if (!Recoveries.TryGetValue(pawn, out var state) || pawn.Map != state.Map)
                return 1f;
            int elapsed = Find.TickManager.TicksGame - state.StartTick;
            return elapsed < 0 ? 1f
                : Mathf.Clamp01(state.StartProgress + elapsed / (float)RecoveryTicks);
        }

        internal static void BeginRecovery(Hediff_GravityDisorder effect)
        {
            Pawn pawn = effect.pawn;
            ClearRecovery(pawn);
            float progress = RecoveryProgress(effect);
            float entry = Mathf.Clamp01(EntryAge(effect) / (float)EntryTicks);
            if (pawn?.Spawned != true || pawn.Dead || pawn.Destroyed
                || GravityDisorderUtility.IsAffected(pawn) || progress >= 1f || entry <= 0f)
                return;
            Recoveries.Add(pawn, new RecoveryState
            {
                Map = pawn.Map,
                StartTick = Find.TickManager.TicksGame,
                StartProgress = progress,
                EntryProgress = entry
            });
        }

        internal static void ClearRecovery(Pawn? pawn)
        {
            if (pawn != null)
                Recoveries.Remove(pawn);
        }

        private static int EntryAge(Hediff_GravityDisorder effect) =>
            effect.tickAdded >= 0
                ? Mathf.Max(0, Find.TickManager.TicksGame - effect.tickAdded)
                : Mathf.Max(0, effect.ageTicks);

        private static float EntryProgress(Pawn pawn)
        {
            var effect = GravityDisorderUtility.GetEffect(pawn);
            if (effect != null)
                return Mathf.Clamp01(EntryAge(effect) / (float)EntryTicks);
            // 起飞中被驱散时保留已达到的高度/倾角比例，再从该处收尾。
            return Recoveries.TryGetValue(pawn, out var state) && pawn.Map == state.Map
                ? state.EntryProgress : 1f;
        }

        // 返回正常外观的混合权重：生效时 1→0，结束时 0→1。
        // 使用 Hediff 已保存的首次添加时间；合并会重置 ageTicks，但不重置 tickAdded。
        // 因而续时不重播，读档不重新起飞，也不新增存档字段。
        internal static float NormalHeightWeight(Pawn pawn) =>
            1f - Mathf.SmoothStep(0f, 1f, EntryProgress(pawn))
                * (1f - Mathf.SmoothStep(0f, 1f, RecoveryProgress(pawn)));

        private static float NormalPoseWeight(Pawn pawn) =>
            1f - (1f - Mathf.SmoothStep(0f, 1f, (1f - EntryProgress(pawn)) / 0.8f))
                * (1f - Mathf.SmoothStep(0f, 1f, RecoveryProgress(pawn) / 0.8f));

        internal static bool OverridePose(Pawn pawn) =>
            InRenderContext(pawn) && NormalPoseWeight(renderingSubject ?? pawn) < 1f;

        internal readonly struct RenderContext
        {
            internal readonly Pawn? Pawn;
            internal readonly Pawn? Subject;

            internal RenderContext(Pawn? pawn, Pawn? subject)
            {
                Pawn = pawn;
                Subject = subject;
            }
        }

        internal static RenderContext EnterRender(Pawn? pawn)
        {
            var previous = new RenderContext(RenderingPawn, renderingSubject);
            Pawn? subject = ShouldDraw(pawn) ? pawn : null;
            // 合体外观绘制的是未 Spawn 的源机械族，控制状态属于外层的人类主体。
            if (subject == null && pawn != null && renderingSubject != null
                && MechFusionRenderUtility.IsRenderingSourcePawn(pawn))
                subject = renderingSubject;
            renderingSubject = subject;
            RenderingPawn = subject != null ? pawn : null;
            return previous;
        }

        internal static void RestoreRender(RenderContext previous)
        {
            RenderingPawn = previous.Pawn;
            renderingSubject = previous.Subject;
        }

        internal static bool ShouldDraw(Pawn? pawn)
        {
            if (normalRenderDepth > 0 || pawn?.Spawned != true || pawn.Map == null
                || pawn.Dead || pawn.Destroyed || RecoveryProgress(pawn) >= 1f)
                return false;
            // 合体的地图外隐藏帧由原系统管理，不能把隐藏 Pawn 拉回地图。
            return !GameComponent_MechanicalFlightRegistry.TryGetRecord(pawn, out var record)
                || record?.Purpose != MechanicalFlightPurpose.FusionRelocation;
        }

        internal static bool InRenderContext(Pawn pawn) =>
            normalRenderDepth == 0 && ReferenceEquals(RenderingPawn, pawn);

        internal static bool OwnsShadow(Pawn pawn) =>
            ShouldDraw(pawn) || (InRenderContext(pawn) && ShouldDraw(renderingSubject));

        private static float SwayStrength(Pawn pawn) =>
            Mathf.SmoothStep(0f, 1f, EntryProgress(pawn))
                * (1f - Mathf.SmoothStep(0f, 1f, RecoveryProgress(pawn) / 0.5f));

        private static float StruggleAngle(Pawn pawn)
        {
            var effect = GravityDisorderUtility.GetEffect(pawn);
            var disappears = effect?.TryGetComp<HediffComp_Disappears>();
            if (disappears == null)
                return 0f;
            int remaining = disappears.EffectiveTicksToDisappear;
            if (remaining <= StruggleStopRemainingTicks)
                return 0f;

            // 每四秒一个窗口，按 Pawn 和窗口编号错开偏转起点、时长、方向和强度。
            // 纯函数不消耗 Rand、不写逐帧状态，保证并行绘制、选取与读档结果一致。
            int tick = Find.TickManager.TicksGame + pawn.thingIDNumber % StruggleCycleTicks;
            int cycle = tick / StruggleCycleTicks;
            uint seed = unchecked((uint)pawn.thingIDNumber * 747796405u
                ^ (uint)cycle * 2891336453u);
            seed ^= seed >> 16;
            int start = 24 + (int)(seed % 73u);
            int duration = 45 + (int)((seed >> 8) % 22u);
            int elapsed = tick % StruggleCycleTicks - start;
            if (elapsed <= 0 || elapsed >= duration)
                return 0f;

            // 一次动作只向一侧偏转再回位，两端和转折处速度均为零，不快速往复抖动。
            float progress = elapsed / (float)duration;
            float envelope = Mathf.Sin(progress * Mathf.PI);
            envelope *= envelope;
            // 在最后三秒前的 30 tick 渐弱，跨过阈值时平滑回到悬浮角度。
            envelope *= Mathf.SmoothStep(0f, 1f,
                (remaining - StruggleStopRemainingTicks) / 30f);
            envelope *= 0.75f + ((seed >> 20) % 26u) / 100f;
            // 起飞完成后才逐渐接入挣扎，避免升起时突然大角度偏转。
            envelope *= Mathf.SmoothStep(0f, 1f, (EntryAge(effect!) - EntryTicks) / 30f);
            float direction = ((seed >> 16) & 1u) == 0u ? -1f : 1f;
            return direction * StruggleAngleAmplitude * envelope;
        }

        internal static float Height(Pawn pawn) =>
            MechanicalFlightPresentationUtility.VanillaFlightDrawOffset
            + PawnHoverUtility.ExtraHeight(pawn, PawnHoverUtility.DefaultExtraHeight,
                PawnHoverUtility.DefaultBobAmplitude * SwayStrength(pawn),
                PawnHoverUtility.DefaultBobPeriodTicks);

        internal static float RecoveredBodyAngle(PawnRenderer renderer, Pawn pawn,
            PawnRenderFlags flags, float struggleWeight = 1f)
        {
            Pawn subject = InRenderContext(pawn) ? renderingSubject ?? pawn : pawn;
            float baseAngle = subject.thingIDNumber % 2 == 0 ? 70f : 290f;
            float struggleAngle = StruggleAngle(subject);
            float angle = PawnHoverUtility.ExtraHeight(subject, baseAngle,
                5f * SwayStrength(subject), 200f) + struggleAngle * struggleWeight;
            float recovery = NormalPoseWeight(subject);
            if (recovery <= 0f)
                return angle;

            float normalAngle;
            using (EnterNormalRender())
            {
                // 外层人类绘制阴影时，合体外观的恢复目标与源机械族一致（0°）。
                // 不在合体上下文外调用未 Spawn 源 Pawn 的 BodyAngle。
                normalAngle = MechFusionRenderUtility.IsActiveFusionWearer(pawn)
                    ? 0f : renderer.BodyAngle(flags);
            }
            // 身体和阴影共用恢复插值，290° 向 0° 沿最短路径回正。
            return Mathf.LerpAngle(angle, normalAngle, recovery);
        }

        internal static bool TryApplyDrawOffset(Pawn pawn, ref Vector3 drawPos)
        {
            if (!ShouldDraw(pawn))
                return false;

            float recovery = NormalHeightWeight(pawn);
            Vector3 normalPos = drawPos;
            if (recovery > 0f)
            {
                // 重新走完整 DrawPos 链，复用飞行高度、迫降及水平移动平滑。
                // 线程局部作用域阻止本补丁递归叠加高度。
                using (EnterNormalRender())
                    normalPos = pawn.DrawPos;
                // 旧飞行选取上下文会省略额外高度；本效果的选取仍跟随身体。
                if (MechanicalFlightGroundAnchorContext.LegacySelectionActive
                    && !MechanicalFlightGroundAnchorContext.Active
                    && !MechanicalFlightGroundAnchorContext.ShadowCompensationActive
                    && MechanicalFlightUtility.TryGetHoverVisualState(pawn, out var record,
                        out var profile) && record != null && profile != null
                    && record.Phase != MechanicalFlightPhase.Crashing)
                {
                    normalPos.z += MechanicalFlightVisualSmoothing.TotalVisualZOffset(pawn,
                        profile, MechanicalFlightVisualSmoothing.GetHeightFactor(pawn, record))
                        - MechanicalFlightPresentationUtility.VanillaFlightDrawOffset
                        * pawn.flight.PositionOffsetFactor;
                }
            }

            float vanillaFactor = pawn.flight?.PositionOffsetFactor ?? 0f;
            drawPos.z -= MechanicalFlightPresentationUtility.VanillaFlightDrawOffset * vanillaFactor;
            if (MechanicalFlightStraightPathPatch.TryGetExactGroundDrawPos(pawn, out Vector3 exact))
            {
                drawPos.x = exact.x;
                drawPos.z = exact.z;
            }

            if (MechanicalFlightGroundAnchorContext.Active)
            {
                drawPos = Vector3.Lerp(drawPos, normalPos, recovery);
                return true;
            }
            if (MechanicalFlightGroundAnchorContext.ShadowCompensationActive)
            {
                // 原版飞行阴影随后会再减 PositionOffsetFactor。
                drawPos.z += vanillaFactor;
                drawPos = Vector3.Lerp(drawPos, normalPos, recovery);
                return true;
            }

            // 鼠标获取目标也使用这一坐标，不沿用旧飞行的地面选取偏移。
            // 迫降可以搬移地面锚点，但本 Hediff 存在时仍由重力场托举身体。
            drawPos.z += Height(pawn);
            drawPos.y += 0.03658537f * (1f - vanillaFactor);
            drawPos = Vector3.Lerp(drawPos, normalPos, recovery);
            return true;
        }

        internal static Vector3 GroundAnchor(Pawn pawn)
        {
            MechanicalFlightGroundAnchorContext.Begin();
            try
            {
                return pawn.DrawPos;
            }
            finally
            {
                MechanicalFlightGroundAnchorContext.End();
            }
        }

        internal static void DrawShadow(PawnRenderer renderer, Pawn pawn)
        {
            if (!ShouldDraw(pawn) || pawn.IsHiddenFromPlayer()
                || InvisibilityUtility.GetAlpha(pawn) <= 0f)
                return;
            Vector3 anchor = GroundAnchor(pawn);
            anchor.y = AltitudeLayer.Filth.AltitudeFor();
            float angle = RecoveredBodyAngle(renderer, pawn, PawnRenderFlags.None,
                ShadowStruggleWeight);
            float tilt = Mathf.Abs(Mathf.Sin(angle * Mathf.Deg2Rad));
            // 长轴沿身体头脚方向；直立时收圆，横斜时拉长。中心仍使用地面锚点。
            // 临近结束时也收圆，保证倒地/爬行者重新飞行时不遗留椭圆。
            float recovery = NormalHeightWeight(pawn);
            tilt *= 1f - recovery;
            float size = pawn.Flying ? 1f : 1f - recovery;
            Vector3 scale = new Vector3(
                Mathf.Lerp(ShadowRoundSize, ShadowShortSize, tilt) * size,
                1f,
                Mathf.Lerp(ShadowRoundSize, ShadowLongSize, tilt) * size);
            Matrix4x4 matrix = Matrix4x4.TRS(anchor,
                Quaternion.AngleAxis(angle, Vector3.up), scale);
            Graphics.DrawMesh(MeshPool.plane10, matrix, renderer.FlightShadowMaterial, 0);
        }
    }
}

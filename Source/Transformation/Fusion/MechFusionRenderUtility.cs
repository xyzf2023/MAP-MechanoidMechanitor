using System;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 合体渲染替换工具。Pawn 的逻辑实体始终是目标人类；这里只负责
    /// 在人类位置绘制被收纳的真实源机械族，并只保留人类主武器显示。
    /// source 渲染期间使用 ThreadStatic 上下文标记，抑制 source 自身武器与
    /// 服装附加效果；上下文在 finally 中必定清理。使用重入 guard，
    /// 防止绘制源机械族时再次进入合体替换。
    /// </summary>
    internal static class MechFusionRenderUtility
    {
        // 仅缓存当前帧的地图外观，不进入存档，也不让静态缓存持有 Pawn。
        private sealed class SilhouetteFrame
        {
            internal Graphic? Graphic;
            internal Vector3 Position;
            internal Rot4 Rotation;
            internal int Frame = -1;
        }

        private static readonly ConditionalWeakTable<Pawn, SilhouetteFrame>
            SilhouetteFrames = new ConditionalWeakTable<Pawn, SilhouetteFrame>();

        // 每个真实源只生成一次详细诊断；弱键不延长 Pawn 生命周期，也不写入存档。
        private static readonly ConditionalWeakTable<Pawn, object> ReportedRenderFailures = new();

        private static readonly AccessTools.FieldRef<PawnRenderer, Graphic>
            SilhouetteGraphicField =
                AccessTools.FieldRefAccess<PawnRenderer, Graphic>("silhouetteGraphic");

        private static readonly AccessTools.FieldRef<PawnRenderer, Vector3>
            SilhouettePositionField =
                AccessTools.FieldRefAccess<PawnRenderer, Vector3>("silhouettePos");

        private static readonly Func<Thing, Color> GetHighlightColor =
            (Func<Thing, Color>)Delegate.CreateDelegate(
                typeof(Func<Thing, Color>),
                AccessTools.Method(typeof(SilhouetteUtility), "GetColor"));

        private static readonly Func<Color, MaterialPropertyBlock> GetHighlightProperties =
            (Func<Color, MaterialPropertyBlock>)Delegate.CreateDelegate(
                typeof(Func<Color, MaterialPropertyBlock>),
                AccessTools.Method(typeof(SilhouetteUtility), "GetCachedMaterialPropertyBlock"));

        // 原版 PreRenderResults 是私有值类型；一次性生成清理委托，避免逐帧反射或装箱。
        private static readonly Action<PawnRenderer> ClearPreRenderResults =
            CreateClearPreRenderResults();

        private static Action<PawnRenderer> CreateClearPreRenderResults()
        {
            ParameterExpression renderer = Expression.Parameter(typeof(PawnRenderer));
            MemberExpression results = Expression.Field(
                renderer, AccessTools.Field(typeof(PawnRenderer), "results"));
            return Expression.Lambda<Action<PawnRenderer>>(
                Expression.Block(
                    Expression.Assign(results, Expression.Default(results.Type)),
                    Expression.Empty()),
                renderer).Compile();
        }

        [ThreadStatic]
        private static bool renderingSource;

        [ThreadStatic]
        private static Pawn? renderingSourcePawn;

        internal static bool IsRenderingSource => renderingSource;

        internal static bool TryGetFusionSource(
            Pawn? wearer,
            out Pawn? source)
        {
            source = null;
            if (wearer == null || renderingSource)
            {
                return false;
            }

            if (!GameComponent_MechFusionSessionRegistry.TryGetSessionForWearer(
                    wearer,
                    out MechFusionSession? session)
                || session == null
                || !session.IsActive)
            {
                return false;
            }

            source = session.SourcePawn;
            return source != null && !source.Destroyed && !source.Discarded;
        }

        internal static bool IsActiveFusionWearer(Pawn? pawn)
        {
            return TryGetFusionSource(pawn, out _);
        }

        internal static void RefreshTransitionGraphics(Pawn pawn)
        {
            SilhouetteFrames.Remove(pawn);
            PawnRenderer? renderer = pawn.Drawer?.renderer;
            if (renderer != null)
            {
                // 隐藏期间 RenderPawnAt 未消费的预绘制数据不能带到恢复显示的一帧。
                ClearPreRenderResults(renderer);
                renderer.SetAllGraphicsDirty();
            }
        }

        /// <summary>
        /// 当前是否正在把该 Pawn 作为“合体外观的源机械族”绘制。
        /// </summary>
        internal static bool IsRenderingSourcePawn(Pawn? pawn)
        {
            return renderingSource
                && pawn != null
                && ReferenceEquals(renderingSourcePawn, pawn);
        }

        internal static bool TryRenderSourceAt(
            Pawn wearer,
            Vector3 drawLoc,
            Rot4 rotation,
            bool neverAimWeapon)
        {
            if (!TryGetFusionSource(wearer, out Pawn? source)
                || source == null
                || renderingSource)
            {
                return false;
            }

            if (SilhouetteFrames.TryGetValue(wearer, out SilhouetteFrame previousFrame))
            {
                previousFrame.Frame = -1;
            }

            renderingSource = true;
            renderingSourcePawn = source;
            PawnRenderer? renderer = null;
            string stage = "获取源渲染器";
            try
            {
                renderer = source.Drawer?.renderer;
                if (renderer == null)
                {
                    return false;
                }

                // 世界源没有地图预绘制调度，不能复用离图前或异常帧遗留的 results。
                ClearPreRenderResults(renderer);
                stage = "初始化源图形";
                renderer.EnsureGraphicsInitialized();
                stage = "绘制源机械体";
                renderer.RenderPawnAt(drawLoc, rotation, neverAimWeapon);
                stage = "更新合体主体轮廓";
                CompleteWearerRender(wearer, renderer, drawLoc, rotation);
                return true;
            }
            catch (Exception ex)
            {
                if (!ReportedRenderFailures.TryGetValue(source, out _))
                {
                    ReportedRenderFailures.Add(source, new object());
                    Log.ErrorOnce(
                        "[MAP-机械族机械师] 合体源机械族渲染失败："
                        + DescribeRenderFailure(wearer, source, stage, rotation) + "\n" + ex,
                        source.thingIDNumber ^ 0x51F2A3B);
                }
                return false;
            }
            finally
            {
                try
                {
                    // 原版在异常或不绘制的提前返回中可能未清理预绘制结果。
                    if (renderer != null)
                        ClearPreRenderResults(renderer);
                }
                finally
                {
                    renderingSource = false;
                    renderingSourcePawn = null;
                }
            }
        }

        private static string DescribeRenderFailure(Pawn wearer, Pawn source, string stage, Rot4 rotation)
        {
            try
            {
                GameComponent_MechFusionSessionRegistry.TryGetSessionForWearer(wearer, out var session);
                var lifeStage = source.ageTracker?.CurLifeStage;
                // 人类种类不一定定义 PawnKindLifeStage，不为诊断强行访问无关分支。
                var kindLifeStage = source.RaceProps.Humanlike ? null : source.ageTracker?.CurKindLifeStage;
                return $"stage={stage}，source={source.ThingID}，wearer={wearer.ThingID}，"
                    + $"def={source.def?.defName}，kind={source.kindDef?.defName}，"
                    + $"session={session?.SessionId}，state={session?.State}，"
                    + $"faction={source.Faction?.def?.defName ?? "null"}，"
                    + $"originalFaction={session?.OriginalSourceFaction?.def?.defName ?? "null"}，"
                    + $"spawned={source.Spawned}，dead={source.Dead}，"
                    + $"destroyed={source.Destroyed}，discarded={source.Discarded}，"
                    + $"worldPawn={Find.WorldPawns.Contains(source)}，"
                    + $"holder={source.ParentHolder?.GetType().FullName ?? "null"}，"
                    + $"humanlike={source.RaceProps.Humanlike}，rotation={rotation}，"
                    + $"ageTracker={source.ageTracker != null}，lifeStage={lifeStage?.defName ?? "null"}，"
                    + $"lifeSilhouette={lifeStage?.silhouetteGraphicData != null}，"
                    + $"kindLifeStage={kindLifeStage != null}，"
                    + $"kindSilhouette={kindLifeStage?.silhouetteGraphicData != null}。";
            }
            catch (Exception diagnosticException)
            {
                // 诊断属性也可能依赖损坏的 Pawn 状态，不能覆盖最初的渲染异常。
                return $"stage={stage}，详细状态读取失败：{diagnosticException.GetType().Name}。";
            }
        }

        private static void CompleteWearerRender(
            Pawn wearer,
            PawnRenderer sourceRenderer,
            Vector3 drawLoc,
            Rot4 rotation)
        {
            SilhouetteFrame frame = SilhouetteFrames.GetValue(
                wearer, _ => new SilhouetteFrame());
            frame.Graphic = sourceRenderer.SilhouetteGraphic;
            frame.Position = frame.Graphic != null ? sourceRenderer.SilhouettePos : drawLoc;
            frame.Rotation = rotation;

            PawnRenderer wearerRenderer = wearer.Drawer.renderer;
            // DynamicDrawManager 在 DrawSilhouetteJob 之前读取这些字段。
            // 保留人类自己的图形，避免机械族图案进入以人类 Def 为键的原版材质缓存。
            SilhouetteGraphicField(wearerRenderer) =
                wearer.ageTracker.CurLifeStage.silhouetteGraphicData?.Graphic
                ?? BaseContent.BadGraphic;
            SilhouettePositionField(wearerRenderer) = frame.Position;
            ClearPreRenderResults(wearerRenderer);
            frame.Frame = RealTime.frameCount;
        }

        /// <summary>
        /// 只额外绘制人类当前主武器，不绘制任何服装的 DrawWornExtras，
        /// 也不绘制固定合体外甲。武器瞄准角度仍以人类真实 DrawPos、姿态和
        /// 目标计算，绘制位置以合体机械族当前 drawLoc 为基础。
        /// </summary>
        internal static void DrawWearerWeaponOnly(
            Pawn wearer,
            Vector3 drawLoc,
            Rot4 facing,
            bool neverAimWeapon)
        {
            if (wearer == null)
            {
                return;
            }

            ThingWithComps? weapon = wearer.equipment?.Primary;
            if (weapon == null)
            {
                return;
            }

            Job? curJob = wearer.CurJob;
            if (curJob != null && curJob.def?.neverShowWeapon == false)
            {
                Stance_Busy? stanceBusy = wearer.stances?.curStance as Stance_Busy;
                float equipmentDrawDistanceFactor = wearer.ageTracker
                    .CurLifeStage.equipmentDrawDistanceFactor;
                float aimAngle = 0f;
                if (!neverAimWeapon
                    && stanceBusy != null
                    && !stanceBusy.neverAimWeapon
                    && stanceBusy.focusTarg.IsValid)
                {
                    Thing? focusThing = stanceBusy.focusTarg.Thing;
                    Vector3 focus = stanceBusy.focusTarg.HasThing
                        && focusThing != null
                        ? focusThing.DrawPos
                        : stanceBusy.focusTarg.Cell.ToVector3Shifted();
                    if ((focus - wearer.DrawPos).MagnitudeHorizontalSquared()
                        > 0.001f)
                    {
                        aimAngle = (focus - wearer.DrawPos).AngleFlat();
                    }

                    Verb currentEffectiveVerb = wearer.CurrentEffectiveVerb;
                    if (currentEffectiveVerb != null
                        && currentEffectiveVerb.AimAngleOverride.HasValue)
                    {
                        aimAngle = currentEffectiveVerb.AimAngleOverride.Value;
                    }

                    Vector3 aimDrawLoc = drawLoc
                        + new Vector3(
                            0f,
                            0f,
                            0.4f + weapon.def.equippedDistanceOffset)
                            .RotatedBy(aimAngle)
                            * equipmentDrawDistanceFactor;
                    PawnRenderUtility.DrawEquipmentAiming(
                        weapon,
                        aimDrawLoc,
                        aimAngle);
                }
                else if (PawnRenderUtility.CarryWeaponOpenly(wearer))
                {
                    Vector3 carriedDrawLoc = drawLoc.WithYOffset(
                        PawnRenderUtility.AltitudeForLayer(
                            facing == Rot4.North ? -10f : 90f));
                    PawnRenderUtility.DrawCarriedWeapon(
                        weapon,
                        carriedDrawLoc,
                        facing,
                        equipmentDrawDistanceFactor);
                }
            }
        }

        internal static bool TryDrawSourceSilhouette(Pawn wearer)
        {
            if (renderingSource)
            {
                // 已经在绘制源机械族：不再替换，避免递归。
                return false;
            }

            if (!TryGetFusionSource(wearer, out Pawn? source) || source == null)
            {
                return false;
            }

            if (!SilhouetteFrames.TryGetValue(wearer, out SilhouetteFrame frame)
                || frame.Frame != RealTime.frameCount
                || frame.Graphic == null)
            {
                // 只使用本帧成功绘制的数据，读档首帧或绘制失败时不复用旧位置。
                return true;
            }

            // 与原版 ComputeSilhouetteMatricesJob 一致，但尺寸和位置来自当前合体外观。
            Vector2 size = frame.Graphic.drawSize;
            Vector3 scale = Find.CameraDriver.InverseFovScale;
            scale.x *= size.x + SilhouetteUtility.AdjustScale(size.x);
            scale.z *= size.y + SilhouetteUtility.AdjustScale(size.y);
            Matrix4x4 trs = Matrix4x4.TRS(
                frame.Position.SetToAltitude(AltitudeLayer.Silhouettes),
                Quaternion.identity,
                scale);

            // GetColoredVersion 复用原版 GraphicDatabase；不更改源 Pawn 的阵营或朝向。
            Graphic graphic = frame.Graphic.GetColoredVersion(
                ShaderDatabase.Silhouette, Color.white, Color.white);
            bool west = frame.Rotation == Rot4.West;
            Mesh mesh = west
                ? MeshPool.GridPlaneFlip(Vector2.one)
                : MeshPool.GridPlane(Vector2.one);
            Material material = west ? graphic.MatWest : graphic.MatEast;
            MaterialPropertyBlock properties =
                GetHighlightProperties(GetHighlightColor(wearer));
            GenDraw.DrawMeshNowOrLater(mesh, trs, material, false, properties);
            return true;
        }

        internal static void BeginSourceRender(Pawn source)
        {
            renderingSource = true;
            renderingSourcePawn = source;
        }

        internal static void EndSourceRender()
        {
            renderingSource = false;
            renderingSourcePawn = null;
        }
    }
}

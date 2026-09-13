using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    internal enum MechFusionBarKind
    {
        Energy,
        Stability
    }

    /// <summary>
    /// 合体资源条。绘制时直接读取权威合体记录，Gizmo 自身不保存任何数据；
    /// 记录失效时立即消失。
    /// </summary>
    [StaticConstructorOnStartup]
    internal sealed class Gizmo_MechFusionBar : Gizmo
    {
        private static readonly Texture2D EnergyFillTex =
            SolidColorMaterials.NewSolidColorTexture(
                new Color(0.25f, 0.55f, 0.9f));

        private static readonly Texture2D StabilityFillTex =
            SolidColorMaterials.NewSolidColorTexture(
                new Color(0.85f, 0.55f, 0.2f));

        private readonly MechFusionSession session;
        private readonly MechFusionBarKind kind;

        public Gizmo_MechFusionBar(
            MechFusionSession session,
            MechFusionBarKind kind)
        {
            this.session = session;
            this.kind = kind;
            Order = -100f;
        }

        public override float GetWidth(float maxWidth)
        {
            return 150f;
        }

        public override GizmoResult GizmoOnGUI(
            Vector2 topLeft,
            float maxWidth,
            GizmoRenderParms parms)
        {
            if (session == null || !session.IsActive)
            {
                return new GizmoResult(GizmoState.Clear);
            }

            bool isEnergy = kind == MechFusionBarKind.Energy;
            float current = isEnergy
                ? session.CurrentEnergy
                : session.CurrentStability;
            float max = isEnergy ? session.MaxEnergy : session.MaxStability;
            float fill = max > 0f ? Mathf.Clamp01(current / max) : 0f;
            string label = (isEnergy
                    ? "MAP_MechanoidMechanitor.Fusion.Energy.Label"
                    : "MAP_MechanoidMechanitor.Fusion.Stability.Label")
                .Translate();
            string tooltip = isEnergy
                ? "MAP_MechanoidMechanitor.Fusion.Energy.Tooltip".Translate(
                    current,
                    max,
                    MechFusionEnergyUtility.GetGroundConsumptionPercentPerDay(
                        session))
                : BuildStabilityTooltip(session);

            Rect rect = new Rect(
                topLeft.x,
                topLeft.y,
                GetWidth(maxWidth),
                75f);
            Widgets.DrawWindowBackground(rect);
            Rect inner = rect.ContractedBy(6f);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(
                new Rect(inner.x, inner.y, inner.width, 20f),
                label);

            Rect barRect = new Rect(inner.x, inner.y + 22f, inner.width, 16f);
            Widgets.FillableBar(
                barRect,
                fill,
                isEnergy ? EnergyFillTex : StabilityFillTex,
                null,
                doBorder: true);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(barRect, $"{current:0.#} / {max:0.#}");
            TooltipHandler.TipRegion(rect, tooltip);

            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            return new GizmoResult(GizmoState.Clear);
        }

        private static string BuildStabilityTooltip(MechFusionSession session)
        {
            float fraction = session.MaxStability > 0f
                ? Mathf.Clamp01(session.CurrentStability / session.MaxStability)
                : 0f;
            int injuryChance = fraction <= 0.5f
                ? Mathf.Clamp(
                    Mathf.RoundToInt((1f - fraction) * 100f),
                    0,
                    100)
                : 0;
            return "MAP_MechanoidMechanitor.Fusion.Stability.Tooltip".Translate(
                fraction.ToStringPercent(),
                injuryChance);
        }
    }
}

using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>只在地图网格重建时绘制休眠设备，不访问稳定器的动画资源。</summary>
    public sealed class Graphic_UnknownDevice : Graphic_Single
    {
        public override void Print(SectionLayer layer, Thing thing, float extraRotation)
        {
            Vector3 center = thing.DrawPos + DrawOffset(thing.Rotation);
            // 对齐原自绘的 AltInc + 基座微层；禁用原版平面顶部的额外高度偏置。
            center.y += Altitudes.AltInc + 0.008f;
            // 保留独立透明材质及完整 UV，同种设备仍可在同一地图分区合并网格。
            Printer_Plane.PrintPlane(layer, center, drawSize, MatSingle,
                topVerticesAltitudeBias: 0f);
        }

        public override Graphic GetColoredVersion(Shader newShader, Color newColor, Color newColorTwo) =>
            GraphicDatabase.Get<Graphic_UnknownDevice>(path, newShader, drawSize, newColor, newColorTwo, data);
    }
}

using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [StaticConstructorOnStartup]
    internal static class GravityFieldPresentation
    {
        internal static readonly Texture2D Icon =
            ContentFinder<Texture2D>.Get("UI/Commands/DesirePower", false) ?? TexCommand.Install;
        private static readonly Material FieldMaterial =
            MaterialPool.MatFrom("Other/ForceField", ShaderDatabase.MoteGlow);
        private static readonly MaterialPropertyBlock PropertyBlock = new();

        internal static void Draw(Vector3 center, float radius)
        {
            center.y = AltitudeLayer.MoteOverhead.AltitudeFor();
            PropertyBlock.SetColor(ShaderPropertyIDs.Color, new Color(0.55f, 0.7f, 1f, 0.18f));
            // 原版 ForceField 贴图的可见圆半径小于画布，沿用参考护盾的缩放修正。
            float scale = radius * 2f * 1.1601562f;
            Matrix4x4 matrix = Matrix4x4.TRS(center, Quaternion.identity,
                new Vector3(scale, 1f, scale));
            Graphics.DrawMesh(MeshPool.plane10, matrix, FieldMaterial, 0, null, 0, PropertyBlock);
        }
    }
}

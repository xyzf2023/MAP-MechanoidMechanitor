using GD3;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.GD5
{
    // 复用 GD5 头像窗的尺寸、定位和生命周期，避免修改其他通讯窗口的电话叠图。
    internal sealed class GD5VisitorPortraitWindow : GraphicWindow
    {
        private readonly Texture2D portrait;
        private readonly float portraitScale;

        internal GD5VisitorPortraitWindow(string path, float drawSize, float drawOffset,
            float windowWidth, float windowHeight)
            : base(path, drawSize, drawOffset, windowWidth, windowHeight)
        {
            portrait = ContentFinder<Texture2D>.Get(path, false);
            portraitScale = drawSize;
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (portrait == null) return;
            float imageSize = InitialSize.x;
            Rect portraitRect = new Rect((inRect.width - imageSize) / 2f,
                (inRect.height - imageSize) / 2f, imageSize, imageSize);
            Widgets.DrawTextureFitted(portraitRect, portrait, portraitScale);
        }
    }
}

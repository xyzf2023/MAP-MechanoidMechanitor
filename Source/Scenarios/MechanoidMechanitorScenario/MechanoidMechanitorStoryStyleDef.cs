using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class MechanoidMechanitorStoryStyleDef : Def
    {
        public int displayOrder;

        public ThingDef? iconThingDef;

        [NoTranslate]
        public string? iconPath;

        [Unsaved(false)]
        private Texture2D? cachedIconTexture;

        [Unsaved(false)]
        private bool iconTextureLoadAttempted;

        public Texture2D? IconTexture
        {
            get
            {
                if (iconPath.NullOrEmpty())
                {
                    return null;
                }

                if (!iconTextureLoadAttempted)
                {
                    iconTextureLoadAttempted = true;
                    cachedIconTexture = ContentFinder<Texture2D>.Get(iconPath);
                }

                return cachedIconTexture;
            }
        }
    }
}

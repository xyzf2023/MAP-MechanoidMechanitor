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

        public bool opensCustomizePage;

        public MechanoidMechanitorStoryConfigurationPreset? presetConfiguration;

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

        public MechanoidMechanitorStoryConfiguration CreateConfigurationSnapshot()
        {
            if (presetConfiguration != null)
            {
                return presetConfiguration.CreateRuntimeConfiguration();
            }

            return MechanoidMechanitorStoryConfiguration.CreateDefault();
        }
    }
}

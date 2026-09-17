using System.Collections.Generic;
using System.Text;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class HediffCompProperties_ExtraEffectDescription : HediffCompProperties
    {
        public List<string> translationKeys = new List<string>();
        public List<HediffExtraDescriptionProvider> providers =
            new List<HediffExtraDescriptionProvider>();

        public HediffCompProperties_ExtraEffectDescription()
        {
            compClass = typeof(HediffComp_ExtraEffectDescription);
        }
    }

    /// <summary>统一追加效果说明，无 Tick、无持久状态；原版负责与其他提示合并。</summary>
    public sealed class HediffComp_ExtraEffectDescription : HediffComp
    {
        private HediffCompProperties_ExtraEffectDescription Props =>
            (HediffCompProperties_ExtraEffectDescription)props;

        public override string CompTipStringExtra
        {
            get
            {
                if (parent?.pawn == null)
                {
                    return string.Empty;
                }

                StringBuilder builder = new StringBuilder();
                if (Props.providers != null)
                {
                    foreach (HediffExtraDescriptionProvider provider in Props.providers)
                    {
                        if (provider == null)
                        {
                            continue;
                        }

                        foreach (HediffExtraDescriptionEntry entry in provider.GetEntries(parent))
                        {
                            AppendLine(builder, entry?.Translate());
                        }
                    }
                }

                if (Props.translationKeys != null)
                {
                    foreach (string key in Props.translationKeys)
                    {
                        if (!string.IsNullOrWhiteSpace(key))
                        {
                            AppendLine(builder, key.Translate().ToString());
                        }
                    }
                }

                return builder.ToString();
            }
        }

        private static void AppendLine(StringBuilder builder, string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(" - ");
            builder.Append(text!.Trim());
        }
    }
}

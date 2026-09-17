using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>一次提示查询中的说明条目；只保存翻译键与本次查询的参数。</summary>
    public sealed class HediffExtraDescriptionEntry
    {
        public string TranslationKey { get; }
        private readonly NamedArgument[] arguments;

        public HediffExtraDescriptionEntry(string translationKey, params NamedArgument[] arguments)
        {
            TranslationKey = translationKey;
            this.arguments = arguments;
        }

        public string Translate()
        {
            if (string.IsNullOrWhiteSpace(TranslationKey))
            {
                return string.Empty;
            }

            return arguments.Length == 0
                ? TranslationKey.Translate().ToString()
                : TranslationKey.Translate(arguments).ToString();
        }
    }
}

using System;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 窗口实例级文本池选择器；使用独立 Random，不触碰游戏 Rand。
    /// </summary>
    public sealed class MechanoidOvermindDialoguePoolSelector
    {
        private const string FallbackText = "TODO";

        private readonly Random random;

        private MechanoidOvermindDialoguePoolDef? lastPool;

        private int lastIndex = -1;

        public MechanoidOvermindDialoguePoolSelector()
        {
            random = new Random();
        }

        public string PickTranslatedText(MechanoidOvermindDialoguePoolDef? pool)
        {
            if (pool?.textKeys == null || pool.textKeys.Count == 0)
            {
                lastPool = pool;
                lastIndex = -1;
                return FallbackText;
            }

            int validCount = 0;
            for (int i = 0; i < pool.textKeys.Count; i++)
            {
                if (IsSelectableKey(pool.textKeys[i]))
                {
                    validCount++;
                }
            }

            if (validCount == 0)
            {
                lastPool = pool;
                lastIndex = -1;
                return FallbackText;
            }

            int pickOrdinal = 0;
            if (validCount > 1)
            {
                pickOrdinal = random.Next(validCount);
                if (ReferenceEquals(lastPool, pool) && lastIndex >= 0)
                {
                    int lastOrdinal = IndexToOrdinal(pool, lastIndex);
                    if (lastOrdinal >= 0 && pickOrdinal == lastOrdinal)
                    {
                        pickOrdinal = (pickOrdinal + 1 + random.Next(validCount - 1)) % validCount;
                    }
                }
            }

            int sourceIndex = OrdinalToIndex(pool, pickOrdinal);
            if (sourceIndex < 0)
            {
                lastPool = pool;
                lastIndex = -1;
                return FallbackText;
            }

            lastPool = pool;
            lastIndex = sourceIndex;
            return TranslateOrFallback(pool.textKeys[sourceIndex]);
        }

        private static string TranslateOrFallback(string? key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return FallbackText;
            }

            try
            {
                // 原版 Translate 在键缺失时会回退为键名本身；必须先用 CanTranslate 判定。
                if (!key.CanTranslate())
                {
                    return FallbackText;
                }

                string translated = key.Translate();
                if (string.IsNullOrEmpty(translated) || translated == key)
                {
                    return FallbackText;
                }

                return translated;
            }
            catch (Exception)
            {
                return FallbackText;
            }
        }

        private static bool IsSelectableKey(string? key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            try
            {
                return key.CanTranslate();
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static int IndexToOrdinal(MechanoidOvermindDialoguePoolDef pool, int sourceIndex)
        {
            int ordinal = 0;
            for (int i = 0; i < pool.textKeys.Count; i++)
            {
                if (!IsSelectableKey(pool.textKeys[i]))
                {
                    continue;
                }

                if (i == sourceIndex)
                {
                    return ordinal;
                }

                ordinal++;
            }

            return -1;
        }

        private static int OrdinalToIndex(MechanoidOvermindDialoguePoolDef pool, int ordinal)
        {
            int seen = 0;
            for (int i = 0; i < pool.textKeys.Count; i++)
            {
                if (!IsSelectableKey(pool.textKeys[i]))
                {
                    continue;
                }

                if (seen == ordinal)
                {
                    return i;
                }

                seen++;
            }

            return -1;
        }
    }
}

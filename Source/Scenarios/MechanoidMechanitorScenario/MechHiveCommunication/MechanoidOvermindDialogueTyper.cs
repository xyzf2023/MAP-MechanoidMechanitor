using System.Globalization;
using System.Text;
using UnityEngine;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 窗口实例级打字机：解析最终翻译文本，按绝对实时时钟逐字输出。
    /// </summary>
    public sealed class MechanoidOvermindDialogueTyper
    {
        private const float CharIntervalSeconds = 0.03f;

        private const float NewlinePauseSeconds = 0.35f;

        private const float PauseMarkerSeconds = 0.5f;

        private const string PauseMarker = "[[PAUSE]]";

        private string source = string.Empty;

        private readonly StringBuilder visible = new StringBuilder();

        private string visibleCache = string.Empty;

        private int parseIndex;

        private float nextOutputRealtime;

        private bool isComplete = true;

        public string VisibleText => visibleCache;

        public bool IsComplete => isComplete;

        public void Start(string? fullText)
        {
            source = fullText ?? string.Empty;
            visible.Length = 0;
            visibleCache = string.Empty;
            parseIndex = 0;
            isComplete = false;
            nextOutputRealtime = Time.realtimeSinceStartup;

            if (source.Length == 0)
            {
                isComplete = true;
            }
        }

        public void Clear()
        {
            source = string.Empty;
            visible.Length = 0;
            visibleCache = string.Empty;
            parseIndex = 0;
            isComplete = true;
            nextOutputRealtime = Time.realtimeSinceStartup;
        }

        public void Tick()
        {
            if (isComplete)
            {
                return;
            }

            float now = Time.realtimeSinceStartup;
            if (now < nextOutputRealtime)
            {
                return;
            }

            TryAdvanceOneUnit(now);
        }

        private void TryAdvanceOneUnit(float now)
        {
            if (parseIndex >= source.Length)
            {
                isComplete = true;
                return;
            }

            if (MatchesAt(parseIndex, PauseMarker))
            {
                parseIndex += PauseMarker.Length;
                if (parseIndex >= source.Length)
                {
                    // 尾部控制字段：消耗后立即完成，避免无意义等待。
                    isComplete = true;
                    return;
                }

                nextOutputRealtime = now + PauseMarkerSeconds;
                return;
            }

            if (TryConsumeNewline(out int newlineLength))
            {
                visible.Append('\n');
                parseIndex += newlineLength;
                RefreshVisibleCache();
                nextOutputRealtime = now + NewlinePauseSeconds;
                if (parseIndex >= source.Length)
                {
                    isComplete = true;
                }

                return;
            }

            string element = StringInfo.GetNextTextElement(source, parseIndex);
            if (string.IsNullOrEmpty(element))
            {
                isComplete = true;
                return;
            }

            visible.Append(element);
            parseIndex += element.Length;
            RefreshVisibleCache();
            nextOutputRealtime = now + CharIntervalSeconds;
            if (parseIndex >= source.Length)
            {
                isComplete = true;
            }
        }

        private void RefreshVisibleCache()
        {
            visibleCache = visible.ToString();
        }

        private bool TryConsumeNewline(out int consumed)
        {
            char c = source[parseIndex];
            if (c == '\r')
            {
                if (parseIndex + 1 < source.Length && source[parseIndex + 1] == '\n')
                {
                    consumed = 2;
                    return true;
                }

                consumed = 1;
                return true;
            }

            if (c == '\n')
            {
                consumed = 1;
                return true;
            }

            // 翻译文本中的字面 "\n"（反斜杠 + n）
            if (c == '\\'
                && parseIndex + 1 < source.Length
                && source[parseIndex + 1] == 'n')
            {
                consumed = 2;
                return true;
            }

            consumed = 0;
            return false;
        }

        private bool MatchesAt(int index, string marker)
        {
            int markerLength = marker.Length;
            if (index + markerLength > source.Length)
            {
                return false;
            }

            for (int i = 0; i < markerLength; i++)
            {
                if (source[index + i] != marker[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 窗口实例级打字机：解析最终翻译文本，按绝对实时时钟逐字输出；支持安全富文本标签。
    /// </summary>
    public sealed class MechanoidOvermindDialogueTyper
    {
        private const float CharIntervalSeconds = 0.03f;

        private const float NewlinePauseSeconds = 0.35f;

        private const float PauseMarkerSeconds = 0.5f;

        private const string PauseMarker = "[[PAUSE]]";

        private enum UnitKind : byte
        {
            Text,
            Newline,
            Pause,
            OpenTag,
            CloseTag
        }

        private sealed class Unit
        {
            public UnitKind Kind;

            public string Text = string.Empty;

            public string TagRaw = string.Empty;

            public string CloseRaw = string.Empty;
        }

        private readonly List<Unit> units = new List<Unit>();

        private readonly List<string> openCloseStack = new List<string>();

        private readonly StringBuilder visible = new StringBuilder();

        private string visibleCache = string.Empty;

        private int unitIndex;

        private float nextOutputRealtime;

        private bool isComplete = true;

        public string VisibleText => visibleCache;

        public bool IsComplete => isComplete;

        public void Start(string? fullText)
        {
            units.Clear();
            openCloseStack.Clear();
            visible.Length = 0;
            visibleCache = string.Empty;
            unitIndex = 0;
            isComplete = false;
            nextOutputRealtime = Time.realtimeSinceStartup;

            string source = fullText ?? string.Empty;
            if (source.Length == 0)
            {
                isComplete = true;
                return;
            }

            if (!TryBuildUnits(source, units))
            {
                units.Clear();
                BuildPlainUnits(StripAngleMarkup(source), units);
            }

            if (units.Count == 0)
            {
                isComplete = true;
            }
        }

        public void Clear()
        {
            units.Clear();
            openCloseStack.Clear();
            visible.Length = 0;
            visibleCache = string.Empty;
            unitIndex = 0;
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

            // 连续零耗时标签在同一次 Tick 内推进。
            while (!isComplete && now >= nextOutputRealtime)
            {
                if (!TryAdvanceOneUnit(now, out bool consumedTime))
                {
                    break;
                }

                if (consumedTime)
                {
                    break;
                }
            }
        }

        private bool TryAdvanceOneUnit(float now, out bool consumedTime)
        {
            consumedTime = false;
            if (unitIndex >= units.Count)
            {
                isComplete = true;
                return false;
            }

            Unit unit = units[unitIndex++];
            switch (unit.Kind)
            {
                case UnitKind.OpenTag:
                    visible.Append(unit.TagRaw);
                    openCloseStack.Add(unit.CloseRaw);
                    RefreshVisibleCache();
                    if (unitIndex >= units.Count)
                    {
                        isComplete = true;
                    }

                    return true;

                case UnitKind.CloseTag:
                    visible.Append(unit.TagRaw);
                    if (openCloseStack.Count > 0)
                    {
                        openCloseStack.RemoveAt(openCloseStack.Count - 1);
                    }

                    RefreshVisibleCache();
                    if (unitIndex >= units.Count)
                    {
                        isComplete = true;
                    }

                    return true;

                case UnitKind.Pause:
                    if (unitIndex >= units.Count)
                    {
                        // 尾部控制字段：消耗后立即完成，避免无意义等待。
                        isComplete = true;
                        return true;
                    }

                    nextOutputRealtime = now + PauseMarkerSeconds;
                    consumedTime = true;
                    return true;

                case UnitKind.Newline:
                    visible.Append('\n');
                    RefreshVisibleCache();
                    nextOutputRealtime = now + NewlinePauseSeconds;
                    consumedTime = true;
                    if (unitIndex >= units.Count)
                    {
                        isComplete = true;
                    }

                    return true;

                default:
                    visible.Append(unit.Text);
                    RefreshVisibleCache();
                    nextOutputRealtime = now + CharIntervalSeconds;
                    consumedTime = true;
                    if (unitIndex >= units.Count)
                    {
                        isComplete = true;
                    }

                    return true;
            }
        }

        private void RefreshVisibleCache()
        {
            if (openCloseStack.Count == 0)
            {
                visibleCache = visible.ToString();
                return;
            }

            StringBuilder sb = new StringBuilder(visible.Length + openCloseStack.Count * 8);
            sb.Append(visible);
            for (int i = openCloseStack.Count - 1; i >= 0; i--)
            {
                sb.Append(openCloseStack[i]);
            }

            visibleCache = sb.ToString();
        }

        private static bool TryBuildUnits(string source, List<Unit> destination)
        {
            List<string> nest = new List<string>();
            int index = 0;
            while (index < source.Length)
            {
                if (MatchesAt(source, index, PauseMarker))
                {
                    destination.Add(new Unit { Kind = UnitKind.Pause });
                    index += PauseMarker.Length;
                    continue;
                }

                if (TryReadNewline(source, index, out int newlineLength))
                {
                    destination.Add(new Unit { Kind = UnitKind.Newline });
                    index += newlineLength;
                    continue;
                }

                if (source[index] == '<')
                {
                    if (!TryReadTag(source, index, out int tagLength, out bool isClose, out string tagName, out string? colorValue, out string raw))
                    {
                        return false;
                    }

                    if (isClose)
                    {
                        if (nest.Count == 0 || !string.Equals(nest[nest.Count - 1], tagName, StringComparison.Ordinal))
                        {
                            return false;
                        }

                        nest.RemoveAt(nest.Count - 1);
                        destination.Add(
                            new Unit
                            {
                                Kind = UnitKind.CloseTag,
                                TagRaw = raw
                            });
                    }
                    else
                    {
                        string closeRaw = tagName == "color" ? "</color>" : ("</" + tagName + ">");
                        if (tagName == "color" && colorValue == null)
                        {
                            return false;
                        }

                        nest.Add(tagName);
                        destination.Add(
                            new Unit
                            {
                                Kind = UnitKind.OpenTag,
                                TagRaw = raw,
                                CloseRaw = closeRaw
                            });
                    }

                    index += tagLength;
                    continue;
                }

                string element = StringInfo.GetNextTextElement(source, index);
                if (string.IsNullOrEmpty(element))
                {
                    break;
                }

                destination.Add(
                    new Unit
                    {
                        Kind = UnitKind.Text,
                        Text = element
                    });
                index += element.Length;
            }

            return nest.Count == 0;
        }

        private static void BuildPlainUnits(string source, List<Unit> destination)
        {
            int index = 0;
            while (index < source.Length)
            {
                if (MatchesAt(source, index, PauseMarker))
                {
                    destination.Add(new Unit { Kind = UnitKind.Pause });
                    index += PauseMarker.Length;
                    continue;
                }

                if (TryReadNewline(source, index, out int newlineLength))
                {
                    destination.Add(new Unit { Kind = UnitKind.Newline });
                    index += newlineLength;
                    continue;
                }

                string element = StringInfo.GetNextTextElement(source, index);
                if (string.IsNullOrEmpty(element))
                {
                    break;
                }

                destination.Add(
                    new Unit
                    {
                        Kind = UnitKind.Text,
                        Text = element
                    });
                index += element.Length;
            }
        }

        private static string StripAngleMarkup(string source)
        {
            StringBuilder sb = new StringBuilder(source.Length);
            int index = 0;
            while (index < source.Length)
            {
                if (source[index] == '<')
                {
                    int close = source.IndexOf('>', index + 1);
                    if (close < 0)
                    {
                        // 无闭合的尖括号按普通文本保留剩余内容。
                        sb.Append(source, index, source.Length - index);
                        break;
                    }

                    index = close + 1;
                    continue;
                }

                sb.Append(source[index]);
                index++;
            }

            return sb.ToString();
        }

        private static bool TryReadTag(
            string source,
            int index,
            out int length,
            out bool isClose,
            out string tagName,
            out string? colorValue,
            out string raw)
        {
            length = 0;
            isClose = false;
            tagName = string.Empty;
            colorValue = null;
            raw = string.Empty;
            if (index >= source.Length || source[index] != '<')
            {
                return false;
            }

            int close = source.IndexOf('>', index + 1);
            if (close < 0)
            {
                return false;
            }

            raw = source.Substring(index, close - index + 1);
            length = raw.Length;
            string inner = source.Substring(index + 1, close - index - 1).Trim();
            if (inner.Length == 0)
            {
                return false;
            }

            if (inner[0] == '/')
            {
                isClose = true;
                string name = inner.Substring(1).Trim();
                if (!IsSupportedSimpleTag(name))
                {
                    return false;
                }

                tagName = name.ToLowerInvariant();
                return true;
            }

            int eq = inner.IndexOf('=');
            if (eq < 0)
            {
                if (string.Equals(inner, "color", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (!IsSupportedSimpleTag(inner))
                {
                    return false;
                }

                tagName = inner.ToLowerInvariant();
                return true;
            }

            string left = inner.Substring(0, eq).Trim();
            string right = inner.Substring(eq + 1).Trim();
            if (!string.Equals(left, "color", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!IsValidColorValue(right))
            {
                return false;
            }

            tagName = "color";
            colorValue = right;
            return true;
        }

        private static bool IsSupportedSimpleTag(string name)
        {
            return string.Equals(name, "b", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "i", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "color", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsValidColorValue(string value)
        {
            if (string.IsNullOrEmpty(value) || value[0] != '#')
            {
                return false;
            }

            int hexLen = value.Length - 1;
            if (hexLen != 6 && hexLen != 8)
            {
                return false;
            }

            for (int i = 1; i < value.Length; i++)
            {
                char c = value[i];
                bool hex = (c >= '0' && c <= '9')
                    || (c >= 'a' && c <= 'f')
                    || (c >= 'A' && c <= 'F');
                if (!hex)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryReadNewline(string source, int index, out int consumed)
        {
            char c = source[index];
            if (c == '\r')
            {
                if (index + 1 < source.Length && source[index + 1] == '\n')
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

            if (c == '\\'
                && index + 1 < source.Length
                && source[index + 1] == 'n')
            {
                consumed = 2;
                return true;
            }

            consumed = 0;
            return false;
        }

        private static bool MatchesAt(string source, int index, string marker)
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

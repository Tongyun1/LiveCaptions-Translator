using System;
using System.Text;

namespace LiveCaptionsTranslator.Mac.Utils;

/// <summary>
/// 字幕文本的展示辅助。
///
/// 句子边界的判定已交给字幕源（见 <c>CaptionUpdate.IsFinal</c>），这里只做两件
/// 与「怎么显示」有关的小事：过滤无意义内容、以及给显示文本兜底限长。
/// </summary>
public static class CaptionSegmenter
{
    /// <summary>句中停顿，当前段过长时优先在这些位置舍弃开头。</summary>
    private static readonly char[] ClauseBreaks = ",，、;；:：—\n".ToCharArray();

    /// <summary>句末标点（中英日均认，全/半角）。两个字幕源共用这一份定义。</summary>
    private static readonly char[] SentenceEnders = "。．.！!？?".ToCharArray();

    /// <summary>文本（忽略尾部空白后）是否以句末标点结尾。</summary>
    public static bool EndsWithSentenceEnder(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;
        for (int i = text.Length - 1; i >= 0; i--)
        {
            if (char.IsWhiteSpace(text[i]))
                continue;
            return Array.IndexOf(SentenceEnders, text[i]) != -1;
        }
        return false;
    }

    /// <summary>显示用的长度上限（UTF-8 字节）。超过就从开头舍弃，只留最新的那段。</summary>
    private const int DisplayMaxBytes = 220;

    /// <summary>
    /// 是否值得显示/记录。纯标点或纯空白（识别器偶尔会吐出「..」）直接丢掉。
    /// </summary>
    public static bool IsMeaningful(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        foreach (char ch in text)
        {
            if (char.IsLetterOrDigit(ch))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 显示前的限长。仅用于显示；翻译与历史仍用完整文本，不能把上下文剪掉。
    ///
    /// 正常情况下字幕源已按句子边界切分，文本不会很长，这里不会介入；
    /// 只有在边界迟迟不到（说话人长时间不停，或本机不提供停顿信息）时兜底，
    /// 避免字幕框堆成一大段。
    /// </summary>
    public static string ShortenForDisplay(string text)
    {
        if (string.IsNullOrEmpty(text) || Encoding.UTF8.GetByteCount(text) < DisplayMaxBytes)
            return text;

        // 先按句中停顿舍，读起来最自然
        while (Encoding.UTF8.GetByteCount(text) >= DisplayMaxBytes)
        {
            int cut = text.IndexOfAny(ClauseBreaks);
            if (cut < 0 || cut + 1 >= text.Length)
                break;
            text = text[(cut + 1)..].TrimStart();
        }

        // 连逗号都没有的长串（实测中确实出现过），退而按词舍
        while (Encoding.UTF8.GetByteCount(text) >= DisplayMaxBytes)
        {
            int space = text.IndexOf(' ');
            if (space < 0 || space + 1 >= text.Length)
            {
                // 中日文这种不用空格分词的，只能按字舍
                text = text[1..];
                continue;
            }
            text = text[(space + 1)..];
        }

        return text;
    }
}

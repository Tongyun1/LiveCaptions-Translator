using System;
using System.Text;

namespace LiveCaptionsTranslator.Mac.Utils;

/// <summary>
/// 字幕文本的切句与相似度判定。
///
/// 两个识别引擎给出的都是「从任务开始到现在」的累计文本，会越来越长
/// （Whisper 攒到静音或 15 秒，系统识别攒到 45 秒轮换），如果直接拿来显示，
/// 字幕框就会堆积好几句话才翻页；如果每次都记历史，同一句话会留下几十条记录。
///
/// 这里按句末标点切出「当前那一句」，显示、翻译、记历史都只用它。
/// 与 Windows 版 TextUtil / Translator.SyncLoop 的做法一致（独立实现，未引用其代码）。
/// </summary>
public static class CaptionSegmenter
{
    /// <summary>句末标点。中英文都要认。</summary>
    private static readonly char[] SentenceEnders = ".?!。？！".ToCharArray();

    /// <summary>
    /// 当前句短于该字节数时，往前多带上一句。
    /// 否则字幕框里会只剩一个「Yes.」，看不出上下文。
    /// </summary>
    private const int ShortSentenceBytes = 10;

    /// <summary>
    /// 判定两句是否为同一句的相似度门槛。取 0.6 与 Windows 版一致：
    /// 识别器常会回头改前面的词（temp grade → Tim Grate），
    /// 留 40% 的容差才能把这些修正认作同一句。
    /// </summary>
    private const double SameSentenceThreshold = 0.6;

    /// <summary>
    /// 从累计文本里取出最后一句（不含前面已说完的句子）。
    /// </summary>
    public static string LatestSentence(string fullText)
    {
        if (string.IsNullOrWhiteSpace(fullText))
            return string.Empty;

        string text = fullText.Trim();

        // 末尾那个标点属于当前句，找上一句的句末标点时要跳过它
        int searchEnd = IsSentenceEnder(text[^1]) ? text.Length - 1 : text.Length;
        int boundary = LastSentenceEnd(text, searchEnd);

        string latest = text[(boundary + 1)..].Trim();

        if (boundary > 0 && Encoding.UTF8.GetByteCount(latest) < ShortSentenceBytes)
        {
            int previous = LastSentenceEnd(text, boundary);
            latest = text[(previous + 1)..].Trim();
        }

        return latest;
    }

    /// <summary>
    /// 在 text[0..end) 内找最后一个句末标点的下标；没有则返回 -1。
    /// 连续标点（省略号）整体跳过，否则「...」会被切出空片段。
    /// </summary>
    private static int LastSentenceEnd(string text, int end)
    {
        for (int i = end - 1; i >= 0; i--)
        {
            if (!IsSentenceEnder(text[i]))
                continue;
            bool partOfRun = (i > 0 && IsSentenceEnder(text[i - 1]))
                          || (i + 1 < text.Length && IsSentenceEnder(text[i + 1]));
            if (!partOfRun)
                return i;
        }
        return -1;
    }

    private static bool IsSentenceEnder(char ch) => Array.IndexOf(SentenceEnders, ch) != -1;

    /// <summary>句中停顿，当前句过长时优先在这些位置舍弃开头。</summary>
    private static readonly char[] ClauseBreaks = ",，、;；:：—\n".ToCharArray();

    /// <summary>
    /// 显示用的长度上限（UTF-8字节）。超过就从开头舍弃，只留最新的那段。
    /// </summary>
    private const int DisplayMaxBytes = 220;

    /// <summary>
    /// 显示前的限长。仅用于显示；翻译与历史仍用完整句子，不能把上下文剪掉。
    ///
    /// 为什么需要它：切句依赖句末标点，而识别结果并不保证有标点
    /// （系统识别在 macOS 13 以前拿不到 addsPunctuation）。没标点时无处可切，
    /// 字幕框会堆成一大段，所以这里做最后一道兵。
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

    /// <summary>这句话是否已说完（以句末标点结尾）。</summary>
    public static bool IsComplete(string text)
    {
        string trimmed = text?.Trim() ?? string.Empty;
        return trimmed.Length > 0 && IsSentenceEnder(trimmed[^1]);
    }

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
    /// 两段文本是否是同一句话的不同阶段。
    /// 先把两者截到等长——新增的尾巴不参与比较，只看前面那段变了多少。
    /// </summary>
    public static bool IsSameSentence(string newer, string older)
    {
        if (string.IsNullOrEmpty(newer) || string.IsNullOrEmpty(older))
            return false;

        int length = Math.Min(newer.Length, older.Length);
        return Similarity(newer[..length], older[..length]) > SameSentenceThreshold;
    }

    /// <summary>相似度，1.0 为完全一致。前缀关系直接视为同一句。</summary>
    public static double Similarity(string text1, string text2)
    {
        if (text1.StartsWith(text2, StringComparison.Ordinal) ||
            text2.StartsWith(text1, StringComparison.Ordinal))
            return 1.0;

        int maxLength = Math.Max(text1.Length, text2.Length);
        if (maxLength == 0)
            return 1.0;

        return 1.0 - (double)LevenshteinDistance(text1, text2) / maxLength;
    }

    /// <summary>编辑距离。滚动两行数组，空间与较短串成正比。</summary>
    private static int LevenshteinDistance(string text1, string text2)
    {
        if (string.IsNullOrEmpty(text1))
            return text2?.Length ?? 0;
        if (string.IsNullOrEmpty(text2))
            return text1.Length;

        if (text1.Length > text2.Length)
            (text1, text2) = (text2, text1);

        var previous = new int[text1.Length + 1];
        var current = new int[text1.Length + 1];

        for (int i = 0; i <= text1.Length; i++)
            previous[i] = i;

        for (int j = 1; j <= text2.Length; j++)
        {
            current[0] = j;
            for (int i = 1; i <= text1.Length; i++)
            {
                int cost = text1[i - 1] == text2[j - 1] ? 0 : 1;
                current[i] = Math.Min(
                    Math.Min(current[i - 1] + 1, previous[i] + 1),
                    previous[i - 1] + cost);
            }
            (current, previous) = (previous, current);
        }

        return previous[text1.Length];
    }
}

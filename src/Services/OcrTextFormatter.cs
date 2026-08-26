using System.Text;
using System.Text.RegularExpressions;

namespace ZSnaper.Services;

public static partial class OcrTextFormatter
{
    [GeneratedRegex(@"[^\S\r\n]+")]
    private static partial Regex HorizontalWhitespaceRegex();

    [GeneratedRegex(@"(?<=[\u3400-\u4DBF\u4E00-\u9FFF\uF900-\uFAFF])\s+(?=[\u3400-\u4DBF\u4E00-\u9FFF\uF900-\uFAFF])")]
    private static partial Regex CjkInnerWhitespaceRegex();

    [GeneratedRegex(@"(?<=[\u3400-\u4DBF\u4E00-\u9FFF\uF900-\uFAFF])\s+(?=\d)|(?<=\d)\s+(?=[\u3400-\u4DBF\u4E00-\u9FFF\uF900-\uFAFF])")]
    private static partial Regex CjkDigitWhitespaceRegex();

    [GeneratedRegex(@"\s+([，。！？；：、,.!?;:%）》】〕〉」』”’…])")]
    private static partial Regex WhitespaceBeforePunctuationRegex();

    [GeneratedRegex(@"([（《【〔〈「『“‘])\s+")]
    private static partial Regex WhitespaceAfterOpeningPunctuationRegex();

    public static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        string[] lines = normalized.Split('\n');
        var paragraphs = new List<string>();
        var paragraph = new StringBuilder();

        foreach (string rawLine in lines)
        {
            string line = NormalizeLine(rawLine);
            if (line.Length == 0)
            {
                FinishParagraph(paragraphs, paragraph);
                continue;
            }

            if (paragraph.Length > 0 && ShouldInsertJoinSpace(paragraph[^1], line[0]))
            {
                paragraph.Append(' ');
            }

            paragraph.Append(line);
        }

        FinishParagraph(paragraphs, paragraph);
        return string.Join(Environment.NewLine + Environment.NewLine, paragraphs);
    }

    internal static string JoinRecognizedWords(IEnumerable<string> words)
    {
        var line = new StringBuilder();

        foreach (string word in words.Where(word => !string.IsNullOrWhiteSpace(word)))
        {
            string normalizedWord = word.Trim();
            if (line.Length > 0 && ShouldInsertJoinSpace(line[^1], normalizedWord[0]))
            {
                line.Append(' ');
            }

            line.Append(normalizedWord);
        }

        return line.ToString();
    }

    private static string NormalizeLine(string line)
    {
        string normalized = HorizontalWhitespaceRegex().Replace(line.Trim(), " ");
        normalized = CjkInnerWhitespaceRegex().Replace(normalized, string.Empty);
        normalized = CjkDigitWhitespaceRegex().Replace(normalized, string.Empty);
        normalized = WhitespaceBeforePunctuationRegex().Replace(normalized, "$1");
        normalized = WhitespaceAfterOpeningPunctuationRegex().Replace(normalized, "$1");
        return normalized;
    }

    private static void FinishParagraph(List<string> paragraphs, StringBuilder paragraph)
    {
        if (paragraph.Length == 0)
        {
            return;
        }

        paragraphs.Add(paragraph.ToString());
        paragraph.Clear();
    }

    private static bool ShouldInsertJoinSpace(char left, char right)
    {
        if (IsCjk(left) || IsCjk(right))
        {
            return false;
        }

        bool leftCanSeparate = char.IsLetterOrDigit(left) || left is '.' or ',' or '!' or '?' or ';' or ':' or ')' or ']' or '}' or '\'' or '"';
        bool rightIsWord = char.IsLetterOrDigit(right) || right is '(' or '[' or '{' or '\'' or '"';
        return leftCanSeparate && rightIsWord;
    }

    private static bool IsCjk(char value) =>
        value is >= '\u3400' and <= '\u4DBF' or
        >= '\u4E00' and <= '\u9FFF' or
        >= '\uF900' and <= '\uFAFF';
}

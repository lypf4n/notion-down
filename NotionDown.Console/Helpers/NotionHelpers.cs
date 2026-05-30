using System.Text;
using System.Text.RegularExpressions;
using Notion.Client;

namespace NotionDown.Console.Helpers;

/// <summary>
/// 工具函数：ID 解析、文件名清洗、页面标题提取
/// </summary>
public static class NotionHelpers
{
    /// <summary>
    /// 从页面的 Properties 中提取标题文本
    /// </summary>
    public static string GetPageTitle(Page page)
    {
        // 优先找 Title 类型的属性（标准标题字段）
        foreach (var kv in page.Properties)
        {
            if (kv.Value is TitlePropertyValue titleProp)
            {
                var title = RichTextToPlainText(titleProp.Title);
                if (!string.IsNullOrEmpty(title))
                    return title;
            }
        }

        // 备选：找任意富文本属性作为标题
        foreach (var kv in page.Properties)
        {
            if (kv.Value is RichTextPropertyValue rtProp)
            {
                var text = RichTextToPlainText(rtProp.RichText);
                if (!string.IsNullOrEmpty(text))
                    return text;
            }
        }

        return page.Id ?? "untitled";
    }

    /// <summary>
    /// 从用户输入中提取标准的 Notion UUID 格式（8-4-4-4-12）
    /// </summary>
    public static string ExtractNotionId(string input)
    {
        var q = input.IndexOf('?');
        if (q >= 0) input = input[..q];

        var normalized = input.Replace("-", "");

        // Notion URL 格式：<title-slug>-<32-char-hex-id>，取最后 32 位 hex
        if (normalized.Length >= 32)
        {
            var last32 = normalized[^32..];
            if (Regex.IsMatch(last32, @"^[a-fA-F0-9]{32}$"))
            {
                var raw = last32.ToLower();
                return raw[..8] + "-" + raw[8..12] + "-" + raw[12..16] + "-" + raw[16..20] + "-" + raw[20..];
            }
        }

        return input;
    }

    /// <summary>
    /// 将标题转为安全的文件名（替换非法字符）
    /// </summary>
    public static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name);
        for (int i = 0; i < sb.Length; i++)
        {
            if (invalid.Contains(sb[i]) || sb[i] == ':')
                sb[i] = '_';
        }

        var result = sb.ToString().Trim().Trim('.');
        return string.IsNullOrEmpty(result) ? "untitled" : result;
    }

    private static string RichTextToPlainText(IEnumerable<RichTextBase>? richTextList)
    {
        if (richTextList is null) return "";

        var sb = new StringBuilder();
        foreach (var rt in richTextList)
        {
            if (rt is RichTextText text)
                sb.Append(text.Text?.Content ?? "");
            else if (rt is RichTextMention mention)
                sb.Append(MentionToMarkdown(mention));
            else if (rt is RichTextEquation equation)
                sb.Append(equation.Equation?.Expression ?? "");
        }

        return sb.ToString();
    }

    private static string MentionToMarkdown(RichTextMention mention)
    {
        var m = mention.Mention;
        if (m is null) return "@unknown";

        if (m.User is not null)
            return "@" + (m.User.Name ?? m.User.Id ?? "unknown");

        if (m.Page is not null)
            return m.Page.Id ?? "@page";

        if (m.Database is not null)
            return m.Database.Id ?? "@database";

        if (m.Date is not null)
        {
            var start = m.Date.Start?.ToString("yyyy-MM-dd") ?? "";
            return start;
        }

        return "@unknown";
    }
}

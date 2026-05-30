using System.Text;
using Notion.Client;

namespace NotionDown.Console.Converters;

/// <summary>
/// 行内富文本转换：Notion RichText → Markdown 行内格式 / 纯文本
/// </summary>
public static class RichTextConverter
{
    /// <summary>
    /// 将 Notion 富文本列表转为 Markdown 行内格式（加粗/斜体/删除线/代码/链接/提及/公式）
    /// </summary>
    public static string RichTextToMarkdown(IEnumerable<RichTextBase>? richTextList)
    {
        if (richTextList is null) return "";

        var sb = new StringBuilder();
        foreach (var rt in richTextList)
        {
            switch (rt)
            {
                case RichTextText text:
                    sb.Append(ConvertTextElement(text));
                    break;

                case RichTextMention mention:
                    sb.Append(MentionToPlain(mention));
                    break;

                case RichTextEquation equation:
                    sb.Append("$" + (equation.Equation?.Expression ?? "") + "$");
                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// 将富文本转为纯文本（去掉所有格式，只要文字内容）
    /// </summary>
    public static string RichTextToPlainText(IEnumerable<RichTextBase>? richTextList)
    {
        if (richTextList is null) return "";

        var sb = new StringBuilder();
        foreach (var rt in richTextList)
        {
            if (rt is RichTextText text)
                sb.Append(text.Text?.Content ?? "");
            else if (rt is RichTextMention mention)
                sb.Append(MentionToPlain(mention));
            else if (rt is RichTextEquation equation)
                sb.Append(equation.Equation?.Expression ?? "");
        }

        return sb.ToString();
    }

    /// <summary>
    /// 转换单个文本元素，按优先级包裹 Markdown 标记：
    /// 先加格式（粗/斜/删/代码），最后包链接
    /// </summary>
    private static string ConvertTextElement(RichTextText text)
    {
        var content = text.Text?.Content ?? "";
        var link = text.Text?.Link;

        if (text.Annotations.IsBold) content = "**" + content + "**";
        if (text.Annotations.IsItalic) content = "*" + content + "*";
        if (text.Annotations.IsStrikeThrough) content = "~~" + content + "~~";
        if (text.Annotations.IsCode) content = "`" + content + "`";

        if (link?.Url is not null)
            content = "[" + content + "](" + link.Url + ")";

        return content;
    }

    /// <summary>
    /// 将 @提及 转为纯文本（用户/页面/数据库/日期）
    /// </summary>
    private static string MentionToPlain(RichTextMention mention)
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
            return m.Date.Start?.ToString("yyyy-MM-dd") ?? "";

        return "@unknown";
    }
}

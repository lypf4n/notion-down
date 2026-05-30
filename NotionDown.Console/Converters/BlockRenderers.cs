using System.Text;
using Notion.Client;

namespace NotionDown.Console.Converters;

/// <summary>
/// 所有具体 Block 类型的 Markdown 渲染函数。
/// 通过 RegisterAll 将所有渲染器注册到注册表，新增类型只需加方法 + 一行注册。
/// </summary>
public static class BlockRenderers
{
    /// <summary>
    /// 将所有 Block 渲染函数注册到注册表（唯一需要新增/删改的地方）
    /// </summary>
    public static void RegisterAll(BlockRendererRegistry registry)
    {
        registry.Register(BlockType.Paragraph, RenderParagraph);
        registry.Register(BlockType.Heading_1, RenderHeading1);
        registry.Register(BlockType.Heading_2, RenderHeading2);
        registry.Register(BlockType.Heading_3, RenderHeading3);
        registry.Register(BlockType.BulletedListItem, RenderBulletedListItem);
        registry.Register(BlockType.NumberedListItem, RenderNumberedListItem);
        registry.Register(BlockType.ToDo, RenderToDo);
        registry.Register(BlockType.Toggle, RenderToggle);
        registry.Register(BlockType.Code, RenderCode);
        registry.Register(BlockType.Quote, RenderQuote);
        registry.Register(BlockType.Callout, RenderCallout);
        registry.Register(BlockType.Divider, RenderDivider);
        registry.Register(BlockType.Image, RenderImage);
        registry.Register(BlockType.Video, RenderVideo);
        registry.Register(BlockType.File, RenderFile);
        registry.Register(BlockType.PDF, RenderPDF);
        registry.Register(BlockType.Audio, RenderAudio);
        registry.Register(BlockType.Bookmark, RenderBookmark);
        registry.Register(BlockType.ChildPage, RenderChildPage);
        registry.Register(BlockType.ChildDatabase, RenderChildDatabase);
        registry.Register(BlockType.LinkToPage, RenderLinkToPage);
        registry.Register(BlockType.Equation, RenderEquation);
        registry.Register(BlockType.Embed, RenderEmbed);
        registry.Register(BlockType.LinkPreview, RenderLinkPreview);
        registry.Register(BlockType.SyncedBlock, (_, _) => ""); // 跳过同步块
        registry.Register(BlockType.Table, (_, _) => "<!-- table (unsupported) -->\n\n");
        registry.Register(BlockType.TableRow, (_, _) => "");
        registry.Register(BlockType.TableOfContents, (_, _) => "[TOC]\n\n");
        registry.Register(BlockType.Breadcrumb, (_, _) => "");
        registry.Register(BlockType.ColumnList, (_, _) => "");
        registry.Register(BlockType.Column, (_, _) => "");
        registry.Register(BlockType.Template, (_, _) => "");
        registry.Register(BlockType.Unsupported, (_, _) => "<!-- unsupported block -->\n\n");
    }

    // === 段落 ===

    private static string RenderParagraph(IBlock block, int indent)
    {
        if (block is not ParagraphBlock b) return "";
        var text = RichTextConverter.RichTextToMarkdown(b.Paragraph?.RichText);
        return string.IsNullOrEmpty(text) ? "\n" : text + "\n";
    }

    // === 标题 ===

    private static string RenderHeading1(IBlock block, int indent) =>
        RenderHeading((block as HeadingOneBlock)?.Heading_1?.RichText, "#");

    private static string RenderHeading2(IBlock block, int indent) =>
        RenderHeading((block as HeadingTwoBlock)?.Heading_2?.RichText, "##");

    private static string RenderHeading3(IBlock block, int indent) =>
        RenderHeading((block as HeadingThreeBlock)?.Heading_3?.RichText, "###");

    private static string RenderHeading(IEnumerable<RichTextBase>? richText, string prefix)
    {
        if (richText is null) return "";
        var text = RichTextConverter.RichTextToMarkdown(richText);
        return string.IsNullOrEmpty(text) ? "" : prefix + " " + text + "\n\n";
    }

    // === 列表 ===

    private static string RenderBulletedListItem(IBlock block, int indent)
    {
        var text = RichTextConverter.RichTextToMarkdown((block as BulletedListItemBlock)?.BulletedListItem?.RichText);
        return new string(' ', indent * 2) + "- " + text + "\n";
    }

    private static string RenderNumberedListItem(IBlock block, int indent)
    {
        var text = RichTextConverter.RichTextToMarkdown((block as NumberedListItemBlock)?.NumberedListItem?.RichText);
        return new string(' ', indent * 2) + "1. " + text + "\n";
    }

    private static string RenderToDo(IBlock block, int indent)
    {
        if (block is not ToDoBlock b) return "";
        var text = RichTextConverter.RichTextToMarkdown(b.ToDo?.RichText);
        var chk = b.ToDo?.IsChecked == true ? "x" : " ";
        return new string(' ', indent * 2) + "- [" + chk + "] " + text + "\n";
    }

    // === 折叠块 ===

    private static string RenderToggle(IBlock block, int indent)
    {
        var text = RichTextConverter.RichTextToMarkdown((block as ToggleBlock)?.Toggle?.RichText);
        return new string(' ', indent * 2) + "> " + text + "\n";
    }

    // === 代码块 ===

    private static string RenderCode(IBlock block, int indent)
    {
        if (block is not CodeBlock b) return "";
        var code = RichTextConverter.RichTextToPlainText(b.Code?.RichText);
        var lang = b.Code?.Language ?? "";
        return "```" + lang + "\n" + code + "\n```\n\n";
    }

    // === 引用 / 标注 ===

    private static string RenderQuote(IBlock block, int indent)
    {
        if (block is not QuoteBlock b) return "";
        var text = RichTextConverter.RichTextToMarkdown(b.Quote?.RichText);
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder();
        foreach (var line in text.Split('\n'))
            sb.AppendLine("> " + line);
        sb.AppendLine();
        return sb.ToString();
    }

    private static string RenderCallout(IBlock block, int indent)
    {
        if (block is not CalloutBlock b) return "";
        var text = RichTextConverter.RichTextToMarkdown(b.Callout?.RichText);
        if (string.IsNullOrEmpty(text)) return "";
        var emoji = (b.Callout?.Icon as EmojiObject)?.Emoji ?? "";
        var prefix = string.IsNullOrEmpty(emoji) ? "> " : "> " + emoji + " ";
        var sb = new StringBuilder();
        foreach (var line in text.Split('\n'))
            sb.AppendLine(prefix + line);
        sb.AppendLine();
        return sb.ToString();
    }

    // === 分割线 ===

    private static string RenderDivider(IBlock block, int indent) => "---\n\n";

    // === 媒体类（Image/Video/File/PDF/Audio）共用 FileObject 多态分发 ===

    private static string RenderImage(IBlock block, int indent) =>
        RenderFileBasedBlock((block as ImageBlock)?.Image, "");

    private static string RenderVideo(IBlock block, int indent) =>
        RenderFileBasedBlock((block as VideoBlock)?.Video, "[Video]");

    private static string RenderFile(IBlock block, int indent) =>
        RenderFileBasedBlock((block as FileBlock)?.File, "[File]");

    private static string RenderPDF(IBlock block, int indent) =>
        RenderFileBasedBlock((block as PDFBlock)?.PDF, "[PDF]");

    private static string RenderAudio(IBlock block, int indent) =>
        RenderFileBasedBlock((block as AudioBlock)?.Audio, "[Audio]");

    private static string RenderFileBasedBlock(FileObject? file, string label)
    {
        if (file is null) return "";

        string? url = null;
        if (file is ExternalFile ext)
            url = ext.External?.Url;
        else if (file is UploadedFile up)
            url = up.File?.Url;

        if (string.IsNullOrEmpty(url)) return "";

        // 如果已下载到本地，使用本地相对路径（空格编码为 %20，否则 Obsidian 不识别）
        var target = url;
        var ctx = RenderContext.Current;
        if (ctx?.UrlToLocalPath is not null && ctx.UrlToLocalPath.TryGetValue(url, out var localPath))
            target = localPath.Replace(" ", "%20");

        var caption = RichTextConverter.RichTextToPlainText(file.Caption);
        string name;
        if (target != url)
        {
            // 已下载到本地 → 用本地文件名
            name = Path.GetFileName(target);
        }
        else if (!string.IsNullOrEmpty(caption))
        {
            name = caption;
        }
        else
        {
            try { name = Path.GetFileName(new Uri(url).AbsolutePath); }
            catch { name = Path.GetFileName(url); }
        }
        if (string.IsNullOrEmpty(label))
            return "![" + name + "](" + target + ")\n\n";

        return label + " [" + name + "](" + target + ")\n\n";
    }

    // === 书签 ===

    private static string RenderBookmark(IBlock block, int indent)
    {
        if (block is not BookmarkBlock b) return "";
        var caption = RichTextConverter.RichTextToPlainText(b.Bookmark?.Caption);
        var url = b.Bookmark?.Url ?? "";
        var title = string.IsNullOrEmpty(caption) ? url : caption;
        return "[" + title + "](" + url + ")\n\n";
    }

    // === 子页面 / 子数据库 / 页面链接 ===

    private static string RenderChildPage(IBlock block, int indent)
    {
        if (block is not ChildPageBlock b) return "";
        var title = b.ChildPage?.Title ?? "Untitled";
        var target = ResolvePageLink(b.Id, title);
        return "[" + title + "](" + target + ")\n\n";
    }

    private static string RenderChildDatabase(IBlock block, int indent)
    {
        if (block is not ChildDatabaseBlock b) return "";
        var title = b.ChildDatabase?.Title ?? "Untitled Database";
        var target = ResolvePageLink(b.Id, title);
        return "[" + title + "](" + target + ")\n\n";
    }

    private static string RenderLinkToPage(IBlock block, int indent)
    {
        if (block is not LinkToPageBlock b) return "";

        string? targetId = b.LinkToPage switch
        {
            PageParent pp => pp.PageId,
            DatabaseParent dp => dp.DatabaseId,
            BlockParent bp => bp.BlockId,
            _ => null
        };

        if (targetId is null) return "";

        var target = ResolvePageLink(targetId, "Link");
        return "[Link](" + target + ")\n\n";
    }

    /// <summary>
    /// 将 Notion pageId 解析为相对文件路径（优先查 RenderContext，找不到则回退为 pageId）
    /// </summary>
    private static string ResolvePageLink(string pageId, string fallbackTitle)
    {
        var ctx = RenderContext.Current;
        if (ctx?.PageIdToPath is not null && ctx.PageIdToPath.TryGetValue(pageId, out var relativePath))
            return relativePath.Replace(" ", "%20");
        return pageId;
    }

    // === 公式块 ===

    private static string RenderEquation(IBlock block, int indent)
    {
        var expr = (block as EquationBlock)?.Equation?.Expression ?? "";
        return "$$\n" + expr + "\n$$\n\n";
    }

    // === 嵌入 / 链接预览 ===

    private static string RenderEmbed(IBlock block, int indent)
    {
        var url = (block as EmbedBlock)?.Embed?.Url ?? "";
        return "[" + url + "](" + url + ")\n\n";
    }

    private static string RenderLinkPreview(IBlock block, int indent)
    {
        var url = (block as LinkPreviewBlock)?.LinkPreview?.Url ?? "";
        return "[" + url + "](" + url + ")\n\n";
    }
}

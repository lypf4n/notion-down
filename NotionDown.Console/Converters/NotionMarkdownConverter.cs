using System.Collections.Concurrent;
using System.Text;
using Notion.Client;
using NotionDown.Console.Helpers;

namespace NotionDown.Console.Converters;

/// <summary>
/// 核心编排器：拉取 Notion Block → 构建页面树 → 渲染 Markdown → 写文件。
/// 支持递归导出子页面、图片下载。
/// </summary>
public class NotionMarkdownConverter
{
    private readonly INotionClient _client;
    private readonly BlockRendererRegistry _registry;
    private readonly HttpClient _http;
    private readonly HashSet<string> _visitedPageIds = new();

    public NotionMarkdownConverter(INotionClient client, BlockRendererRegistry registry, HttpClient http)
    {
        _client = client;
        _registry = registry;
        _http = http;
    }

    /// <summary>
    /// 将单个 Notion 页面（含所有子页面）递归导出为 Markdown 文件
    /// </summary>
    public async Task ConvertPageAsync(string pageId, string outputDir)
    {
        System.Console.WriteLine($"正在拉取页面...");
        var root = await BuildPageTreeAsync(pageId);
        if (root is null)
        {
            System.Console.Error.WriteLine("未能获取页面内容，请检查：");
            System.Console.Error.WriteLine("  1. Token 是否正确");
            System.Console.Error.WriteLine("  2. 集成是否已连接到该页面（页面右上角 ··· → 连接）");
            return;
        }

        PlanPaths(root, outputDir);
        await ExportTreeAsync(root);
    }

    /// <summary>
    /// 将整个数据库的所有页面批量导出
    /// </summary>
    public async Task ConvertDatabaseAsync(string databaseId, string outputDir)
    {
        string? cursor = null;
        do
        {
            var queryResult = await _client.Databases.QueryAsync(databaseId, new DatabasesQueryParameters
            {
                StartCursor = cursor,
                PageSize = 100
            });

            foreach (var page in queryResult.Results)
            {
                _visitedPageIds.Clear();
                var root = await BuildPageTreeAsync(page.Id);
                if (root is null) continue;

                PlanPaths(root, outputDir);
                await ExportTreeAsync(root);
            }

            cursor = queryResult.HasMore ? queryResult.NextCursor : null;
        } while (cursor != null);
    }

    // =========================================================================
    // 页面树
    // =========================================================================

    private class PageNode
    {
        public string PageId = "";
        public string Title = "untitled";
        public List<IBlock> Blocks = new();
        public List<PageNode> Children = new();

        // 由 PlanPaths 赋值
        public string MdFilePath = "";   // .md 文件完整路径
        public string AssetDir = "";     // 图片/附件 + 子页面存放目录（<name>/，和 .md 并列）
    }

    /// <summary>
    /// 带重试的 API 调用：429/502/503/504 自动退避重试（最多3次）
    /// </summary>
    private async Task<T> RetryApiAsync<T>(Func<Task<T>> call, string description)
    {
        for (int i = 0; i < 3; i++)
        {
            try
            {
                return await call();
            }
            catch (NotionApiException ex) when (
                ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests ||
                ex.StatusCode == System.Net.HttpStatusCode.BadGateway ||
                ex.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable ||
                ex.StatusCode == System.Net.HttpStatusCode.GatewayTimeout)
            {
                if (i == 2) throw;
                var delay = (i + 1) * 2000;
                System.Console.WriteLine($"  ⏳ {description} HTTP {(int)ex.StatusCode}，{delay / 1000}s 后重试 ({i + 1}/3)...");
                await Task.Delay(delay);
            }
        }
        throw new InvalidOperationException(); // unreachable
    }

    /// <summary>
    /// 递归构建页面树，遇到 ChildPage block 则深入拉取
    /// </summary>
    private async Task<PageNode?> BuildPageTreeAsync(string pageId)
    {
        if (!_visitedPageIds.Add(pageId)) return null;

        Page page;
        try
        {
            page = await RetryApiAsync(() => _client.Pages.RetrieveAsync(pageId), $"拉取页面 {pageId[..8]}...");
        }
        catch (NotionApiException ex)
        {
            System.Console.Error.WriteLine($"获取页面失败 ({pageId}): HTTP {(int)ex.StatusCode} {ex.NotionAPIErrorCode}");
            return null;
        }

        var title = NotionHelpers.GetPageTitle(page);
        var blocks = await RetryApiAsync(() => FetchAllBlocksAsync(pageId), $"拉取Block {pageId[..8]}...");

        var node = new PageNode
        {
            PageId = pageId,
            Title = title,
            Blocks = blocks
        };

        // 遇到 ChildPage / ChildDatabase 则递归构建子树
        foreach (var block in blocks)
        {
            string? childId = null;
            if (block is ChildPageBlock childPage)
                childId = childPage.Id;
            else if (block is ChildDatabaseBlock childDb)
                childId = childDb.Id;

            if (childId is not null)
            {
                var childNode = await BuildPageTreeAsync(childId);
                if (childNode is not null)
                    node.Children.Add(childNode);
            }
        }

        return node;
    }

    // =========================================================================
    // 路径规划：仿 Notion 原生导出结构
    // =========================================================================

    /// <summary>
    /// 为根页面分配包装文件夹（标题命名，重名则加(1)(2)），再递归分配子页面路径。
    /// </summary>
    private static void PlanPaths(PageNode node, string outputDir)
    {
        // 根页面外包一层去重文件夹
        var safeTitle = NotionHelpers.SanitizeFileName(node.Title);
        var wrapperDir = DedupPath(outputDir, safeTitle);

        PlanPathsInternal(node, wrapperDir);
    }

    private static void PlanPathsInternal(PageNode node, string parentDir)
    {
        var safeTitle = NotionHelpers.SanitizeFileName(node.Title);

        node.MdFilePath = Path.Combine(parentDir, safeTitle + ".md");
        node.AssetDir = Path.Combine(parentDir, safeTitle);

        foreach (var child in node.Children)
            PlanPathsInternal(child, node.AssetDir);
    }

    private static string DedupPath(string baseDir, string name)
    {
        var path = Path.Combine(baseDir, name);
        if (!Directory.Exists(path)) return path;

        var counter = 1;
        string alt;
        do { alt = Path.Combine(baseDir, $"{name}({counter})"); counter++; }
        while (Directory.Exists(alt));
        return alt;
    }

    // =========================================================================
    // 导出
    // =========================================================================

    /// <summary>
    /// 深度优先导出整棵树：先导出子节点（以便在渲染父节点时知道子节点路径），再渲染父节点
    /// </summary>
    private async Task ExportTreeAsync(PageNode node)
    {
        // 先导出子页面（确定其文件路径）
        foreach (var child in node.Children)
            await ExportTreeAsync(child);

        // 确保目录存在（.md 所在目录 + 图片附件目录）
        var mdDir = Path.GetDirectoryName(node.MdFilePath)!;
        Directory.CreateDirectory(mdDir);
        Directory.CreateDirectory(node.AssetDir);

        // 下载该页面的所有媒体文件
        var urlToLocalPath = await DownloadMediaFilesAsync(node);

        // 构建子页面路径映射（从当前页面 .md 所在目录到子页面 .md 的相对路径）
        var pageIdToPath = new Dictionary<string, string>();
        foreach (var child in node.Children)
        {
            var relative = Path.GetRelativePath(mdDir, child.MdFilePath).Replace('\\', '/');
            pageIdToPath[child.PageId] = relative;
        }

        // 渲染为 Markdown
        var ctx = new RenderContext.Data
        {
            PageIdToPath = pageIdToPath,
            UrlToLocalPath = urlToLocalPath
        };

        RenderContext.Current = ctx;
        try
        {
            var markdown = RenderToMarkdown(node.Title, node.Blocks, _registry);
            await File.WriteAllTextAsync(node.MdFilePath, markdown);
            System.Console.WriteLine($"已导出: {node.MdFilePath}");
        }
        finally
        {
            RenderContext.Current = null;
        }
    }

    // =========================================================================
    // 媒体下载
    // =========================================================================

    /// <summary>
    /// 扫描 Block 列表中找到的所有 Notion 托管文件，并发下载到 node.AssetDir，返回 URL→本地相对路径 映射
    /// </summary>
    private async Task<Dictionary<string, string>> DownloadMediaFilesAsync(PageNode node)
    {
        var map = new Dictionary<string, string>();
        var entries = CollectFileEntries(node.Blocks);

        if (entries.Count == 0)
        {
            System.Console.WriteLine($"  (无媒体文件)");
            return map;
        }

        System.Console.WriteLine($"  发现 {entries.Count} 个媒体文件，开始下载...");

        // 预分配顺序文件名：image_1.png, image_2.png, video_1.mp4 ...
        var counters = new Dictionary<string, int>();
        foreach (var (url, label) in entries)
        {
            var ext = GetExtFromUrl(url);
            var prefix = label switch { "" => "image", _ => label.ToLower() };
            counters.TryGetValue(prefix, out var n);
            n++;
            counters[prefix] = n;
            map[url] = $"{prefix}_{n}{ext}";
        }

        var count = 0;
        var total = entries.Count;
        var failed = 0;
        var okUrls = new ConcurrentBag<string>();
        using var semaphore = new SemaphoreSlim(4);

        var tasks = entries.Select(entry =>
        {
            var (url, _) = entry;
            var destName = map[url];
            return Task.Run(async () =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var localPath = await DownloadToNameAsync(url, node.AssetDir, node.MdFilePath, destName);
                    if (localPath is not null)
                        okUrls.Add(url);
                    else
                        Interlocked.Increment(ref failed);
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref failed);
                    System.Console.Error.WriteLine($"\n  下载失败 ({destName}): {ex.Message}");
                }
                finally
                {
                    var done = Interlocked.Increment(ref count);
                    System.Console.Write($"\r  下载进度: {done}/{total}");
                    semaphore.Release();
                }
            });
        });

        await Task.WhenAll(tasks);
        System.Console.WriteLine($"  完成 (成功 {okUrls.Count}, 失败 {failed})");

        // 只返回成功下载的 URL 映射
        var result = new Dictionary<string, string>();
        var mdDir = Path.GetDirectoryName(node.MdFilePath)!;
        foreach (var url in okUrls)
        {
            var rel = Path.GetRelativePath(mdDir, Path.Combine(node.AssetDir, map[url])).Replace('\\', '/');
            result[url] = rel;
        }
        return result;
    }

    private async Task<string?> DownloadToNameAsync(string url, string assetDir, string mdFilePath, string fileName)
    {
        var response = await _http.GetAsync(url);
        if (!response.IsSuccessStatusCode) return null;

        var bytes = await response.Content.ReadAsByteArrayAsync();
        var destPath = Path.Combine(assetDir, fileName);
        await using var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write);
        await fs.WriteAsync(bytes);

        var mdDir = Path.GetDirectoryName(mdFilePath)!;
        return Path.GetRelativePath(mdDir, destPath).Replace('\\', '/');
    }

    private static string GetExtFromUrl(string url)
    {
        try
        {
            var path = new Uri(url).AbsolutePath;
            var ext = Path.GetExtension(Uri.UnescapeDataString(path));
            if (!string.IsNullOrEmpty(ext)) return ext;
        }
        catch { }
        return ".bin";
    }

    private static List<(string Url, string Label)> CollectFileEntries(List<IBlock> blocks)
    {
        var seen = new HashSet<string>();
        var entries = new List<(string, string)>();

        foreach (var block in blocks)
        {
            var (url, label) = GetFileInfo(block);
            if (url is not null && seen.Add(url))
                entries.Add((url, label));
        }
        return entries;
    }

    private static (string? Url, string Label) GetFileInfo(IBlock block)
    {
        var (file, label) = block switch
        {
            ImageBlock b => (b.Image, ""),
            VideoBlock b => (b.Video, "Video"),
            FileBlock b => (b.File, "File"),
            PDFBlock b => (b.PDF, "PDF"),
            AudioBlock b => (b.Audio, "Audio"),
            _ => ((FileObject?)null, "")
        };

        if (file is UploadedFile up && up.File?.Url is string url)
            return (url, label);

        return (null, "");
    }

    // =========================================================================
    // Block 遍历
    // =========================================================================

    /// <summary>
    /// 分页拉取指定节点下的所有子 Block
    /// </summary>
    public async Task<List<IBlock>> FetchAllBlocksAsync(string blockId)
    {
        var allBlocks = new List<IBlock>();
        string? cursor = null;

        do
        {
            var page = await _client.Blocks.RetrieveChildrenAsync(new BlockRetrieveChildrenRequest
            {
                BlockId = blockId,
                StartCursor = cursor,
                PageSize = 100
            });

            allBlocks.AddRange(page.Results);
            cursor = page.HasMore ? page.NextCursor : null;
        } while (cursor != null);

        return allBlocks;
    }

    /// <summary>
    /// 各 block 渲染为 markdown 字符串后直接拼接返回。空行已在渲染时以 \n 形式产出。
    /// </summary>
    private static string RenderToMarkdown(string? title, List<IBlock> blocks, BlockRendererRegistry registry)
    {
        var raw = new StringBuilder();
        if (!string.IsNullOrEmpty(title))
            raw.Append("# " + title + "\n");
        foreach (var block in blocks)
        {
            var rendered = registry.Render(block, 0);
            if (rendered is not null)
                raw.Append(rendered);
        }
        return raw.ToString();
    }

    // 智能修复：从 API 获取新鲜签名 URL，匹配 .md 中的过期链接
    // =========================================================================

    // =========================================================================
    // 修复模式：扫 .md 中所有图片，按出现顺序编号，有远程 URL 就下载替换
    // =========================================================================

    public async Task RepairMediaAsync(string outputDir)
    {
        var mdFiles = Directory.GetFiles(outputDir, "*.md", SearchOption.AllDirectories);
        System.Console.WriteLine($"扫描到 {mdFiles.Length} 个 .md 文件\n");

        foreach (var mdPath in mdFiles)
        {
            var content = await File.ReadAllTextAsync(mdPath);
            var updated = content;

            // 找出所有图片引用，按文档顺序编号
            var imageMatches = System.Text.RegularExpressions.Regex.Matches(
                content, @"!\[([^\]]*)\]\(([^\)]+)\)");
            if (imageMatches.Count == 0) continue;

            var mdDir = Path.GetDirectoryName(mdPath)!;
            var mdName = Path.GetFileNameWithoutExtension(mdPath);
            var assetDir = Path.Combine(mdDir, mdName);

            var fixedCount = 0;
            var failCount = 0;
            var dirtyAltCount = 0;

            for (int i = 0; i < imageMatches.Count; i++)
            {
                var m = imageMatches[i];
                var alt = m.Groups[1].Value;
                var link = m.Groups[2].Value;
                var fullMatch = m.Value;
                var pos = i + 1;

                // 清理脏 alt
                string cleanAlt = alt;
                if (alt.Contains("?X-Amz-"))
                {
                    cleanAlt = alt.Split('?')[0];
                    dirtyAltCount++;
                }

                // 已经是本地路径，不改链接，只清理 alt
                if (!link.StartsWith("http://") && !link.StartsWith("https://"))
                {
                    var localName = Path.GetFileName(link);
                    if (localName != alt)
                    {
                        updated = updated.Replace(fullMatch, $"![{localName}]({link})");
                        dirtyAltCount++;
                    }
                    continue;
                }

                var ext = GetExtFromUrl(link);
                var fileName = $"image_{pos}{ext}";

                try
                {
                    Directory.CreateDirectory(assetDir);
                    var localPath = await DownloadToNameAsync(link, assetDir, mdPath, fileName);
                    if (localPath is not null)
                    {
                        var encodedPath = localPath.Replace(" ", "%20");
                        var newMd = $"![{fileName}]({encodedPath})";
                        updated = updated.Replace(fullMatch, newMd);
                        fixedCount++;
                        System.Console.WriteLine($"  [{pos}] {link[..Math.Min(60, link.Length)]}... → {fileName} ✓");
                    }
                    else
                    {
                        failCount++;
                        System.Console.WriteLine($"  [{pos}] {fileName} ✗ 下载失败");
                    }
                }
                catch (Exception ex)
                {
                    failCount++;
                    System.Console.WriteLine($"  [{pos}] {fileName} ✗ {ex.Message}");
                }
            }

            if (fixedCount > 0 || dirtyAltCount > 0)
            {
                await File.WriteAllTextAsync(mdPath, updated);
                var parts = new List<string>();
                if (fixedCount > 0) parts.Add($"修复 {fixedCount} 个链接");
                if (failCount > 0) parts.Add($"失败 {failCount}");
                if (dirtyAltCount > 0) parts.Add($"清理 {dirtyAltCount} 个alt");
                System.Console.WriteLine($"📄 {Path.GetFileName(mdPath)}: " + string.Join(", ", parts) + "\n");
            }
        }

        System.Console.WriteLine("修复完成。");
    }

    private static List<string> FindRemoteUrls(string markdown)
    {
        var seen = new HashSet<string>();
        var urls = new List<string>();
        var matches = System.Text.RegularExpressions.Regex.Matches(
            markdown, @"\]\((https?://[^\)]+)\)");
        foreach (System.Text.RegularExpressions.Match m in matches)
        {
            var u = m.Groups[1].Value;
            if (seen.Add(u)) urls.Add(u);
        }
        return urls;
    }

    // =========================================================================
    // 特殊字符修复：# → Sharp
    // =========================================================================

    public async Task FixSpecialCharsAsync(string outputDir)
    {
        // 1. 重命名文件和文件夹：# → Sharp（从深到浅）
        var allEntries = Directory.GetFileSystemEntries(outputDir, "*", SearchOption.AllDirectories);
        foreach (var entry in allEntries.OrderByDescending(e => e.Length))
        {
            var name = Path.GetFileName(entry);
            if (!name.Contains('#')) continue;

            var newName = name.Replace("#", "Sharp");
            var parent = Path.GetDirectoryName(entry)!;
            var newPath = Path.Combine(parent, newName);

            if (entry == newPath) continue;
            if (File.Exists(entry))
                File.Move(entry, newPath);
            else if (Directory.Exists(entry))
                Directory.Move(entry, newPath);
            System.Console.WriteLine($"  重命名: {name} → {newName}");
        }

        // 2. 修复 .md 内容：图片链接的 # → Sharp
        var mdFiles = Directory.GetFiles(outputDir, "*.md", SearchOption.AllDirectories);
        foreach (var mdPath in mdFiles)
        {
            var content = await File.ReadAllTextAsync(mdPath);
            var updated = content;

            // 图片链接中的 # → Sharp（![...](path#...) → ![...](pathSharp...)）
            updated = System.Text.RegularExpressions.Regex.Replace(
                updated, @"\]\(([^\)]*#.*?)\)", m =>
                {
                    var link = m.Groups[1].Value;
                    return "](" + link.Replace("#", "Sharp") + ")";
                });

            // 代码块 ```c# → ```csharp
            updated = updated.Replace("```c#", "```csharp");

            if (updated != content)
            {
                await File.WriteAllTextAsync(mdPath, updated);
                System.Console.WriteLine($"  修复: {Path.GetFileName(mdPath)}");
            }
        }

        System.Console.WriteLine("特殊字符修复完成。");
    }
}

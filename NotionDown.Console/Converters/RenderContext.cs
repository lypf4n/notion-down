namespace NotionDown.Console.Converters;

/// <summary>
/// 渲染上下文：通过 AsyncLocal 传递路径映射，避免修改所有 Renderer 的函数签名。
/// 只有需要上下文信息的 Renderer（ChildPage、媒体类）才读取它。
/// </summary>
public static class RenderContext
{
    private static readonly AsyncLocal<Data?> _current = new();

    public static Data? Current { get => _current.Value; set => _current.Value = value; }

    public class Data
    {
        /// <summary>Notion pageId → 相对文件路径（从当前页面的 .md 所在目录算起）</summary>
        public Dictionary<string, string> PageIdToPath { get; init; } = new();

        /// <summary>Notion 托管文件 URL → 已下载的本地相对路径</summary>
        public Dictionary<string, string> UrlToLocalPath { get; init; } = new();
    }
}

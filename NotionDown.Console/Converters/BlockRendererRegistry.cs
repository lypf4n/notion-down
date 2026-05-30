using Notion.Client;

namespace NotionDown.Console.Converters;

/// <summary>
/// Block → Markdown 渲染委托。每个 Block 类型对应一个无状态的渲染函数。
/// </summary>
public delegate string BlockRenderer(IBlock block, int indentLevel);

/// <summary>
/// Block 渲染注册表 —— 开闭原则的核心。
/// 新增 Block 类型只需调用 Register() 注册新的渲染函数，无需修改注册表本身。
/// </summary>
public class BlockRendererRegistry
{
    private readonly Dictionary<BlockType, BlockRenderer> _renderers = new();

    /// <summary>
    /// 注册一个 Block 类型对应的渲染函数
    /// </summary>
    public void Register(BlockType type, BlockRenderer renderer)
    {
        _renderers[type] = renderer;
    }

    /// <summary>
    /// 根据 Block 类型分发到对应渲染函数
    /// </summary>
    public string Render(IBlock block, int indentLevel = 0)
    {
        if (_renderers.TryGetValue(block.Type, out var renderer))
            return renderer(block, indentLevel);

        return ""; // 未注册的 Block 类型静默跳过
    }
}

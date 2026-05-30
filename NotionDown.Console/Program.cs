using Microsoft.Extensions.Configuration;
using Notion.Client;
using NotionDown.Console.Converters;
using NotionDown.Console.Helpers;

// ================================================================
// === Notion → Markdown 导出工具 ==================================
// ================================================================

var env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Development";
var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile($"appsettings.{env}.json", optional: true)
    .Build();

var token = config["Notion:Token"];

if (args.Length == 0)
{
    Console.Error.WriteLine("用法:");
    Console.Error.WriteLine("  导出: NotionDown.Console <page-id|page-url> [output-dir]");
    Console.Error.WriteLine("  修复: NotionDown.Console --repair [output-dir]");
    Console.Error.WriteLine("  特殊: NotionDown.Console --fix [output-dir]");
    return 1;
}

if (string.IsNullOrEmpty(token))
{
    Console.Error.WriteLine("未配置 Token，请编辑 appsettings.json 设置 Notion.Token。");
    return 1;
}

// --- 修复模式：扫 .md 中的远程图片链接，按位置编号下载替换 ---
if (args[0] == "--repair")
{
    var repairDir = args.Length > 1 ? args[1] : config["Export:OutputDir"] ?? "./notion-output";

    using var repairHttp = CreateHttpClient();
    var repairRegistry = new BlockRendererRegistry();
    BlockRenderers.RegisterAll(repairRegistry);
    var repairClient = NotionClientFactory.Create(new ClientOptions { AuthToken = "" });
    var repairConverter = new NotionMarkdownConverter(repairClient, repairRegistry, repairHttp);
    await repairConverter.RepairMediaAsync(repairDir);
    return 0;
}

// --- 特殊字符修复：文件夹/文件名 # → Sharp，图片链接 # → Sharp，```c# → ```csharp ---
if (args[0] == "--fix")
{
    var fixDir = args.Length > 1 ? args[1] : config["Export:OutputDir"] ?? "./notion-output";
    var fixer = new NotionMarkdownConverter(
        NotionClientFactory.Create(new ClientOptions { AuthToken = "" }),
        new BlockRendererRegistry(),
        CreateHttpClient());
    await fixer.FixSpecialCharsAsync(fixDir);
    return 0;
}

// --- 导出模式 ---
var inputId = NotionHelpers.ExtractNotionId(args[0]);
Console.WriteLine($"解析的页面ID: {inputId}");
var exportDir = args.Length > 1 ? args[1] : config["Export:OutputDir"] ?? "./notion-output";
Directory.CreateDirectory(exportDir);

using var httpClient = CreateHttpClient();
var notionClient = NotionClientFactory.Create(new ClientOptions { AuthToken = token });

var blockRegistry = new BlockRendererRegistry();
BlockRenderers.RegisterAll(blockRegistry);

var conv = new NotionMarkdownConverter(notionClient, blockRegistry, httpClient);

try
{
    await conv.ConvertPageAsync(inputId, exportDir);
}
catch (NotionApiException ex) when (ex.NotionAPIErrorCode == NotionAPIErrorCode.ObjectNotFound)
{
    try
    {
        await conv.ConvertDatabaseAsync(inputId, exportDir);
    }
    catch (NotionApiException dbEx)
    {
        Console.Error.WriteLine($"找不到页面或数据库: {inputId}");
        Console.Error.WriteLine($"错误 (HTTP {(int)dbEx.StatusCode}): {dbEx.NotionAPIErrorCode} - {dbEx.Message}");
        return 1;
    }
}
catch (NotionApiException ex)
{
    Console.Error.WriteLine($"Notion API 错误 (HTTP {(int)ex.StatusCode}): {ex.NotionAPIErrorCode}");
    Console.Error.WriteLine($"{ex.Message}");
    return 1;
}

Console.WriteLine("完成。");
return 0;

static HttpClient CreateHttpClient()
{
    var handler = new HttpClientHandler
    {
        UseProxy = true,
        SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13
    };
    return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
}

using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// NCMapper 文档离线包静态服务器
//
// 用途：内网/离网环境开箱即用的文档站点服务器（无网络全功能浏览，含站内搜索）。
// 实现：ASP.NET Core 共享框架（FrameworkReference，编译期引用）静态文件中间件 +
//       NativeAOT self-contained 单文件发布——零额外依赖、零运行时。
// 行为：
//   * 监听 http://0.0.0.0:8080（可用命令行参数 / DOCS_SERVER_PORT 环境变量覆盖端口）
//   * 站点根 = exe 所在目录下 site/（或上级目录 site/，兼容打包两种布局）
//   * 默认文件 index.html；路径穿越防护由 ASP.NET 静态文件中间件内置
// 用法：DocSiteServer [端口]

int port = ResolvePort(args);

// 站点根定位：单文件 AOT 下 AppContext.BaseDirectory = exe 所在目录（.NET 6+ 不临时解压）。
// 兼容两种打包布局：server/site/（exe 同目录）与 上级目录 site/（zip 根 site/ 与 server/ 平级）。
string siteRoot = Path.Combine(AppContext.BaseDirectory, "site");
if (!Directory.Exists(siteRoot))
{
    siteRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "site"));
}
if (!Directory.Exists(siteRoot))
{
    Console.Error.WriteLine($"[ERROR] 站点目录缺失：{siteRoot}");
    Console.Error.WriteLine("        离线包结构应为：server/（本程序）+ site/（站点）+ start 脚本 + README");
    return 2;
}

var builder = WebApplication.CreateSlimBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options => options.SingleLine = true);
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

var app = builder.Build();

// 默认文件（index.html）与静态文件中间件共享同一文件提供器（站点根 = site/）
var fileProvider = new PhysicalFileProvider(siteRoot);
app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider });

// 启动信息（供内网客户直接复制访问地址）
Console.WriteLine("────────────────────────────────────────────");
Console.WriteLine($" NCMapper 文档站点已启动（站点根：{siteRoot}）");
Console.WriteLine($" 本机访问   : http://localhost:{port}");
foreach (var ip in GetLocalIPv4Addresses())
{
    Console.WriteLine($" 局域网访问 : http://{ip}:{port}");
}
Console.WriteLine(" 按 Ctrl+C 停止");
Console.WriteLine("────────────────────────────────────────────");

await app.RunAsync();
return 0;

/// <summary>
/// 解析监听端口：命令行参数优先，其次 DOCS_SERVER_PORT 环境变量，默认 8080。
/// </summary>
/// <param name="args">命令行参数（首个参数为端口号）</param>
/// <returns>合法端口号（1-65535）</returns>
static int ResolvePort(string[] args)
{
    if (args.Length > 0 && int.TryParse(args[0], out int p) && p is >= 1 and <= 65535)
    {
        return p;
    }
    if (int.TryParse(Environment.GetEnvironmentVariable("DOCS_SERVER_PORT"), out int pe) && pe is >= 1 and <= 65535)
    {
        return pe;
    }
    return 8080;
}

/// <summary>
/// 获取本机 IPv4 局域网地址列表（用于启动日志打印可访问 URL）。
/// </summary>
/// <returns>IPv4 地址字符串列表；主机名解析失败时返回空列表（不影响服务启动）</returns>
static List<string> GetLocalIPv4Addresses()
{
    var result = new List<string>();
    try
    {
        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var addr in host.AddressList)
        {
            if (addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                result.Add(addr.ToString());
            }
        }
    }
    catch
    {
        // 主机名解析失败不阻塞服务启动，仅少打印局域网地址
    }
    return result;
}

using ComputerUse.Mcp.Abstractions;
using ComputerUse.Mcp.Capture;
using ComputerUse.Mcp.Coordination;
using ComputerUse.Mcp.Domain;
using ComputerUse.Mcp.Identity;
using ComputerUse.Mcp.Input;
using ComputerUse.Mcp.Mcp;
using ComputerUse.Mcp.Memory;
using ComputerUse.Mcp.Native;
using ComputerUse.Mcp.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

if (args.Length > 0 && args[0] == PrintWindowHelper.Argument)
    return PrintWindowHelper.Run(args);

NativeMethods.SetProcessDpiAwarenessContext(NativeMethods.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});
builder.Services.Configure<ConsoleLifetimeOptions>(options => options.SuppressStatusMessages = true);

builder.Services.AddSingleton(Limits.V1);
builder.Services.AddSingleton<NativeStaDispatcher>();
builder.Services.AddSingleton<IWindowQuery, Win32WindowQuery>();
builder.Services.AddSingleton<IMonitorQuery, Win32MonitorQuery>();
builder.Services.AddSingleton<IProcessQuery, Win32ProcessQuery>();
builder.Services.AddSingleton<IVirtualDesktopMembership, VirtualDesktopMembership>();
builder.Services.AddSingleton<ISessionGuard, Win32SessionGuard>();
builder.Services.AddSingleton<IWindowActivator, Win32WindowActivator>();
builder.Services.AddSingleton<IHitTester, Win32HitTester>();
builder.Services.AddSingleton<IInputInjector, SendInputAdapter>();
builder.Services.AddSingleton<IClipboardWorker, ClipboardWorker>();
builder.Services.AddSingleton<ICapturePipeline, CapturePipeline>();
builder.Services.AddSingleton<TargetTokenService>();
builder.Services.AddSingleton<FrameCache>();
builder.Services.AddSingleton<DesktopOperationCoordinator>();
builder.Services.AddSingleton<OperationIdCache>();
builder.Services.AddSingleton<IHostProcessResolver, HostProcessResolver>();
builder.Services.AddSingleton<WindowListService>();
builder.Services.AddSingleton<ScreenshotService>();
builder.Services.AddSingleton<OperateService>();
builder.Services.AddSingleton(sp =>
    new MemoryCatalog(MemoryCatalog.DefaultRootDirectory, sp.GetRequiredService<Limits>()));
builder.Services.AddSingleton<RememberService>();
builder.Services.AddSingleton<ObserveService>();
builder.Services.AddSingleton<ClickControlService>();

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new Implementation
        {
            Name = Contract.ServerName,
            Version = Contract.ServerVersion,
            Title = "Computer Use"
        };
    })
    .WithStdioServerTransport()
    .WithTools<ComputerUseTools>();

using var host = builder.Build();
await host.RunAsync();
return 0;

// Author: Rene Pally

using FileMonitor.Service.Driver;
using FileMonitor.Service.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure as Windows Service
builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "FileMonitorService";
});

// Configure Kestrel for gRPC (HTTP/2)
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(50051, listenOptions =>
    {
        listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2;
    });
});

// Register core services
builder.Services.AddSingleton<DriverCommunication>();
builder.Services.AddSingleton<EventBroadcaster>();
builder.Services.AddHostedService<DriverListenerWorker>();
builder.Services.AddGrpc();

var app = builder.Build();

app.MapGrpcService<FileMonitorGrpcService>();

await app.RunAsync();

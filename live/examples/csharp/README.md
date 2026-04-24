# FileMonitor — C# Client Guide

Connect to a running `FileMonitor.exe` instance from any .NET 8+ application using the **FileMonitor.Client** SDK.

---

## Prerequisites

| Requirement | Details |
|---|---|
| `FileMonitor.exe` | Must be running as Administrator before you connect |
| .NET | 8.0 or later |
| `FileMonitor.Client.dll` | From the release zip — see [Getting the SDK](#getting-the-sdk) |

---

## Getting the SDK

Download the latest release zip from the [Releases](https://github.com/jrpally/drivermonitoring/releases) page and extract it. The SDK is inside the `client\` folder:

```
FileMonitor-v1.0.0-win-x64\
└── client\
    ├── FileMonitor.Client.dll
    └── FileMonitor.Client.deps.json
```

---

## Adding the SDK to your project

### Option A — copy the DLL (recommended)

1. Copy `FileMonitor.Client.dll` and `FileMonitor.Client.deps.json` into your project, e.g. into a `libs\` folder.
2. Add a reference in your `.csproj`:

```xml
<ItemGroup>
  <Reference Include="FileMonitor.Client">
    <HintPath>libs\FileMonitor.Client.dll</HintPath>
  </Reference>
</ItemGroup>
```

### Option B — project reference (repository clone)

```xml
<ItemGroup>
  <ProjectReference Include="..\path\to\src\FileMonitor.Client\FileMonitor.Client.csproj" />
</ItemGroup>
```

> The SDK targets `net8.0`. Your project must also target `net8.0` or later.

---

## Quick start

```csharp
using FileMonitor.Client;

// Start FileMonitor.exe as Admin first, then connect:
using var client = new FileMonitorClient("http://localhost:50051");

client.OnFileEvent += evt =>
{
    Console.WriteLine($"[{evt.EventType,-10}] {evt.FilePath}  (PID {evt.ProcessId} — {evt.ProcessName})");
};

client.OnDisconnected += ex =>
{
    if (ex != null) Console.Error.WriteLine($"Disconnected: {ex.Message}");
};

// Begin receiving events (non-blocking — runs on a background Task)
client.StartSubscription();

Console.WriteLine("Listening. Press Enter to quit.");
Console.ReadLine();
```

---

## Filtering events

`StartSubscription` accepts two optional filters:

```csharp
using FileMonitor.Grpc;

client.StartSubscription(
    eventFilter: (uint)(FileEventType.FileEventWrite | FileEventType.FileEventDelete),
    pathFilter:  @"\Device\HarddiskVolume3\Users\Alice"
);
```

- **`eventFilter`** — bitmask of `FileEventType` values. Pass `0` (the default) to receive all events.
- **`pathFilter`** — NT path prefix. Only events whose path starts with this string are delivered. Pass `""` to receive events for all paths.

### Event type reference

| Constant | Value | Triggered when |
|---|---|---|
| `FileEventCreate`  | 1   | A file is created or opened |
| `FileEventClose`   | 2   | A file handle is closed |
| `FileEventRead`    | 4   | Data is read from a file |
| `FileEventWrite`   | 8   | Data is written to a file |
| `FileEventDelete`  | 16  | A file is deleted |
| `FileEventRename`  | 32  | A file is renamed or moved |
| `FileEventSetInfo` | 64  | File metadata (size, attributes, times) is changed |
| `FileEventCleanup` | 128 | The last handle to a file is closed |

---

## Controlling monitoring at runtime

You can pause and resume the kernel driver without stopping the service or disconnecting clients:

```csharp
await client.StopMonitoringAsync();   // pause — driver stops intercepting I/O
await client.StartMonitoringAsync();  // resume
```

---

## Querying service status

```csharp
var status = await client.GetStatusAsync();

Console.WriteLine($"Driver connected : {status.IsDriverConnected}");
Console.WriteLine($"Monitoring active: {status.IsMonitoring}");
Console.WriteLine($"Events processed : {status.EventsProcessed}");
Console.WriteLine($"Active clients   : {status.ActiveSubscribers}");
```

---

## FileEvent fields

Each event delivered to `OnFileEvent` has the following fields:

| Field | Type | Description |
|---|---|---|
| `EventType` | `FileEventType` | The operation that was intercepted |
| `FilePath` | `string` | Full NT path, e.g. `\Device\HarddiskVolume3\Users\...` |
| `ProcessId` | `uint` | PID of the process that triggered the event |
| `ThreadId` | `uint` | Thread ID |
| `Timestamp` | `long` | UTC timestamp in .NET ticks (100 ns intervals since 0001-01-01) |
| `ProcessName` | `string` | Process image name resolved by the service |

Convert `Timestamp` to a `DateTimeOffset`:

```csharp
var time = new DateTimeOffset(evt.Timestamp, TimeSpan.Zero);
Console.WriteLine(time.ToString("HH:mm:ss.fff"));
```

---

## Running the included example

```cmd
cd live\examples\csharp
dotnet run
```

The example subscribes to all events and prints them with colour coding. Press `S` to stop monitoring, `R` to resume, `Ctrl+C` to exit.

---

## gRPC service definition

The full wire protocol is defined in [proto/file_monitor.proto](../../../proto/file_monitor.proto).
If you prefer to generate your own stubs instead of using this SDK, see the [Python guide](../python/README.md) for an example of using `protoc` directly.

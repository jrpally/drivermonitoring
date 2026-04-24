# FileMonitor — Python Client Guide

Connect to a running `FileMonitor.exe` instance from Python using **grpcio**.

---

## Prerequisites

| Requirement | Details |
|---|---|
| `FileMonitor.exe` | Must be running as Administrator before you connect |
| Python | 3.8 or later |
| pip packages | `grpcio`, `grpcio-tools` |

---

## Install dependencies

```cmd
pip install grpcio grpcio-tools
```

---

## Generate Python stubs (run once)

The stubs are generated from the proto file that ships with the repository (or the release zip under `proto\file_monitor.proto`). A helper batch file is provided:

```cmd
cd live\examples\python
generate_stubs.bat
```

This runs:

```cmd
python -m grpc_tools.protoc -I ..\..\..\proto ^
    --python_out=. ^
    --grpc_python_out=. ^
    ..\..\..\proto\file_monitor.proto
```

It creates two files alongside `client.py`:

```
live\examples\python\
├── file_monitor_pb2.py       ← message classes
├── file_monitor_pb2_grpc.py  ← stub + servicer classes
└── client.py                 ← example client (this guide)
```

---

## Quick start

```python
import grpc
import file_monitor_pb2
import file_monitor_pb2_grpc

# Start FileMonitor.exe as Admin first, then connect:
with grpc.insecure_channel("localhost:50051") as channel:
    stub = file_monitor_pb2_grpc.FileMonitorServiceStub(channel)

    # Check status before subscribing
    status = stub.GetStatus(file_monitor_pb2.StatusRequest())
    print(f"Driver connected : {status.is_driver_connected}")
    print(f"Monitoring active: {status.is_monitoring}")
    print(f"Events processed : {status.events_processed}")
    print()

    # Subscribe — server-streaming RPC (blocks until the server closes the stream)
    request = file_monitor_pb2.SubscribeRequest(
        event_filter=0,  # 0 = all events; see bitmask table below
        path_filter="",  # "" = no path filter
    )
    for event in stub.Subscribe(request):
        print(f"[{event.event_type}] {event.file_path}  PID={event.process_id}")
```

---

## Filtering events

Both parameters on `SubscribeRequest` are optional.

### event_filter — bitmask

| Name | Value | Triggered when |
|---|---|---|
| `FILE_EVENT_CREATE`   | 1   | A file is created or opened |
| `FILE_EVENT_CLOSE`    | 2   | A file handle is closed |
| `FILE_EVENT_READ`     | 4   | Data is read from a file |
| `FILE_EVENT_WRITE`    | 8   | Data is written to a file |
| `FILE_EVENT_DELETE`   | 16  | A file is deleted |
| `FILE_EVENT_RENAME`   | 32  | A file is renamed or moved |
| `FILE_EVENT_SET_INFO` | 64  | File metadata is changed |
| `FILE_EVENT_CLEANUP`  | 128 | The last handle to a file is closed |

Combine with bitwise OR:

```python
WRITE_OR_DELETE = 8 | 16  # == 24

request = file_monitor_pb2.SubscribeRequest(
    event_filter=WRITE_OR_DELETE,
    path_filter=r"\Device\HarddiskVolume3\Users\Alice",
)
```

### path_filter

An NT path prefix string. Only events whose `file_path` starts with this value are delivered. Leave empty (`""`) to receive events for all paths.

---

## FileEvent fields

Each message received from `Subscribe` has these fields:

| Field | Type | Description |
|---|---|---|
| `event_type` | `int` | Event type value (see bitmask table above) |
| `file_path` | `str` | Full NT path, e.g. `\Device\HarddiskVolume3\Users\...` |
| `process_id` | `int` | PID of the process that triggered the event |
| `thread_id` | `int` | Thread ID |
| `timestamp` | `int` | UTC timestamp in .NET ticks — see conversion below |
| `process_name` | `str` | Process image name resolved by the service |

### Converting timestamp to datetime

`timestamp` uses .NET ticks: 100-nanosecond intervals since **0001-01-01 UTC**.

```python
from datetime import datetime, timezone

TICKS_PER_SECOND  = 10_000_000
EPOCH_TICKS       = 621_355_968_000_000_000  # ticks from 0001-01-01 to Unix epoch

def ticks_to_datetime(ticks: int) -> datetime:
    unix_seconds = (ticks - EPOCH_TICKS) / TICKS_PER_SECOND
    return datetime.fromtimestamp(unix_seconds, tz=timezone.utc)
```

Usage:

```python
for event in stub.Subscribe(request):
    ts = ticks_to_datetime(event.timestamp)
    print(f"{ts:%H:%M:%S.%f}  [{event.event_type}]  {event.file_path}")
```

---

## Controlling monitoring at runtime

```python
# Pause the driver (stops intercepting I/O)
stub.StopMonitoring(file_monitor_pb2.MonitoringRequest())

# Resume
stub.StartMonitoring(file_monitor_pb2.MonitoringRequest())
```

---

## Running the included example

```cmd
cd live\examples\python
python client.py
```

Press `Ctrl+C` to stop.

---

## gRPC service definition

The full wire protocol is defined in [proto/file_monitor.proto](../../../proto/file_monitor.proto).

Available RPCs:

| RPC | Type | Description |
|---|---|---|
| `Subscribe` | Server streaming | Receive a continuous stream of `FileEvent` messages |
| `StartMonitoring` | Unary | Tell the driver to start intercepting I/O |
| `StopMonitoring` | Unary | Tell the driver to pause |
| `GetStatus` | Unary | Query driver connection, event counters, subscriber count |

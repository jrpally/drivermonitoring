"""
FileMonitor Python client example
==================================
Connects to a running FileMonitor.exe instance and prints all file system events.

Prerequisites
-------------
1. Run FileMonitor.exe as Administrator (it installs the driver and starts the gRPC server).
2. Install dependencies:
       pip install grpcio grpcio-tools
3. Generate Python stubs from the proto file (run once from this directory):
       python -m grpc_tools.protoc -I../../../proto ^
           --python_out=. ^
           --grpc_python_out=. ^
           ../../../proto/file_monitor.proto
   This creates file_monitor_pb2.py and file_monitor_pb2_grpc.py alongside this script.
4. Run this script:
       python client.py
"""

import grpc
import file_monitor_pb2
import file_monitor_pb2_grpc

GRPC_ADDRESS = "localhost:50051"

# Map numeric event-type values to human-readable names
EVENT_NAMES = {
    0:   "UNKNOWN",
    1:   "CREATE",
    2:   "CLOSE",
    4:   "READ",
    8:   "WRITE",
    16:  "DELETE",
    32:  "RENAME",
    64:  "SETINFO",
    128: "CLEANUP",
}


def main():
    print(f"Connecting to FileMonitor at {GRPC_ADDRESS} ...")

    with grpc.insecure_channel(GRPC_ADDRESS) as channel:
        stub = file_monitor_pb2_grpc.FileMonitorServiceStub(channel)

        # Check status before subscribing
        try:
            status = stub.GetStatus(file_monitor_pb2.StatusRequest())
            print(f"Driver connected : {status.is_driver_connected}")
            print(f"Monitoring active: {status.is_monitoring}")
            print(f"Events processed : {status.events_processed}")
            print(f"Active subscribers: {status.active_subscribers}")
        except grpc.RpcError as e:
            print(f"Could not reach server: {e.details()}")
            return

        print()
        print(f"{'Time':<12} {'PID':>6}  {'Process':<22} {'Event':<10}  Path")
        print("-" * 90)

        # Subscribe to all events (event_filter=0 means all; path_filter="" means all paths)
        request = file_monitor_pb2.SubscribeRequest(
            event_filter=0,   # 0 = all event types
            path_filter="",   # empty = all paths; e.g. r"\Device\HarddiskVolume3\Users"
        )

        try:
            for event in stub.Subscribe(request):
                event_name = EVENT_NAMES.get(event.event_type, f"0x{event.event_type:02X}")
                process = event.process_name or str(event.process_id)
                if len(process) > 20:
                    process = process[:19] + "…"

                from datetime import datetime, timezone
                # Timestamp is .NET ticks (100-nanosecond intervals since 0001-01-01)
                TICKS_PER_SECOND = 10_000_000
                EPOCH_DIFF = 621_355_968_000_000_000  # ticks between 0001-01-01 and 1970-01-01
                ts_unix = (event.timestamp - EPOCH_DIFF) / TICKS_PER_SECOND
                time_str = datetime.fromtimestamp(ts_unix).strftime("%H:%M:%S.%f")[:12]

                print(f"{time_str:<12} {event.process_id:>6}  {process:<22} {event_name:<10}  {event.file_path}")

        except KeyboardInterrupt:
            print("\nDisconnected.")
        except grpc.RpcError as e:
            if e.code() == grpc.StatusCode.CANCELLED:
                print("\nStream cancelled.")
            else:
                print(f"\ngRPC error: [{e.code()}] {e.details()}")


if __name__ == "__main__":
    main()

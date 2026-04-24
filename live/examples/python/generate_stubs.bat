@echo off
REM Generate Python gRPC stubs from the proto file.
REM Run this script once before running client.py.
REM Requires: pip install grpcio-tools

python -m grpc_tools.protoc ^
    -I..\..\..\proto ^
    --python_out=. ^
    --grpc_python_out=. ^
    ..\..\..\proto\file_monitor.proto

echo.
echo Stubs generated: file_monitor_pb2.py  file_monitor_pb2_grpc.py

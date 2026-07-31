// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using System.IO.Ports;
using System.Text;
using System.Threading;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// Serial port connection for communicating with devices via USB serial ports.
/// </summary>
public class SerialConnectionInstance : ObjectInstance, IDisposable
{
    private SerialPort? _serialPort;
    private readonly object _lock = new object();
    private bool _isConnected = false;
    private bool _disposed = false;
    
    public bool IsConnected => !_disposed && _isConnected && _serialPort?.IsOpen == true;
    
    public SerialConnectionInstance() : base(null)
    {
    }
    
    /// <summary>
    /// Finalizer - ensures serial port is cleaned up even if Dispose() is not called.
    /// </summary>
    ~SerialConnectionInstance()
    {
        Dispose(false);
    }
    
    /// <summary>
    /// Disposes the serial connection and closes the port.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;
            
        if (disposing)
        {
            DisconnectInternal();
        }
        
        _disposed = true;
    }
    
    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        // Handle property access
        if (name == "isConnected")
            return RuntimeValue.Boolean(IsConnected);
        
        // Handle method access
        if (name == "connect" || name == "disconnect" || name == "write" || 
            name == "read" || name == "readLine")
        {
            var wrapper = new FunctionValue(null, null, false, null);
            wrapper.BuiltInInstance = this;
            wrapper.BuiltInMethod = name;
            return RuntimeValue.Function(wrapper);
        }
        
        throw new Exception($"Undefined property '{name}' on SerialConnection.");
    }
    
    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args)
    {
        switch (methodName)
        {
            case "connect":
                return Connect(args);
            case "disconnect":
                return Disconnect(args);
            case "write":
                return Write(args);
            case "read":
                return Read(args);
            case "readLine":
                return ReadLine(args);
            default:
                throw new Exception($"Unknown method: {methodName}");
        }
    }
    
    private RuntimeValue Connect(List<RuntimeValue> args)
    {
        if (args.Count < 2)
            throw new Exception("connect() expects 2 arguments: (portName, baudRate)");
        
        if (args[0].Type != ValueType.String)
            throw new Exception("connect() portName must be a string");
        if (args[1].Type != ValueType.Integer)
            throw new Exception("connect() baudRate must be an integer");
        
        var portName = args[0].AsString();
        var baudRate = args[1].AsInteger();
        
        lock (_lock)
        {
            if (_serialPort != null && _serialPort.IsOpen)
            {
                _serialPort.Close();
            }
            
            try
            {
                _serialPort = new SerialPort(portName, baudRate)
                {
                    ReadTimeout = 2000,
                    WriteTimeout = 2000,
                    NewLine = "\n"
                };
                
                _serialPort.Open();
                _isConnected = true;
                
                // Wait a bit for device to initialize
                Thread.Sleep(2000);
                
                return RuntimeValue.Boolean(true);
            }
            catch (Exception ex)
            {
                _isConnected = false;
                throw new Exception($"Failed to connect to serial port '{portName}': {ex.Message}");
            }
        }
    }
    
    private RuntimeValue Disconnect(List<RuntimeValue> args)
    {
        if (args.Count != 0)
            throw new Exception("disconnect() expects 0 arguments");
        
        DisconnectInternal();
        return RuntimeValue.Null();
    }
    
    private void DisconnectInternal()
    {
        lock (_lock)
        {
            if (_serialPort != null && _serialPort.IsOpen)
            {
                try
                {
                    _serialPort.Close();
                }
                catch { }
            }
            _serialPort?.Dispose();
            _serialPort = null;
            _isConnected = false;
        }
    }
    
    private RuntimeValue Write(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new Exception("write() expects 1 argument: (data)");
        
        if (!IsConnected)
            throw new Exception("Serial port not connected. Call connect() first.");
        
        var data = args[0].AsString();
        
        lock (_lock)
        {
            try
            {
                _serialPort!.Write(data);
                return RuntimeValue.Null();
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to write to serial port: {ex.Message}");
            }
        }
    }
    
    private RuntimeValue Read(List<RuntimeValue> args)
    {
        if (args.Count > 1)
            throw new Exception("read() expects 0 or 1 argument: (byteCount?)");
        
        if (!IsConnected)
            throw new Exception("Serial port not connected. Call connect() first.");
        
        lock (_lock)
        {
            try
            {
                if (args.Count == 1 && args[0].Type == ValueType.Integer)
                {
                    var byteCount = args[0].AsInteger();
                    var buffer = new char[byteCount];
                    var bytesRead = _serialPort!.Read(buffer, 0, byteCount);
                    return RuntimeValue.String(new string(buffer, 0, bytesRead));
                }
                else
                {
                    // Read all available bytes
                    var available = _serialPort!.BytesToRead;
                    if (available == 0)
                        return RuntimeValue.String("");
                    
                    var buffer = new char[available];
                    var bytesRead = _serialPort.Read(buffer, 0, available);
                    return RuntimeValue.String(new string(buffer, 0, bytesRead));
                }
            }
            catch (TimeoutException)
            {
                return RuntimeValue.String("");
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to read from serial port: {ex.Message}");
            }
        }
    }
    
    private RuntimeValue ReadLine(List<RuntimeValue> args)
    {
        if (args.Count != 0)
            throw new Exception("readLine() expects 0 arguments");
        
        if (!IsConnected)
            throw new Exception("Serial port not connected. Call connect() first.");
        
        lock (_lock)
        {
            try
            {
                var line = _serialPort!.ReadLine();
                return RuntimeValue.String(line.TrimEnd('\r', '\n'));
            }
            catch (TimeoutException)
            {
                throw new Exception("Serial port read timeout - no data received");
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to read line from serial port: {ex.Message}");
            }
        }
    }
}

// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using System.Collections.Generic;
using System.Text.Json;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;
using System.Linq;

/// <summary>
/// Arduino device connection supporting both serial (USB) and HTTP/REST (WiFi) communication.
/// </summary>
public class ArduinoConnectionInstance : ObjectInstance, IDisposable
{
    private SerialConnectionInstance? _serialConnection;
    private RestClientInstance? _restClient;
    private string? _baseUrl;
    private string? _portName;
    private int? _baudRate;
    private bool _disposed = false;
    private readonly object _lock = new object();
    
    public bool IsConnected
    {
        get
        {
            lock (_lock)
            {
                if (_disposed) return false;
                if (_restClient != null)
                {
                    // For HTTP mode, we consider it connected if RestClient is initialized
                    // Actual connection is tested on each request
                    return true;
                }
                return _serialConnection?.IsConnected ?? false;
            }
        }
    }
    
    /// <summary>
    /// Constructor for HTTP/REST mode (WiFi-enabled devices like ESP32)
    /// </summary>
    public ArduinoConnectionInstance(string baseUrl) : base(null)
    {
        _baseUrl = baseUrl;
        _restClient = new RestClientInstance(baseUrl);
    }
    
    /// <summary>
    /// Constructor for serial mode (USB connection)
    /// </summary>
    public ArduinoConnectionInstance(string portName, int baudRate) : base(null)
    {
        _portName = portName;
        _baudRate = baudRate;
        _serialConnection = new SerialConnectionInstance();
    }
    
    /// <summary>
    /// Finalizer - ensures connections are cleaned up.
    /// </summary>
    ~ArduinoConnectionInstance()
    {
        Dispose(false);
    }
    
    /// <summary>
    /// Disposes the Arduino connection and closes any open connections.
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
            lock (_lock)
            {
                DisconnectInternal();
                _serialConnection?.Dispose();
                _restClient = null;
            }
        }
        
        _disposed = true;
    }
    
    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        // Handle property access
        if (name == "isConnected")
            return RuntimeValue.Boolean(IsConnected);
        
        // Handle method access
        if (name == "connect" || name == "disconnect" || name == "digitalWrite" || 
            name == "digitalRead" || name == "analogRead" || name == "analogWrite" || 
            name == "pinMode")
        {
            var wrapper = new FunctionValue(null, null, false, null);
            wrapper.BuiltInInstance = this;
            wrapper.BuiltInMethod = name;
            return RuntimeValue.Function(wrapper);
        }
        
        throw new Exception($"Undefined property '{name}' on ArduinoConnection.");
    }
    
    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args)
    {
        switch (methodName)
        {
            case "connect":
                return Connect(args);
            case "disconnect":
                return Disconnect(args);
            case "digitalWrite":
                return DigitalWrite(args);
            case "digitalRead":
                return DigitalRead(args);
            case "analogRead":
                return AnalogRead(args);
            case "analogWrite":
                return AnalogWrite(args);
            case "pinMode":
                return PinMode(args);
            default:
                throw new Exception($"Unknown method: {methodName}");
        }
    }
    
    private RuntimeValue Connect(List<RuntimeValue> args)
    {
        if (args.Count != 0)
            throw new Exception("connect() expects 0 arguments");
        
        lock (_lock)
        {
            if (_restClient != null)
            {
                // HTTP mode - test connection
                try
                {
                    var response = _restClient.CallMethod("get", new List<RuntimeValue> 
                    { 
                        RuntimeValue.String("/ping") 
                    });
                    
                    if (response.Type == ValueType.Object)
                    {
                        var obj = response.AsObject();
                        var status = obj.Get("status", null);
                        if (status != null && status.AsInteger() == 200)
                        {
                            return RuntimeValue.Boolean(true);
                        }
                    }
                    return RuntimeValue.Boolean(false);
                }
                catch
                {
                    return RuntimeValue.Boolean(false);
                }
            }
            else if (_serialConnection != null && _portName != null && _baudRate.HasValue)
            {
                // Serial mode
                try
                {
                    return _serialConnection.CallMethod("connect", new List<RuntimeValue>
                    {
                        RuntimeValue.String(_portName),
                        RuntimeValue.Integer(_baudRate.Value)
                    });
                }
                catch (Exception ex)
                {
                    // Return false instead of throwing - allows user code to handle gracefully
                    return RuntimeValue.Boolean(false);
                }
            }
            else
            {
                throw new Exception("ArduinoConnection not properly initialized");
            }
        }
    }
    
    private RuntimeValue Disconnect(List<RuntimeValue> args)
    {
        if (args.Count != 0)
            throw new Exception("disconnect() expects 0 arguments");
        
        return DisconnectInternal();
    }
    
    private RuntimeValue DisconnectInternal()
    {
        lock (_lock)
        {
            if (_serialConnection != null)
            {
                _serialConnection.CallMethod("disconnect", new List<RuntimeValue>());
            }
            // HTTP mode doesn't need explicit disconnect
            return RuntimeValue.Null();
        }
    }
    
    private RuntimeValue DigitalWrite(List<RuntimeValue> args)
    {
        if (args.Count != 2)
            throw new Exception("digitalWrite() expects 2 arguments: (pin, value)");
        
        if (args[0].Type != ValueType.Integer)
            throw new Exception("digitalWrite() pin must be an integer");
        if (args[1].Type != ValueType.Boolean)
            throw new Exception("digitalWrite() value must be a boolean");
        
        var pin = args[0].AsInteger();
        var value = args[1].AsBoolean();
        
        if (_restClient != null)
        {
            // HTTP mode
            var body = new JsonObject();
            body.Set("pin", RuntimeValue.Integer(pin));
            body.Set("value", RuntimeValue.Integer(value ? 1 : 0));
            
            var response = _restClient.CallMethod("post", new List<RuntimeValue>
            {
                RuntimeValue.String("/digital/write"),
                RuntimeValue.Object(body)
            });
            
            return response;
        }
        else if (_serialConnection != null)
        {
            // Serial mode
            var command = $"DIGITAL_WRITE:{pin}:{(value ? 1 : 0)}\n";
            _serialConnection.CallMethod("write", new List<RuntimeValue> { RuntimeValue.String(command) });
            
            var response = _serialConnection.CallMethod("readLine", new List<RuntimeValue>());
            if (response.AsString().StartsWith("OK"))
            {
                return RuntimeValue.Null();
            }
            else
            {
                throw new Exception($"Arduino error: {response.AsString()}");
            }
        }
        else
        {
            throw new Exception("ArduinoConnection not connected");
        }
    }
    
    private RuntimeValue DigitalRead(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new Exception("digitalRead() expects 1 argument: (pin)");
        
        if (args[0].Type != ValueType.Integer)
            throw new Exception("digitalRead() pin must be an integer");
        
        var pin = args[0].AsInteger();
        
        if (_restClient != null)
        {
            // HTTP mode
            var queryParams = new JsonObject();
            queryParams.Set("pin", RuntimeValue.String(pin.ToString()));
            
            var response = _restClient.CallMethod("get", new List<RuntimeValue>
            {
                RuntimeValue.String("/digital/read"),
                RuntimeValue.Null(),
                RuntimeValue.Object(queryParams)
            });
            
            if (response.Type == ValueType.Object)
            {
                var obj = response.AsObject();
                var body = obj.Get("body", null);
                if (body != null && body.Type == ValueType.String)
                {
                    try
                    {
                        var jsonDoc = JsonDocument.Parse(body.AsString());
                        var root = jsonDoc.RootElement;
                        if (root.TryGetProperty("value", out var valueProp))
                        {
                            var value = valueProp.GetInt32();
                            return RuntimeValue.Boolean(value == 1);
                        }
                    }
                    catch { }
                }
            }
            throw new Exception("Failed to read digital pin value");
        }
        else if (_serialConnection != null)
        {
            // Serial mode
            var command = $"DIGITAL_READ:{pin}\n";
            _serialConnection.CallMethod("write", new List<RuntimeValue> { RuntimeValue.String(command) });
            
            var response = _serialConnection.CallMethod("readLine", new List<RuntimeValue>());
            var responseStr = response.AsString();
            
            if (responseStr.StartsWith("OK:"))
            {
                var valueStr = responseStr.Substring(3).Trim();
                var value = valueStr == "1" || valueStr.ToLower() == "true";
                return RuntimeValue.Boolean(value);
            }
            else
            {
                throw new Exception($"Arduino error: {responseStr}");
            }
        }
        else
        {
            throw new Exception("ArduinoConnection not connected");
        }
    }
    
    private RuntimeValue AnalogRead(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new Exception("analogRead() expects 1 argument: (pin)");
        
        if (args[0].Type != ValueType.Integer)
            throw new Exception("analogRead() pin must be an integer");
        
        var pin = args[0].AsInteger();
        
        if (_restClient != null)
        {
            // HTTP mode
            var queryParams = new JsonObject();
            queryParams.Set("pin", RuntimeValue.String(pin.ToString()));
            
            var response = _restClient.CallMethod("get", new List<RuntimeValue>
            {
                RuntimeValue.String("/analog/read"),
                RuntimeValue.Null(),
                RuntimeValue.Object(queryParams)
            });
            
            if (response.Type == ValueType.Object)
            {
                var obj = response.AsObject();
                var body = obj.Get("body", null);
                if (body != null && body.Type == ValueType.String)
                {
                    try
                    {
                        var jsonDoc = JsonDocument.Parse(body.AsString());
                        var root = jsonDoc.RootElement;
                        if (root.TryGetProperty("value", out var valueProp))
                        {
                            var value = valueProp.GetInt32();
                            return RuntimeValue.Integer(value);
                        }
                    }
                    catch { }
                }
            }
            throw new Exception("Failed to read analog pin value");
        }
        else if (_serialConnection != null)
        {
            // Serial mode
            var command = $"ANALOG_READ:{pin}\n";
            _serialConnection.CallMethod("write", new List<RuntimeValue> { RuntimeValue.String(command) });
            
            var response = _serialConnection.CallMethod("readLine", new List<RuntimeValue>());
            var responseStr = response.AsString();
            
            if (responseStr.StartsWith("OK:"))
            {
                var valueStr = responseStr.Substring(3).Trim();
                if (int.TryParse(valueStr, out int value))
                {
                    return RuntimeValue.Integer(value);
                }
            }
            throw new Exception($"Arduino error: {responseStr}");
        }
        else
        {
            throw new Exception("ArduinoConnection not connected");
        }
    }
    
    private RuntimeValue AnalogWrite(List<RuntimeValue> args)
    {
        if (args.Count != 2)
            throw new Exception("analogWrite() expects 2 arguments: (pin, value)");
        
        if (args[0].Type != ValueType.Integer)
            throw new Exception("analogWrite() pin must be an integer");
        if (args[1].Type != ValueType.Integer)
            throw new Exception("analogWrite() value must be an integer (0-255)");
        
        var pin = args[0].AsInteger();
        var value = args[1].AsInteger();
        
        if (value < 0 || value > 255)
            throw new Exception("analogWrite() value must be between 0 and 255");
        
        if (_restClient != null)
        {
            // HTTP mode
            var body = new JsonObject();
            body.Set("pin", RuntimeValue.Integer(pin));
            body.Set("value", RuntimeValue.Integer(value));
            
            var response = _restClient.CallMethod("post", new List<RuntimeValue>
            {
                RuntimeValue.String("/analog/write"),
                RuntimeValue.Object(body)
            });
            
            return response;
        }
        else if (_serialConnection != null)
        {
            // Serial mode
            var command = $"ANALOG_WRITE:{pin}:{value}\n";
            _serialConnection.CallMethod("write", new List<RuntimeValue> { RuntimeValue.String(command) });
            
            var response = _serialConnection.CallMethod("readLine", new List<RuntimeValue>());
            if (response.AsString().StartsWith("OK"))
            {
                return RuntimeValue.Null();
            }
            else
            {
                throw new Exception($"Arduino error: {response.AsString()}");
            }
        }
        else
        {
            throw new Exception("ArduinoConnection not connected");
        }
    }
    
    private RuntimeValue PinMode(List<RuntimeValue> args)
    {
        if (args.Count != 2)
            throw new Exception("pinMode() expects 2 arguments: (pin, mode)");
        
        if (args[0].Type != ValueType.Integer)
            throw new Exception("pinMode() pin must be an integer");
        if (args[1].Type != ValueType.String)
            throw new Exception("pinMode() mode must be a string (INPUT, OUTPUT, INPUT_PULLUP)");
        
        var pin = args[0].AsInteger();
        var mode = args[1].AsString().ToUpper();
        
        if (mode != "INPUT" && mode != "OUTPUT" && mode != "INPUT_PULLUP")
            throw new Exception("pinMode() mode must be INPUT, OUTPUT, or INPUT_PULLUP");
        
        if (_restClient != null)
        {
            // HTTP mode
            var body = new JsonObject();
            body.Set("pin", RuntimeValue.Integer(pin));
            body.Set("mode", RuntimeValue.String(mode));
            
            var response = _restClient.CallMethod("post", new List<RuntimeValue>
            {
                RuntimeValue.String("/pin/mode"),
                RuntimeValue.Object(body)
            });
            
            return response;
        }
        else if (_serialConnection != null)
        {
            // Serial mode
            var command = $"PIN_MODE:{pin}:{mode}\n";
            _serialConnection.CallMethod("write", new List<RuntimeValue> { RuntimeValue.String(command) });
            
            var response = _serialConnection.CallMethod("readLine", new List<RuntimeValue>());
            if (response.AsString().StartsWith("OK"))
            {
                return RuntimeValue.Null();
            }
            else
            {
                throw new Exception($"Arduino error: {response.AsString()}");
            }
        }
        else
        {
            throw new Exception("ArduinoConnection not connected");
        }
    }
}

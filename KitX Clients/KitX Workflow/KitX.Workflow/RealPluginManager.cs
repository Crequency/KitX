using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using KitX.Core.Contract.Workflow;
using KitX.Core.Contract.Plugin;
using KitX.Core.Contract.Device;
using KitX.Core.Contract.Event;
using KitX.Core.Contract.Plugin.Events;
using KitX.Workflow.Hosting;
using KitX.Shared.CSharp.Device;
using KitX.Shared.CSharp.Plugin;
using KitX.Shared.CSharp.WebCommand;
using KitX.Shared.CSharp.WebCommand.Infos;
using Serilog;

namespace KitX.Workflow;

/// <summary>
/// 真实的插件管理器实现，通过 WebSocket 与插件通信。
/// Implements both Contract.IPluginManager (primary, for BlockScripting) and
/// KCS IPluginManager (legacy, for KCS pipeline compatibility).
/// </summary>
public class RealPluginManager : IPluginManager, IRealPluginManagerBridge
{
    private readonly IPluginServer _pluginServer;
    private readonly IDeviceServer _deviceServer;
    private readonly IDeviceHttpClient _deviceHttpClient;
    private readonly IEventService _eventService;
    private readonly IDeviceDiscoveryService _deviceDiscoveryService;
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        WriteIndented = true,
        IncludeFields = true,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// 待处理的响应字典，用于异步等待插件响应
    /// </summary>
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pendingResponses = new();

    /// <summary>
    /// 构造函数（仅需 IPluginServer，其他依赖从 ServiceLocator 解析）
    /// </summary>
    /// <param name="pluginServer">插件服务器实例</param>
    public RealPluginManager(IPluginServer pluginServer)
        : this(pluginServer,
              ServiceLocator.GetRequiredService<IEventService>(),
              ServiceLocator.GetRequiredService<IDeviceDiscoveryService>(),
              ServiceLocator.GetRequiredService<IDeviceServer>(),
              ServiceLocator.GetRequiredService<IDeviceHttpClient>())
    {
    }

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="pluginServer">插件服务器实例</param>
    /// <param name="eventService">事件服务实例</param>
    /// <param name="deviceDiscoveryService">设备发现服务实例</param>
    /// <param name="deviceServer">设备服务器实例</param>
    /// <param name="deviceHttpClient">HTTP 客户端，用于跨设备调用</param>
    public RealPluginManager(IPluginServer pluginServer, IEventService eventService, IDeviceDiscoveryService deviceDiscoveryService, IDeviceServer deviceServer, IDeviceHttpClient deviceHttpClient)
    {
        _pluginServer = pluginServer ?? throw new ArgumentNullException(nameof(pluginServer));
        _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
        _deviceDiscoveryService = deviceDiscoveryService ?? throw new ArgumentNullException(nameof(deviceDiscoveryService));
        _deviceServer = deviceServer;
        _deviceHttpClient = deviceHttpClient ?? throw new ArgumentNullException(nameof(deviceHttpClient));

        Log.Information("[RealPluginManager] Constructor. This HashCode: {ThisHashCode}, PluginServer HashCode: {PluginServerHashCode}",
            GetHashCode(), _pluginServer.GetHashCode());

        // 订阅插件消息接收事件以处理响应
        _pluginServer.PluginMessageReceived += OnPluginMessageReceived;

        // 订阅插件响应事件（当插件返回带RequestId的响应时触发）
        _eventService.Subscribe<PluginResponseEventArgs>(WorkflowEventNames.PluginResponse, (sender, args) => OnPluginResponse(this, args));
    }

    /// <summary>
    /// 处理插件响应事件
    /// </summary>
    private void OnPluginResponse(object? sender, PluginResponseEventArgs e)
    {
        try
        {
            Log.Information($"[RealPluginManager] OnPluginResponse called with RequestId: {e.RequestId}");

            if (_pendingResponses.TryRemove(e.RequestId, out var tcs))
            {
                var command = JsonSerializer.Deserialize<Command>(e.Content, _serializerOptions);
                var responseBody = command.BodyLength > 0
                    ? Encoding.UTF8.GetString(command.Body.AsSpan(0, command.BodyLength))
                    : string.Empty;
                Log.Information($"[RealPluginManager] Setting result from PluginResponse: {responseBody}");
                tcs.SetResult(responseBody);
            }
            else
            {
                Log.Warning($"[RealPluginManager] RequestId {e.RequestId} not found in pending responses");
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[RealPluginManager] Error handling plugin response");
        }
    }

    /// <summary>
    /// 处理收到的插件消息
    /// </summary>
    private void OnPluginMessageReceived(object? sender, PluginMessageReceivedEventArgs e)
    {
        try
        {
            if (e.Message is null) return;

            var kwc = JsonSerializer.Deserialize<Request>(e.Message, _serializerOptions);
            if (kwc?.Content is null) return;

            var command = JsonSerializer.Deserialize<Command>(kwc.Content, _serializerOptions);
            if (command.Request is null) return; // Command is a struct, check if Request is empty

            // 最早跳过 TriggerFired，由 TriggerManager 处理，不产生多余日志
            if (command.Request == CommandRequestInfo.TriggerFired)
                return;

            // 仅对非 TriggerFired 消息记录日志
            Log.Information($"[RealPluginManager] OnPluginMessageReceived called with message: {e.Message.Substring(0, Math.Min(200, e.Message.Length))}...");
            Log.Information($"[RealPluginManager] kwc.Content: {kwc.Content.Substring(0, Math.Min(100, kwc.Content.Length))}...");

            // 检查是否是响应消息
            if (command.Tags != null && command.Tags.TryGetValue("RequestId", out var requestId))
            {
                Log.Information($"[RealPluginManager] Found RequestId: {requestId}");
                if (_pendingResponses.TryRemove(requestId, out var tcs))
                {
                    var responseBody = command.BodyLength > 0
                        ? Encoding.UTF8.GetString(command.Body.AsSpan(0, command.BodyLength))
                        : string.Empty;
                    Log.Information($"[RealPluginManager] Setting result: {responseBody}");
                    tcs.SetResult(responseBody);
                }
                else
                {
                    Log.Warning($"[RealPluginManager] RequestId {requestId} not found in pending responses");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[RealPluginManager] Error handling plugin message");
        }
    }

    /// <summary>
    /// 调用插件方法（无返回值） — Contract.IPluginManager
    /// </summary>
    /// <remarks>
    /// 使用 fire-and-forget 模式，不等待插件响应。
    /// 因为 void 返回类型的插件功能不会发送响应。
    /// </remarks>
    public void Call(PluginCallInfo callInfo)
    {
        Log.Information($"[RealPluginManager] Call() invoked (fire-and-forget): {callInfo.PluginName}.{callInfo.MethodName}");

        // 如果指定了目标设备，通过 RemoteCallAsync 路由（fire-and-forget）
        if (!string.IsNullOrEmpty(callInfo.TargetDevice))
        {
            Log.Information("[RealPluginManager] Call: TargetDevice={Device} set, routing via RemoteCallAsync (fire-and-forget)",
                callInfo.TargetDevice);
            _ = Task.Run(() => RemoteCallAsync(callInfo, callInfo.TargetDevice));
            return;
        }

        SendRequestWithoutWaitAsync(callInfo);
    }

    /// <summary>
    /// 调用插件方法（有返回值） — Contract.IPluginManager
    /// </summary>
    public T Call<T>(PluginCallInfo callInfo)
    {
        Log.Information($"[RealPluginManager] Call<{typeof(T).Name}>() invoked: {callInfo.PluginName}.{callInfo.MethodName}");

        // 如果指定了目标设备，通过 RemoteCallAsync 路由
        if (!string.IsNullOrEmpty(callInfo.TargetDevice))
        {
            Log.Information("[RealPluginManager] Call<{Type}>: TargetDevice={Device} set, routing via RemoteCallAsync",
                typeof(T).Name, callInfo.TargetDevice);
            var remoteResult = RemoteCallAsync(callInfo, callInfo.TargetDevice).GetAwaiter().GetResult();
            return (T)remoteResult!;
        }

        var result = CallAsync(callInfo).GetAwaiter().GetResult();
        return ParseResult<T>(result);
    }
    /// <summary>
    /// 自动调用插件方法：根据函数声明的返回类型自动选择调用策略。
    /// - void 返回类型 → fire-and-forget (Call)，不等待响应
    /// - 非 void 返回类型 → 类型化同步等待 (Call<T>)，返回具体类型的结果
    /// 调用方无需关心分发逻辑，所有判断由 Manager 内部完成。
    /// </summary>
    /// <param name="callInfo">插件调用信息</param>
    /// <returns>非 void 函数返回具体类型结果；void 函数返回 null</returns>
    public object? CallAuto(PluginCallInfo callInfo)
    {
        var returnType = GetFunctionReturnType(callInfo.PluginName, callInfo.MethodName);

        if (returnType == null || returnType == typeof(void))
        {
            Log.Information("[RealPluginManager] CallAuto: {Plugin}.{Method} → fire-and-forget (void/unknown)",
                callInfo.PluginName, callInfo.MethodName);
            Call(callInfo);
            return null;
        }

        Log.Information("[RealPluginManager] CallAuto: {Plugin}.{Method} → Call<{Type}>() (typed wait)",
            callInfo.PluginName, callInfo.MethodName, returnType.Name);

        var typedCallMethod = typeof(IPluginManager)
            .GetMethods()
            .First(m => m.Name == "Call" && m.IsGenericMethod)
            .MakeGenericMethod(returnType);

        return typedCallMethod.Invoke(this, new object[] { callInfo });
    }

    /// <summary>
    /// 异步调用插件方法
    /// </summary>
    private async Task<string> CallAsync(PluginCallInfo callInfo)
    {
        const string location = $"{nameof(RealPluginManager)}.{nameof(CallAsync)}";

        // 查找插件连接
        var connection = FindPluginConnection(callInfo.PluginName);
        if (connection is null)
        {
            Log.Error($"[RealPluginManager] Plugin connection not found: {callInfo.PluginName}");
            Log.Information($"[RealPluginManager] Available connections: {_pluginServer.Connections.Count}");
            foreach (var conn in _pluginServer.Connections)
            {
                Log.Information($"[RealPluginManager] Connection: {conn.ConnectionId}, Plugin: {conn.PluginInfo?.Name}");
            }
            throw new InvalidOperationException($"Plugin not found or not connected: {callInfo.PluginName}");
        }

        Log.Information($"[RealPluginManager] Found connection for {callInfo.PluginName}, ConnectionId: {connection.ConnectionId}");

        // 生成唯一请求ID
        var requestId = Guid.NewGuid().ToString();

        // 创建任务CompletionSource用于等待响应
        var tcs = new TaskCompletionSource<string>();
        _pendingResponses[requestId] = tcs;

        try
        {
            // 构建命令
            var command = new Command
            {
                Request = CommandRequestInfo.ReceiveCommand,
                FunctionName = callInfo.MethodName,
                PluginConnectionId = connection.ConnectionId ?? string.Empty,
                Tags = new()
                {
                    { "RequestId", requestId }
                }
            };

            // 处理参数
            if (callInfo.Parameters != null && callInfo.Parameters.Length > 0)
            {
                command.FunctionArgs = new();
                for (int i = 0; i < callInfo.Parameters.Length; i++)
                {
                    var paramValue = callInfo.Parameters[i]?.ToString() ?? string.Empty;
                    var paramName = callInfo.ParameterNames?.Length > i ? callInfo.ParameterNames[i] : i.ToString();
                    var paramType = callInfo.ParameterTypes?.Length > i ? callInfo.ParameterTypes[i].Name.ToLower() : "string";
                    command.FunctionArgs.Add(new Parameter
                    {
                        Name = paramName,
                        Type = paramType,
                        Value = paramValue,
                        IsOptional = false
                    });
                }
                command.Body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(command.FunctionArgs, _serializerOptions));
                command.BodyLength = command.Body.Length;
            }

            // 构建请求
            var request = new Request
            {
                Type = RequestTypes.Command,
                Version = RequestVersions.V1,
                Content = JsonSerializer.Serialize(command, _serializerOptions)
            };

            // 发送请求
            var message = JsonSerializer.Serialize(request, _serializerOptions);
            connection.Send(message);

            Log.Information($"[RealPluginManager] Sent request to {callInfo.PluginName}.{callInfo.MethodName}, RequestId: {requestId}");

            // 等待插件响应，设置超时
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                return await tcs.Task.WaitAsync(cts.Token);
            }
            catch (TimeoutException)
            {
                Log.Warning($"[RealPluginManager] Request {requestId} timed out");
                _pendingResponses.TryRemove(requestId, out _);
                throw new TimeoutException($"Plugin call timed out: {callInfo.PluginName}.{callInfo.MethodName}");
            }
            catch (OperationCanceledException)
            {
                Log.Warning($"[RealPluginManager] Request {requestId} was cancelled");
                _pendingResponses.TryRemove(requestId, out _);
                throw;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"[RealPluginManager] Error calling {callInfo.PluginName}.{callInfo.MethodName}");
            throw;
        }
    }

    /// <summary>
    /// 发送请求但不等待响应（fire-and-forget）
    /// </summary>
    /// <remarks>
    /// 用于 void 返回类型的插件调用，因为这类调用不会有响应返回。
    /// </remarks>
    private void SendRequestWithoutWaitAsync(PluginCallInfo callInfo)
    {
        // 查找插件连接
        var connection = FindPluginConnection(callInfo.PluginName);
        if (connection is null)
        {
            Log.Error($"[RealPluginManager] Plugin connection not found: {callInfo.PluginName}");
            throw new InvalidOperationException($"Plugin not found or not connected: {callInfo.PluginName}");
        }

        Log.Information($"[RealPluginManager] Sending fire-and-forget request to {callInfo.PluginName}.{callInfo.MethodName}");

        try
        {
            // 构建命令（不包含 RequestId，因为不需要等待响应）
            var command = new Command
            {
                Request = CommandRequestInfo.ReceiveCommand,
                FunctionName = callInfo.MethodName,
                PluginConnectionId = connection.ConnectionId ?? string.Empty
            };

            // 处理参数
            if (callInfo.Parameters != null && callInfo.Parameters.Length > 0)
            {
                command.FunctionArgs = new();
                for (int i = 0; i < callInfo.Parameters.Length; i++)
                {
                    var paramValue = callInfo.Parameters[i]?.ToString() ?? string.Empty;
                    var paramName = callInfo.ParameterNames?.Length > i ? callInfo.ParameterNames[i] : i.ToString();
                    var paramType = callInfo.ParameterTypes?.Length > i ? callInfo.ParameterTypes[i].Name.ToLower() : "string";
                    command.FunctionArgs.Add(new Parameter
                    {
                        Name = paramName,
                        Type = paramType,
                        Value = paramValue,
                        IsOptional = false
                    });
                }
                command.Body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(command.FunctionArgs, _serializerOptions));
                command.BodyLength = command.Body.Length;
            }

            // 构建请求
            var request = new Request
            {
                Type = RequestTypes.Command,
                Version = RequestVersions.V1,
                Content = JsonSerializer.Serialize(command, _serializerOptions)
            };

            // 发送请求
            var message = JsonSerializer.Serialize(request, _serializerOptions);
            connection.Send(message);

            Log.Information($"[RealPluginManager] Fire-and-forget request sent to {callInfo.PluginName}.{callInfo.MethodName}");
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"[RealPluginManager] Error sending fire-and-forget request to {callInfo.PluginName}.{callInfo.MethodName}");
            throw;
        }
    }

    /// <summary>
    /// 解析结果
    /// </summary>
    private T ParseResult<T>(string result)
    {
        if (result is null || typeof(T) == typeof(void))
            return default!;

        try
        {
            var returnType = typeof(T);

            // 处理基础类型
            if (returnType == typeof(string))
                return (T)(object)result;

            if (returnType == typeof(int))
                return (T)(object)int.Parse(result);

            if (returnType == typeof(long))
                return (T)(object)long.Parse(result);

            if (returnType == typeof(float))
                return (T)(object)float.Parse(result);

            if (returnType == typeof(double))
                return (T)(object)double.Parse(result);

            if (returnType == typeof(bool))
                return (T)(object)bool.Parse(result);

            // 对于 object 类型，先尝试 JSON 反序列化，失败则返回原始字符串
            // BlockScript 通过 PluginCall() 使用 Call<object?>()，插件可能返回
            // 纯文本（如 "Hello"）而非 JSON 包装的值
            if (returnType == typeof(object))
            {
                try
                {
                    return (T)JsonSerializer.Deserialize<object>(result, _serializerOptions)!;
                }
                catch (JsonException)
                {
                    return (T)(object)result;
                }
            }

            // 对于其他复杂类型，从 JSON 反序列化
            return JsonSerializer.Deserialize<T>(result, _serializerOptions)!;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, $"[RealPluginManager] Error parsing result: {result}");
            return default!;
        }
    }

    /// <summary>
    /// 获取插件方法的返回值类型，用于运行时类型化调用
    /// 与 MethodEmitter 编译期类型解析逻辑一致，但在运行时通过反射使用
    /// </summary>
    /// <param name="pluginName">插件名称 (e.g. "TestPlugin.CSharp")</param>
    /// <param name="methodName">方法名称 (e.g. "SayHello")</param>
    /// <returns>返回值 Type，未找到则返回 null</returns>
    public Type? GetFunctionReturnType(string pluginName, string methodName)
    {
        var connection = FindPluginConnection(pluginName);
        if (connection?.PluginInfo?.Functions == null)
            return null;

        var func = connection.PluginInfo.Functions.Find(f => f.Name == methodName);
        if (string.IsNullOrEmpty(func.Name))
            return null;

        return MapReturnType(func.ReturnValueType);
    }

    /// <summary>
    /// 将字符串类型名映射为 Type，与 TypeMapper/MethodEmitter 保持一致
    /// </summary>
    private static Type? MapReturnType(string? typeName)
    {
        return typeName?.ToLowerInvariant() switch
        {
            null or "" => null,
            "void" => typeof(void),
            "string" => typeof(string),
            "int" => typeof(int),
            "long" => typeof(long),
            "float" => typeof(float),
            "double" => typeof(double),
            "bool" => typeof(bool),
            "object" => typeof(object),
            _ => Type.GetType(typeName) ?? typeof(object)
        };
    }

    /// <summary>
    /// 检查插件是否存在
    /// </summary>
    public bool IsPluginExists(string pluginName)
    {
        var exists = FindPluginConnection(pluginName) != null;
        Log.Information($"[RealPluginManager] IsPluginExists({pluginName}): {exists}");
        if (!exists)
        {
            foreach (var conn in _pluginServer.Connections)
            {
                Log.Information($"[RealPluginManager] Available connection: {conn.ConnectionId}, Plugin: {conn.PluginInfo?.Name}");
            }
        }
        return exists;
    }

    /// <summary>
    /// 检查插件方法是否存在
    /// </summary>
    public bool IsMethodExists(string pluginName, string methodName)
    {
        var connection = FindPluginConnection(pluginName);
        if (connection?.PluginInfo?.Functions == null)
            return false;

        return connection.PluginInfo.Functions.Exists(f => f.Name == methodName);
    }

    /// <summary>
    /// 查找插件连接
    /// </summary>
    private IPluginConnection? FindPluginConnection(string pluginName)
    {
        Log.Information($"[RealPluginManager] FindPluginConnection: _pluginServer HashCode={_pluginServer.GetHashCode()}, Connections Count={_pluginServer.Connections.Count}");
        foreach (var c in _pluginServer.Connections)
        {
            Log.Information($"[RealPluginManager] FindPluginConnection: connection PluginInfo.Name={c.PluginInfo?.Name}");
        }
        var result = _pluginServer.Connections
            .FirstOrDefault(c => c.PluginInfo?.Name == pluginName);
        Log.Information($"[RealPluginManager] FindPluginConnection result: {result?.GetHashCode()}");
        return result;
    }

    /// <summary>
    /// 根据设备名称查找已连接设备的 token。
    /// </summary>
    private string? FindDeviceToken(DeviceInfo deviceInfo)
    {
        if (deviceInfo?.Device == null)
            return null;

        try
        {
            // Use IDeviceServer.GetDeviceToken() to get the token
            if (_deviceServer != null)
            {
                return _deviceServer.GetDeviceToken(deviceInfo.Device);
            }

            Log.Warning("[RealPluginManager] DeviceServer is not available");
            return null;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[RealPluginManager] Error finding device token for {Device}", deviceInfo.Device?.DeviceName);
        }

        return null;
    }

    /// <summary>
    /// 根据设备名称查找 DeviceInfo。
    /// </summary>
    private DeviceInfo? FindDeviceInfoByName(string deviceName)
    {
        if (string.IsNullOrEmpty(deviceName))
            return null;

        try
        {
            // Use IDeviceServer.GetSignedInDevices() to find matching device
            if (_deviceServer != null)
            {
                var signedInDevices = _deviceServer.GetSignedInDevices();
                foreach (var locator in signedInDevices)
                {
                    if (locator.DeviceName.Equals(deviceName, StringComparison.OrdinalIgnoreCase))
                    {
                        // Found the locator by name — now we need DeviceInfo which has the full info
                        // We can construct a basic DeviceInfo from the locator + DevicesDiscoveryServer data
                        var defaultInfo = _deviceDiscoveryService.DefaultDeviceInfo;
                        if (defaultInfo != null && defaultInfo.Device.IsSameDevice(locator))
                        {
                            return defaultInfo;
                        }

                        // Fallback: construct from locator alone (missing some fields)
                        return new DeviceInfo
                        {
                            Device = locator,
                            SendTime = DateTime.UtcNow
                        };
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[RealPluginManager] Error finding DeviceInfo for {DeviceName}", deviceName);
        }

        return null;
    }

    /// <summary>
    /// 向远端设备发送插件调用请求。
    /// </summary>
    /// <param name="callInfo">插件调用信息</param>
    /// <param name="targetDeviceName">目标设备名称（DeviceLocator.DeviceName）</param>
    /// <returns>调用结果（解析后的对象），失败返回 null</returns>
    public object? RemoteCall(PluginCallInfo callInfo, string targetDeviceName)
    {
        const string location = $"{nameof(RealPluginManager)}.{nameof(RemoteCall)}";

        if (string.IsNullOrEmpty(targetDeviceName))
        {
            Log.Warning("[{Location}] RemoteCall: targetDeviceName is empty, falling back to local", location);
            return CallAuto(callInfo);
        }

        // 1. 查找目标 DeviceInfo
        var deviceInfo = FindDeviceInfoByName(targetDeviceName);
        if (deviceInfo == null)
        {
            Log.Error("[{Location}] RemoteCall: device not found or not connected: {DeviceName}", location, targetDeviceName);
            throw new InvalidOperationException($"Device not found or not connected: {targetDeviceName}");
        }

        // 2. 查找 token
        var token = FindDeviceToken(deviceInfo);
        if (string.IsNullOrEmpty(token))
        {
            Log.Error("[{Location}] RemoteCall: no session token for device {DeviceName}. " +
                "Device must be connected via DevicesServer first.", location, targetDeviceName);
            throw new InvalidOperationException($"Device not connected (no token): {targetDeviceName}");
        }

        // 3. 构建 Command
        var command = new Command
        {
            Request = CommandRequestInfo.ReceiveCommand,
            FunctionName = callInfo.MethodName,
            PluginConnectionId = callInfo.PluginName,  // 插件名称作为连接标识
            Tags = new System.Collections.Generic.Dictionary<string, string>
            {
                ["RequestId"] = Guid.NewGuid().ToString()
            }
        };

        // 4. 处理参数
        if (callInfo.Parameters != null && callInfo.Parameters.Length > 0)
        {
            command.FunctionArgs = new System.Collections.Generic.List<Parameter>();
            for (int i = 0; i < callInfo.Parameters.Length; i++)
            {
                var paramValue = callInfo.Parameters[i]?.ToString() ?? string.Empty;
                var paramName = callInfo.ParameterNames?.Length > i ? callInfo.ParameterNames[i] : i.ToString();
                var paramType = callInfo.ParameterTypes?.Length > i ? callInfo.ParameterTypes[i].Name.ToLower() : "string";
                command.FunctionArgs.Add(new Parameter
                {
                    Name = paramName,
                    Type = paramType,
                    Value = paramValue,
                    IsOptional = false
                });
            }
            command.Body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(command.FunctionArgs, _serializerOptions));
            command.BodyLength = command.Body.Length;
        }

        // 5. 构建 Request
        var request = new Request
        {
            Type = RequestTypes.Command,
            Version = RequestVersions.V1,
            Target = deviceInfo.Device,
            Content = JsonSerializer.Serialize(command, _serializerOptions)
        };

        // 6. 通过 DeviceHttpClient 发送 HTTP POST
        Log.Information("[{Location}] RemoteCall: invoking {Plugin}.{Method} on device {Device}",
            location, callInfo.PluginName, callInfo.MethodName, targetDeviceName);

        var response = _deviceHttpClient.InvokePluginAsync(deviceInfo, token, request).GetAwaiter().GetResult();
        if (response == null)
        {
            Log.Error("[{Location}] RemoteCall: HTTP request failed for {Plugin}.{Method}",
                location, callInfo.PluginName, callInfo.MethodName);
            throw new InvalidOperationException($"Failed to send request to device: {targetDeviceName}");
        }

        if (!response.IsSuccessStatusCode)
        {
            Log.Error("[{Location}] RemoteCall: HTTP {Status} from device {Device}: {Reason}",
                location, response.StatusCode, targetDeviceName, response.ReasonPhrase);
            throw new InvalidOperationException($"Remote invoke failed: HTTP {response.StatusCode}");
        }

        // 7. 读取响应内容
        var resultContent = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        Log.Information("[{Location}] RemoteCall: received response from {Device}: {Content}",
            location, targetDeviceName, resultContent.Length > 200
                ? resultContent.Substring(0, 200) + "..."
                : resultContent);

        // 8. 解析返回值类型并转换
        var returnType = GetFunctionReturnType(callInfo.PluginName, callInfo.MethodName);
        if (returnType == null || returnType == typeof(void))
            return null;

        return ParseResult(returnType, resultContent);
    }

    /// <summary>
    /// 向远端设备发送插件调用请求（异步版本）。
    /// </summary>
    public async Task<object?> RemoteCallAsync(PluginCallInfo callInfo, string targetDeviceName, CancellationToken ct = default)
    {
        const string location = $"{nameof(RealPluginManager)}.{nameof(RemoteCallAsync)}";

        if (string.IsNullOrEmpty(targetDeviceName))
        {
            Log.Warning("[{Location}] RemoteCallAsync: targetDeviceName is empty, falling back to local", location);
            return CallAuto(callInfo);
        }

        var deviceInfo = FindDeviceInfoByName(targetDeviceName);
        if (deviceInfo == null)
            throw new InvalidOperationException($"Device not found: {targetDeviceName}");

        var token = FindDeviceToken(deviceInfo);
        if (string.IsNullOrEmpty(token))
            throw new InvalidOperationException($"Device not connected (no token): {targetDeviceName}");

        var command = new Command
        {
            Request = CommandRequestInfo.ReceiveCommand,
            FunctionName = callInfo.MethodName,
            PluginConnectionId = callInfo.PluginName,
            Tags = new System.Collections.Generic.Dictionary<string, string>
            {
                ["RequestId"] = Guid.NewGuid().ToString()
            }
        };

        if (callInfo.Parameters != null && callInfo.Parameters.Length > 0)
        {
            command.FunctionArgs = new System.Collections.Generic.List<Parameter>();
            for (int i = 0; i < callInfo.Parameters.Length; i++)
            {
                command.FunctionArgs.Add(new Parameter
                {
                    Name = callInfo.ParameterNames?.Length > i ? callInfo.ParameterNames[i] : i.ToString(),
                    Type = callInfo.ParameterTypes?.Length > i ? callInfo.ParameterTypes[i].Name.ToLower() : "string",
                    Value = callInfo.Parameters[i]?.ToString() ?? string.Empty,
                    IsOptional = false
                });
            }
            command.Body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(command.FunctionArgs, _serializerOptions));
            command.BodyLength = command.Body.Length;
        }

        var request = new Request
        {
            Type = RequestTypes.Command,
            Version = RequestVersions.V1,
            Target = deviceInfo.Device,
            Content = JsonSerializer.Serialize(command, _serializerOptions)
        };

        Log.Information("[{Location}] RemoteCallAsync: {Plugin}.{Method} on {Device}",
            location, callInfo.PluginName, callInfo.MethodName, targetDeviceName);

        var response = await _deviceHttpClient.InvokePluginAsync(deviceInfo, token, request, ct);
        if (response == null || !response.IsSuccessStatusCode)
        {
            var reason = response?.ReasonPhrase ?? "network error";
            Log.Error("[{Location}] RemoteCallAsync failed: HTTP {Status} from {Device}",
                location, response?.StatusCode, targetDeviceName);
            throw new InvalidOperationException($"Remote invoke failed: HTTP {response?.StatusCode}");
        }

        var resultContent = await response.Content.ReadAsStringAsync(ct);
        var returnType = GetFunctionReturnType(callInfo.PluginName, callInfo.MethodName);
        if (returnType == null || returnType == typeof(void))
            return null;

        return ParseResult(returnType, resultContent);
    }

    /// <summary>
    /// 解析插件调用返回值
    /// </summary>
    private object? ParseResult(Type returnType, string result)
    {
        if (string.IsNullOrEmpty(result) || returnType == typeof(void))
            return null;

        try
        {
            if (returnType == typeof(string))
                return result;
            if (returnType == typeof(int))
                return int.Parse(result);
            if (returnType == typeof(long))
                return long.Parse(result);
            if (returnType == typeof(float))
                return float.Parse(result);
            if (returnType == typeof(double))
                return double.Parse(result);
            if (returnType == typeof(bool))
                return bool.Parse(result);
            if (returnType == typeof(object))
            {
                try
                {
                    return JsonSerializer.Deserialize<object>(result, _serializerOptions) ?? result;
                }
                catch
                {
                    return result;
                }
            }
            return JsonSerializer.Deserialize(result, returnType, _serializerOptions);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[RealPluginManager] Error parsing result as {Type}", returnType.Name);
            return result;
        }
    }
}

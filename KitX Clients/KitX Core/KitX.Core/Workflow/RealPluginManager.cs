using System;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kscript.CSharp.Parser.Core;
using Kscript.CSharp.Parser.Models;
using KitX.Core.Contract.Plugin;
using KitX.Core.Device;
using KitX.Shared.CSharp.Plugin;
using KitX.Shared.CSharp.WebCommand;
using KitX.Shared.CSharp.WebCommand.Infos;
using Serilog;

namespace KitX.Core.Workflow;

/// <summary>
/// 真实的插件管理器实现，通过 WebSocket 与插件通信
/// </summary>
public class RealPluginManager : IPluginManager
{
    private readonly PluginsServer _pluginsServer;
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
    /// 无参构造函数（供 KScript.Parser 动态创建实例使用）
    /// </summary>
    public RealPluginManager() : this(PluginsServer.Instance)
    {
    }

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="pluginsServer">插件服务器实例</param>
    public RealPluginManager(PluginsServer pluginsServer)
    {
        _pluginsServer = pluginsServer;

        // 订阅插件消息接收事件以处理响应
        _pluginsServer.PluginMessageReceived += OnPluginMessageReceived;

        // 订阅插件响应事件（当插件返回带RequestId的响应时触发）
        _pluginsServer.PluginResponse += OnPluginResponse;
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
    /// 调用插件方法（无返回值）
    /// </summary>
    /// <remarks>
    /// 使用 fire-and-forget 模式，不等待插件响应。
    /// 因为 void 返回类型的插件功能不会发送响应。
    /// </remarks>
    public void Call(PluginCallInfo callInfo)
    {
        Log.Information($"[RealPluginManager] Call() invoked (fire-and-forget): {callInfo.PluginName}.{callInfo.MethodName}");
        SendRequestWithoutWaitAsync(callInfo);
    }

    /// <summary>
    /// 调用插件方法（有返回值）
    /// </summary>
    public T Call<T>(PluginCallInfo callInfo)
    {
        Log.Information($"[RealPluginManager] Call<{typeof(T).Name}>() invoked: {callInfo.PluginName}.{callInfo.MethodName}");
        var result = CallAsync(callInfo).GetAwaiter().GetResult();
        return ParseResult<T>(result);
    }

    /// <summary>
    /// 自动调用插件方法：根据函数声明的返回类型自动选择调用策略。
    /// - void 返回类型 → fire-and-forget (Call)，不等待响应
    /// - 非 void 返回类型 → 类型化同步等待 (Call&lt;T&gt;)，返回具体类型的结果
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
            Log.Information($"[RealPluginManager] Available connections: {_pluginsServer.Connections.Count}");
            foreach (var conn in _pluginsServer.Connections)
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
            foreach (var conn in _pluginsServer.Connections)
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
        return _pluginsServer.Connections
            .FirstOrDefault(c => c.PluginInfo?.Name == pluginName);
    }
}

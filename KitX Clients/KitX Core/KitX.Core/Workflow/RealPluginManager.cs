using System;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kscript.CSharp.Parser.Core;
using Kscript.CSharp.Parser.Models;
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
    }

    /// <summary>
    /// 处理收到的插件消息
    /// </summary>
    private void OnPluginMessageReceived(object? sender, PluginMessageReceivedEventArgs e)
    {
        try
        {
            var kwc = JsonSerializer.Deserialize<Request>(e.Message, _serializerOptions);
            if (kwc?.Content is null) return;

            var command = JsonSerializer.Deserialize<Command>(kwc.Content, _serializerOptions);
            if (command.Request is null) return; // Command is a struct, check if Request is empty

            // 检查是否是响应消息
            if (command.Tags != null && command.Tags.TryGetValue("RequestId", out var requestId))
            {
                if (_pendingResponses.TryRemove(requestId, out var tcs))
                {
                    var responseBody = command.BodyLength > 0
                        ? Encoding.UTF8.GetString(command.Body.AsSpan(0, command.BodyLength))
                        : string.Empty;
                    tcs.SetResult(responseBody);
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
    public void Call(PluginCallInfo callInfo)
    {
        Log.Information($"[RealPluginManager] Call() invoked: {callInfo.PluginName}.{callInfo.MethodName}");
        CallAsync(callInfo).Wait();
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
                    command.FunctionArgs.Add(new Parameter
                    {
                        Name = i.ToString(),
                        Value = paramValue,
                        Type = callInfo.ParameterTypes?.Length > i
                            ? callInfo.ParameterTypes[i].Name.ToLower()
                            : "string"
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

            // 注意: Loader 在处理 ReceiveCommand 后不会返回响应
            // 对于 void 方法，我们直接返回空结果
            // 对于有返回值的方法，需要插件支持返回响应（当前不支持）
            return string.Empty;
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"[RealPluginManager] Error calling {callInfo.PluginName}.{callInfo.MethodName}");
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

            // 对于复杂类型，从 JSON 反序列化
            return JsonSerializer.Deserialize<T>(result, _serializerOptions)!;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, $"[RealPluginManager] Error parsing result: {result}");
            return default!;
        }
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

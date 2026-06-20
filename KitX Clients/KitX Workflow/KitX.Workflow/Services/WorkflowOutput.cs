using System.Collections.Concurrent;
using System.Text;

namespace KitX.Workflow.Services;

/// <summary>
/// 工作流脚本输出辅助类
/// 用户可以使用 WorkflowOutput.WriteLine() 来输出调试信息
/// </summary>
public static class WorkflowOutput
{
    private static readonly ConcurrentQueue<string> _outputQueue = new();

    /// <summary>
    /// 写入一行输出
    /// </summary>
    public static void WriteLine(object? value)
    {
        _outputQueue.Enqueue(value?.ToString() ?? "null");
    }

    /// <summary>
    /// 写入一行输出（格式化）
    /// </summary>
    public static void WriteLine(string format, params object[] args)
    {
        _outputQueue.Enqueue(string.Format(format, args));
    }

    /// <summary>
    /// 获取所有累积的输出并清空队列
    /// </summary>
    public static string GetAndClear()
    {
        var sb = new StringBuilder();
        while (_outputQueue.TryDequeue(out var line))
        {
            sb.AppendLine(line);
        }
        return sb.ToString();
    }

    /// <summary>
    /// 检查是否有输出
    /// </summary>
    public static bool HasOutput => !_outputQueue.IsEmpty;
}

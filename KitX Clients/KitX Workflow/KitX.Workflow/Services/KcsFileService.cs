using System.Text.Json;
using KitX.Core.Contract.Workflow;
using Serilog;

namespace KitX.Workflow.Services;

/// <summary>
/// KCS文件服务实现 - 仅负责KCS文件的读写
/// </summary>
public class KcsFileService : IKcsFileService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// 加载KCS文件
    /// </summary>
    public async System.Threading.Tasks.Task<KcsFileFormat?> LoadKcsFileAsync(string filePath)
    {
        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            var kcs = JsonSerializer.Deserialize<KcsFileFormat>(json, _jsonOptions);
            return kcs;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[KcsFileService] Error loading KCS file: {FilePath}", filePath);
            return null;
        }
    }

    /// <summary>
    /// 保存KCS文件
    /// </summary>
    public async System.Threading.Tasks.Task SaveKcsFileAsync(string filePath, KcsFileFormat kcs)
    {
        try
        {
            var json = JsonSerializer.Serialize(kcs, _jsonOptions);

            await File.WriteAllTextAsync(filePath, json);
            Log.Information("[KcsFileService] KCS file saved: {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[KcsFileService] Error saving KCS file: {FilePath}", filePath);
            throw;
        }
    }
}

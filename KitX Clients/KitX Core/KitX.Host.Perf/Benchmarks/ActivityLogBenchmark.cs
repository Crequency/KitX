using Common.Activity;
using LiteDB;
using CActivity = Common.Activity.Activity;

namespace KitX.Host.Perf.Benchmarks;

/// <summary>
/// G6 · 活动日志读取：LiteDB 临时文件库写入 10,000 条活动，对比
/// <c>FindAll().ToList()</c>（现状，全量加载）vs 按 Id 倒序 <c>Limit(100)</c>（修法，分页）。
/// 各 10 次计时。临时库放 <see cref="Path.GetTempPath"/>，跑完删除。
/// </summary>
public static class ActivityLogBenchmark
{
    private const int RecordCount = 10_000;
    private const string CollectionName = "Activities";

    public static List<string> Run()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"kitx-perf-activity-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new LiteDatabase(dbPath);
            var col = db.GetCollection<CActivity>(CollectionName);
            for (var i = 0; i < RecordCount; i++)
            {
                col.Insert(new CActivity
                {
                    Id = i + 1,
                    Name = "AppLifetime",
                    Author = "KitX",
                    Title = "Activity " + i,
                    Category = "DashboardEvent",
                });
            }
            col.EnsureIndex(x => x.Id);

            var findAll = Benchmark.Measure(2, 10, () => FindAll(col));
            var limit = Benchmark.Measure(2, 10, () => Limit(col));

            return
            [
                findAll.Row("G6 活动日志读取 · 现状 FindAll", $"{RecordCount} 条全量加载，× 10 次"),
                limit.Row("G6 活动日志读取 · 修法 Limit(100)", $"{RecordCount} 条中取 100，× 10 次"),
            ];
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    private static void FindAll(ILiteCollection<CActivity> col)
    {
        var all = col.FindAll().ToList();
        if (all.Count != RecordCount)
            throw new InvalidOperationException($"FindAll 返回 {all.Count} 条，预期 {RecordCount}");
    }

    private static void Limit(ILiteCollection<CActivity> col)
    {
        var page = col.Query().OrderByDescending(x => x.Id).Limit(100).ToList();
        if (page.Count != 100)
            throw new InvalidOperationException($"Limit 返回 {page.Count} 条，预期 100");
    }
}

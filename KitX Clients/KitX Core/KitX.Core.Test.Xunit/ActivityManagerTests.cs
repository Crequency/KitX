using System.Linq;
using CActivity = Common.Activity.Activity;
using KitX.Core.Activity;
using LiteDB;

namespace KitX.Core.Test.Xunit;

/// <summary>
/// G6 回归：ActivityManager 读取分页化 + 写入保留策略。
/// 覆盖：倒序分页（Limit/Skip 组合）、limit<=0 全量、CountActivities、
/// 超出上限裁剪最旧、清库后读取为空。
/// 使用内存 LiteDB（:memory:），不触碰真实数据目录。
/// 注意：LiteDB 对 int 主键 0 视为"未设置"并自动分配（从 1 起），
/// 因此 Seed 的 Id 一律从 1 开始；Retention 用高位 Id 段避开
/// NextActivityId 静态计数器生成的低位 Id。
/// </summary>
public class ActivityManagerTests : IDisposable
{
    private const int SeedIdBase = 1_000_000;

    private readonly LiteDatabase _db;

    public ActivityManagerTests()
    {
        _db = new LiteDatabase(":memory:");
        ActivityManager.ActivitiesDatabase = _db;
    }

    public void Dispose()
    {
        ActivityManager.ActivitiesDatabase = null;
        _db.Dispose();
    }

    private void Seed(int count, int idBase = 1)
    {
        var col = _db.GetCollection<CActivity>(ActivityManager.CollectionName);
        for (var i = 0; i < count; i++)
            col.Insert(new CActivity { Id = idBase + i, Name = $"A{i}", Author = "x", Title = "t", Category = "c" });
    }

    private ILiteCollection<CActivity> Collection => _db.GetCollection<CActivity>(ActivityManager.CollectionName);

    [Fact]
    public void ReadActivities_Pages_Newest_First()
    {
        Seed(10);

        var page0 = ActivityManager.ReadActivities(3, 0);
        Assert.Equal(new[] { 10, 9, 8 }, page0.Select(a => a.Id));

        var page1 = ActivityManager.ReadActivities(3, 3);
        Assert.Equal(new[] { 7, 6, 5 }, page1.Select(a => a.Id));

        var last = ActivityManager.ReadActivities(2, 8);
        Assert.Equal(new[] { 2, 1 }, last.Select(a => a.Id));
    }

    [Fact]
    public void ReadActivities_LimitZero_Returns_All_Newest_First()
    {
        Seed(5);

        var all = ActivityManager.ReadActivities();
        Assert.Equal(5, all.Count);
        Assert.Equal(new[] { 5, 4, 3, 2, 1 }, all.Select(a => a.Id));
    }

    [Fact]
    public void CountActivities_Reflects_Store()
    {
        Seed(7);
        Assert.Equal(7, ActivityManager.CountActivities());
    }

    [Fact]
    public void Retention_Policy_Trims_Oldest()
    {
        // 5,000 high-Id rows at the cap plus 1,000 older low-Id rows (over-cap excess).
        // TrimToCap must delete exactly the oldest excess (smallest Ids) and settle the
        // store at the cap. Deterministic by construction — no fire-and-forget write path.
        Seed(1000, 1);
        Seed(5000, SeedIdBase);
        Assert.Equal(6000, Collection.LongCount());

        ActivityManager.TrimToCap();

        Assert.Equal(5000, Collection.LongCount());
        Assert.Equal(5000, ActivityManager.CountActivities());

        // The 1,000 oldest rows (Ids below SeedIdBase) were removed.
        Assert.Equal(SeedIdBase, Collection.Query().OrderBy(a => a.Id).FirstOrDefault()!.Id);
        Assert.Null(Collection.FindOne(a => a.Id < SeedIdBase));
    }

    [Fact]
    public void TrimToCap_OnBoundedStore_IsNoOp()
    {
        Seed(100);
        ActivityManager.TrimToCap();
        Assert.Equal(100, Collection.LongCount());
    }

    [Fact]
    public void Clearing_The_Store_Reads_Empty()
    {
        Seed(5);
        Assert.Equal(5, ActivityManager.CountActivities());

        Collection.DeleteAll();

        Assert.Empty(ActivityManager.ReadActivities());
        Assert.Equal(0, ActivityManager.CountActivities());
    }
}

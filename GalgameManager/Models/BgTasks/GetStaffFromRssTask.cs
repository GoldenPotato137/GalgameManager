using System.Collections.Concurrent;
using GalgameManager.Enums;
using GalgameManager.Helpers;
using GalgameManager.Services;

namespace GalgameManager.Models.BgTasks;

public class GetStaffFromRssTask (StaffService staffService) : BgTaskBase
{
    public override string Title => "GetStaffFromRssTask_Title".GetLocalized();
    public ConcurrentQueue<(Staff, RssType)> GetStaffQueue = new();
    public int MaxRunning = 3;
    private readonly ConcurrentBag<Staff> _fetchingStaffs = [];
    private readonly object _changeMsgLock = new();

    /// 添加一个staff到解析队列中
    public void AddStaff(Staff staff, RssType rss)
    {
        GetStaffQueue.Enqueue((staff, rss));
    }

    protected override Task RecoverFromJsonInternal() => Task.CompletedTask;

    protected override Task RunInternal()
    {
        return Task.Run((async Task () =>
        {
            await Task.Delay(500); //防止创建任务时立即结束，来不及添加staff
            while (true)
            {
                if (GetStaffQueue.IsEmpty && _fetchingStaffs.IsEmpty) break;
                while (_fetchingStaffs.Count < MaxRunning && GetStaffQueue.TryDequeue(out (Staff staff, RssType rss) s))
                {
                    _fetchingStaffs.Add(s.staff);
                    _ = Task.Run(async ()=>
                    {
                        await staffService.ParseStaffAsync(s.staff, s.rss);
                    }).ContinueWith(t =>
                    {
                        _fetchingStaffs.TryTake(out _);
                        UpdateProgressMsg();
                    });
                }
                UpdateProgressMsg();
                await Task.Delay(500);
            }
            ChangeProgress(1, 1, string.Empty, false); //触发任务完成提醒
        })!);

        void UpdateProgressMsg()
        {
            lock (_changeMsgLock)
            {
                var msg = string.Empty;
                msg += "GetStaffFromRssTask_Progress".GetLocalized(GetStaffQueue.Count);
                foreach (Staff staff in _fetchingStaffs)
                    msg += $"{staff.Name} ";
                msg += $"\n{"GetStaffFromRssTask_Progress_Waiting".GetLocalized(GetStaffQueue.Count)}";
                ChangeProgress(_fetchingStaffs.Count, _fetchingStaffs.Count + GetStaffQueue.Count, msg);
            }
        }
    }

    public override bool OnSearch(string key) => true;
}
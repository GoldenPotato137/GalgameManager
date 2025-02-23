using GalgameManager.Contracts.Phrase;
using GalgameManager.Contracts.Services;
using GalgameManager.Enums;
using GalgameManager.Helpers;
using GalgameManager.Models;
using GalgameManager.Models.BgTasks;
using LiteDB;
using Microsoft.UI.Xaml.Controls;

namespace GalgameManager.Services;

public class StaffService : IStaffService
{
    public event Action<Galgame>? OnGameStaffChanged;
    private ILiteCollection<Staff> _dbSet = null!;
    private readonly ILocalSettingsService _settingsService;
    private readonly IGalgameCollectionService _galgameService;
    private readonly IBgTaskService _bgTaskService;
    private readonly IInfoService _infoService;
    private readonly Dictionary<Guid, Staff> _staffs = new();

    public StaffService(IGalgameCollectionService galgameService, IBgTaskService bgTaskService,
        ILocalSettingsService settingsService, IInfoService infoService)
    {
        _settingsService = settingsService;
        _galgameService = galgameService;
        _bgTaskService = bgTaskService;
        _infoService = infoService;
        
        galgameService.PhrasedEvent2 += OnGalgamePhrasedEvent;
        galgameService.GalgameAddedEvent += OnGalgamePhrasedEvent;
        galgameService.GalgameDeletedEvent += OnGalgameDeletedEvent;
    }
    
    public async Task InitAsync()
    {
        _dbSet = _settingsService.Database.GetCollection<Staff>("staff");
        await Task.Run(() =>
        {
            foreach (Staff staff in _dbSet.Include(x => x.Games).FindAll())
            {
                _staffs[staff.Id] = staff;
                List<StaffGame> toRemove = [];
                foreach (StaffGame game in staff.Games)
                {
                    Galgame? tmp = _galgameService.GetGalgameFromUuid(game.LoadedGameId);
                    if (tmp is not null) game.Game = tmp;
                    else toRemove.Add(game);
                }
                foreach (StaffGame game in toRemove) staff.Games.Remove(game);
            }
        });
    }

    public Staff? GetStaff(Guid? id) => id is null ? null : _staffs.GetValueOrDefault(id.Value);
    
    public Staff? GetStaff(StaffIdentifier identifier)
    {
        Staff? result = null;
        var maxScore = 0;
        foreach (Staff staff in _staffs.Values)
        {
            if (identifier.Match(staff) <= maxScore) continue;
            result = staff;
            maxScore = identifier.Match(staff);
        }
        return result;
    }

    public List<Staff> GetStaffs(Galgame game)
    {
        List<Staff> result = [];
        result.AddRange(_staffs.Values.Where(staff => staff.Games.Any(x => x.Game.Uuid == game.Uuid)));
        return result;
    }

    public async Task<Staff> ParseStaffAsync(Staff staff, RssType rss)
    {
        if (_galgameService.PhraserList[(int)rss] is not IGalStaffParser phraser) return staff;
        try
        {
            Staff? result = await phraser.GetStaffAsync(staff);
            if (result is null) return staff;
            var imagePath = await DownloadHelper.DownloadAndSaveImageAsync(staff.ImageUrl);
            await UiThreadInvokeHelper.InvokeAsync(() =>
            {
                staff.Ids = result.Ids;
                staff.JapaneseName = result.JapaneseName;
                staff.EnglishName = result.EnglishName;
                staff.ChineseName = result.ChineseName;
                staff.Gender = result.Gender;
                staff.Career.SyncCollection(result.Career);
                staff.ImagePath = imagePath;
                staff.ImageUrl = result.ImageUrl;
                staff.Description = result.Description;
                staff.BirthDate = result.BirthDate;
            });
            Save(staff);
        }
        catch (Exception e)
        {
            _infoService.Log(InfoBarSeverity.Informational, $"failed on parsing staff: {e}");
        }
        return staff;
    }

    public void Save(Staff staff)
    {
        _staffs[staff.Id] = staff;
        _dbSet.Upsert(staff);
    }

    public void Delete(Staff staff)
    {
        _staffs.Remove(staff.Id);
        _dbSet.Delete(staff.Id);
    }
    
    private async void OnGalgamePhrasedEvent(Galgame galgame)
    {
        try
        {
            IGalStaffParser? phraser = _galgameService.PhraserList[(int)galgame.RssType] as IGalStaffParser;
            if (phraser is null) return;
            List<StaffRelation> tmpStaffs = await phraser.GetStaffsAsync(galgame);
            {
                // 一个人可能身兼多职，需要合并
                List<StaffRelation> tmpToRemove = [];
                foreach (StaffRelation staff in tmpStaffs)
                {
                    if(tmpToRemove.Contains(staff)) continue;
                    foreach (StaffRelation staff2 in tmpStaffs)
                    {
                        if (staff == staff2 || staff.GetIdentifier().Match(staff2) == 0) continue;
                        foreach(Career c in staff2.Career.Where(c => !staff.Career.Contains(c)))
                            staff.Career.Add(c);
                        foreach (Career c in staff2.Relation.Where(c => !staff.Relation.Contains(c)))
                            staff.Relation.Add(c);
                        tmpToRemove.Add(staff2);
                    }
                }
                foreach (StaffRelation staff in tmpToRemove)
                    tmpStaffs.Remove(staff);
            }
            List<Staff> toFetch = [];
            foreach (StaffRelation staff in tmpStaffs)
            {
                Staff? tmp = GetStaff(staff.GetIdentifier());
                if (tmp is null)
                {
                    tmp = staff;
                    toFetch.Add(tmp);
                }
                else if (tmp.ImagePath is null) toFetch.Add(tmp);
                tmp.AddGame(galgame, staff.Relation);
                Save(tmp);
            }
            // 去掉不在搜刮到的人员列表里的但曾经记录是该游戏staff的人员
            List<Staff> toRemove = _staffs.Values
                .Where(x => x.Games.Any(g => g.Game.Uuid == galgame.Uuid))
                .Where(x => tmpStaffs.All(y => y.GetIdentifier().Match(x) == 0))
                .ToList();
            foreach (Staff staff in toRemove)
            {
                staff.RemoveGame(galgame);
                Save(staff);
            }

            OnGameStaffChanged?.Invoke(galgame);
            GetStaffFromRssTask? task = _bgTaskService.GetBgTask<GetStaffFromRssTask>(string.Empty);
            if (task is null)
            {
                task = new(this);
                _ =  _bgTaskService.AddBgTask(task);
            }
            foreach (Staff staff in toFetch)
                task.AddStaff(staff, galgame.RssType);
        }
        catch (HttpRequestException)
        {
            //ignore
        }
        catch (Exception e)
        {
            _infoService.DeveloperEvent(msg: "failed on listening galgame phrased event", e: e);
        }
    }
    
    private void OnGalgameDeletedEvent(Galgame galgame)
    {
        Task.Run(() =>
        {
            try
            {
                foreach (Staff staff in _staffs.Values.Where(x => x.Games.Any(g => g.Game.Uuid == galgame.Uuid)))
                {
                    UiThreadInvokeHelper.Invoke(() =>
                    {
                        List<StaffGame> toRemove = staff.Games.Where(x => x.Game.Uuid == galgame.Uuid).ToList();
                        foreach (StaffGame game in toRemove)
                            staff.Games.Remove(game);
                    });
                    if (staff.Games.Count > 0) Save(staff);
                    else Delete(staff);
                }
            }
            catch (Exception e)
            {
                _infoService.DeveloperEvent(msg: "failed on listening galgame deleted event", e: e);
            }
        });
    }
}
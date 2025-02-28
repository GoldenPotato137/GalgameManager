using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using GalgameManager.Contracts.Services; 
using GalgameManager.Enums;
using GalgameManager.Helpers;
using GalgameManager.Models;
using GalgameManager.Models.BgTasks;
using GalgameManager.Models.Sources;
using LiteDB;
using Microsoft.UI.Xaml.Controls;
using Newtonsoft.Json;

namespace GalgameManager.Services;

public class GalgameSourceCollectionService(
    ILocalSettingsService localSettingsService,
    IBgTaskService bgTaskService,
    IInfoService infoService)
    : IGalgameSourceCollectionService
{
    public Action<GalgameSourceBase>? OnSourceDeleted { get; set; }
    public Action? OnSourceChanged { get; set; }

    private ObservableCollection<GalgameSourceBase> _galgameSources = new();

    private readonly List<JsonConverter> _converters =
    [
        new GalgameAndUidConverter(),
        new GalgameSourceCustomConverter(),
    ];
    private ILiteCollection<GalgameSourceBase> _dbSet = null!;

    public async Task InitAsync()
    {
        _dbSet = localSettingsService.Database.GetCollection<GalgameSourceBase>("source");
        LocalSettingStatus settingStatus = await localSettingsService.ReadSettingAsync<LocalSettingStatus>
            (KeyValues.DataStatus, true) ?? new();
        await LiteDbUpgrade(settingStatus);
        LoadData();
        await SourceUpgradeAsync(settingStatus);
        foreach (GalgameSourceBase source in _galgameSources) // 部分崩溃的情况可能导致source里面部分galgame为null
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            List<GalgameAndPath> tmp = source.Galgames.Where(g => g.Galgame is null).ToList();
            foreach (GalgameAndPath g in tmp)
            {
                source.Galgames.Remove(g);
                infoService.DeveloperEvent(InfoBarSeverity.Error,
                    "GalgameSourceCollectionService_InitAsync_GalgameIsNull".GetLocalized(g.Path, source.Url));
            }
        }
        // 去除找不到的库
        List<GalgameSourceBase> toRemove = _galgameSources.Where(source => !Directory.Exists(source.Path)).ToList();
        if (toRemove.Count > 0)
        {
            foreach (GalgameSourceBase source in toRemove)
                _galgameSources.Remove(source);
            infoService.Event(EventType.GalgameEvent, InfoBarSeverity.Warning,
                "GalgameSourceCollectionService_RemoveNonExist_Title".GetLocalized(),
                msg: "GalgameSourceCollectionService_RemoveNonExist_Msg".GetLocalized(
                    $"\n{string.Join('\n', toRemove.Select(s => s.Path))}"));
        }
        await ImportAsync(settingStatus);
        // 给Galgame注入Source列表
        foreach (GalgameSourceBase s in _galgameSources)
            foreach (Galgame g in s.GetGalgameList().Where(g => !g.Sources.Contains(s)))
                g.Sources.Add(s);
        // 计算子库
        CalcSubSources();
        // 添加监听变动检测
        foreach (GalgameSourceBase s in _galgameSources)
        {
            s.DetectChanged += DetectionChanged;
            DetectionChanged(s); // 手动触发一次，挂上监听（如果这个库之前有设置监听需求）
        }
        return;

        void LoadData()
        {
            _galgameSources.Clear();
            _galgameSources.SyncCollection(_dbSet.FindAll().ToList());
            IGalgameCollectionService gameService = App.GetService<IGalgameCollectionService>();
            foreach (GalgameSourceBase source in _galgameSources)
            {
                foreach (GalgameAndPathDbDto dto in source.GetLoadedGalgames())
                {
                    if (gameService.GetGalgameFromUuid(dto.GalgameId) is { } game)
                        source.Galgames.Add(new GalgameAndPath(game, dto.Path));
                    else
                    {
                        infoService.Event(EventType.NotCriticalUnexpectedError, InfoBarSeverity.Warning,
                            title: "GalgameSourceCollectionService_NotSuchGameUUID_Title".GetLocalized(),
                            msg: "GalgameSourceCollectionService_NotSuchGameUUID_Msg".GetLocalized(dto.Path,
                                source.Name));
                        Save(source);
                    }
                }
            }
        }
    }

    public async Task StartAsync()
    {
        // 检查所有库中的游戏是否还在源中
        List<(Task<List<Galgame>>, GalgameSourceBase)> sourceCheckTasks = new();
        foreach (GalgameSourceBase source in _galgameSources)
            sourceCheckTasks.Add((CheckGamesInSourceAsync(source), source));
        foreach ((Task<List<Galgame>> task, GalgameSourceBase source) t in sourceCheckTasks)
        {
            try
            {
                List<Galgame> removedGames = await t.task;
                if (removedGames.Count > 0)
                    infoService.Event(EventType.GalgameEvent, InfoBarSeverity.Warning,
                        "GalgameSourceCollectionService_CheckGamesInSource_Title".GetLocalized(t.source.Name),
                        msg: "GalgameSourceCollectionService_CheckGamesInSource_Msg".GetLocalized(
                            $"\n{string.Join('\n', removedGames.Select(g => g.Name.Value))}"));
            }
            catch (Exception e)
            {
                infoService.Event(EventType.NotCriticalUnexpectedError, InfoBarSeverity.Error,
                    title: "GalgameSourceCollectionService_CheckGamesInSourceFailed".GetLocalized(t.source.Name),
                    exception: e);
            }
        }
        
        foreach (GalgameSourceBase source in _galgameSources.Where(f => f.ScanOnStart)) 
            _ = bgTaskService.AddBgTask(new GetGalgameInSourceTask(source));
    }
    
    public ObservableCollection<GalgameSourceBase> GetGalgameSources() => _galgameSources;
    
    public GalgameSourceBase? GetGalgameSourceFromUrl(string url)
    {
        try
        {
            (GalgameSourceType type, var path) = GalgameSourceBase.ResolveUrl(url);
            return GetGalgameSource(type, path);
        }
        catch (Exception e)
        {
            infoService.Event(EventType.NotCriticalUnexpectedError, InfoBarSeverity.Error, e.Message, e);
            return null;
        }
        
    }

    public GalgameSourceBase? GetGalgameSource(GalgameSourceType type, string path)
    {
        IEnumerable<GalgameSourceBase> tmp = _galgameSources.Where(s => s.SourceType == type);
        switch (type)
        {
            case GalgameSourceType.LocalFolder:
                return tmp.FirstOrDefault(s => Utils.ArePathsEqual(s.Path, path));
            case GalgameSourceType.UnKnown:
            case GalgameSourceType.LocalZip:
            default:
                return tmp.FirstOrDefault(s => s.Path == path);
        }
    }

    public async Task<GalgameSourceBase> AddGalgameSourceAsync(GalgameSourceType sourceType, string path,
        bool tryGetGalgame = true)
    {
        if (_galgameSources.Any(galFolder => galFolder.Path == path && galFolder.SourceType == sourceType))
        {
            throw new PvnException($"这个galgame库{sourceType.SourceTypeToString()}://{path}已经添加过了");
        }

        GalgameSourceBase? galgameSource;

        switch (sourceType)
        {
            case GalgameSourceType.UnKnown:
                throw new ArgumentOutOfRangeException(nameof(sourceType), sourceType, null);
            case GalgameSourceType.LocalFolder:
                galgameSource = new GalgameFolderSource(path);
                break;
            case GalgameSourceType.LocalZip:
                galgameSource = new GalgameZipSource(path);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(sourceType), sourceType, null);
        }
        _galgameSources.Add(galgameSource);
        Save(galgameSource);
        if (tryGetGalgame)
        {
            await bgTaskService.AddBgTask(new GetGalgameInSourceTask(galgameSource));
        }
        
        CalcSubSources();
        galgameSource.DetectChanged += DetectionChanged;
        DetectionChanged(galgameSource); // 手动触发一次，挂上监听
        OnSourceChanged?.Invoke();
        
        return galgameSource;
    }
    
    public async Task DeleteGalgameFolderAsync(GalgameSourceBase source)
    {
        var delete = false;
        ContentDialog dialog = new()
        {
            XamlRoot = App.MainWindow!.Content.XamlRoot,
            Title = "GalgameFolderCollectionService_DeleteGalgameFolderAsync_Title".GetLocalized(),
            Content = "GalgameFolderCollectionService_DeleteGalgameFolderAsync_Content".GetLocalized(),
            PrimaryButtonText = "Yes".GetLocalized(),
            SecondaryButtonText = "Cancel".GetLocalized(),
            PrimaryButtonCommand = new RelayCommand(() => delete = true),
            DefaultButton = ContentDialogButton.Secondary
        };
        await dialog.ShowAsync();
        if (!delete || !_galgameSources.Contains(source)) return;
        
        try
        {
            List<Galgame> srcGames = source.GetGalgameList().ToList();
            foreach (Galgame galgame in srcGames) 
                MoveOutNoOperate(source, galgame);
        }
        catch (Exception e)
        {
            infoService.DeveloperEvent(InfoBarSeverity.Error,
                msg: $"Failed to move game out of source {source.Url}\n{e.StackTrace}");
        }
        _galgameSources.Remove(source);
        _dbSet.Delete(source.Id);
        CalcSubSources();
        source.Detect = false; // 关掉监听，触发取消监听事件
        OnSourceDeleted?.Invoke(source);
        OnSourceChanged?.Invoke();
    }

    public void MoveInNoOperate(GalgameSourceBase target, Galgame game, string path)
    {
        if (game.Sources.Any(s => s == target))
        {
            infoService.DeveloperEvent(
                e: new PvnException($"Can not move game {game.Name.Value} into source {target.Path}: already there"));
            return;
        }
        target.AddGalgame(game, path);
        Save(target);
    }

    public void MoveOutNoOperate(GalgameSourceBase target, Galgame game)
    {
        if (game.Sources.All(s => s != target))
        {
            infoService.DeveloperEvent(e: new PvnException($"Can not move game {game.Name} " +
                                                            $"out of source {target.Path}: not in source"));
            return;
        }
        target.DeleteGalgame(game);
        Save(target);
    }

    public BgTaskBase MoveAsync(GalgameSourceBase? moveInSrc, string? moveInPath, GalgameSourceBase? moveOutSrc, Galgame game)
    {
        if (game.Sources.Any(s => s == moveInSrc))
        {
            infoService.DeveloperEvent(e: new PvnException($"{game.Name.Value} is already in {moveInSrc!.Url}"));
            moveInSrc = null;
            moveInPath = null;
        }
        if (moveOutSrc is not null && game.Sources.All(s => s != moveOutSrc))
        {
            infoService.DeveloperEvent(e: new PvnException($"{game.Name.Value} is not in {moveOutSrc.Url}"));
            moveOutSrc = null;
        }
        SourceMoveTask task = new(game, moveInSrc, moveInPath, moveOutSrc);
        bgTaskService.AddBgTask(task);
        return task;
    }

    public string GetSourcePath(GalgameSourceType type, string gamePath)
    {
        switch (type)
        {
            case GalgameSourceType.LocalFolder or GalgameSourceType.LocalZip:
                return Directory.GetParent(gamePath)!.FullName;
            default:
                throw new NotImplementedException();
        }
    }

    public async Task ExportAsync(Action<string, int, int>? progress)
    {
        ObservableCollection<GalgameSourceBase> exportData = new();
        for (var i = 0; i < _galgameSources.Count; i++)
        {
            GalgameSourceBase source = _galgameSources[i];
            progress?.Invoke("GalgameSourceCollectionService_Export_Progress".GetLocalized(source.Name), i + 1,
                _galgameSources.Count);
            GalgameSourceBase clone = source.DeepClone(new JsonSerializerSettings { Converters = _converters });
            clone.ImagePath = await localSettingsService.AddImageToExportAsync(clone.ImagePath);
            exportData.Add(clone);
        }

        await localSettingsService.AddToExportAsync(KeyValues.GalgameSources, exportData, converters: _converters);
    }

    /// <summary>
    /// 扫描所有库
    /// </summary>
    public void ScanAll()
    {
        foreach(GalgameSourceBase b in _galgameSources)
            bgTaskService.AddBgTask(new GetGalgameInSourceTask(b));

    }

    /// <summary>   
    /// 扫描某个库
    /// </summary>
    /// <param name="source"></param>
    public void Scan(GalgameSourceBase source)
    {
        bgTaskService.AddBgTask(new GetGalgameInSourceTask(source));

    }
    
    /// <summary>
    /// 保存所有游戏库
    /// </summary>
    private async Task SaveAllAsync() => await Task.Run(() => { _dbSet.Upsert(_galgameSources); });

    public void Save(GalgameSourceBase source) => _dbSet.Upsert(source);

    /// <summary>
    /// 重新计算所有库的归属关系
    /// </summary>
    private void CalcSubSources()
    {
        // 确实有O(nlogn)的写法，但不是特别有必要，先O(n^2)吧
        foreach (GalgameSourceBase src in _galgameSources)
        {
            src.ParentSource = null;
            src.SubSources.Clear();
        }

        foreach (GalgameSourceBase src in _galgameSources)
        {
            GalgameSourceBase? target = null;
            foreach (GalgameSourceBase current in _galgameSources)
                if (src != current && Utils.IsPathContained(current.Path, src.Path) &&
                    (target is null || current.Path.Length > target.Path.Length))
                    target = current;
            src.ParentSource = target;
            target?.SubSources.Add(src);
        }
    }

    /// 检查某个源的游戏是否还在源中，如果不在则移出
    private Task<List<Galgame>> CheckGamesInSourceAsync(GalgameSourceBase source)
    {
        switch (source.SourceType)
        {
            case GalgameSourceType.LocalFolder:
                return Task.Run(() =>
                {
                    IEnumerable<Galgame> gamesToRemove =
                        from gal in source.Galgames
                        where !Directory.Exists(gal.Path)
                        select gal.Galgame;
                    List<Galgame> toRemove = gamesToRemove.ToList();
                    foreach (Galgame g in toRemove)
                        MoveOutNoOperate(source, g);
                    return toRemove;
                });
            case GalgameSourceType.LocalZip:
            case GalgameSourceType.UnKnown:
            default:
                throw new NotSupportedException();
        }
    }

    private async Task ImportAsync(LocalSettingStatus status)
    {
        if (status.ImportGalgameSource) return;
        foreach (GalgameSourceBase source in _galgameSources)
        {
            source.ImagePath = await localSettingsService.GetImageFromImportAsync(source.ImagePath);
        }
        status.ImportGalgameSource = true;
        await SaveAllAsync();
        await localSettingsService.SaveSettingAsync(KeyValues.DataStatus, status, true);
    }

    #region DETECTION SOURCE CHANGE

    private void DetectionChanged(GalgameSourceBase source)
    {
        Task.Run(async () =>
        {
            try
            {
                IGalgameSourceService srcHandler = SourceServiceFactory.GetSourceService(source.SourceType);
                await srcHandler.RemoveListenAsync(source); // 先移除旧有监听
                if (source.Detect) await srcHandler.AddListenAsync(source);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        });
    }

    #endregion
    
    #region UPGRADES
    
    /// <summary>
    /// <b>since v.1.8.0</b><br/>
    /// 1. 修改存储库的结构（data.galgameFolders.json -> data.galgameSources.json, GalgameFolder -> GalgameSourceBase）<br/>
    /// 2. 给各库命名<br/>
    /// 3. 将galgame源归属记录从galgame移入source管理 <br/>
    /// </summary>
    private async Task SourceUpgradeAsync(LocalSettingStatus status)
    {
        if (status.GalgameSourceFormatUpgrade) return;
        // 修改存储库结构
        try
        {
            var template = new[] // 旧的GalgameFolder存储库结构
            {
                new
                {
                    Path = string.Empty,
                    ScanOnStart = false,
                },
            };
            var tmp = await localSettingsService.ReadOldSettingAsync(KeyValues.GalgameFolders, template);
            if (tmp is not null)
            {
                foreach (var folder in tmp.Where(f => !string.IsNullOrEmpty(f.Path)))
                {
                    GalgameFolderSource source = new(folder.Path) { ScanOnStart = folder.ScanOnStart };
                    _galgameSources.Add(source);
                }
            }
            await localSettingsService.RemoveSettingAsync(KeyValues.GalgameFolders, true);
        }
        catch (Exception e) //不应该发生
        {
            infoService.Event(EventType.UpgradeError, InfoBarSeverity.Warning, "升级游戏库数据库结构失败", e);
        }
        // 给各库命名
        {
            foreach (GalgameSourceBase src in _galgameSources)
                src.SetNameFromPath();
        }
        // 将游戏搬入对应的源中
        {
            IList<Galgame> games = App.GetService<IGalgameCollectionService>().Galgames;
            foreach (Galgame g in games)
            {
#pragma warning disable CS0618 // 类型或成员已过时，升级旧数据使用
                var gamePath = g.Path;
#pragma warning restore CS0618 // 类型或成员已过时
                if (!string.IsNullOrEmpty(gamePath))
                {
                    var folderPath = Path.GetDirectoryName(gamePath);
                    if (string.IsNullOrEmpty(folderPath))
                    {
                        infoService.Event(EventType.NotCriticalUnexpectedError, InfoBarSeverity.Error,
                            "UnexpectedEvent".GetLocalized(),
                            new PvnException($"Can not get the parent folder of the game{gamePath}"));
                        continue;
                    }

                    GalgameSourceBase? source = GetGalgameSource(GalgameSourceType.LocalFolder, folderPath);
                    source ??= await AddGalgameSourceAsync(GalgameSourceType.LocalFolder, folderPath);
                    MoveInNoOperate(source, g, gamePath);
                }
            }
        }
        
        await SaveAllAsync();
        status.GalgameSourceFormatUpgrade = true;
        await localSettingsService.SaveSettingAsync(KeyValues.DataStatus, status, true);
    }
    
    /// <summary>
    /// 升级为Litedb存储数据，since v1.9.0
    /// </summary>
    private async Task LiteDbUpgrade(LocalSettingStatus status)
    {
        if (status.SourceLiteDbUpgrade) return;
        try
        {
            _galgameSources = await localSettingsService.ReadSettingAsync<ObservableCollection<GalgameSourceBase>>
                (KeyValues.GalgameSources, true, converters: _converters) ?? new();
            await SaveAllAsync();
            await localSettingsService.RemoveSettingAsync(KeyValues.GalgameSources, true);
        }
        catch (Exception e)
        {
            infoService.Event(EventType.AppError, InfoBarSeverity.Error, "Source LiteDB upgrade failed", e);
        }
        status.SourceLiteDbUpgrade = true;
        await localSettingsService.SaveSettingAsync(KeyValues.DataStatus, status, true);
    }
    
    #endregion
}


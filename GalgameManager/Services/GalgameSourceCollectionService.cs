﻿using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using GalgameManager.Contracts.Services; 
using GalgameManager.Enums;
using GalgameManager.Helpers;
using GalgameManager.Models;
using GalgameManager.Models.BgTasks;
using GalgameManager.Models.Sources;
using Microsoft.UI.Xaml.Controls;
using Newtonsoft.Json;

namespace GalgameManager.Services;

public class GalgameSourceCollectionService : IGalgameSourceCollectionService
{
    public Action<GalgameSourceBase>? OnSourceDeleted { get; set; }
    public Action? OnSourceChanged { get; set; }

    private ObservableCollection<GalgameSourceBase> _galgameSources = new();
    private readonly ILocalSettingsService _localSettingsService;
    private readonly IBgTaskService _bgTaskService;
    private readonly IInfoService _infoService;
    private readonly List<JsonConverter> _converters;

    public GalgameSourceCollectionService(ILocalSettingsService localSettingsService, IBgTaskService bgTaskService,
        IInfoService infoService)
    {
        _localSettingsService = localSettingsService;
        _bgTaskService = bgTaskService;
        _infoService = infoService;
        App.OnAppClosing += async () => await Save();
        _converters =
        [
            new GalgameAndUidConverter(),
            new GalgameSourceCustomConverter(),
        ];
    }
    
    public async Task InitAsync()
    {
        _galgameSources = await _localSettingsService.ReadSettingAsync<ObservableCollection<GalgameSourceBase>>(
                              KeyValues.GalgameSources, true,
                              converters: _converters)
                          ?? new ObservableCollection<GalgameSourceBase>();
        LocalSettingStatus settingStatus = await _localSettingsService.ReadSettingAsync<LocalSettingStatus>
            (KeyValues.DataStatus, true) ?? new();
        await SourceUpgradeAsync(settingStatus);
        foreach (GalgameSourceBase source in _galgameSources) // 部分崩溃的情况可能导致source里面部分galgame为null
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            List<GalgameAndPath> tmp = source.Galgames.Where(g => g.Galgame is null).ToList();
            foreach (GalgameAndPath g in tmp)
            {
                source.Galgames.Remove(g);
                _infoService.DeveloperEvent(InfoBarSeverity.Error,
                    "GalgameSourceCollectionService_InitAsync_GalgameIsNull".GetLocalized(g.Path, source.Url));
            }
        }
        // 去除找不到的库
        List<GalgameSourceBase> toRemove = _galgameSources.Where(source => !Directory.Exists(source.Path)).ToList();
        if (toRemove.Count > 0)
        {
            foreach (GalgameSourceBase source in toRemove)
                _galgameSources.Remove(source);
            _infoService.Event(EventType.GalgameEvent, InfoBarSeverity.Warning,
                "GalgameSourceCollectionService_RemoveNonExist_Title".GetLocalized(),
                msg: "GalgameSourceCollectionService_RemoveNonExist_Msg".GetLocalized(
                    $"\n{string.Join('\n', toRemove.Select(s => s.Path))}"));
        }
        
        // 给Galgame注入Source列表
        foreach (GalgameSourceBase s in _galgameSources)
            foreach (Galgame g in s.GetGalgameList().Where(g => !g.Sources.Contains(s)))
                g.Sources.Add(s);
        // 计算子库
        CalcSubSources();
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
                    _infoService.Event(EventType.GalgameEvent, InfoBarSeverity.Warning,
                        "GalgameSourceCollectionService_CheckGamesInSource_Title".GetLocalized(t.source.Name),
                        msg: "GalgameSourceCollectionService_CheckGamesInSource_Msg".GetLocalized(
                            $"\n{string.Join('\n', removedGames.Select(g => g.Name.Value))}"));
            }
            catch (Exception e)
            {
                _infoService.Event(EventType.NotCriticalUnexpectedError, InfoBarSeverity.Error,
                    title: "GalgameSourceCollectionService_CheckGamesInSourceFailed".GetLocalized(t.source.Name),
                    exception: e);
            }
        }
        
        foreach (GalgameSourceBase source in _galgameSources.Where(f => f.ScanOnStart)) 
            _ = _bgTaskService.AddBgTask(new GetGalgameInSourceTask(source));
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
            _infoService.Event(EventType.NotCriticalUnexpectedError, InfoBarSeverity.Error, e.Message, e);
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
        await Save();
        if (tryGetGalgame)
        {
            await _bgTaskService.AddBgTask(new GetGalgameInSourceTask(galgameSource));
        }
        
        CalcSubSources();
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
                MoveOutOperate(source, galgame);
        }
        catch (Exception e)
        {
            _infoService.DeveloperEvent(InfoBarSeverity.Error,
                msg: $"Failed to move game out of source {source.Url}\n{e.StackTrace}");
        }
        _galgameSources.Remove(source);
        CalcSubSources();
        await Save();
        OnSourceDeleted?.Invoke(source);
        OnSourceChanged?.Invoke();
    }

    public void MoveInNoOperate(GalgameSourceBase target, Galgame game, string path)
    {
        if (game.Sources.Any(s => s == target))
        {
            _infoService.DeveloperEvent(
                e: new PvnException($"Can not move game {game.Name.Value} into source {target.Path}: already there"));
            return;
        }
        target.AddGalgame(game, path);
    }

    public void MoveOutOperate(GalgameSourceBase target, Galgame game)
    {
        if (game.Sources.All(s => s != target))
        {
            _infoService.DeveloperEvent(e: new PvnException($"Can not move game {game.Name} " +
                                                            $"out of source {target.Path}: not in source"));
            return;
        }
        target.DeleteGalgame(game);
    }

    public BgTaskBase MoveAsync(GalgameSourceBase? moveInSrc, string? moveInPath, GalgameSourceBase? moveOutSrc, Galgame game)
    {
        if (game.Sources.Any(s => s == moveInSrc))
        {
            _infoService.DeveloperEvent(e: new PvnException($"{game.Name.Value} is already in {moveInSrc!.Url}"));
            moveInSrc = null;
            moveInPath = null;
        }
        if (moveOutSrc is not null && game.Sources.All(s => s != moveOutSrc))
        {
            _infoService.DeveloperEvent(e: new PvnException($"{game.Name.Value} is not in {moveOutSrc.Url}"));
            moveOutSrc = null;
        }
        SourceMoveTask task = new(game, moveInSrc, moveInPath, moveOutSrc);
        _bgTaskService.AddBgTask(task);
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
            clone.ImagePath = await _localSettingsService.AddImageToExportAsync(clone.ImagePath);
            exportData.Add(clone);
        }

        await _localSettingsService.AddToExportAsync(KeyValues.GalgameSources, exportData, converters: _converters);
    }

    /// <summary>
    /// 扫描所有库
    /// </summary>
    public void ScanAll()
    {
        foreach(GalgameSourceBase b in _galgameSources)
            _bgTaskService.AddBgTask(new GetGalgameInSourceTask(b));
    }
    
    private async Task Save()
    {
        await _localSettingsService.SaveSettingAsync(KeyValues.GalgameSources, _galgameSources, true,
            converters: _converters);
    }

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
                        MoveOutOperate(source, g);
                    return toRemove;
                });
            case GalgameSourceType.LocalZip:
            case GalgameSourceType.UnKnown:
            default:
                throw new NotSupportedException();
        }
    }

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
            var tmp = await _localSettingsService.ReadOldSettingAsync(KeyValues.GalgameFolders, template);
            if (tmp is not null)
            {
                foreach (var folder in tmp.Where(f => !string.IsNullOrEmpty(f.Path)))
                {
                    GalgameFolderSource source = new(folder.Path) { ScanOnStart = folder.ScanOnStart };
                    _galgameSources.Add(source);
                }
            }
            await _localSettingsService.RemoveSettingAsync(KeyValues.GalgameFolders, true);
        }
        catch (Exception e) //不应该发生
        {
            _infoService.Event(EventType.UpgradeError, InfoBarSeverity.Warning, "升级游戏库数据库结构失败", e);
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
                var gamePath = g.Path;
                if (!string.IsNullOrEmpty(gamePath))
                {
                    var folderPath = Path.GetDirectoryName(gamePath);
                    if (string.IsNullOrEmpty(folderPath))
                    {
                        _infoService.Event(EventType.NotCriticalUnexpectedError, InfoBarSeverity.Error,
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
        
        await Save();
        status.GalgameSourceFormatUpgrade = true;
        await _localSettingsService.SaveSettingAsync(KeyValues.DataStatus, status, true);
    }

    #endregion
}


﻿using GalgameManager.Contracts.Services;
using GalgameManager.Enums;
using GalgameManager.Helpers;
using GalgameManager.Models.Sources;
using GalgameManager.Services;
using H.NotifyIcon.Core;

namespace GalgameManager.Models.BgTasks;

public class GetGalgameInSourceTask : BgTaskBase
{
    public string GalgameSourceUrl = string.Empty;
    private GalgameSourceBase? _galgameFolderSource;

    public GetGalgameInSourceTask() { }

    public GetGalgameInSourceTask(GalgameSourceBase source)
    {
        _galgameFolderSource = source;
        GalgameSourceUrl = source.Url;
    }
    
    protected override Task RecoverFromJsonInternal()
    {
        _galgameFolderSource = App.GetService<IGalgameSourceCollectionService>()?.GetGalgameSourceFromUrl(GalgameSourceUrl);
        return Task.CompletedTask;
    }

    protected override Task RunInternal()
    {
        //TODO
        if (_galgameFolderSource is null || _galgameFolderSource.IsRunning)
            return Task.CompletedTask;
        ILocalSettingsService localSettings = App.GetService<ILocalSettingsService>();
        GalgameCollectionService galgameService = (App.GetService<IGalgameCollectionService>() as GalgameCollectionService)!;
        var log = string.Empty;
        
        return Task.Run((async Task () =>
        {
            log += $"{DateTime.Now}\n{GalgameSourceUrl}\n\n";
            var ignoreFetchResult = await localSettings.ReadSettingAsync<bool>(KeyValues.IgnoreFetchResult);

            _galgameFolderSource.IsRunning = true;
            var cnt = 0;
            await foreach (var (path, l) in _galgameFolderSource.ScanAllGalgames())
            {
                if (path == null)
                {
                    log += $"{path}: {l}\n";
                    continue;
                }
                if (_galgameFolderSource.Galgames.FirstOrDefault(g => Utils.ArePathsEqual(g.Path, path)) is { } game) 
                {
                    log += $"{path}: AlreadyExists ({game.Galgame.Name.Value})\n";
                    continue;
                }
                
                ChangeProgress(0, 1, "GalgameFolder_GetGalInFolder_Progress".GetLocalized(path));
                var msg = $"{path}: ";
                await UiThreadInvokeHelper.InvokeAsync(async Task() =>
                {
                    try
                    {
                        await galgameService.AddGameAsync(_galgameFolderSource.SourceType, path, ignoreFetchResult,
                            false);
                        cnt++;
                        msg += "AddGalgameResult_Success".GetLocalized();
                    }
                    catch (Exception e)
                    {
                        msg += e.Message;
                    }
                    msg += "\n";
                });
                log += msg;

            }
            ChangeProgress(0, 1, "GalgameFolder_GetGalInFolder_Saving".GetLocalized(cnt));
            FileHelper.SaveWithoutJson(_galgameFolderSource.GetLogName(), log, "Logs");
            await Task.Delay(1000); //等待文件保存
            
            ChangeProgress(1, 1, "GalgameFolder_GetGalInFolder_Done".GetLocalized(cnt));
            _galgameFolderSource.IsRunning = false;
            if (App.MainWindow is null && await localSettings.ReadSettingAsync<bool>(KeyValues.NotifyWhenGetGalgameInFolder))
            {
                App.SystemTray?.ShowNotification(nameof(NotificationIcon.Info), 
                    "GalgameFolder_GetGalInFolder_Done".GetLocalized(cnt));
            }
        })!);
    }

    public override bool OnSearch(string key) => GalgameSourceUrl.Contains(key);
    
    public override string Title { get; } = "GetGalgameInFolderTask_Title".GetLocalized();
    
    
}
﻿using System.Collections.ObjectModel;
using GalgameManager.Contracts.Services;
using GalgameManager.Enums;
using GalgameManager.Helpers;
using GalgameManager.Models;
using Microsoft.UI.Xaml.Controls;
using Serilog;

namespace GalgameManager.Services;

/// <summary>
/// 消息及异常记录与通知服务<br/>
/// </summary>
public class InfoService : IInfoService
{
    public event Action<InfoBarSeverity, string?, string?, int>? OnInfo;
    public event Action<InfoBarSeverity, string?, string?>? OnEvent;
    public ObservableCollection<Info> Infos { get; } = new();
    private readonly IAppCenterService _appCenterService;
    private readonly ILocalSettingsService _localSettingsService;
    
    public InfoService(IAppCenterService appCenterService, ILocalSettingsService localSettingsService)
    {
        _appCenterService = appCenterService;
        _localSettingsService = localSettingsService;

        var logDir = Path.Combine(_localSettingsService.LocalFolder.FullName, "Logs");
        if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);

        Serilog.Log.Logger = new LoggerConfiguration().WriteTo.File(
                path: FileHelper.GetFullPath("log.txt", "Logs"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();
    }

    public void Info(InfoBarSeverity infoBarSeverity, string? title = null, string? msg = null, int? displayTimeMs = 3000)
    {
        UiThreadInvokeHelper.Invoke(() => { OnInfo?.Invoke(infoBarSeverity, title, msg, displayTimeMs ?? 3000);});
    }

    public void Event(EventType type, InfoBarSeverity infoBarSeverity, string title, Exception? exception = null, string? msg = null)
    {
        UiThreadInvokeHelper.Invoke(async () =>
        {
            Log(infoBarSeverity, $"{title}: {exception?.ToString() ?? msg}");
            if (await ShouldNotifyEvent(type))
                OnEvent?.Invoke(infoBarSeverity, title, exception?.ToString() ?? msg);
            // 下面这句话有时会抛出System.Runtime.InteropServices.COMException (0x80004005)，但容器却能正常插入
            Infos.Insert(0, new Info(infoBarSeverity, title, exception?.ToString() ?? msg ?? string.Empty));
        });
        _appCenterService.UploadEvent(title, exception, msg);
    }

    public void DeveloperEvent(InfoBarSeverity infoBarSeverity = InfoBarSeverity.Warning, string? msg = null,
        Exception? e = null)
    {
        Event(EventType.NotCriticalUnexpectedError, infoBarSeverity, "UnexpectedEvent".GetLocalized(), 
            exception: e, msg: msg);
    }

    public void Log(InfoBarSeverity severity = InfoBarSeverity.Warning, string msg = "")
    {
        if (severity <= InfoBarSeverity.Success &&
            !_localSettingsService.ReadSettingAsync<bool>(KeyValues.DevelopmentMode).Result)
            return;
        switch (severity)
        {
            case InfoBarSeverity.Warning:
                Serilog.Log.Warning("{Msg}", msg);
                break;
            case InfoBarSeverity.Error:
                Serilog.Log.Error("{Msg}", msg);
                break;
            default:
            case InfoBarSeverity.Informational:
            case InfoBarSeverity.Success:
                Serilog.Log.Information("{Msg}", msg);
                break;
        }
    }

    private async Task<bool> ShouldNotifyEvent(EventType type)
    {
        switch (type)
        {
            case EventType.PvnSyncEvent:
                return await _localSettingsService.ReadSettingAsync<bool>(KeyValues.EventPvnSyncNotify);
            case EventType.PvnSyncEmptyEvent:
                return await _localSettingsService.ReadSettingAsync<bool>(KeyValues.EventPvnSyncEmptyNotify);
            case EventType.NotCriticalUnexpectedError:
                return await _localSettingsService.ReadSettingAsync<bool>(KeyValues.DevelopmentMode);
            default:
                return true;
        }
    }
}
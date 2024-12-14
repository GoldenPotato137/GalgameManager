﻿using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalgameManager.Contracts.Services;
using GalgameManager.Enums;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace GalgameManager.ViewModels;

public partial class ShellViewModel : ObservableObject
{
    [ObservableProperty] private bool _isBackEnabled;
    [ObservableProperty] private object? _selected;
    [ObservableProperty] private bool _isDeveloperMode;
    private int _unreadInfos;
    private readonly IBgTaskService _bgTaskService;
    [ObservableProperty] private bool _updateBadgeVisibility;
    [ObservableProperty] private string? _title;
    [ObservableProperty] private bool _infoPageBadgeVisibility;
    [ObservableProperty] private int _infoPageBadgeCount;

    public INavigationService NavigationService
    {
        get;
    }

    public INavigationViewService NavigationViewService
    {
        get;
    }

    public ShellViewModel(INavigationService navigationService, INavigationViewService navigationViewService,
        IUpdateService updateService, IBgTaskService bgTaskService, ILocalSettingsService localSettingsService,
        IInfoService infoService)
    {
        NavigationService = navigationService;
        NavigationViewService = navigationViewService;
        _bgTaskService = bgTaskService;

        _isDeveloperMode = localSettingsService.ReadSettingAsync<bool>(KeyValues.DevelopmentMode).Result;
        
        NavigationService.Navigated += OnNavigated;
        updateService.SettingBadgeEvent += result => UpdateBadgeVisibility = result;
        _bgTaskService.BgTaskAdded += _ => UpdateInfoPageBadge();
        _bgTaskService.BgTaskRemoved += _ => UpdateInfoPageBadge();
        infoService.Infos.CollectionChanged += (_, _) =>
        {
            if(_isDeveloperMode == false) return;
            _unreadInfos = infoService.Infos.Count(info =>
                info.Severity is InfoBarSeverity.Warning or InfoBarSeverity.Error && info.Read == false);
            UpdateInfoPageBadge();
        };
        localSettingsService.OnSettingChanged += HandleLocalSettingChanged;
        infoService.OnInfo += (severity, title, msg, displayTime) =>
            _ = DisplayInfoMsgAsync(severity, title, msg, displayTime);
        infoService.OnEvent += (severity, title, msg) => _ = DisplayEventMsgAsync(severity, title, msg);
        
        UpdateInfoPageBadge();
    }

    private void OnNavigated(object sender, NavigationEventArgs e)
    {
        IsBackEnabled = NavigationService.CanGoBack;

        var selectedItem = NavigationViewService.GetSelectedItem(e.SourcePageType);
        if (selectedItem != null)
        {
            Selected = selectedItem;
        }

        IsInfoBarOpen = false;
    }

    private void HandleLocalSettingChanged(string key, object? value)
    {
        if (key == KeyValues.DevelopmentMode)
            IsDeveloperMode = value as bool? ?? false;
    }
    
    private void UpdateInfoPageBadge()
    {
        InfoPageBadgeCount = _bgTaskService.GetBgTasks().Count() + _unreadInfos;
        InfoPageBadgeVisibility = InfoPageBadgeCount > 0;
    }
    
    #region INFO_BAR_CTRL

    private int _infoBarIndex;
    [ObservableProperty] private bool _isInfoBarOpen;
    [ObservableProperty] private string? _infoBarTitle;
    [ObservableProperty] private string? _infoBarMessage;
    [ObservableProperty] private InfoBarSeverity _infoBarSeverity = InfoBarSeverity.Informational;
    public ObservableCollection<ShellEventViewModel> ShellEvents { get; } = new();

    private async Task DisplayInfoMsgAsync(InfoBarSeverity infoBarSeverity, string? title, string? msg,
        int delayMs = 3000)
    {
        if (title is null && msg is null)
        {
            IsInfoBarOpen = false;
            return;
        }
        
        var currentIndex = ++_infoBarIndex;
        InfoBarSeverity = infoBarSeverity;
        InfoBarTitle = title;
        InfoBarMessage = msg;
        IsInfoBarOpen = true;
        await Task.Delay(delayMs);
        if (currentIndex == _infoBarIndex)
            IsInfoBarOpen = false;
    }

    private async Task DisplayEventMsgAsync(InfoBarSeverity infoBarSeverity, string? title, string? msg,
        int delayMs = 3000)
    {
        if (msg?.Length >= 200)
        {
            msg = msg[..200];
            if (msg.Count(c => c == '\n') > 1)
            {
                msg = string.Join('\n', msg.Split('\n').Take(2));
                msg = msg[..^4];
            }
            msg += "...";
        }
        
        if(ShellEvents.Count > 3) ShellEvents.RemoveAt(0); // 最多同时显示3个事件
        ShellEventViewModel shellEvent = new()
        {
            Title = title,
            Message = msg,
            Severity = infoBarSeverity,
            Command = new RelayCommand(NavigateToInfoPage)
        };
        ShellEvents.Add(shellEvent);
        await Task.Delay(delayMs);
        ShellEvents.Remove(shellEvent);
    }
    
    [RelayCommand]
    private void NavigateToInfoPage()
    {
        NavigationService.NavigateTo(typeof(InfoViewModel).FullName!);
    }

    #endregion
}

public class ShellEventViewModel
{
    public string? Title;
    public string? Message;
    public InfoBarSeverity Severity;
    // 每个元素都带个Command：ListView的bug，见https://github.com/microsoft/microsoft-ui-xaml/issues/560
    public required IRelayCommand Command;
}

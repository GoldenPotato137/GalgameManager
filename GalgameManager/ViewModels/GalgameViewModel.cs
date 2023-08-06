﻿using System.Collections.ObjectModel;
using System.Diagnostics;
using Windows.System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalgameManager.Contracts.Services;
using GalgameManager.Contracts.ViewModels;
using GalgameManager.Core.Contracts.Services;
using GalgameManager.Enums;
using GalgameManager.Helpers;
using GalgameManager.Models;
using GalgameManager.Services;
using GalgameManager.Views.Dialog;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GalgameManager.ViewModels;

public partial class GalgameViewModel : ObservableRecipient, INavigationAware
{
    private readonly IDataCollectionService<Galgame> _dataCollectionService;
    private readonly GalgameCollectionService _galgameService;
    private readonly INavigationService _navigationService;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly JumpListService _jumpListService;
    private Galgame? _item;
    [ObservableProperty] private bool _isPhrasing;
    [ObservableProperty] private Visibility _isTagVisible = Visibility.Collapsed;
    [ObservableProperty] private Visibility _isDescriptionVisible = Visibility.Collapsed;
    [ObservableProperty] private bool _canOpenInBgm = false;
    [ObservableProperty] private bool _canOpenInVndb = false;

    [ObservableProperty] private Visibility _infoBarVisibility = Visibility.Collapsed;
    [ObservableProperty] private string _infoBarMsg = string.Empty;
    [ObservableProperty] private InfoBarSeverity _infoBarSeverity = InfoBarSeverity.Informational;
    private int _msgIndex;
    
    #region UI_STRINGS

    public readonly string UiPlay ="GalgamePage_Play".GetLocalized();
    public readonly string UiEdit = "GalgamePage_Edit".GetLocalized();
    public readonly string UiChangeSavePosition = "GalgamePage_ChangeSavePosition".GetLocalized();
    public readonly string UiDevelopers = "GalgamePage_Developers".GetLocalized();
    public readonly string UiExpectedPlayTime = "GalgamePage_ExpectedPlayTime".GetLocalized();
    public readonly string UiSavePosition = "GalgamePage_SavePosition".GetLocalized();
    public readonly string UiLastPlayTime = "GalgamePage_LastPlayTime".GetLocalized();
    public readonly string UiDescription = "GalgamePage_Description".GetLocalized();
    public readonly string UiPlayFlyOutTitle = "GalgamePage_UiPlayFlyOutTitle".GetLocalized();
    public readonly string UiYes = "Yes".GetLocalized();
    
    #endregion
    
    public Galgame? Item
    {
        get => _item;
        private set => SetProperty(ref _item, value);
    }

    public GalgameViewModel(IDataCollectionService<Galgame> dataCollectionService, INavigationService navigationService, IJumpListService jumpListService, ILocalSettingsService localSettingsService)
    {
        _dataCollectionService = dataCollectionService;
        _galgameService = (GalgameCollectionService)dataCollectionService;
        _navigationService = navigationService;
        _galgameService.PhrasedEvent += () => IsPhrasing = false;
        _jumpListService = (JumpListService)jumpListService;
        _localSettingsService = localSettingsService;
        Item = new Galgame();
    }

    public async void OnNavigatedTo(object parameter)
    {
        var path = parameter as string ?? null;
        var startGame = false;
        Tuple<string, bool>? para = parameter as Tuple<string, bool> ?? null;
        if (para != null)
        {
            path = para.Item1;
            startGame = para.Item2;
        }

        ObservableCollection<Galgame>? data = await _dataCollectionService.GetContentGridDataAsync();
        try
        {
            Item = data.First(i => i.Path == path);
            Item.CheckSavePosition();
            UpdateVisibility();
            if (startGame && await _localSettingsService.ReadSettingAsync<bool>(KeyValues.QuitStart))
                await Play();
        }
        catch (Exception) //找不到这个游戏，回到主界面
        {
            _navigationService.NavigateTo(typeof(HomeViewModel).FullName!);
        }
    }

    public void OnNavigatedFrom()
    {
    }
    
    private void UpdateVisibility()
    {
        IsTagVisible = Item?.Tags.Value?.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        IsDescriptionVisible = Item?.Description! != string.Empty ? Visibility.Visible : Visibility.Collapsed;
        CanOpenInBgm = !string.IsNullOrEmpty(Item?.Ids[(int)RssType.Bangumi]);
        CanOpenInVndb = !string.IsNullOrEmpty(Item?.Ids[(int)RssType.Vndb]);
    }

    [RelayCommand]
    private async Task OpenInBgm()
    {
        if(string.IsNullOrEmpty(Item!.Ids[(int)RssType.Bangumi])) return;
        await Launcher.LaunchUriAsync(new Uri("https://bgm.tv/subject/"+Item!.Ids[(int)RssType.Bangumi]));
    }
    
    [RelayCommand]
    private async Task OpenInVndb()
    {
        if(string.IsNullOrEmpty(Item!.Ids[(int)RssType.Vndb])) return;
        await Launcher.LaunchUriAsync(new Uri("https://vndb.org/v"+Item!.Ids[(int)RssType.Vndb]));
    }

    private async Task DisplayMsg(InfoBarSeverity severity, string msg, int displayTimeMs = 3000)
    {
        var myIndex = ++_msgIndex;
        InfoBarVisibility = Visibility.Visible;
        InfoBarMsg = msg;
        await Task.Delay(displayTimeMs);
        if (myIndex == _msgIndex)
            InfoBarVisibility = Visibility.Collapsed;
    }

    [RelayCommand]
    private async Task Play()
    {
        if (Item == null) return;
        if (Item.ExePath == null)
            await _galgameService.GetGalgameExeAsync(Item);
        if (Item.ExePath == null) return;

        Item.LastPlay = DateTime.Now.ToShortDateString();
        Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Item.ExePath,
                WorkingDirectory = Item.Path,
                UseShellExecute = Item.RunAsAdmin,
                Verb = Item.RunAsAdmin ? "runas" : null
            }
        };
        try
        {
            process.Start();
            _galgameService.Sort();
            await Task.Delay(1000); //等待1000ms，让游戏进程启动后再最小化
            Item.RecordPlayTime(process);
            ((OverlappedPresenter)App.MainWindow.AppWindow.Presenter).Minimize(); //最小化窗口
            await _jumpListService.AddToJumpListAsync(Item);

            await process.WaitForExitAsync();
            Item.TotalPlayTime--;
            Item.TotalPlayTime++; //手动刷新一下时间显示
            ((OverlappedPresenter)App.MainWindow.AppWindow.Presenter).Restore(); //恢复窗口
            await SaveAsync(); //保存游戏信息(更新时长)
        }
        catch
        {
            //ignore : 用户取消了UAC
        }
    }

    [RelayCommand]
    private async Task GetInfoFromRss()
    {
        if (Item == null) return;
        IsPhrasing = true;
        await _galgameService.PhraseGalInfoAsync(Item);
        UpdateVisibility();
    }

    [RelayCommand]
    private void Setting()
    {
        if (Item == null) return;
        _navigationService.NavigateTo(typeof(GalgameSettingViewModel).FullName!, Item);
    }

    [RelayCommand]
    private async Task ChangeSavePosition()
    {
        if(Item == null) return;
        await _galgameService.ChangeGalgameSavePosition(Item);
        await Task.Delay(1000); //等待1000ms建立软连接后再刷新
        Item.CheckSavePosition();
    }
    
    [RelayCommand]
    private void ResetExePath(object obj)
    {
        Item!.ExePath = null;
    }
    
    [RelayCommand]
    private async Task DeleteFromDisk()
    {
        if(Item == null) return;
        ContentDialog dialog = new()
        {
            XamlRoot = App.MainWindow.Content.XamlRoot,
            Title = "HomePage_Delete_Title".GetLocalized(),
            Content = "HomePage_Delete_Message".GetLocalized(),
            PrimaryButtonText = "Yes".GetLocalized(),
            SecondaryButtonText = "Cancel".GetLocalized(),
            DefaultButton = ContentDialogButton.Secondary
        };
        dialog.PrimaryButtonClick += async (_, _) =>
        {
            await _galgameService.RemoveGalgame(Item, true);
        };
        await dialog.ShowAsync();
    }
    
    [RelayCommand]
    private async Task OpenInExplorer()
    {
        if(Item == null) return;
        await Launcher.LaunchUriAsync(new Uri(Item.Path));
    }

    [RelayCommand]
    private void JumpToPlayedTimePage()
    {
        _navigationService.NavigateTo(typeof(PlayedTimeViewModel).FullName!, Item);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        await _galgameService.SaveGalgamesAsync(Item);
    }

    [RelayCommand]
    private async Task ChangePlayStatus()
    {
        if (Item == null) return;
        ChangePlayStatusDialog dialog = new(Item)
        {
            XamlRoot = App.MainWindow.Content.XamlRoot,
        };
        await dialog.ShowAsync();
        if (dialog.Canceled) return;
        if (dialog.UploadToBgm)
        {
            _ = DisplayMsg(InfoBarSeverity.Informational, "HomePage_UploadingToBgm".GetLocalized(), 1000 * 10);
            (GalStatusSyncResult, string) result = await _galgameService.UploadPlayStatusAsync(Item, RssType.Bangumi);
            await DisplayMsg(result.Item1.ToInfoBarSeverity(), result.Item2);
        }
        if (dialog.UploadToVndb)
            throw new NotImplementedException();
    }
}
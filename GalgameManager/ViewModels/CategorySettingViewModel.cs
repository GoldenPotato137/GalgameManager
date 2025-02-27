﻿using System.Collections.ObjectModel;
using Windows.Storage;
using Windows.Storage.Pickers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.WinUI.Collections;
using GalgameManager.Contracts.Services;
using GalgameManager.Contracts.ViewModels;
using GalgameManager.Enums;
using GalgameManager.Helpers;
using GalgameManager.Models;
using GalgameManager.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GalgameManager.ViewModels;

public partial class CategorySettingViewModel : ObservableObject, INavigationAware
{
    private readonly INavigationService _navigationService;
    private readonly CategoryService _categoryService;
    private readonly GalgameCollectionService _galgameCollectionService;
    public Category Category = new();
    public ObservableCollection<CategoryGroupChecker> CategoryGroups = new();
    public AdvancedCollectionView Games = new();
    private int _displayIndex;
    [ObservableProperty] private Visibility _downloadImgVisibility = Visibility.Collapsed;
    [ObservableProperty] private string _infoBarMessage = string.Empty;
    [ObservableProperty] private InfoBarSeverity _infoBarSeverity = InfoBarSeverity.Informational;
    [ObservableProperty] private bool _infoBarIsOpen;
    [ObservableProperty] private string _galgameSearchKey = string.Empty;
    
    [RelayCommand]
    private void GalgameSearch(string searchKey)
    {
        Games.RefreshFilter();
    }

    public CategorySettingViewModel(INavigationService navigationService, ICategoryService categoryService, 
        IGalgameCollectionService dataCollectionService)
    {
        _navigationService = navigationService;
        _categoryService = (CategoryService)categoryService;
        _galgameCollectionService = (GalgameCollectionService)dataCollectionService;
    }
    
    public async void OnNavigatedTo(object parameter)
    {
        if (parameter is Category category)
        {
            if (_categoryService.IsInCategoryGroup(category, CategoryGroupType.Developer))
                DownloadImgVisibility = Visibility.Visible;
            
            Category = category;
            ObservableCollection<CategoryGroup> tmpCategoryGroups = await _categoryService.GetCategoryGroupsAsync();
            foreach (CategoryGroup group in tmpCategoryGroups)
            {
                CategoryGroupChecker tmp = new()
                {
                    Group = group,
                    IsSelect = group.Categories.Contains(Category)
                };
                tmp.Click += ClickCategoryGroup;
                CategoryGroups.Add(tmp);
            }
            IList<Galgame> games = _galgameCollectionService.Galgames;
            foreach (Galgame game in games)
            {
                Games.Add(new GameChecker()
                {
                    Game = game,
                    IsSelect = game.Categories.Contains(Category)
                });
            }
        }
        else
            throw new ArgumentException("parameter is not Category");
        Games.Filter += g =>
        {
            if (g is GameChecker gameChecker)
            {
                return GalgameSearchKey.IsNullOrEmpty() || gameChecker.Game.ApplySearchKey(GalgameSearchKey);
            }

            return false;
        };
    }

    public void OnNavigatedFrom()
    {
        foreach (CategoryGroupChecker groupChecker in CategoryGroups)
        {
            if (groupChecker.IsSelect && groupChecker.Group.Categories.Contains(Category) == false)
                groupChecker.Group.Categories.Add(Category);
            else if (groupChecker.IsSelect == false && groupChecker.Group.Categories.Contains(Category))
                groupChecker.Group.Categories.Remove(Category);
        }

        foreach (GameChecker gameChecker in Games)
        {
            if (gameChecker.IsSelect && Category.Belong(gameChecker.Game) == false)
                Category.Add(gameChecker.Game);
            else if (gameChecker.IsSelect == false && Category.Belong(gameChecker.Game))
                Category.Remove(gameChecker.Game);
        }
        
        _categoryService.Save(category: Category);
        foreach (CategoryGroup group in CategoryGroups.Select(g => g.Group))
            _categoryService.Save(categoryGroup: group);
    }

    /// <summary>
    /// 使用InfoBar显示消息
    /// </summary>
    /// <param name="severity">严重程度</param>
    /// <param name="msg">消息</param>
    /// <param name="delayMs">显示时间（毫秒）</param>
    private async Task DisplayMsgAsync(InfoBarSeverity severity, string msg, int delayMs = 3000)
    {
        var index = ++ _displayIndex;
        InfoBarMessage = msg;
        InfoBarSeverity = severity;
        InfoBarIsOpen = true;
        await Task.Delay(delayMs);
        if (index == _displayIndex)
            InfoBarIsOpen = false;
    }

    [RelayCommand]
    private void Back()
    {
        _navigationService.GoBack();
    }

    [RelayCommand]
    private async Task PickImage()
    {
        FileOpenPicker openPicker = new()
        {
            ViewMode = PickerViewMode.Thumbnail,
            SuggestedStartLocation = PickerLocationId.PicturesLibrary
        };
        WinRT.Interop.InitializeWithWindow.Initialize(openPicker, App.MainWindow!.GetWindowHandle());
        openPicker.FileTypeFilter.Add(".jpg");
        openPicker.FileTypeFilter.Add(".jpeg");
        openPicker.FileTypeFilter.Add(".png");
        openPicker.FileTypeFilter.Add(".bmp");
        StorageFile? file = await openPicker.PickSingleFileAsync();
        if (file is not null)
            Category.ImagePath = file.Path;
    }

    [RelayCommand]
    private void DownloadImage()
    {
        _categoryService.UpdateCategory(Category);
        _ = DisplayMsgAsync(InfoBarSeverity.Success, "CategorySettingPage_HavePutInQueue".GetLocalized());
    }
    
    private void ClickCategoryGroup(CategoryGroupChecker? groupChecker)
    {
        if(groupChecker is null) return;
        
        var cnt = CategoryGroups.Count(checker => checker.IsSelect);
        if (cnt == 0)
            _ = DisplayMsgAsync(InfoBarSeverity.Error, "CategorySettingPage_AtLeastOnCategoryGroup".GetLocalized());
    }
}

public class CategoryGroupChecker
{
    public CategoryGroup Group = new();
    public event GenericDelegate<CategoryGroupChecker>? Click;
    private bool _isSelect;

    public bool IsSelect
    {
        get => _isSelect;
        set
        {
            _isSelect = value;
            Click?.Invoke(this);
        }
    }
}

public class GameChecker
{
    public Galgame Game = new();
    public bool IsSelect;
}
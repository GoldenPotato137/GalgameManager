﻿using GalgameManager.Contracts.Services;
using GalgameManager.Enums;
using GalgameManager.Helpers;
using GalgameManager.Models;
using GalgameManager.Models.Filters;
using GalgameManager.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace GalgameManager.Views.GalgamePagePanel;

public partial class GameHeaderOldPanel
{
    private readonly ICategoryService _categoryService = App.GetService<ICategoryService>();
    private readonly IInfoService _infoService = App.GetService<IInfoService>();
    private readonly IFilterService _filterService = App.GetService<IFilterService>();
    private readonly INavigationService _navigationService = App.GetService<INavigationService>();
    private readonly ILocalSettingsService _localSettingsService = App.GetService<ILocalSettingsService>();

    public GameHeaderOldPanel()
    {
        InitializeComponent();
    }

    private void ClickDeveloper(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (Game is null) return;
        Category? category = _categoryService.GetDeveloperCategory(Game);
        if (category is null)
        {
            _infoService.Info(InfoBarSeverity.Error, msg: "HomePage_NoDeveloperCategory".GetLocalized());
            return;
        }
        _filterService.ClearFilters();
        _filterService.AddFilter(new CategoryFilter(category));
        _navigationService.NavigateTo(typeof(HomeViewModel).FullName!);
    }

    private async void ChangeTimeFormat(object sender, RightTappedRoutedEventArgs e)
    {
        try
        {
            var current = await _localSettingsService.ReadSettingAsync<bool>(KeyValues.TimeAsHour);
            await _localSettingsService.SaveSettingAsync(KeyValues.TimeAsHour, !current);
            Game!.RaisePropertyChanged(nameof(Galgame.TotalPlayTime));
        }
        catch (Exception ex)
        {
            _infoService.Event(EventType.PageError, InfoBarSeverity.Error, "Oops, something went wrong", ex);
        }
    }

    private void ClickTotalPlayTime(object sender, RoutedEventArgs e)
    {
        _navigationService.NavigateTo(typeof(PlayedTimeViewModel).FullName!, Game);
    }
}

﻿using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using GalgameManager.Contracts.Services;
using GalgameManager.Contracts.ViewModels;
using GalgameManager.Enums;
using GalgameManager.Models;
using GalgameManager.Models.BgTasks;
using Microsoft.UI.Xaml;

namespace GalgameManager.ViewModels;

public partial class InfoViewModel : ObservableObject, INavigationAware
{
    public ObservableCollection<BgTaskViewModel> BgTasks = new();
    [ObservableProperty] private ObservableCollection<Info> _infos = new();
    [ObservableProperty] private Visibility _noBgTaskVisibility = Visibility.Collapsed;
    [ObservableProperty] private bool _bgTaskExpanded;
    [ObservableProperty] private Visibility _noInfoVisibility = Visibility.Collapsed;
    [ObservableProperty] private bool _infoExpanded;
    private readonly IBgTaskService _bgTaskService;
    private readonly IInfoService _infoService;
    
    public InfoViewModel(IBgTaskService bgTaskService, IInfoService infoService)
    {
        _bgTaskService = bgTaskService;
        _infoService = infoService;
    }

    public void OnNavigatedTo(object parameter)
    {
        BgTasks.Clear();
        foreach (BgTaskBase task in _bgTaskService.GetBgTasks()) 
            BgTasks.Add(new BgTaskViewModel(task));
        App.GetService<ILocalSettingsService>().RemoveSettingAsync(KeyValues.LastError); //打开消息界面后才移除崩溃记录
        Infos = _infoService.Infos;
        foreach (Info info in Infos)
            info.Read = true;
        if(_infoService.Infos.Count > 0)
            _infoService.Infos.Move(0, 0); // 触发 CollectionChanged 事件以便更新未读信息数
        
        _bgTaskService.BgTaskAdded += AddBgTask;
        _bgTaskService.BgTaskRemoved += RemoveBgTask;
        BgTasks.CollectionChanged += HandleCollectionChanged;
        Infos.CollectionChanged += HandleCollectionChanged;
        
        UpdateVisibility();
    }

    public void OnNavigatedFrom()
    {
        _bgTaskService.BgTaskAdded -= AddBgTask;
        _bgTaskService.BgTaskRemoved -= RemoveBgTask;
        BgTasks.CollectionChanged -= HandleCollectionChanged;
        Infos.CollectionChanged -= HandleCollectionChanged;
    }

    private void AddBgTask(BgTaskBase task)
    {
        BgTasks.Add(new BgTaskViewModel(task));
    }
    
    private void RemoveBgTask(BgTaskBase task)
    {
        BgTaskViewModel? vm = BgTasks.FirstOrDefault(vm => vm.Task == task);
        if (vm is not null) BgTasks.Remove(vm);
    }
    
    private void UpdateVisibility()
    {
        NoBgTaskVisibility = BgTasks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoInfoVisibility = Infos.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        BgTaskExpanded = BgTasks.Count > 0;
        InfoExpanded = Infos.Count > 0;
    }
    
    private void HandleCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateVisibility();
    }
}

public partial class BgTaskViewModel : ObservableObject
{
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private string _title = string.Empty;
    public readonly BgTaskBase Task;

    public BgTaskViewModel(BgTaskBase task)
    {
        Task = task;
        Task.OnProgress += Update;
        Update(Task.CurrentProgress);
    }

    private void Update(Progress progress)
    {
        Title = Task.Title;
        Message = $"{progress.Message}, {progress.Current} / {progress.Total}";
    }
}
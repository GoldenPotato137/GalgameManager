﻿using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using CommunityToolkit.WinUI;
using GalgameManager.Contracts.Services;
using GalgameManager.Enums;
using GalgameManager.Helpers;
using GalgameManager.Helpers.Phrase;
using GalgameManager.Models;
using LiteDB;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;

namespace GalgameManager.Services;

public class CategoryService : ICategoryService
{
    private ObservableCollection<CategoryGroup> _categoryGroups = new();
    private readonly GalgameCollectionService _galgameService;
    private readonly IInfoService _infoService;
    private CategoryGroup? _developerGroup, _statusGroup;
    private readonly Category[] _statusCategory = new Category[6];
    private bool _isInit;
    private readonly ILocalSettingsService _localSettings;
    private readonly BlockingCollection<Category> _queue = new();
    private readonly BgmPhraser _bgmPhraser;
    private readonly DispatcherQueue? _dispatcher;
    private ILiteCollection<CategoryGroup> _groupDbSet = null!;
    private ILiteCollection<Category> _categoryDbSet = null!;

    public CategoryGroup? GetGroup(Guid id) => _categoryGroups.FirstOrDefault(group => group.Id == id);

    public CategoryGroup StatusGroup => _statusGroup!;
    public CategoryGroup DeveloperGroup => _developerGroup!;

    public CategoryService(ILocalSettingsService localSettings, IGalgameCollectionService galgameService,
        IInfoService infoService)
    {
        _localSettings = localSettings;
        _infoService = infoService;
        _galgameService = (galgameService as GalgameCollectionService)!;
        _bgmPhraser = (BgmPhraser)_galgameService.PhraserList[(int)RssType.Bangumi];
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        Thread worker = new(Worker)
        {
            IsBackground = true
        };
        worker.Start();
    }

    public async Task Init()
    {
        if (_isInit) return;

        _groupDbSet = _localSettings.Database.GetCollection<CategoryGroup>("category_group");
        _categoryDbSet = _localSettings.Database.GetCollection<Category>("category");
        await LiteDbUpgrade();
        await LoadDataAsync();
        await Upgrade();
        await ImportAsync();
        
        InitStatusGroup();
        InitDeveloperGroup();
        
        foreach (Galgame g in _galgameService.Galgames) 
            g.GalPropertyChanged += HandleGalPropertyChanged;
        _galgameService.GalgameAddedEvent += galgame =>
        {
            if (_isInit == false) return;
            galgame.GalPropertyChanged += HandleGalPropertyChanged;
            HandleGalPropertyChanged(galgame, nameof(Galgame.Developer), galgame.Developer.Value);
            HandleGalPropertyChanged(galgame, nameof(Galgame.PlayType), galgame.PlayType);
            HandleGalPropertyChanged(galgame, nameof(Galgame.LastPlayTime), galgame.LastPlayTime);
        };
        _galgameService.GalgameDeletedEvent += galgame =>
        {
            galgame.GalPropertyChanged -= HandleGalPropertyChanged;
            List<Category> categories = galgame.Categories.ToList();
            foreach (Category category in categories)
            {
                category.Remove(galgame);
                Save(category);
            }
        }; 

        // 给Galgame注入Category
        foreach (Category category in _categoryGroups.SelectMany(group => group.Categories))
            category.GalgamesX.ForEach(g =>
            {
                if (g.Categories.Contains(category)) return;
                g.Categories.Add(category);
            });

        _isInit = true;
        return;
        
        void HandleGalPropertyChanged(Galgame gal, string name, object? _)
        {
            switch (name)
            {
                case nameof(Galgame.Developer):
                    Task __ = UpdateCategory(gal, updateDeveloper: true);
                    break;
                case nameof(Galgame.PlayType):
                    Task ___ = UpdateCategory(gal, updateStatus: true);
                    break;
                case nameof(Galgame.LastPlayTime):
                    foreach (Category category in gal.Categories)
                    {
                        category.UpdateLastPlayed();
                        Save(category);
                    }
                    break;
            }
        }
        
        async Task LoadDataAsync()
        {
            await Task.Run(() =>
            {
                List<Category> categories = _categoryDbSet.FindAll().ToList();
                foreach(Category category in categories)
                foreach (Guid uuid in category.GetLoadedGalgameIds())
                {
                    Galgame? galgame = _galgameService.GetGalgameFromUuid(uuid);
                    if (galgame is not null) category.GalgamesX.Add(galgame);
                    else //不应该发生
                        _infoService.Event(EventType.NotCriticalUnexpectedError, InfoBarSeverity.Error,
                            $"Galgame {uuid} not found");
                }
                _categoryGroups = new ObservableCollection<CategoryGroup>(_groupDbSet.FindAll());
                foreach (CategoryGroup group in _categoryGroups)
                foreach (Guid id in group.GetLoadedCategoryIds())
                {
                    Category? category = categories.FirstOrDefault(c => c.Id == id);
                    if (category is not null) group.Categories.Add(category);
                    else //不应该发生
                        _infoService.Event(EventType.NotCriticalUnexpectedError, InfoBarSeverity.Error,
                            $"Category {id} not found");
                }
            });
        }
    }

    public async Task<ObservableCollection<CategoryGroup>> GetCategoryGroupsAsync()
    {
        if (_isInit == false)
            await Init();
        return _categoryGroups;
    }

    /// <summary>
    /// 新增分类组
    /// </summary>
    /// <param name="name">分类组名</param>
    /// <returns>创建的分类组</returns>
    public CategoryGroup AddCategoryGroup(string name)
    {
        CategoryGroup newGroup = new(name, CategoryGroupType.Custom);
        _categoryGroups.Add(newGroup);
        Save(categoryGroup: newGroup);
        return newGroup;
    }
    
    /// <summary>
    /// 删除分类组
    /// </summary>
    /// <param name="categoryGroup">分类组</param>
    public void DeleteCategoryGroup(CategoryGroup categoryGroup)
    {
        foreach (Category category in categoryGroup.Categories) // 删除分类组里的分类（如果没有其他分类组在用的话）
        {
            if (_categoryGroups.Count(group => group.Categories.Contains(category)) == 1)
            {
                category.Delete();
                _categoryDbSet.Delete(category.Id);
            }
        }
        _categoryGroups.Remove(categoryGroup);
        _groupDbSet.Delete(categoryGroup.Id);
    }
    
    /// <summary>
    /// 将源分类合并到目标分类，然后删除源分类 <br/>
    /// 如果目标分类和源分类相同，则不进行任何操作
    /// </summary>
    /// <param name="target">目标分类</param>
    /// <param name="source">源分类</param>
    public void Merge(Category target, Category source)
    {
        if (target == source) return;
        target.Add(source);
        Save(target);
        DeleteCategory(source);
    }

    public Category? GetCategory(Guid id)
    {
        return _categoryGroups.SelectMany(group => group.Categories).FirstOrDefault(category => category.Id == id);
    }

    public Category? GetCategory(string name)
    {
        return _categoryGroups.SelectMany(group => group.Categories).FirstOrDefault(category => category.Name == name);
    }

    /// <summary>
    /// 删除分类
    /// </summary>
    /// <param name="category">分类</param>
    public void DeleteCategory(Category category)
    {
        category.Delete();
        foreach (CategoryGroup categoryGroup in _categoryGroups)
        {
            var result = categoryGroup.Categories.Remove(category);
            if (result) Save(categoryGroup: categoryGroup);
        }
        _categoryDbSet.Delete(category.Id);
    }

    /// <summary>
    /// 更新某个分类的信息（目前只有开发商的图片）
    /// </summary>
    /// <param name="category"></param>
    public void UpdateCategory(Category category)
    {
        _queue.Add(category);
    }

    /// <summary>
    /// 更新所有游戏的分类（开发商及游玩状态）
    /// </summary>
    public async Task UpdateAllGames()
    {
        IList<Galgame> games = _galgameService.Galgames;
        foreach (Galgame game in games)
            await UpdateCategory(game, updateDeveloper: true, updateStatus: true);
        //todo:空Category删除
    }
    
    /// <summary>
    /// 根据某个游戏的信息更新各种分类（受设置中自动分类控制，若关闭自动分类则什么都不做）
    /// </summary>
    /// <param name="galgame">游戏</param>
    /// <param name="updateDeveloper">是否根据开发商信息更新开发商分类组</param>
    /// <param name="updateStatus">是否根据游玩状态信息更新游玩状态分类组</param>
    private async Task UpdateCategory(Galgame galgame, bool updateDeveloper = false, bool updateStatus = false)
    {
        if (_isInit == false) await Init();
        // 更新开发商分类组
        if (updateDeveloper && await _localSettings.ReadSettingAsync<bool>(KeyValues.AutoCategory) 
            && galgame.Developer.Value != Galgame.DefaultString && galgame.Developer.Value != string.Empty)
        {
            //移除旧的开发商分类
            Category? old = GetDeveloperCategory(galgame);
            if (old is not null)
            {
                old.Remove(galgame);
                Save(category: old);
            }
            
            var developerStrings = galgame.Developer.Value!.Split(',');
            foreach (var developerStr in developerStrings)
            {
                Producer producer = ProducerDataHelper.Producers.FirstOrDefault(p =>
                    p.Names.Any(name => string.Equals(name, developerStr, StringComparison.CurrentCultureIgnoreCase))) ?? new Producer(developerStr);
                Category? developer = _developerGroup!.Categories.FirstOrDefault(c => 
                        producer.Names.Any(name => string.Equals(name, c.Name, StringComparison.CurrentCultureIgnoreCase)));
                if (developer is null)
                {
                    developer = new Category(producer.Name);
                    _queue.Add(developer);
                    _developerGroup!.Categories.Add(developer);
                    Save(category: developer);
                    Save(categoryGroup: _developerGroup);
                }
                developer.Add(galgame);
                Save(category: developer);
            }
        }
        // 更新游玩状态分类
        if (updateStatus && await _localSettings.ReadSettingAsync<bool>(KeyValues.AutoCategory))
        {
            Category? playType = GetStatusCategory(galgame);
            if (playType == _statusCategory[(int)galgame.PlayType]) return;
            if (playType is not null)
            {
                playType.Remove(galgame);
                Save(category: playType);
            }
            _statusCategory[(int)galgame.PlayType].Add(galgame);
            Save(category: _statusCategory[(int)galgame.PlayType]);
        }

    }

    private async void Worker()
    {
        foreach (Category category in _queue.GetConsumingEnumerable())
        {
            var imgUrl = await _bgmPhraser.GetDeveloperImageUrlAsync(category.Name);
            if (imgUrl is null) continue;
            var imagPath = await DownloadHelper.DownloadAndSaveImageAsync(imgUrl);
            if (imagPath is not null && _dispatcher is not null)
            {
                await _dispatcher.EnqueueAsync(() => { category.ImagePath = imagPath; });
                Save(category: category);
            }
        }
    }

    /// <summary>
    /// 保存分类信息
    /// </summary>
    /// <param name="category">要保存的分类</param>
    /// <param name="categoryGroup">要保存的分类组</param>
    public void Save(Category? category = null, CategoryGroup? categoryGroup = null)
    {
        if (!_localSettings.IsDatabaseUsable) return;
        if (category is not null)
            _categoryDbSet.Upsert(category);
        else if (categoryGroup is not null)
        {
            _groupDbSet.Upsert(categoryGroup);
            foreach (Category c in categoryGroup.Categories)
                _categoryDbSet.Upsert(c);
        }
    }
    
    /// <summary>
    /// 保存所有分类信息
    /// </summary>
    private async Task SaveAllAsync()
    {
        if (!_localSettings.IsDatabaseUsable) return;
        await Task.Run(() =>
        {
            _groupDbSet.Upsert(_categoryGroups);
            foreach (CategoryGroup group in _categoryGroups)
                _categoryDbSet.Upsert(group.Categories);
        }); //防止阻塞UI线程
    }
    
    public async Task ExportAsync(Action<string, int, int>? progress)
    {
        ObservableCollection<CategoryGroup> tmp = new(_categoryGroups.Select(g => g.Clone()));
        var sum = tmp.Sum(group => group.Categories.Count);
        var current = 0;
        foreach (CategoryGroup group in tmp)
        foreach (Category category in group.Categories)
        {
            progress?.Invoke(ResourceExtensions.GetLocalized("CategoryService_Export_Progress", category.Name),
                current++, sum);
            if (await _localSettings.AddImageToExportAsync(category.ImagePath) is { } path)
                category.ImagePath = path;
        }

        await _localSettings.AddToExportAsync(KeyValues.CategoryGroups, tmp,
            converters: new() { new GalgameAndUidConverter() });
    }

    /// <summary>
    /// 获取开发商分类，如果没有则返回null
    /// </summary>
    public Category? GetDeveloperCategory(Galgame galgame)
    {
        foreach(Category category in galgame.Categories)
            if(_developerGroup!.Categories.Contains(category))
                return category;
        return null;
    }

    /// <summary>
    /// 获取状态分类，如果没有则返回null
    /// </summary>
    private Category? GetStatusCategory(IEnumerable<Category> categories)
    {
        return categories.FirstOrDefault(category => _statusGroup!.Categories.Contains(category));
    }

    /// <summary>
    /// 获取状态分类，如果没有则返回null
    /// </summary>
    private Category? GetStatusCategory(Galgame galgame)
    {
        return GetStatusCategory(galgame.Categories);
    }

    private void InitStatusGroup()
    {
        // 不知道为什么会保存多个游玩状态，先把多余的删了，临时解决方案
        // todo: 找到真正bug来源
        try
        {
            List<CategoryGroup> status = _categoryGroups.Where(g => g.Type == CategoryGroupType.Status).ToList();
            if (status.Count > 1)
            {
                CategoryGroup toKeep = status.First();
                foreach (CategoryGroup group in status.Skip(1))
                    if (group.GamesCount > toKeep.GamesCount)
                        toKeep = group;
                foreach (CategoryGroup group in status.Where(g => g != toKeep))
                {
                    _categoryGroups.Remove(group);
                    _groupDbSet.Delete(group.Id);
                }
            }
        }
        catch (Exception e)
        {
            _infoService.DeveloperEvent(e: e);
        }
        
        _statusGroup = _categoryGroups.FirstOrDefault(group => group.Type == CategoryGroupType.Status);
        if (_statusGroup is null)
        {
            _statusGroup = new CategoryGroup(ResourceExtensions.GetLocalized("CategoryService_Status"),
                CategoryGroupType.Status);
            _categoryGroups.Add(_statusGroup);
            SetStatusCategory();
            foreach(Galgame game in _galgameService.Galgames.Where(g => GetStatusCategory(g) is null)) 
                _statusCategory[(int)game.PlayType].Add(game);
        }
        else
            SetStatusCategory();

        return;

        void SetStatusCategory()
        {
            (Guid, PlayType)[] statusGuids =
            [
                (new Guid("00000000-0000-0000-0000-000000000001"), PlayType.None),
                (new Guid("00000000-0000-0000-0000-000000000002"), PlayType.Played),
                (new Guid("00000000-0000-0000-0000-000000000003"), PlayType.Playing),
                (new Guid("00000000-0000-0000-0000-000000000004"), PlayType.Shelved),
                (new Guid("00000000-0000-0000-0000-000000000005"), PlayType.Abandoned),
                (new Guid("00000000-0000-0000-0000-000000000006"), PlayType.WantToPlay),
            ];
            foreach ((Guid guid, PlayType type) in statusGuids)
            {
                Category? category = _statusGroup!.Categories.FirstOrDefault(c => c.Id == guid);
                if (category is null)
                {
                    category = new Category(type.GetLocalized()) { Id = guid };
                    _statusGroup.Categories.Add(category);
                    Save(category: category);
                    Save(categoryGroup: _statusGroup);
                }
                _statusCategory[(int)type] = category;
            }
        }
    }

    private void InitDeveloperGroup()
    {
        try
        {
            _developerGroup = _categoryGroups.First(cg => cg.Type == CategoryGroupType.Developer);
            _developerGroup.Name = ResourceExtensions.GetLocalized("CategoryService_Developer");
        }
        catch
        {
            _developerGroup = new CategoryGroup(ResourceExtensions.GetLocalized("CategoryService_Developer"),
                CategoryGroupType.Developer);
            _categoryGroups.Add(_developerGroup);
            Save(categoryGroup: _developerGroup);
        }
    }

    private async Task ImportAsync()
    {
        LocalSettingStatus? status =
            await _localSettings.ReadSettingAsync<LocalSettingStatus>(KeyValues.DataStatus, true);
        if (status?.ImportCategory is not false) return;
        foreach (CategoryGroup group in _categoryGroups)
        foreach (Category category in group.Categories)
        {
            if (await _localSettings.GetImageFromImportAsync(category.ImagePath) is { } path)
                category.ImagePath = path;
        }
        status.ImportCategory = true;
        await SaveAllAsync();
        await _localSettings.SaveSettingAsync(KeyValues.DataStatus, status, true);
    }

    #region UPGRADE
    
    /// <summary>
    /// 旧的存储格式与新的存储格式不兼容，需要升级
    /// </summary>
    private async Task Upgrade()
    {
        LocalSettingStatus status =
            await _localSettings.ReadSettingAsync<LocalSettingStatus>(KeyValues.DataStatus, true) 
            ?? new();
        // 改变游戏索引格式，since v1.8.0
        await UpdateGameIndexFormat(status);
        // 添加“想玩”分类，since v1.8.0
        await AddWantToPlayCategory(status);
        // 给各分类添加LastPlayed字段, since v1.8.0
        if (!status.CategoryAddLastPlayed)
        {
            foreach (CategoryGroup group in _categoryGroups)
            foreach (Category category in group.Categories)
                category.UpdateLastPlayed();
            await SaveAllAsync();
            status.CategoryAddLastPlayed = true;
            await _localSettings.SaveSettingAsync(KeyValues.DataStatus, status, true);
        }
    }

    private async Task UpdateGameIndexFormat(LocalSettingStatus status)
    {
        if (status.CategoryGameIndexUpgrade) return;
        try
        {
            var template = new[]
            {
                // CategoryGroup
                new
                {
                    Categories = new[]
                    {
                        // Category
                        new
                        {
                            Galgames = new[] { string.Empty }, //List of paths
                        },
                    },
                },
            };
            var tmp = await _localSettings.ReadOldSettingAsync(KeyValues.CategoryGroups, template);
            if (tmp is not null)
            {
                for (var i = 0; i < tmp.Length && i < _categoryGroups.Count; i++)
                {
                    // ReSharper disable once ConditionIsAlwaysTrueOrFalse
                    if (tmp[i].Categories is null) continue;
                    for (var j = 0; j < tmp[i].Categories.Length && j < _categoryGroups[i].Categories.Count; j++)
                    {
                        // ReSharper disable once ConditionIsAlwaysTrueOrFalse
                        if (tmp[i].Categories[j].Galgames is null) continue;
                        foreach (var path in tmp[i].Categories[j].Galgames)
                        {
                            Galgame? galgame = _galgameService.Galgames.FirstOrDefault(g => g.LocalPath == path);
                            if (galgame is not null) _categoryGroups[i].Categories[j].Add(galgame);
                        }
                    }
                }
            }
            await SaveAllAsync();
            status.CategoryGameIndexUpgrade = true;
            await _localSettings.SaveSettingAsync(KeyValues.DataStatus, status, true);
        }
        catch (Exception e)
        {
            _infoService.Event(EventType.UpgradeError, InfoBarSeverity.Warning, "升级分类存储格式（游戏索引方案）失败", e);
        }
    }

    private async Task AddWantToPlayCategory(LocalSettingStatus status)
    {
        if (status.CategoryAddWantToPlay) return;
        try
        {
            CategoryGroup? statusGroup = _categoryGroups.FirstOrDefault(g => g.Type == CategoryGroupType.Status);
            if (statusGroup is not null)
            {
                var containsWantToPlay =
                    statusGroup.Categories.Any(c => c.Id == new Guid("00000000-0000-0000-0000-000000000006"));
                if (!containsWantToPlay)
                {
                    statusGroup.Categories.Add(new Category(PlayType.WantToPlay.GetLocalized())
                        { Id = new Guid("00000000-0000-0000-0000-000000000006") });
                    await SaveAllAsync();
                }
            }
            status.CategoryAddWantToPlay = true;
            await _localSettings.SaveSettingAsync(KeyValues.DataStatus, status, true);
        }
        catch (Exception e)
        {
            _infoService.Event(EventType.UpgradeError, InfoBarSeverity.Warning, "添加“想玩”分类失败", e);
        }
    }

    private async Task LiteDbUpgrade()
    {
        LocalSettingStatus status =
            await _localSettings.ReadSettingAsync<LocalSettingStatus>(KeyValues.DataStatus, true) ?? new();
        if (status.CategoryLiteDbUpgrade) return;
        try
        {
            //读取旧JSON格式数据
            _categoryGroups = await _localSettings.ReadSettingAsync<ObservableCollection<CategoryGroup>>
                                  (KeyValues.CategoryGroups, true, converters: new() { new GalgameAndUidConverter() })
                              ?? new ObservableCollection<CategoryGroup>();
            foreach (CategoryGroup group in _categoryGroups)
                group.Categories.ForEach(c => c.GalgamesX.RemoveNull());
            //升级为LiteDB保存
            await Task.Run(() =>
            {
                _groupDbSet.Upsert(_categoryGroups);
                foreach (CategoryGroup group in _categoryGroups)
                    _categoryDbSet.Upsert(group.Categories);
                _localSettings.RemoveSettingAsync(KeyValues.CategoryGroups, true);
            });
        }
        catch (Exception e)
        {
            _infoService.Event(EventType.AppError, InfoBarSeverity.Error, "升级为LiteDB失败", e);
        }
        status.CategoryLiteDbUpgrade = true;
        await _localSettings.SaveSettingAsync(KeyValues.DataStatus, status, true);
    }
    
    #endregion

    /// <summary>
    /// 是否在某个type的分类组中
    /// </summary>
    /// <param name="category">分类</param>
    /// <param name="type">type</param>
    public bool IsInCategoryGroup(Category category, CategoryGroupType type)
    {
        return _categoryGroups.Any(g => g.Type == type && g.Categories.Contains(category));
    }
}
using System.Collections.ObjectModel;
using System.Reflection;
using Windows.Storage;
using Windows.Storage.Pickers;
using GalgameManager.Contracts.Phrase;
using GalgameManager.Contracts.Services;
using GalgameManager.Enums;
using GalgameManager.Helpers;
using GalgameManager.Helpers.Phrase;
using GalgameManager.Models;
using GalgameManager.Models.BgTasks;
using GalgameManager.Models.Sources;
using GalgameManager.Views.Dialog;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using LiteDB;

namespace GalgameManager.Services;

public partial class GalgameCollectionService : IGalgameCollectionService
{
    /// _galgames 无序, _displayGalgames有序，<br/>
    /// <b>所有对这个数组的操作均应该使用UI线程执行，以防出现COMException</b>
    private readonly ObservableCollection<Galgame> _galgames = [];
    private static ILocalSettingsService LocalSettingsService { get; set; } = null!;
    private readonly IJumpListService _jumpListService;
    private readonly IInfoService _infoService;
    private readonly IBgTaskService _bgTaskService;
    private readonly IGalgameSourceCollectionService _galSrcService;
    private ILiteCollection<Galgame> _dbSet = null!;
    public event Action<Galgame>? GalgameAddedEvent; //当有galgame添加时触发
    public event Action<Galgame>? GalgameDeletedEvent; //当有galgame删除时触发
    public event Action<Galgame>? MetaSavedEvent; //当有galgame元数据保存时触发
    public event Action? GalgameLoadedEvent; //当galgame列表加载完成时触发
    public event Action? PhrasedEvent; //当有galgame信息下载完成时触发
    public event Action<Galgame>? PhrasedEvent2; //当有galgame信息下载完成时触发 
    public event Action<Galgame>? GalgameChangedEvent;
    public bool IsPhrasing;

    public IGalInfoPhraser[] PhraserList
    {
        get;
    } = new IGalInfoPhraser[Galgame.PhraserNumber];

    public GalgameCollectionService(ILocalSettingsService localSettingsService, IJumpListService jumpListService, 
        IGalgameSourceCollectionService galgameSourceService, IInfoService infoService, IBgTaskService bgTaskService)
    {
        LocalSettingsService = localSettingsService;
        LocalSettingsService.OnSettingChanged += async (key, _) => await OnSettingChanged(key);
        _jumpListService = jumpListService;
        // _filterService.OnFilterChanged += () => UpdateDisplay(UpdateType.ApplyFilter);
        _infoService = infoService;
        _bgTaskService = bgTaskService;
        _galSrcService = galgameSourceService;
        PhrasedEvent2 += OnGameParsed;
        GalgameAddedEvent += OnGameParsed;
        
        
        BgmPhraser bgmPhraser = new(GetBgmData().Result);
        VndbPhraser vndbPhraser = new(GetVndbData().Result);
        YmgalPhraser ymgalPhraser = new();
        CngalPhraser cngalPhraser = new();
        MixedPhraser mixedPhraser = new(bgmPhraser, vndbPhraser, ymgalPhraser, GetMixData());
        PhraserList[(int)RssType.Bangumi] = bgmPhraser;
        PhraserList[(int)RssType.Vndb] = vndbPhraser;
        PhraserList[(int)RssType.Ymgal] = ymgalPhraser;
        PhraserList[(int)RssType.Cngal] = cngalPhraser;
        PhraserList[(int)RssType.Mixed] = mixedPhraser;
        return;

        void OnGameParsed(Galgame game)
        {
            if (!LocalSettingsService.ReadSettingAsync<bool>(KeyValues.DownloadCharacters).Result) return;
            var isNew = false;
            GetGalgameCharactersFromRssTask? task =
                _bgTaskService!.GetBgTask<GetGalgameCharactersFromRssTask>(string.Empty);
            if (task is null)
            {
                task = new GetGalgameCharactersFromRssTask();
                isNew = true;
            }
            task.AddGalgame(game);
            if (isNew)
                _ = _bgTaskService.AddBgTask(task);
        }
    }
    
    public async Task InitAsync()
    {
        _dbSet = LocalSettingsService.Database.GetCollection<Galgame>("galgame");
        await LoadGalgames();
        await _jumpListService.CheckJumpListAsync(_galgames);
        await Upgrade();
    }

    public async Task StartAsync()
    {
        // 以下代码用于临时修复dev版中的问题，发布版中应该删除
        // foreach (var gal in _galgames.Where(g => g.Sources.Count == 0))
        // {
        //     var targetSrc = _galSrcService.GetGalgameSource(GalgameSourceType.LocalFolder,
        //         Directory.GetParent(gal.Path)!.FullName);
        //     if (targetSrc is null) continue;
        //     _galSrcService.MoveInNoOperate(targetSrc, gal, gal.Path);
        // }
        await Task.CompletedTask;
    }

    /// <summary>
    /// 从设置中读取galgames
    /// </summary>
    private async Task LoadGalgames()
    {
        await ImportAsync();

        List<Galgame> galgames = [];
        await Task.Run(() =>
        {
            galgames = _dbSet.FindAll().ToList();
        }); //用Task.Run运行，防止阻塞UI线程
        _galgames.SyncCollection(galgames);
        
        foreach (Galgame g in _galgames)
        {
            g.ErrorOccurred += e =>
                _infoService.Event(EventType.GalgameEvent, InfoBarSeverity.Warning, "GalgameEvent", e);
            // 数目增加
            if (g.Ids.Length < Galgame.PhraserNumber)
            {
                g.Ids = g.Ids.ResizeArray(Galgame.PhraserNumber);
            }
        }
        GalgameLoadedEvent?.Invoke();
    }

    /// <summary>
    /// 可能不同版本行为不同，需要对已存储的galgame进行升级
    /// </summary>
    private async Task Upgrade()
    {
        if (!await LocalSettingsService.ReadSettingAsync<bool>(KeyValues.IdFromMixedUpgraded))
        {
            foreach (Galgame galgame in _galgames)
                galgame.UpdateIdFromMixed();
            await LocalSettingsService.SaveSettingAsync(KeyValues.IdFromMixedUpgraded, true);
        }

        if (!await LocalSettingsService.ReadSettingAsync<bool>(KeyValues.SavePathUpgraded))
        {
            _galgames.ToList().ForEach(galgame => galgame.FindSaveInPath());
            await LocalSettingsService.SaveSettingAsync(KeyValues.SavePathUpgraded, true);
        }
        
        // 给混合搜刮器设置的搜刮优先级添加新添加的搜刮器
        if (await LocalSettingsService.ReadSettingAsync<int>(KeyValues.MixedPhraserOrderVersion) !=
            MixedPhraserOrder.Version)
            await MixedPhraserOrderUpdate();

        // 游戏列表数据库化
        await UpgradeToLiteDb();
    }
    
    public async Task RemoveGalgame(Galgame galgame, bool removeFromDisk = false)
    {
        await UiThreadInvokeHelper.InvokeAsync(() => _galgames.Remove(galgame));
        List<GalgameSourceBase> tmpList = new(galgame.Sources);
        foreach (GalgameSourceBase s in tmpList)
            _galSrcService.MoveOutNoOperate(s, galgame);
        if (removeFromDisk) await Task.Run(galgame.Delete);
        _dbSet.Delete(galgame.Uuid);
        await UiThreadInvokeHelper.InvokeAsync(() => GalgameDeletedEvent?.Invoke(galgame));
    }
    
    public async Task<Galgame> PhraseGalInfoAsync(Galgame galgame, RssType rssType = RssType.None,
        bool requireConfirm = false)
    {
        if (!_galgames.Contains(galgame)) throw new PvnException($"Game {galgame.Name.Value} is not in game list");
        IsPhrasing = true;
        try
        {
            RssType selectedRss = rssType;
            if(selectedRss == RssType.None)
                selectedRss = galgame.RssType == RssType.None ? await LocalSettingsService.ReadSettingAsync<RssType>(KeyValues.RssType) : galgame.RssType;
            Galgame result = await PhraserAsync(galgame, PhraserList[(int)selectedRss]);
            if (requireConfirm)
            {
                ConfirmGalInfoDialog dialog = new(galgame, result, this);
                ContentDialogResult tmp = await dialog.ShowAsync();
                if (tmp == ContentDialogResult.Secondary)
                    throw new PvnException("Canceled".GetLocalized());
            }
        
            if (await LocalSettingsService.ReadSettingAsync<bool>(KeyValues.SyncPlayStatusWhenPhrasing))
            {
                // 优先Bgm
                await DownLoadPlayStatusAsync(galgame, RssType.Vndb);
                await DownLoadPlayStatusAsync(galgame, RssType.Bangumi);
            }
            await SaveGalgameAsync(galgame);
            IsPhrasing = false;
            await UiThreadInvokeHelper.InvokeAsync(() =>
            {
                PhrasedEvent?.Invoke();
                PhrasedEvent2?.Invoke(galgame);
            });
            return result;
        }
        finally
        {
            IsPhrasing = false;
        }
    }

    public async Task<Galgame> ParseGalInfoOnlyAsync(Galgame galgame, RssType rssType = RssType.None, bool requireConfirm = false)
    {
        RssType selectedRss = rssType;
        if (selectedRss == RssType.None)
            selectedRss = galgame.RssType == RssType.None
                ? LocalSettingsService.ReadSettingAsync<RssType>(KeyValues.RssType).Result
                : galgame.RssType;
        Galgame result = await PhraserAsync(galgame, PhraserList[(int)selectedRss]);
        if (requireConfirm)
        {
            ConfirmGalInfoDialog dialog = new(galgame, result, this);
            ContentDialogResult tmp = await dialog.ShowAsync();
            if (tmp == ContentDialogResult.Secondary)
                throw new PvnException("Canceled".GetLocalized());
        }
        return result;
    }

    public async Task ExportAsync(Action<string, int, int>? progress)
    {
        ObservableCollection<Galgame> tmp = new(_galgames.Select(g => g.DeepClone()));
        for(var i = 0; i < tmp.Count; i++)
        {
            Galgame game = tmp[i];
            progress?.Invoke("GalgameCollectionService_Export_Progress".GetLocalized(game.Name.Value ?? string.Empty),
                i + 1, tmp.Count);
            if (Utils.IsImageValid(game.ImagePath.Value))
                game.ImagePath.ForceSet(await LocalSettingsService.AddImageToExportAsync(game.ImagePath.Value) ??
                                        Galgame.DefaultImagePath);
            foreach (GalgameCharacter character in game.Characters)
            {
                if (Utils.IsImageValid(character.ImagePath))
                    character.ImagePath = await LocalSettingsService.AddImageToExportAsync(character.ImagePath) ??
                                          Galgame.DefaultImagePath;
                if (Utils.IsImageValid(character.PreviewImagePath))
                    character.PreviewImagePath =
                        await LocalSettingsService.AddImageToExportAsync(character.PreviewImagePath) ??
                        Galgame.DefaultImagePath;
            }
        }
        await LocalSettingsService.AddToExportAsync(KeyValues.Galgames, tmp);
    }

    public async Task<GalgameCharacter> PhraseGalCharacterAsync(GalgameCharacter galgameCharacter, RssType rssType = RssType.None)
    {
        GalgameCharacter result = await PhraserCharacterAsync(galgameCharacter, PhraserList[(int)rssType]);
        return result;
    }

    private static async Task<GalgameCharacter> PhraserCharacterAsync(GalgameCharacter galgameCharacter, IGalInfoPhraser phraser)
    {
        if (phraser is not IGalCharacterPhraser characterPhraser) return galgameCharacter;
        GalgameCharacter? tmp = await characterPhraser.GetGalgameCharacter(galgameCharacter);
        if (tmp == null) return galgameCharacter;
        galgameCharacter.Name = tmp.Name;
        galgameCharacter.Summary = tmp.Summary;
        galgameCharacter.Gender = tmp.Gender;
        galgameCharacter.BirthDay = tmp.BirthDay;
        galgameCharacter.BirthMon = tmp.BirthMon;
        galgameCharacter.BirthYear = tmp.BirthYear;
        galgameCharacter.BirthDate = tmp.BirthDate;
        galgameCharacter.BloodType = tmp.BloodType;
        galgameCharacter.Height = tmp.Height;
        galgameCharacter.Weight = tmp.Weight;
        galgameCharacter.BWH = tmp.BWH;
        
        galgameCharacter.ImagePath = await DownloadHelper.DownloadAndSaveImageAsync(tmp.ImageUrl, 
            fileNameWithoutExtension:$"{galgameCharacter.Name}_Large") ?? Galgame.DefaultImagePath;
        galgameCharacter.PreviewImagePath = await DownloadHelper.DownloadAndSaveImageAsync(tmp.PreviewImageUrl, 
                                                fileNameWithoutExtension:$"{galgameCharacter.Name}_Preview") ??
                                            Galgame.DefaultImagePath;
        return galgameCharacter;
    }

    private static async Task<Galgame> PhraserAsync(Galgame galgame, IGalInfoPhraser phraser)
    {
        Galgame? tmp = await phraser.GetGalgameInfo(galgame);
        if (tmp == null) return galgame;

        await UiThreadInvokeHelper.InvokeAsync(async () =>
        {
            galgame.RssType = phraser.GetPhraseType();
            galgame.Id = tmp.Id;
            galgame.Description.Value = tmp.Description.Value;
            if (tmp.Description != Galgame.DefaultString)
                galgame.Description.Value = tmp.Description.Value;
            if (tmp.Developer != Galgame.DefaultString)
                galgame.Developer.Value = tmp.Developer.Value;
            if (tmp.ExpectedPlayTime != Galgame.DefaultString)
                galgame.ExpectedPlayTime.Value = tmp.ExpectedPlayTime.Value;
            if (await LocalSettingsService.ReadSettingAsync<bool>(KeyValues.OverrideLocalName))
            {
                if (await LocalSettingsService.ReadSettingAsync<bool>(KeyValues.OverrideLocalNameWithChinese))
                {
                    galgame.Name.Value = !string.IsNullOrEmpty(tmp.CnName) ? tmp.CnName : tmp.Name.Value;
                }
                else
                {
                    galgame.Name.Value = tmp.Name.Value;
                }
            }
            galgame.ImageUrl = tmp.ImageUrl;
            galgame.Rating.Value = tmp.Rating.Value;
            if (!galgame.Tags.IsLock && tmp.Tags.Value?.Count > 0) // Tags不能直接赋值，直接替换容器会抛出奇怪的绑定异常
            {
                galgame.Tags.Value ??= new ObservableCollection<string>(); //不应该发生
                galgame.Tags.Value.Clear();
                foreach (var tag in tmp.Tags.Value)
                    galgame.Tags.Value.Add(tag);
            }
            galgame.Characters = tmp.Characters;
            galgame.ImagePath.Value = await DownloadHelper.DownloadAndSaveImageAsync(galgame.ImageUrl) ?? Galgame.DefaultImagePath;
            galgame.ReleaseDate.Value = tmp.ReleaseDate.Value;
            galgame.LastFetchInfoTime = DateTime.Now;
        });
        
        return galgame;
    }
    
    /// <summary>
    /// 下载某个游戏的游玩状态
    /// </summary>
    /// <param name="galgame">游戏</param>
    /// <param name="source">下载源</param>
    /// <returns>(下载结果，结果解释)</returns>
    public async Task<(GalStatusSyncResult, string)> DownLoadPlayStatusAsync(Galgame galgame, RssType source)
    {
        if (PhraserList[(int)source] is IGalStatusSync galStatusSync)
            return await galStatusSync.DownloadAsync(galgame);
        return (GalStatusSyncResult.Other, "这个数据源不支持同步游玩状态");
    }

    /// <summary>
    /// 从某个信息源下载所有游戏的游玩状态
    /// </summary>
    /// <param name="source">信息源</param>
    /// <returns>(结果，结果解释)</returns>
    public async Task<(GalStatusSyncResult ,string)> DownloadAllPlayStatus(RssType source)
    {
        var msg = string.Empty;
        GalStatusSyncResult result = GalStatusSyncResult.Other;
        IGalInfoPhraser phraser = PhraserList[(int)source];
        if (phraser is IGalStatusSync sync)
            (result, msg) = await sync.DownloadAllAsync(_galgames);
        await SaveGalgamesAsync();
        return (result, msg);
    }

    /// <summary>
    /// 刷新显示列表
    /// </summary>
    public void RefreshDisplay()
    {
    }

    /// <summary>
    /// 向信息源上传游玩状态
    /// </summary>
    /// <param name="galgame">要同步的游戏</param>
    /// <param name="rssType">信息源</param>
    /// <returns>(上传结果， 结果解释)</returns>
    /// <exception cref="NotSupportedException">若信息源没有实现IGalStatusSync，则抛此异常</exception>
    public async Task<(GalStatusSyncResult, string)> UploadPlayStatusAsync(Galgame galgame, RssType rssType)
    {
        IGalInfoPhraser phraser = PhraserList[(int)rssType];
        if (phraser is IGalStatusSync syncer)
            return await syncer.UploadAsync(galgame);
        throw new NotSupportedException("这个数据源不支持同步游玩状态");
    }

    /// <summary>
    /// 获取所有galgame
    /// </summary>
    public ObservableCollection<Galgame> Galgames => _galgames;

    /// <summary>
    /// 获取搜索建议
    /// </summary>
    /// <param name="current">当前文本串</param>
    /// <param name="searchName">是否包括游戏名的搜索建议</param>
    /// <param name="searchDeveloper">是否包括开发商搜索建议</param>
    /// <param name="searchTag">是否包括Tag搜索建议</param>
    /// <returns>搜索建议，若没有则返回空List</returns>
    public async Task<List<string>> GetSearchSuggestions(string current, bool searchName = true,
        bool searchDeveloper = true, bool searchTag = true)
    {
        List<string> tmp = new();
        await Task.Run(() =>
        {
            if (searchName) //Name
                tmp.AddRange(from galgame in _galgames
                    where galgame.Name.Value is not null && galgame.Name.Value.ContainX(current)
                    select galgame.Name.Value);
            if (searchDeveloper) //Developer
                tmp.AddRange(from galgame in _galgames
                    where galgame.Developer.Value is not null && galgame.Developer.Value.ContainX(current)
                    select galgame.Developer.Value);
            if (searchTag) //Tag
                tmp.AddRange(from galgame in _galgames
                    from tag in galgame.Tags.Value ?? new ObservableCollection<string>()
                    where tag.ContainX(current)
                    select tag);
        });
        //去重
        tmp.Sort((a,b)=> a.CompareX(b));
        return tmp.Where((t, i) => i == 0 || t.CompareX(tmp[i - 1]) !=0).ToList();
    }
    
    public Galgame? GetGalgameFromUid(GalgameUid? uid, GalgameUidFetchMode mode = GalgameUidFetchMode.Same)
    {
        if (uid is null) return null;
        if (mode == GalgameUidFetchMode.Same)
            return _galgames.FirstOrDefault(g => g.Uid.IsSame(uid));
        if (mode == GalgameUidFetchMode.MaxSimilarity)
        {
            var max = 0;
            Galgame? result = null;
            foreach(Galgame g in _galgames)
                if (g.Uid.Similarity(uid) > max)
                {
                    result = g;
                    max = g.Uid.Similarity(uid);
                }
            return result;
        }
        return null;
    }

    public Galgame? GetGalgameFromUuid(Guid? uuid)
    {
        if (uuid is null) return null;
        return _galgames.FirstOrDefault(g => g.Uuid == uuid);
    }

    public Galgame? GetGalgameFromId(string? id, RssType rssType)
    {
        if (id is null) return null;
        return _galgames.FirstOrDefault(g => g.Ids[(int)rssType] == id);
    }
    
    public Galgame? GetGalgameFromName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        return _galgames.FirstOrDefault(g => g.Name.Value == name);
    }
    
    public Task SaveGalgamesAsync()
    {
        return Task.Run(() =>
        {
            _dbSet.Upsert(_galgames);
        });
    }
    
    public async Task SaveGalgameAsync(Galgame galgame)
    {
        _dbSet.Upsert(galgame);
        if (await LocalSettingsService.ReadSettingAsync<bool>(KeyValues.SaveBackupMetadata))
            await SaveMetaAsync(galgame);
    }
    
    /// <summary>
    /// 保存galgame的信息备份（包括meta.json和封面图）
    /// </summary>
    /// <param name="galgame"></param>
    private async Task SaveMetaAsync(Galgame galgame)
    {
        IEnumerable<GalgameSourceType> types = galgame.Sources.Select(s => s.SourceType).Distinct();
        List<(Task, GalgameSourceType)> tasks = new();
        foreach (GalgameSourceType type in types) 
            tasks.Add((SourceServiceFactory.GetSourceService(type).SaveMetaAsync(galgame), type));
        foreach ((Task, GalgameSourceType) t in tasks)
        {
            try
            {
                await t.Item1;
            }
            catch (Exception e)
            {
                _infoService.Event(EventType.GalgameEvent, InfoBarSeverity.Warning,
                    "GalgameCollectionService_BackupMetaFailed".GetLocalized(galgame.Name.Value
                                                                             ?? string.Empty, t.Item2.ToString()), e);
            }
        }
    }

    /// <summary>
    /// 保存所有galgame的信息备份（包括meta.json和封面图）
    /// </summary>
    public async Task SaveAllMetaAsync()
    {
        foreach (Galgame galgame in _galgames)
        {
            MetaSavedEvent?.Invoke(galgame);
            await SaveMetaAsync(galgame);
        }
    }

    /// <summary>
    /// 获取galgame的存档文件夹
    /// </summary>
    /// <param name="galgame">galgame</param>
    /// <returns>存档文件夹地址，若用户取消返回null</returns>
    private async Task<string?> GetGalgameSaveAsync(Galgame galgame)
    {
        List<string> subFolders = galgame.GetSubFolders();
        FolderPickerDialog dialog = new(App.MainWindow!.Content.XamlRoot, "GalgameCollectionService_SelectSavePosition".GetLocalized(), subFolders);
        return await dialog.ShowAndAwaitResultAsync();
    }
    
    /// <summary>
    /// 获取并设置galgame的可执行文件
    /// </summary>
    /// <param name="galgame">galgame</param>
    /// <returns>可执行文件地址，如果用户取消或找不到可执行文件则返回null</returns>
    public async Task<string?> GetGalgameExeAsync(Galgame galgame)
    {
        if (!galgame.CheckExistLocal() || galgame.LocalPath is null) return null;
        List<string> exes = galgame.GetExesAndBats();
        switch (exes.Count)
        {
            case 0:
            {
                ContentDialog dialog = new()
                {
                    XamlRoot = App.MainWindow!.Content.XamlRoot,
                    Title = "Error".GetLocalized(),
                    Content = "GalgameCollectionService_NotExeFounded".GetLocalized(),
                    PrimaryButtonText = "Yes".GetLocalized()
                };
                await dialog.ShowAsync();
                return null;
            }
            case 1:
                galgame.ExePath = exes[0];
                break;
            default:
            {
                SelectFileDialog dialog = new(galgame.LocalPath, new[] {".exe", ".bat", ".lnk"}, 
                    "GalgameCollectionService_SelectExe".GetLocalized(), false);
                await dialog.ShowAsync();
                if (dialog.SelectedFilePath == null) return null;
                galgame.ExePath = dialog.SelectedFilePath;
                break;
            }
        }
        return galgame.ExePath;
    }

    /// <summary>
    /// 转换存档位置
    /// </summary>
    /// <param name="galgame">galgame</param>
    public async Task ChangeGalgameSavePosition(Galgame galgame)
    {
        if (galgame.SavePath is not null && new DirectoryInfo(galgame.SavePath).Exists == false)
            galgame.SavePath = null;
            
        if (galgame.SavePath is not null) //目前在云端
        {
            await Task.Run(() =>
            {
                FolderOperations.ConvertSymbolicLinkToActual(galgame.SavePath);
                galgame.SavePath = null;
            });
        }
        else //目前在本地
        {
            var remoteRoot = await LocalSettingsService.ReadSettingAsync<string>(KeyValues.RemoteFolder);
            if (string.IsNullOrEmpty(remoteRoot))
            {
                ContentDialog dialog = new()
                {
                    XamlRoot = App.MainWindow!.Content.XamlRoot,
                    Title = "Error".GetLocalized(),
                    Content = "GalgameCollectionService_CloudRootNotSet".GetLocalized(),
                    PrimaryButtonText = "Yes".GetLocalized()
                };
                await dialog.ShowAsync();
                return;
            }
            var localSavePath = await GetGalgameSaveAsync(galgame);
            if (localSavePath == null) return;
            var tmp = localSavePath[..localSavePath.LastIndexOf('\\')];
            var target = tmp[tmp.LastIndexOf('\\')..] + localSavePath[localSavePath.LastIndexOf('\\')..];
            remoteRoot += target;

            try
            {
                if (new DirectoryInfo(remoteRoot).Exists) //云端已存在同名文件夹
                {
                    var choose = 0;
                    ContentDialog dialog = new()
                    {
                        XamlRoot = App.MainWindow!.Content.XamlRoot,
                        Title = "GalgameCollectionService_SelectOperateTitle".GetLocalized(),
                        Content = "GalgameCollectionService_SelectOperateMsg".GetLocalized(),
                        PrimaryButtonText = "GalgameCollectionService_Local".GetLocalized(),
                        SecondaryButtonText = "GalgameCollectionService_Cloud".GetLocalized(),
                        CloseButtonText = "Cancel".GetLocalized()
                    };
                    dialog.PrimaryButtonClick += (_, _) => choose = 1;
                    dialog.SecondaryButtonClick += (_, _) => choose = 2;
                    await dialog.ShowAsync();
                    if (choose == 1)
                    {
                        new DirectoryInfo(remoteRoot).Delete(true); //删除云端文件夹
                        FolderOperations.ConvertFolderToSymbolicLink(localSavePath, remoteRoot);
                    }
                    else if (choose == 2)
                    {
                        new DirectoryInfo(localSavePath).Delete(true); //删除本地文件夹
                        FolderOperations.CreateSymbolicLink(localSavePath, remoteRoot);
                    }
                }
                else
                    FolderOperations.ConvertFolderToSymbolicLink(localSavePath, remoteRoot);
                galgame.SavePath = localSavePath;
            }
            catch (Exception e) //创建符号链接失败，把存档复制回去
            {
                if(Directory.Exists(localSavePath))
                    Directory.Delete(localSavePath, true);
                FolderOperations.Copy(remoteRoot, localSavePath);
                //弹出提示框
                StackPanel stackPanel = new();
                stackPanel.Children.Add(new TextBlock {Text = "GalgameCollectionService_CreateSymbolicLinkFailed".GetLocalized()});
                stackPanel.Children.Add(new TextBlock
                {
                    Text = e.Message + "\n" + e.StackTrace, 
                    TextWrapping = TextWrapping.Wrap
                });
                ContentDialog dialog = new()
                {
                    XamlRoot = App.MainWindow!.Content.XamlRoot,
                    Title = "Error".GetLocalized(),
                    Content = stackPanel,
                    PrimaryButtonText = "Yes".GetLocalized()
                };
                await dialog.ShowAsync();
            }
        }
        
        await SaveGalgameAsync(galgame);
    }

    /// <summary>
    /// 从设置中读取bangumi的设置
    /// </summary>
    private async Task<BgmPhraserData> GetBgmData()
    {
        BgmPhraserData data = new()
        {
            Token = (await LocalSettingsService.ReadSettingAsync<BgmAccount>(KeyValues.BangumiAccount))?.BangumiAccessToken ?? ""
        };
        return data;
    }
    
    /// <summary>
    /// 从设置中读取Vndb的设置
    /// </summary>
    private async Task<VndbPhraserData> GetVndbData()
    {
        VndbPhraserData data = new()
        {
            Token = (await LocalSettingsService.ReadSettingAsync<VndbAccount>(KeyValues.VndbAccount))?.Token
        };
        return data;
    }

    private MixedPhraserData GetMixData()
    {
        return new MixedPhraserData
        {
            Order = LocalSettingsService.ReadSettingAsync<MixedPhraserOrder>(KeyValues.MixedPhraserOrder).Result!,
        };
    }

    private async Task OnSettingChanged(string key)
    {
        switch (key)
        {
            case KeyValues.BangumiAccount:
                PhraserList[(int)RssType.Bangumi].UpdateData(await GetBgmData());
                break;
            case KeyValues.VndbAccount:
                PhraserList[(int)RssType.Vndb].UpdateData(await GetVndbData());
                break;
            case KeyValues.MixedPhraserOrder:
                PhraserList[(int)RssType.Mixed].UpdateData(GetMixData());
                break;
        }
    }
    
    #region UPGRADE
    private async Task MixedPhraserOrderUpdate()
    {
        try
        {
            MixedPhraserOrder orders =
                (await LocalSettingsService.ReadSettingAsync<MixedPhraserOrder>(KeyValues.MixedPhraserOrder))!;
            IEnumerable<PropertyInfo> properties = orders.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.PropertyType == typeof(ObservableCollection<RssType>));
            MixedPhraserOrder defOrder = new MixedPhraserOrder().SetToDefault();
            foreach (PropertyInfo prop in properties)
            {
                ObservableCollection<RssType> order = (ObservableCollection<RssType>)prop.GetValue(orders)!;
                ObservableCollection<RssType> target = (ObservableCollection<RssType>)prop.GetValue(defOrder)!;
                foreach (RssType type in target.Where(type => !order.Contains(type)))
                    order.Add(type);
            }

            await LocalSettingsService.SaveSettingAsync(KeyValues.MixedPhraserOrderVersion,
                MixedPhraserOrder.Version);
            await LocalSettingsService.SaveSettingAsync(KeyValues.MixedPhraserOrder, orders);
        }
        catch (Exception e) //不应该发生
        {
            _infoService.Event(EventType.AppError, InfoBarSeverity.Error, "Upgrade failed", e);
        }
    }

    /// <summary>
    /// 升级存储格式到LiteDB
    /// </summary>
    /// <returns></returns>
    private async Task UpgradeToLiteDb()
    {
        LocalSettingStatus status = await LocalSettingsService.ReadSettingAsync<LocalSettingStatus>(KeyValues.DataStatus, true) ?? new();
        if (status.GameLiteDbUpgrade) return;
        try
        {
            foreach (Galgame game in _galgames)
                _dbSet.Upsert(game);
            await LocalSettingsService.SaveSettingAsync(KeyValues.DataStatus, status, true);
            await LocalSettingsService.RemoveSettingAsync(KeyValues.Galgames, true); //先保存标识再删除，防止删除出错导致读取继续使用旧json方案
        }
        catch (Exception e)
        {
            _infoService.Event(EventType.UpgradeError, InfoBarSeverity.Warning, "GalgameCollectionService_UpgradeToLiteDB_Failed".GetLocalized(), e);
        }
        status.GameLiteDbUpgrade = true;
    }

    #endregion

    private async Task ImportAsync()
    {
        LocalSettingStatus? status =
            await LocalSettingsService.ReadSettingAsync<LocalSettingStatus>(KeyValues.DataStatus, true);
        if (status?.ImportGalgame is not false) return;
        foreach (Galgame game in await LocalSettingsService.ReadSettingAsync<List<Galgame>>
                     (KeyValues.Galgames, true) ?? []) 
            _galgames.Add(game);
        foreach (Galgame game in _galgames)
        {
            game.ImagePath.ForceSet(await LocalSettingsService.GetImageFromImportAsync(game.ImagePath.Value));
            foreach (GalgameCharacter character in game.Characters)
            {
                character.ImagePath = (await LocalSettingsService.GetImageFromImportAsync(character.ImagePath))!;
                character.PreviewImagePath =
                    (await LocalSettingsService.GetImageFromImportAsync(character.PreviewImagePath))!;
            }
        }
        status.ImportGalgame = true;
        await LocalSettingsService.SaveSettingAsync(KeyValues.DataStatus, status, true);
        await SaveGalgamesAsync();
        _galgames.Clear(); //只是为了保存临时借用数组，还原回之前的状态（全空）
    }
}

public class FolderPickerDialog : ContentDialog
{
    private string? _selectedFolder;
    private readonly TaskCompletionSource<string?> _folderSelectedTcs = new TaskCompletionSource<string?>();
    public FolderPickerDialog(XamlRoot xamlRoot, string title, List<string> files)
    {
        XamlRoot = xamlRoot;
        Title = title;
        Content = CreateContent(files);
        PrimaryButtonText = "Yes".GetLocalized();
        SecondaryButtonText = "GalgameCollectionService_FolderPickerDialog_ChoseAnotherFolder".GetLocalized();
        CloseButtonText = "Cancel".GetLocalized();
        IsPrimaryButtonEnabled = false;
        PrimaryButtonClick += (_, _) => { _folderSelectedTcs.TrySetResult(_selectedFolder); };
        SecondaryButtonClick += async (_, _) =>
        {
            FolderPicker folderPicker = new();
            folderPicker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, App.MainWindow!.GetWindowHandle());
            StorageFolder? folder = await folderPicker.PickSingleFolderAsync();
            if (folder != null)
            {
                _selectedFolder = folder.Path;
                _folderSelectedTcs.TrySetResult(folder.Path);
            }
            else
                _folderSelectedTcs.TrySetResult(null);
        };
        CloseButtonClick += (_, _) => { _folderSelectedTcs.TrySetResult(null); };
    }
    private UIElement CreateContent(List<string> files)
    {
        StackPanel stackPanel = new();
        foreach (var file in files)
        {
            RadioButton radioButton = new()
            {
                Content = file,
                GroupName = "ExeFiles"
            };
            radioButton.Checked += RadioButton_Checked;
            stackPanel.Children.Add(radioButton);
        }
        return stackPanel;
    }
    private void RadioButton_Checked(object sender, RoutedEventArgs e)
    {
        RadioButton radioButton = (RadioButton)sender;
        _selectedFolder = radioButton.Content.ToString()!;
        IsPrimaryButtonEnabled = true;
    }
    public async Task<string?> ShowAndAwaitResultAsync()
    {
        await ShowAsync();
        return await _folderSelectedTcs.Task;
    }
}

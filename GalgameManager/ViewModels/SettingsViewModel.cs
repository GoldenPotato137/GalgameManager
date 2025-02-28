using Windows.ApplicationModel;
using Windows.Services.Store;
using Windows.Storage;
using Windows.Storage.Pickers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalgameManager.Contracts.Services;
using GalgameManager.Contracts.ViewModels;
using GalgameManager.Enums;
using GalgameManager.Helpers;
using GalgameManager.Models;
using GalgameManager.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.Security.Credentials.UI;
using Windows.Security.Credentials;
using GalgameManager.Helpers.Phrase;
using GalgameManager.Models.BgTasks;
using GalgameManager.Models.Sources;
using GalgameManager.Views.Dialog;
using AppInstance = Microsoft.Windows.AppLifecycle.AppInstance;

namespace GalgameManager.ViewModels;


public partial class SettingsViewModel : ObservableObject, INavigationAware
{
    private readonly ILocalSettingsService _localSettingsService;
    private readonly GalgameCollectionService _galgameCollectionService;
    private readonly INavigationService _navigationService;
    private readonly IUpdateService _updateService;
    private readonly IThemeSelectorService _themeSelectorService;
    private readonly ICategoryService _categoryService;
    private readonly IInfoService _infoService;
    private readonly IBgTaskService _bgTaskService;
    private string _versionDescription;

    #region UI_STRINGS //历史遗留，不要继续使用这种方式获取字符串
    
    private static readonly ResourceLoader ResourceLoader = new();
    public readonly string UiThemeTitle = ResourceLoader.GetString("SettingsPage_ThemeTitle");
    public readonly string UiThemeDescription = ResourceLoader.GetString("SettingsPage_ThemeDescription");
    public readonly string UiRssTitle = ResourceLoader.GetString("SettingsPage_RssTitle");
    public readonly string UiRssDescription = ResourceLoader.GetString("SettingsPage_RssDescription");
    public readonly string UiDownloadTitle = ResourceLoader.GetString("SettingsPage_DownloadTitle");
    public readonly string UiDownloadDescription = ResourceLoader.GetString("SettingsPage_DownloadDescription");
    public readonly string UiCloudSyncTitle = ResourceLoader.GetString("SettingsPage_CloudSyncTitle");
    public readonly string UiCloudSyncDescription = ResourceLoader.GetString("SettingsPage_CloudSyncDescription");
    public readonly string UiCloudSyncRoot = ResourceLoader.GetString("SettingsPage_CloudSync_Root");
    public readonly string UiLibraryTitle = "SettingsPage_LibraryTitle".GetLocalized();
    public readonly string UiLibraryDescription = "SettingsPage_LibraryDescription".GetLocalized();
    public readonly string UiLibraryMetaBackup = "SettingsPage_Library_MetaBackup".GetLocalized();
    public readonly string UiLibraryMetaBackupDescription = "SettingsPage_Library_MetaBackupDescription".GetLocalized();
    public readonly string UiLibrarySearchSubPath = "SettingsPage_Library_SearchSubPath".GetLocalized();
    public readonly string UiLibrarySearchSubPathDescription = "SettingsPage_Library_SearchSubPathDescription".GetLocalized();
    public readonly string UiLibrarySearchSubPathDepth = "SettingsPage_Library_SearchSubPathDepth".GetLocalized();
    public readonly string UiLibrarySearchSubPathDepthDescription = "SettingsPage_Library_SearchSubPathDepthDescription".GetLocalized();
    public readonly string UiLibraryNameDescription = "SettingsPage_Library_NameDescription".GetLocalized();
    public readonly string UiLibrarySearchRegex = "SettingsPage_Library_SearchRegex".GetLocalized();
    public readonly string UiLibrarySearchRegexDescription = "SettingsPage_Library_SearchRegexDescription".GetLocalized();
    public readonly string UiLibrarySearchRegexIndex = "SettingsPage_Library_SearchRegexIndex".GetLocalized();
    public readonly string UiLibrarySearchRegexIndexDescription = "SettingsPage_Library_SearchRegexIndexDescription".GetLocalized();
    public readonly string UiLibrarySearchRegexRemoveBorder = "SettingsPage_Library_SearchRegexRemoveBorder".GetLocalized();
    public readonly string UiLibrarySearchRegexRemoveBorderDescription = "SettingsPage_Library_SearchRegexRemoveBorderDescription".GetLocalized();
    public readonly string UiLibrarySearchRegexTryItOut = "SettingsPage_Library_SearchRegexTryItOut".GetLocalized();
    public readonly string UiLibraryGameSearchRule = "SettingsPage_Library_GameSearchRule".GetLocalized();
    public readonly string UiLibraryGameSearchRuleDescription = "SettingsPage_Library_GameSearchRuleDescription".GetLocalized();
    public readonly string UiLibraryGameSearchRuleMustContain = "SettingsPage_Library_GameSearchRuleMustContain".GetLocalized();
    public readonly string UiLibraryGameSearchRuleShouldContain = "SettingsPage_Library_GameSearchRuleShouldContain".GetLocalized();

    #endregion

    public string VersionDescription
    {
        get => _versionDescription;
        set => SetProperty(ref _versionDescription, value);
    }
    
    public async void OnNavigatedTo(object parameter)
    {
        await _updateService.UpdateSettingsBadgeAsync();
        UpdateAvailable = await _updateService.CheckUpdateAsync();
    }

    public void OnNavigatedFrom()
    {
        _updateService.SettingBadgeEvent -= HandelSettingBadgeEvent;
        _galgameCollectionService.MetaSavedEvent -= SetSaveMetaPopUp;
    }

    public SettingsViewModel(IThemeSelectorService themeSelectorService, ILocalSettingsService localSettingsService, 
        IGalgameCollectionService galgameService, IUpdateService updateService, INavigationService navigationService,
        ICategoryService categoryService, IInfoService infoService, IBgTaskService bgTaskService)
    {
        _categoryService = categoryService;
        _themeSelectorService = themeSelectorService;
        _navigationService = navigationService;
        _updateService = updateService;
        updateService.SettingBadgeEvent += HandelSettingBadgeEvent;
        _versionDescription = GetVersionDescription();
        _localSettingsService = localSettingsService;
        _infoService = infoService;
        _bgTaskService = bgTaskService;
        
        //THEME
        _elementTheme = themeSelectorService.Theme;
        _fixHorizontalPicture = _localSettingsService.ReadSettingAsync<bool>(KeyValues.FixHorizontalPicture).Result;
        TimeAsHour = _localSettingsService.ReadSettingAsync<bool>(KeyValues.TimeAsHour).Result;
        GalgamePageNewLayout = _localSettingsService.ReadSettingAsync<bool>(KeyValues.GalgamePageNewLayout).Result;
        //GAME
        _recordOnlyForeground = _localSettingsService.ReadSettingAsync<bool>(KeyValues.RecordOnlyWhenForeground).Result;
        _playingWindowMode = _localSettingsService.ReadSettingAsync<WindowMode>(KeyValues.PlayingWindowMode).Result;
        LocalEmulatorPath = _localSettingsService.ReadSettingAsync<string>(KeyValues.LocaleEmulatorPath).Result;
        PlayingWindowModes = new[] {WindowMode.Minimize, WindowMode.SystemTray, WindowMode.None };
        //RSS
        RssType = _localSettingsService.ReadSettingAsync<RssType>(KeyValues.RssType).Result;
        //DOWNLOAD_BEHAVIOR
        _overrideLocalName = _localSettingsService.ReadSettingAsync<bool>(KeyValues.OverrideLocalName).Result;
        _overrideLocalNameWithChinese = _localSettingsService.ReadSettingAsync<bool>(KeyValues.OverrideLocalNameWithChinese).Result;
        _autoCategory = _localSettingsService.ReadSettingAsync<bool>(KeyValues.AutoCategory).Result;
        _downloadPlayStatusWhenPhrasing = _localSettingsService.ReadSettingAsync<bool>(KeyValues.SyncPlayStatusWhenPhrasing).Result;
        _downloadCharacters = _localSettingsService.ReadSettingAsync<bool>(KeyValues.DownloadCharacters).Result;
        //LIBRARY
        _galgameCollectionService = ((GalgameCollectionService?)galgameService)!;
        _galgameCollectionService.MetaSavedEvent += SetSaveMetaPopUp;
        _metaBackup = _localSettingsService.ReadSettingAsync<bool>(KeyValues.SaveBackupMetadata).Result;
        _searchSubFolder = _localSettingsService.ReadSettingAsync<bool>(KeyValues.SearchChildFolder).Result;
        _searchSubFolderDepth = _localSettingsService.ReadSettingAsync<int>(KeyValues.SearchChildFolderDepth).Result;
        _ignoreFetchResult = _localSettingsService.ReadSettingAsync<bool>(KeyValues.IgnoreFetchResult).Result;
        _regex = _localSettingsService.ReadSettingAsync<string>(KeyValues.RegexPattern).Result ?? ".+";
        _regexIndex = _localSettingsService.ReadSettingAsync<int>(KeyValues.RegexIndex).Result;
        _regexRemoveBorder = _localSettingsService.ReadSettingAsync<bool>(KeyValues.RegexRemoveBorder).Result;
        _gameFolderMustContain = _localSettingsService.ReadSettingAsync<string>(KeyValues.GameFolderMustContain).Result ?? "";
        _gameFolderShouldContain = _localSettingsService.ReadSettingAsync<string>(KeyValues.GameFolderShouldContain).Result ?? "";
        //CLOUD
        RemoteFolder = _localSettingsService.ReadSettingAsync<string>(KeyValues.RemoteFolder).Result ?? "";
        //QUICK_START
        _startPage = _localSettingsService.ReadSettingAsync<PageEnum>(KeyValues.StartPage).Result;
        QuitStart = _localSettingsService.ReadSettingAsync<bool>(KeyValues.QuitStart).Result;
        _authenticationType = _localSettingsService.ReadSettingAsync<AuthenticationType>(KeyValues.AuthenticationType).Result;
        _autoStartWhenLogin = _localSettingsService.ReadSettingAsync<bool>(KeyValues.AutoStartWhenLogin).Result;
        _minToTrayWhenAutoStart = _localSettingsService.ReadSettingAsync<bool>(KeyValues.MinToTrayWhenAutoStart).Result;
        //Notification
        NotifyWhenGetGalgameInFolder = _localSettingsService.ReadSettingAsync<bool>(KeyValues.NotifyWhenGetGalgameInFolder).Result;
        NotifyWhenUnpackGame = _localSettingsService.ReadSettingAsync<bool>(KeyValues.NotifyWhenUnpackGame).Result;
        _eventPvnSync = _localSettingsService.ReadSettingAsync<bool>(KeyValues.EventPvnSyncNotify).Result;
        _eventPvnSyncEmpty = _localSettingsService.ReadSettingAsync<bool>(KeyValues.EventPvnSyncEmptyNotify).Result;
        //Other
        UploadToAppCenter = _localSettingsService.ReadSettingAsync<bool>(KeyValues.UploadData).Result;
        MemoryImprove = _localSettingsService.ReadSettingAsync<bool>(KeyValues.MemoryImprove).Result;
        WindowModes = new[] { WindowMode.Normal, WindowMode.Close, WindowMode.SystemTray };
        CloseMode = _localSettingsService.ReadSettingAsync<WindowMode>(KeyValues.CloseMode).Result;
        DevelopmentMode = _localSettingsService.ReadSettingAsync<bool>(KeyValues.DevelopmentMode).Result;
        
        //Check the availability of Windows Hello
        UserConsentVerifierAvailability verifierAvailability = UserConsentVerifier.CheckAvailabilityAsync().AsTask().Result;
        AuthenticationTypes = verifierAvailability != UserConsentVerifierAvailability.Available
            ? new[] { AuthenticationType.NoAuthentication, AuthenticationType.CustomPassword }
            : new[] { AuthenticationType.NoAuthentication, AuthenticationType.WindowsHello, AuthenticationType.CustomPassword };
    }

    #region INFOBAR_CONTROL

    [ObservableProperty] private string _infoBarMsg = string.Empty;
    [ObservableProperty] private InfoBarSeverity _infoBarSeverity = InfoBarSeverity.Informational;
    [ObservableProperty] private bool _isInfoBarOpen;
    private int _displayIndex;

    /// <summary>
    /// 使用InfoBar显示消息
    /// </summary>
    /// <param name="severity">严重程度</param>
    /// <param name="msg">消息本体</param>
    /// <param name="time">显示时间(ms)</param>
    private async Task DisplayMsgAsync(InfoBarSeverity severity, string msg, int time = 3000)
    {
        var index = ++_displayIndex;
        InfoBarSeverity = severity;
        InfoBarMsg = msg;
        IsInfoBarOpen = true;
        await Task.Delay(time);
        if (index == _displayIndex)
            IsInfoBarOpen = false;
    }

    #endregion

    #region UPDATE

    [ObservableProperty] private bool _updateAvailable;
    
    private async Task ShowUpdateNotification()
    {
        ContentDialog updateDialog = new()
        {
            XamlRoot = App.MainWindow!.Content.XamlRoot,
            Title = "SettingsPage_UpdateNotification_Title".GetLocalized(),
            Content = "SettingsPage_UpdateNotification_Msg".GetLocalized(),
            PrimaryButtonText = "SettingsPage_SeeWhatsNew".GetLocalized(),
            CloseButtonText = "OK",
            DefaultButton = ContentDialogButton.Primary
        };
        updateDialog.PrimaryButtonClick += (_, _) =>
            _navigationService.NavigateTo(typeof(UpdateContentViewModel).FullName!);
        await _localSettingsService.SaveSettingAsync(KeyValues.LastNoticeUpdateVersion, RuntimeHelper.GetVersion());
        await updateDialog.ShowAsync();
    }
    
    private async void HandelSettingBadgeEvent(bool result)
    {
        if (result == false) return;
        await ShowUpdateNotification();
        await _updateService.UpdateSettingsBadgeAsync();
    }
    
    #endregion

    #region THEME
    public readonly ElementTheme[] Themes = { ElementTheme.Default, ElementTheme.Light, ElementTheme.Dark };
    [ObservableProperty ]private ElementTheme _elementTheme;
    
    partial void OnElementThemeChanged(ElementTheme value)
    {
        _themeSelectorService.SetThemeAsync(value);
    }

    [ObservableProperty] private bool _fixHorizontalPicture;
    partial void OnFixHorizontalPictureChanged(bool value) => _localSettingsService.SaveSettingAsync(KeyValues.FixHorizontalPicture, value);
    
    // 时间显示单位改为小时
    [ObservableProperty] private bool _timeAsHour;
    partial void OnTimeAsHourChanged(bool value) => _localSettingsService.SaveSettingAsync(KeyValues.TimeAsHour, value);

    // 使用新界面
    [ObservableProperty] private bool _galgamePageNewLayout;
    partial void OnGalgamePageNewLayoutChanged(bool value) => _localSettingsService.SaveSettingAsync(KeyValues.GalgamePageNewLayout, value);
    #endregion

    #region GAME

    [ObservableProperty] private bool _recordOnlyForeground;
    [ObservableProperty] private WindowMode _playingWindowMode;
    [ObservableProperty] private string? _localEmulatorPath;
    public WindowMode[] PlayingWindowModes;
    
    partial void OnRecordOnlyForegroundChanged(bool value) => _localSettingsService.SaveSettingAsync(KeyValues.RecordOnlyWhenForeground, value);
    
    partial void OnPlayingWindowModeChanged(WindowMode value) => _localSettingsService.SaveSettingAsync(KeyValues.PlayingWindowMode, value);
    
    partial void OnLocalEmulatorPathChanged(string? value) => _localSettingsService.SaveSettingAsync(KeyValues.LocaleEmulatorPath, value);
    
    [RelayCommand]
    private async Task SelectLocalEmulatorPath()
    {
        FileOpenPicker openPicker = new()
        {
            SuggestedStartLocation = PickerLocationId.Desktop,
            FileTypeFilter = { ".exe" }
        };
        WinRT.Interop.InitializeWithWindow.Initialize(openPicker, App.MainWindow!.GetWindowHandle());
        StorageFile? file = await openPicker.PickSingleFileAsync();
        if (file?.Path != null)
        {
            LocalEmulatorPath = file.Path;
            _infoService.Info(InfoBarSeverity.Informational); //清除之前的消息
            if (file.Name != "LEProc.exe")
                _infoService.Info(InfoBarSeverity.Warning,
                    msg: "SettingsPage_Game_LocalEmulatorPathWarning".GetLocalized(), displayTimeMs: 5000);
        }
    }

    #endregion
    
    #region RSS

    [ObservableProperty] private RssType _rssType;
    // ReSharper disable once CollectionNeverQueried.Global
    public readonly RssType[] RssTypes = { RssType.Mixed , RssType.Bangumi, RssType.Vndb, RssType.Ymgal, RssType.Cngal};
    
    partial void OnRssTypeChanged(RssType value)
    {
        _localSettingsService.SaveSettingAsync(KeyValues.RssType, value);
    }
    
    [RelayCommand]
    public async Task SetMixedPhraserOrderAsync()
    {
        MixedPhraserOrder order = (await _localSettingsService.
            ReadSettingAsync<MixedPhraserOrder>(KeyValues.MixedPhraserOrder))!;
        MixedPhraserOrderDialog dialog = new(order);
        ContentDialogResult result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;
        await _localSettingsService.SaveSettingAsync(KeyValues.MixedPhraserOrder, order);
    }


    #endregion

    #region DOWNLOAD_BEHAVIOR

    [ObservableProperty] private bool _overrideLocalName;
    [ObservableProperty] private bool _overrideLocalNameWithChinese;
    [ObservableProperty] private bool _autoCategory;
    [ObservableProperty] private bool _downloadPlayStatusWhenPhrasing;
    [ObservableProperty] private bool _downloadCharacters;
    
    partial void OnOverrideLocalNameChanged(bool value) => _localSettingsService.SaveSettingAsync(KeyValues.OverrideLocalName, value);
    
    partial void OnOverrideLocalNameWithChineseChanged(bool value) => _localSettingsService.SaveSettingAsync(KeyValues.OverrideLocalNameWithChinese, value);
    
    partial void OnAutoCategoryChanged(bool value) => _localSettingsService.SaveSettingAsync(KeyValues.AutoCategory, value);
    
    partial void OnDownloadPlayStatusWhenPhrasingChanged(bool value) => _localSettingsService.SaveSettingAsync(KeyValues.SyncPlayStatusWhenPhrasing, value);
    
    partial void OnDownloadCharactersChanged(bool value) => _localSettingsService.SaveSettingAsync(KeyValues.DownloadCharacters, value);

    [RelayCommand]
    private async Task CategoryNow()
    {
        await _categoryService.UpdateAllGames();
    }

    [RelayCommand]
    private async Task DownloadPlayStatusFormBgmNow()
    {
        _ = DisplayMsgAsync(InfoBarSeverity.Informational, "HomePage_Downloading".GetLocalized(), 1000 * 120);
        (GalStatusSyncResult, string) result = await _galgameCollectionService.DownloadAllPlayStatus(RssType.Bangumi);
        await DisplayMsgAsync(result.Item1.ToInfoBarSeverity(), result.Item2);
    }
    
    [RelayCommand]
    private async Task DownloadPlayStatusFormVndbNow()
    {
        _ = DisplayMsgAsync(InfoBarSeverity.Informational, "HomePage_Downloading".GetLocalized(), 1000 * 120);
        (GalStatusSyncResult, string) result = await _galgameCollectionService.DownloadAllPlayStatus(RssType.Vndb);
        await DisplayMsgAsync(result.Item1.ToInfoBarSeverity(), result.Item2);
    }

    #endregion

    #region LIBRARY

    [ObservableProperty] private bool _metaBackup;
    [ObservableProperty] private string _metaBackupProgress = "";
    [ObservableProperty] private string _removeMetaBackupProgress = string.Empty;
    [ObservableProperty] private bool _searchSubFolder;
    [ObservableProperty] private int _searchSubFolderDepth;
    [ObservableProperty] private bool _ignoreFetchResult;
    [ObservableProperty] private string _regex;
    [ObservableProperty] private int _regexIndex;
    [ObservableProperty] private bool _regexRemoveBorder;
    [ObservableProperty] private string _gameFolderMustContain;
    [ObservableProperty] private string _gameFolderShouldContain;
    [ObservableProperty] private string _regexTryItOut = "";

    partial void OnMetaBackupChanged(bool value) => _localSettingsService.SaveSettingAsync(KeyValues.SaveBackupMetadata, value);

    partial void OnSearchSubFolderChanged(bool value) => _localSettingsService.SaveSettingAsync(KeyValues.SearchChildFolder, value);

    partial void OnSearchSubFolderDepthChanged(int value) => _localSettingsService.SaveSettingAsync(KeyValues.SearchChildFolderDepth, value);

    partial void OnIgnoreFetchResultChanged(bool value) => _localSettingsService.SaveSettingAsync(KeyValues.IgnoreFetchResult, value);
    
    partial void OnRegexChanged(string value) => _localSettingsService.SaveSettingAsync(KeyValues.RegexPattern, value);

    partial void OnRegexIndexChanged(int value) => _localSettingsService.SaveSettingAsync(KeyValues.RegexIndex, value);

    partial void OnRegexRemoveBorderChanged(bool value) => _localSettingsService.SaveSettingAsync(KeyValues.RegexRemoveBorder, value);

    partial void OnGameFolderShouldContainChanged(string value) => _localSettingsService.SaveSettingAsync(KeyValues.GameFolderShouldContain, value);

    partial void OnGameFolderMustContainChanged(string value) => _localSettingsService.SaveSettingAsync(KeyValues.GameFolderMustContain, value);

    [RelayCommand]
    private void OnRegexTryItOut() => RegexTryItOut = NameRegex.GetName(RegexTryItOut, Regex, RegexRemoveBorder, RegexIndex);

    private void SetSaveMetaPopUp(Galgame galgame)
    {
        MetaBackupProgress = "SettingsPage_Library_MetaBackupProgress".GetLocalized() + galgame.Name.Value;
    }

    [RelayCommand]
    private async Task SaveMetaBackUp()
    {
        await _galgameCollectionService.SaveAllMetaAsync();
        MetaBackupProgress = "Done!";
    }

    [RelayCommand]
    private async Task RemoveMetaBackUp()
    {
        foreach(Galgame game in _galgameCollectionService.Galgames)
        foreach (GalgameSourceBase source in game.Sources)
        {
            RemoveMetaBackupProgress = "SettingsPage_Library_RemoveMetaBackupProgress".GetLocalized(game.Name);
            await SourceServiceFactory.GetSourceService(source.SourceType).RemoveMetaAsync(game);
        }
        RemoveMetaBackupProgress = "Done!";
    }

    #endregion

    #region CLOUD

    [ObservableProperty] private string? _remoteFolder;
    partial void OnRemoteFolderChanged(string? value)
    {
        _localSettingsService.SaveSettingAsync(KeyValues.RemoteFolder, value);
    }
    [RelayCommand]
    private async Task SelectRemoteFolder()
    {
        FolderPicker openPicker = new();
        WinRT.Interop.InitializeWithWindow.Initialize(openPicker, App.MainWindow!.GetWindowHandle());
        openPicker.SuggestedStartLocation = PickerLocationId.HomeGroup;
        openPicker.FileTypeFilter.Add("*");
        StorageFolder? folder = await openPicker.PickSingleFolderAsync();
        RemoteFolder = folder?.Path ?? RemoteFolder;
    }

    #endregion

    #region QUIT_START 

    [ObservableProperty] private bool _quitStart;
    public readonly PageEnum[] StartPages = { PageEnum.Home , PageEnum.Category, PageEnum.MultiStream};
    [ObservableProperty] private PageEnum _startPage;
    public readonly AuthenticationType[] AuthenticationTypes;
    [ObservableProperty] private AuthenticationType _authenticationType;
    [ObservableProperty] private bool _autoStartWhenLogin;
    [ObservableProperty] private bool _minToTrayWhenAutoStart;

    partial void OnQuitStartChanged(bool value) => _localSettingsService.SaveSettingAsync(KeyValues.QuitStart, value);

    partial void OnStartPageChanged(PageEnum value) => _localSettingsService.SaveSettingAsync(KeyValues.StartPage, value);

    async partial void OnAuthenticationTypeChanged(AuthenticationType value)
    {
        switch (value)
        {
            case AuthenticationType.NoAuthentication:
            case AuthenticationType.WindowsHello:
                break;
            case AuthenticationType.CustomPassword:
                var result = await TrySetCustomPassword();
                if (!result)
                {
                    AuthenticationType = AuthenticationType.NoAuthentication;
                    return;
                }
                break;
        }

        await _localSettingsService.SaveSettingAsync(KeyValues.AuthenticationType, value);
    }

    private async Task<bool> TrySetCustomPassword()
    {
        PasswordDialog passwordDialog = new()
        {
            Title = "SetYourPasswordLiteral".GetLocalized(),
            Message = "SaveYourPasswordCarefullyLiteral".GetLocalized(),
            PrimaryButtonText = "ConfirmLiteral".GetLocalized(),
            CloseButtonText = "Cancel".GetLocalized(),
            PasswordBoxPlaceholderText = "PasswordLiteral".GetLocalized(),
        };
        await passwordDialog.ShowAsync();

        var password = passwordDialog.Password;
        if (string.IsNullOrEmpty(password) is not true)
        {
            PasswordCredential credential = new(KeyValues.CustomPasswordSaverName, KeyValues.CustomPasswordDisplayName, password);
            new PasswordVault().Add(credential);
            return true;
        }
        else
        {
            return false;
        }
    }

    partial void OnAutoStartWhenLoginChanged(bool value)
    {
        Task.Run(async () =>
        {
            try
            {
                StartupTask startupTask = await StartupTask.GetAsync("PotatoVNStartup");
                if (value && (startupTask.State is StartupTaskState.DisabledByUser or StartupTaskState.DisabledByPolicy))
                {
                    if (startupTask.State == StartupTaskState.DisabledByUser)
                        _infoService.Info(InfoBarSeverity.Warning, "SettingsPage_Start_AutoStartDisabledByUser".GetLocalized());
                    else if(startupTask.State == StartupTaskState.DisabledByPolicy)
                        _infoService.Info(InfoBarSeverity.Warning, "SettingsPage_Start_AutoStartDisabledByPolicy".GetLocalized());
                    await UiThreadInvokeHelper.InvokeAsync(() => { AutoStartWhenLogin = false; });
                    return;
                }

                if (value) await startupTask.RequestEnableAsync();
                else startupTask.Disable();
                await _localSettingsService.SaveSettingAsync(KeyValues.AutoStartWhenLogin, value);
            }
            catch (Exception e)
            {
                _infoService.Info(InfoBarSeverity.Error, "SettingsPage_Start_AutoStartFail".GetLocalized(), e.Message);
            }
        });
    }

    partial void OnMinToTrayWhenAutoStartChanged(bool value) =>
        _localSettingsService.SaveSettingAsync(KeyValues.MinToTrayWhenAutoStart, value);
    
    #endregion

    #region Other

    [ObservableProperty] private bool _uploadToAppCenter;
    [ObservableProperty] private bool _memoryImprove;
    [ObservableProperty] private WindowMode _closeMode;
    [ObservableProperty] private bool _developmentMode;
    public readonly WindowMode[] WindowModes;
    
    partial void OnUploadToAppCenterChanged(bool value) => _localSettingsService.SaveSettingAsync(KeyValues.UploadData, value);
    
    partial void OnMemoryImproveChanged(bool value) => _localSettingsService.SaveSettingAsync(KeyValues.MemoryImprove, value);

    partial void OnCloseModeChanged(WindowMode value) => _localSettingsService.SaveSettingAsync(KeyValues.CloseMode, value);
    
    partial void OnDevelopmentModeChanged(bool value) => _localSettingsService.SaveSettingAsync(KeyValues.DevelopmentMode, value);

    [RelayCommand]
    private async Task ExportData()
    {
        try
        {
            if (_bgTaskService.GetBgTask<ExportTask>(string.Empty) is not null)
            {
                _infoService.Info(InfoBarSeverity.Warning, "SettingsPage_Other_Export_Exporting".GetLocalized());
                return;
            }
            
            FolderPicker openPicker = new();
            WinRT.Interop.InitializeWithWindow.Initialize(openPicker, App.MainWindow!.GetWindowHandle());
            openPicker.SuggestedStartLocation = PickerLocationId.HomeGroup;
            openPicker.FileTypeFilter.Add("*");
            StorageFolder? folder = await openPicker.PickSingleFolderAsync();
            var path = folder?.Path;
            if (path is null) return;

            ExportTask task = new(path);
            await _bgTaskService.AddBgTask(task);
        }
        catch (Exception e)
        {
            _infoService.Info(InfoBarSeverity.Error, "SettingsPage_Other_Export_Fail".GetLocalized(),
                $"{e.Message}\n{e.StackTrace}");
        }
    }

    [RelayCommand]
    private async Task ImportData()
    {
        try
        {
            if (_bgTaskService.GetBgTask<ExportTask>(string.Empty) is not null)
            {
                _infoService.Info(InfoBarSeverity.Warning, "SettingsPage_Other_Export_Exporting".GetLocalized());
                return;
            }
            
            FileOpenPicker openPicker = new();
            WinRT.Interop.InitializeWithWindow.Initialize(openPicker, App.MainWindow!.GetWindowHandle());
            openPicker.SuggestedStartLocation = PickerLocationId.HomeGroup;
            openPicker.FileTypeFilter.Add(".zip");
            StorageFile file = await openPicker.PickSingleFileAsync();
            var path = file?.Path;
            if (path is null) return;
            if (file?.Name.EndsWith("pvnExport.zip") is not true)
                throw new PvnException("SettingsPage_Other_Import_WrongFile".GetLocalized());

            _infoService.Info(InfoBarSeverity.Success, msg: "SettingsPage_Other_Import_Copying".GetLocalized());
            await Task.Run(() =>
            {
                File.Copy(path, Path.Combine(_localSettingsService.LocalFolder.FullName, file.Name));
            });
            _infoService.Info(InfoBarSeverity.Success, msg: "SettingsPage_Other_Import_Restart".GetLocalized(),
                displayTimeMs: 1000 * 10);
            await Task.Delay(10 * 1000);
            AppInstance.Restart("/import"); // 并没有实现这个参数，只是为了和正常启动区分
        }
        catch (Exception e)
        {
            _infoService.Info(InfoBarSeverity.Error, "SettingsPage_Other_Import_Fail".GetLocalized(),
                $"{e.Message}\n{e.StackTrace}");
        }
    }

    #endregion

    #region Notification

    [ObservableProperty] private bool _notifyWhenGetGalgameInFolder;
    [ObservableProperty] private bool _notifyWhenUnpackGame;
    [ObservableProperty] private bool _eventPvnSync;
    [ObservableProperty] private bool _eventPvnSyncEmpty;
    
    partial void OnNotifyWhenGetGalgameInFolderChanged(bool value) => _localSettingsService.SaveSettingAsync(KeyValues.NotifyWhenGetGalgameInFolder, value);
    
    partial void OnNotifyWhenUnpackGameChanged(bool value) => _localSettingsService.SaveSettingAsync(KeyValues.NotifyWhenUnpackGame, value);
    
    partial void OnEventPvnSyncChanged(bool value)
    {
        _localSettingsService.SaveSettingAsync(KeyValues.EventPvnSyncNotify, value);
        if (!value)
            EventPvnSyncEmpty = false;
    }

    partial void OnEventPvnSyncEmptyChanged(bool value) => _localSettingsService.SaveSettingAsync(KeyValues.EventPvnSyncEmptyNotify, value);

    #endregion

    #region ABOUT

    [RelayCommand]
    private async Task Rate()
    {
        StoreContext context = StoreContext.GetDefault();
        WinRT.Interop.InitializeWithWindow.Initialize(context, App.MainWindow!.GetWindowHandle());
        await context.RequestRateAndReviewAppAsync();
    }

    [RelayCommand]
    private void UpdateContent()
    {
        _navigationService.NavigateTo(typeof(UpdateContentViewModel).FullName!);
    }
    
    private static string GetVersionDescription()
    {
        return $"{"AppDisplayName".GetLocalized()} - {RuntimeHelper.GetVersion()}";
    }

    #endregion
}

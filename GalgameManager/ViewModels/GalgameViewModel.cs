using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalgameManager.Contracts.Services;
using GalgameManager.Contracts.ViewModels;
using GalgameManager.Enums;
using GalgameManager.Helpers;
using GalgameManager.Models;
using GalgameManager.Models.BgTasks;
using GalgameManager.Models.Sources;
using GalgameManager.Services;
using GalgameManager.Views.Dialog;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.ComponentModel;
using GalgameManager.Views.GalgamePagePanel;

namespace GalgameManager.ViewModels;

public partial class GalgameViewModel : ObservableObject, INavigationAware
{
    private const int ProcessMaxWaitSec = 60; //(手动指定游戏进程)等待游戏进程启动的最大时间
    private readonly GalgameCollectionService _galgameService;
    private readonly IStaffService _staffService;
    private readonly INavigationService _navigationService;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly JumpListService _jumpListService;
    private readonly IBgTaskService _bgTaskService;
    private readonly IPvnService _pvnService;
    private readonly IInfoService _infoService;
    [ObservableProperty] private Galgame? _item;
    public ObservableCollection<GamePanelBase> Panels { get; } = new();
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChangeSavePositionCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetExePathCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteFromDiskCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetLocalPathCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedThreadCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectProcessCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectTextCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearTextCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetPathCommand))]
    [ObservableProperty] private bool _isLocalGame; //是否是本地游戏（而非云端同步过来/本地已删除的虚拟游戏）
    [ObservableProperty] private bool _isPhrasing;

    [ObservableProperty] private Visibility _isRemoveSelectedThreadVisible = Visibility.Collapsed;
    [ObservableProperty] private Visibility _isSelectProcessVisible = Visibility.Collapsed;
    [ObservableProperty] private Visibility _isResetPathVisible = Visibility.Collapsed;
    [ObservableProperty] private bool _canOpenInBgm;
    [ObservableProperty] private bool _canOpenInVndb;
    [ObservableProperty] private bool _canOpenInYmgal;
    [ObservableProperty] private bool _canOpenInCngal;

    [ObservableProperty] private bool _infoBarOpen;
    [ObservableProperty] private string _infoBarMsg = string.Empty;
    [ObservableProperty] private InfoBarSeverity _infoBarSeverity = InfoBarSeverity.Informational;
    private int _msgIndex;
    private bool IsNotLocalGame => !IsLocalGame;

    [ObservableProperty]
    private bool _useNewLayout;

    public GalgameViewModel(IGalgameCollectionService dataCollectionService, IStaffService staffService,
        INavigationService navigationService, IJumpListService jumpListService,
        ILocalSettingsService localSettingsService, IBgTaskService bgTaskService,
        IPvnService pvnService, IInfoService infoService)
    {
        _galgameService = (GalgameCollectionService)dataCollectionService;
        _staffService = staffService;
        _navigationService = navigationService;
        _jumpListService = (JumpListService)jumpListService;
        _localSettingsService = localSettingsService;
        _bgTaskService = bgTaskService;
        _pvnService = pvnService;
        _infoService = infoService;
    }
    
    public async void OnNavigatedTo(object parameter)
    {
        if (parameter is not GalgamePageParameter param) //参数不正确，返回主菜单
        {
            _navigationService.NavigateTo(typeof(HomeViewModel).FullName!);
            return;
        }

        // 加载布局设置
        UseNewLayout = await _localSettingsService.ReadSettingAsync<bool>(KeyValues.GalgamePageNewLayout);

        Item = param.Galgame;
        IsLocalGame = Item.IsLocalGame;
        Item.SavePath = Item.SavePath; //更新存档位置显示
        _galgameService.PhrasedEvent2 += Update;
        _staffService.OnGameStaffChanged += Update;
        // 初始化面板
        Update(Item);
        
        if (param.StartGame && await _localSettingsService.ReadSettingAsync<bool>(KeyValues.QuitStart))
            await Play();
        if (param.SelectProgress)
        {
            await Task.Delay(1000);
            await SelectProcess();
        }
    }

    public void OnNavigatedFrom()
    {
        _galgameService.PhrasedEvent2 -= Update;
        _staffService.OnGameStaffChanged -= Update;
    }
    
    /// <summary>
    /// 等待游戏进程启动，若超时则返回null
    /// </summary>
    /// <param name="processName">进程名</param>
    private static async Task<Process?> WaitForProcessStartAsync(string processName)
    {
        Process[] processes = Process.GetProcessesByName(processName);
        var waitSec = 0;
        while (processes.Length == 0)
        {
            await Task.Delay(100);
            processes = Process.GetProcessesByName(processName);
            if (++waitSec > ProcessMaxWaitSec)
                return null;
        }
        return processes[0];
    }
    
    private void Update(Galgame? game)
    {
        if (game is null || game != Item) return;
        IsPhrasing = false;
        try
        {
            CanOpenInBgm = !string.IsNullOrEmpty(Item?.Ids[(int)RssType.Bangumi]);
            CanOpenInVndb = !string.IsNullOrEmpty(Item?.Ids[(int)RssType.Vndb]);
            CanOpenInYmgal = !string.IsNullOrEmpty(Item?.Ids[(int)RssType.Ymgal]);
            CanOpenInCngal = !string.IsNullOrEmpty(Item?.Ids[(int)RssType.Cngal]);
        }
        catch (Exception ex)
        {
            // 原理上来说是不会越界的，但莫名奇妙有用户反馈过越界问题
            _infoService.Info(InfoBarSeverity.Warning, $"Error setting open flags: {ex.Message}");
        }
        IsRemoveSelectedThreadVisible = Item?.ProcessName is not null ? Visibility.Visible : Visibility.Collapsed;
        IsSelectProcessVisible = Item?.ProcessName is null ? Visibility.Visible : Visibility.Collapsed;
        IsResetPathVisible = Item?.ExePath is not null || Item?.TextPath is not null ? Visibility.Visible : Visibility.Collapsed;
        OnPropertyChanged(nameof(Item));
    }

    #region INFOBAR_CTRL

    private async Task DisplayMsg(InfoBarSeverity severity, string msg, int displayTimeMs = 3000)
    {
        var myIndex = ++_msgIndex;
        InfoBarOpen = true;
        InfoBarMsg = msg;
        InfoBarSeverity = severity;
        await Task.Delay(displayTimeMs);
        if (myIndex == _msgIndex)
            InfoBarOpen = false;
    }

    #endregion

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
    
    [RelayCommand]
    private async Task OpenInYmgal()
    {
        if(string.IsNullOrEmpty(Item!.Ids[(int)RssType.Ymgal])) return;
        await Launcher.LaunchUriAsync(new Uri("https://www.ymgal.games/ga"+Item!.Ids[(int)RssType.Ymgal]));
    }

    [RelayCommand]
    private async Task OpenInCngal()
    {
        if(string.IsNullOrEmpty(Item!.Ids[(int)RssType.Cngal])) return;
        await Launcher.LaunchUriAsync(new Uri("https://www.cngal.org/entries/index/"+Item!.Ids[(int)RssType.Cngal]));
    }
    
    [RelayCommand(CanExecute = nameof(IsLocalGame))]
    private async Task Play()
    {
        if (!Item!.IsLocalGame) return;
        if (Item.ExePath is not null && !File.Exists(Item.ExePath)) Item.ExePath = null;
        if (Item.ExePath == null && Item.Startup_parameters==string.Empty)
            await _galgameService.GetGalgameExeAsync(Item);
        await CalcStartupPara();
        if (Item.ExePath == null && Item.Startup_parameters == string.Empty) return;
        Process process;
        if (Item.Startup_parameters == string.Empty || Item.Startup_parameters == null) // 有时会莫名其妙变成null
        {
            process = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = Item.ExePath,
                    WorkingDirectory = Item.LocalPath,
                    UseShellExecute = Item.RunAsAdmin | Item.ExePath!.ToLower().EndsWith("lnk"),
                    Verb = Item.RunAsAdmin ? "runas" : null,
                }
            };
        }
        else
        {
            var filename = Item.Startup_parameters;
            var arguments = Item.Startup_parameters_arguments;
            process = new()
            {
                StartInfo = new ProcessStartInfo()
                {
                    FileName = filename,
                    Arguments = arguments,
                    CreateNoWindow = true,
                    Verb = Item.RunAsAdmin ? "runas" : null,
                    UseShellExecute = true,
                }
            };
        }
        try
        {
            process.Start();
            Item.LastPlayTime = DateTime.Now;
            // _galgameService.Sort();
            if (Item.ProcessName is not null)
            {
                await Task.Delay(1000 * 2); //有可能引导进程和游戏进程是一个名字，等2s让引导进程先退出
                process = await WaitForProcessStartAsync(Item.ProcessName) ?? process;
            }
            if (Item.Startup_parameters != string.Empty && Item.ProcessName is null) 
            { 
                //启动的进程和游戏进程不是同一个进程，需要知道到底启动什么进程
                await Task.Delay(1000 * 2);
                if (TryGetProcessFromName() is { } p) // 尝试根据游戏可执行文件名获取进程
                {
                    process = p;
                    Item.ProcessName = p.ProcessName;
                }
                else
                    await SelectProcess();
            }
            _ = _bgTaskService.AddBgTask(new RecordPlayTimeTask(Item, process));
            await _jumpListService.AddToJumpListAsync(Item);
            
            await Task.Delay(1000); //等待1000ms，让游戏进程启动后再最小化
            if(process.HasExited == false)
                App.SetWindowMode(await _localSettingsService.ReadSettingAsync<WindowMode>(KeyValues.PlayingWindowMode));
            
            await process.WaitForExitAsync();
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
    }

    [RelayCommand]
    private void Setting()
    {
        if (Item == null) return;
        _navigationService.NavigateTo(typeof(GalgameSettingViewModel).FullName!, Item);
    }

    [RelayCommand(CanExecute = nameof(IsLocalGame))]
    private async Task ChangeSavePosition()
    {
        if (Item?.IsLocalGame != true) return;
        await _galgameService.ChangeGalgameSavePosition(Item);
    }
    
    [RelayCommand(CanExecute = nameof(IsLocalGame))]
    private void ResetExePath(object obj)
    {
        if (Item is null || !Item.IsLocalGame) return;
        Item!.ExePath = null;
    }
    
    [RelayCommand(CanExecute = nameof(IsLocalGame))]
    private async Task DeleteFromDisk()
    {
        if (Item is null || !Item.IsLocalGame) return;
        ContentDialog dialog = new()
        {
            XamlRoot = App.MainWindow!.Content.XamlRoot,
            Title = "HomePage_Delete_Title".GetLocalized(),
            Content = "HomePage_Delete_Message".GetLocalized(),
            PrimaryButtonText = "Yes".GetLocalized(),
            SecondaryButtonText = "Cancel".GetLocalized(),
            DefaultButton = ContentDialogButton.Secondary
        };
        dialog.PrimaryButtonClick += async (_, _) =>
        {
            await _galgameService.RemoveGalgame(Item, true);
            _navigationService.NavigateTo(typeof(HomeViewModel).FullName!);
        };
        await dialog.ShowAsync();
    }
    
    [RelayCommand]
    private async Task OpenInExplorer()
    {
        if(Item == null) return;
        var path = Item.Sources.FirstOrDefault(s => s.SourceType == GalgameSourceType.LocalFolder)?.GetPath(Item);
        if (path is null) //不应该发生
        {
            _infoService.DeveloperEvent(InfoBarSeverity.Error, "Can't find the path of the game");
            return;
        }
        await Launcher.LaunchUriAsync(new Uri(path));
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (Item is null) return;
        await _galgameService.SaveGalgameAsync(Item);
    }

    [RelayCommand]
    private async Task ChangeRunInLocaleEmulator()
    {
        if (Item is null) return;
        if (Item.RunInLocaleEmulator && !await CheckLocaleEmulator())
            Item.RunInLocaleEmulator = false;

        if (!Item.RunInLocaleEmulator)
        {
            Item.Startup_parameters = string.Empty;
            Item.ExePath = null;
            await RemoveSelectedThread();
        }
        else
            await CalcStartupPara();
        await SaveAsync();
    }

    [RelayCommand]
    private async Task ChangeHighDpi()
    {
        if (Item is null || string.IsNullOrEmpty(Item.ExePath)) 
        {
            _infoService.Info(InfoBarSeverity.Error, "GalgamePage_HighDpi_ExePathIsEmpty".GetLocalized());
            return;
        }
        
        try 
        {
            // 构建 PowerShell 命令
            var regPath = @"HKCU:\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
            var command = Item.HighDpi
                ? $"Remove-ItemProperty -Path '{regPath}' -Name '{Item.ExePath.Replace("'", "''")}'"
                : $"Set-ItemProperty -Path '{regPath}' -Name '{Item.ExePath.Replace("'", "''")}' -Value '~ PERPROCESSSYSTEMDPIFORCEOFF HIGHDPIAWARE'";

            // 创建启动管理员权限的 PowerShell 进程
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-Command \"{command}\"",
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            try
            {
                var process = Process.Start(startInfo);
                if (process != null)
                {
                    await process.WaitForExitAsync();
                    if (process.ExitCode == 0)
                    {
                        Item.HighDpi = !Item.HighDpi;
                        await SaveAsync();
                        _ = DisplayMsg(InfoBarSeverity.Success, "GalgamePage_HighDpi_Success".GetLocalized());
                    }
                    else
                    {
                        _infoService.Info(InfoBarSeverity.Error, "GalgamePage_HighDpi_Fail".GetLocalized() + $" {process.ExitCode}");
                    }
                }
            }
            catch (Win32Exception)
            {
                // 用户取消了UAC提示
                _infoService.Info(InfoBarSeverity.Warning, "GalgamePage_HighDpi_NeedAdmin".GetLocalized());
            }
        }
        catch (Exception ex)
        {
            _infoService.Info(InfoBarSeverity.Error, "GalgamePage_HighDpi_Fail".GetLocalized() + $" {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ChangePlayStatus()
    {
        //Idea: 加一个检测是否有对应源的ID
        if (Item == null) return;
        ChangePlayStatusDialog dialog = new(Item)
        {
            XamlRoot = App.MainWindow!.Content.XamlRoot,
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
        {
            _ = DisplayMsg(InfoBarSeverity.Informational, "HomePage_UploadingToVndb".GetLocalized(), 1000 * 10);
            (GalStatusSyncResult, string) result = await _galgameService.UploadPlayStatusAsync(Item, RssType.Vndb);
            await DisplayMsg(result.Item1.ToInfoBarSeverity(), result.Item2);
        }
        await _galgameService.SaveGalgameAsync(Item);
        _pvnService.Upload(Item, PvnUploadProperties.Review);
    }

    [RelayCommand]
    private async Task SyncFromBgm()
    {
        if (Item == null) return;
        _ =  DisplayMsg(InfoBarSeverity.Informational, "HomePage_Downloading".GetLocalized(), 1000 * 100);
        (GalStatusSyncResult, string) result = await _galgameService.DownLoadPlayStatusAsync(Item, RssType.Bangumi);
        await DisplayMsg(result.Item1.ToInfoBarSeverity(), result.Item2);
    }
    
    [RelayCommand]
    private async Task SyncFromVndb()
    {
        if (Item == null) return;
        _ =  DisplayMsg(InfoBarSeverity.Informational, "HomePage_Downloading".GetLocalized(), 1000 * 100);
        (GalStatusSyncResult, string) result = await _galgameService.DownLoadPlayStatusAsync(Item, RssType.Vndb);
        await DisplayMsg(result.Item1.ToInfoBarSeverity(), result.Item2);
    }

    [RelayCommand(CanExecute = nameof(IsNotLocalGame))]
    private async Task SetLocalPath()
    {
        try
        {
            FileOpenPicker openPicker = new();
            WinRT.Interop.InitializeWithWindow.Initialize(openPicker, App.MainWindow!.GetWindowHandle());
            openPicker.ViewMode = PickerViewMode.Thumbnail;
            openPicker.FileTypeFilter.Add(".exe");
            openPicker.FileTypeFilter.Add(".bat");
            openPicker.FileTypeFilter.Add(".EXE");
            StorageFile? file = await openPicker.PickSingleFileAsync();
            if (file is not null)
            {
                var folder = file.Path[..file.Path.LastIndexOf('\\')];
                await _galgameService.SetLocalPathAsync(Item!, folder);
                Item!.ExePath = file.Path;
                IsLocalGame = Item!.IsLocalGame;
                _ = DisplayMsg(InfoBarSeverity.Success, "GalgamePage_PathSet".GetLocalized());
                _galgameService.RefreshDisplay(); //重新构造显示列表以刷新特殊显示非本地游戏（因为GameToOpacityConverter只会在构造列表的时候被调用）
            }
        }
        catch (Exception e)
        {
            _ = DisplayMsg(InfoBarSeverity.Error, e.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(IsLocalGame))]
    private async Task RemoveSelectedThread()
    {
        Item!.ProcessName = null;
        Update(Item);
        _ = DisplayMsg(InfoBarSeverity.Success, "GalgamePage_RemoveSelectedThread_Success".GetLocalized());
        await SaveAsync();
    }

    [RelayCommand(CanExecute = nameof(IsLocalGame))]
    private async Task SelectProcess()
    {
        if (!Item!.IsLocalGame) return;
        SelectProcessDialog dialog = new();
        await dialog.ShowAsync();
        if (dialog.SelectedProcessName is not null)
        {
            Item.ProcessName = dialog.SelectedProcessName;
            Update(Item);
            await SaveAsync();
            _ = DisplayMsg(InfoBarSeverity.Success, "HomePage_ProcessNameSet".GetLocalized());
        }
    }

    [RelayCommand(CanExecute = nameof(IsLocalGame))]
    private async Task SelectText()
    {
        if (Item is null || !Item.IsLocalGame) return;
        var path = Item.TextPath;
        if (path is null || File.Exists(path) == false)
        {
            SelectFileDialog dialog = new(Item!.LocalPath!, new[] { ".txt", ".pdf" },
                "GalgamePage_SelectText_Title".GetLocalized());
            await dialog.ShowAsync();
            path = dialog.SelectedFilePath;
            if (dialog.RememberMe)
            {
                Item.TextPath = path;
                await SaveAsync();
            }
        }
        
        if (path is not null)
            _ = Launcher.LaunchUriAsync(new Uri(path));
    }
    
    [RelayCommand(CanExecute = nameof(IsLocalGame))]
    private async Task ClearText()
    {
        if (Item is null) return;
        Item.TextPath = null;
        await SaveAsync();
    }

    [RelayCommand]
    private async Task MoveToSource()
    {
        if (Item is null) return;
        ChangeSourceDialog dialog = new(Item);
        await dialog.ShowAsync();
    }

    private async Task<bool> CheckLocaleEmulator()
    {
        var path = await _localSettingsService.ReadSettingAsync<string>(KeyValues.LocaleEmulatorPath);
        if (path is not null && File.Exists(path)) return true;
        _infoService.Info(InfoBarSeverity.Error, msg: "GalgamePage_InvalidLocaleEmulatorPath".GetLocalized(),
            displayTimeMs: 5000);
        return false;
    }

    private async Task CalcStartupPara()
    {
        if (Item is null) return;
        if (!Item.RunInLocaleEmulator) return;
        if (!await CheckLocaleEmulator() || !File.Exists(Item.ExePath))
        {
            Item.Startup_parameters = string.Empty;
            return;
        }
        Item.Startup_parameters =
            $"\"{await _localSettingsService.ReadSettingAsync<string>(KeyValues.LocaleEmulatorPath)}\" \"{Item.ExePath}\"";
    }

    private Process? TryGetProcessFromName()
    {
        if (Item?.ExePath is null) return null;
        var name = Path.GetFileNameWithoutExtension(Item.ExePath);
        return Process.GetProcesses().FirstOrDefault(p => p.ProcessName == name);
    }

    [RelayCommand]
    private async Task ResetPath()
    {
        if (Item is null || !Item.IsLocalGame) return;
        if (Item.HighDpi)
            await ChangeHighDpi();
        if (Item.HighDpi)
            Item.HighDpi = false;
        Item!.ExePath = null;
        await ClearText();
        
    }

    private static GamePanelBase GetPanel(GalgamePagePanel panel, Galgame? game)
    {
        return panel switch
        {
            GalgamePagePanel.HeaderOld => new GameHeaderOldPanel { Game = game},
            GalgamePagePanel.Description => new GameDescriptionPanel { Game = game },
            GalgamePagePanel.Tag => new GameTagPanel {Game = game},
            GalgamePagePanel.Character => new GameCharacterPanel{Game = game},
            GalgamePagePanel.Staff => new GameStaffPanel {Game = game},
            _ => throw new NotImplementedException(),
        };
    } 
}

public class GalgamePageParameter
{
    /// 目标游戏
    [Required] public Galgame Galgame = null!;
    /// 如果设置有打开直接启动游戏，则直接启动游戏
    public bool StartGame;
    /// 显示手动选择线程弹窗
    public bool SelectProgress;
}
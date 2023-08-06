﻿using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using GalgameManager.Enums;
using GalgameManager.Helpers;
using GalgameManager.Helpers.Phrase;
using SystemPath = System.IO.Path;

namespace GalgameManager.Models;

public partial class Galgame : ObservableObject, IComparable<Galgame>
{
    public const string DefaultImagePath = "ms-appx:///Assets/WindowIcon.ico";
    public const string DefaultString = "——";
    public const string MetaPath = ".PotatoVN";
    
    public string Path
    {
        get;
        set;
    } = "";
    
    [ObservableProperty] private LockableProperty<string> _imagePath = DefaultImagePath;

    public string? ImageUrl;
    // ReSharper disable once FieldCanBeMadeReadOnly.Global
    [JsonInclude] public Dictionary<string, int> PlayedTime = new(); //ShortDateString() -> PlayedTime, 分钟
    [ObservableProperty] private LockableProperty<string> _name = "";
    [JsonIgnore][ObservableProperty] private string _cnName = "";
    [ObservableProperty] private LockableProperty<string> _description = "";
    [ObservableProperty] private LockableProperty<string> _developer = DefaultString;
    [ObservableProperty] private LockableProperty<string> _lastPlay = DefaultString;
    [ObservableProperty] private LockableProperty<string> _expectedPlayTime = DefaultString;
    [ObservableProperty] private LockableProperty<float> _rating = 0;
    [JsonIgnore][ObservableProperty] private string _savePosition = "本地";
    [ObservableProperty] private string? _exePath;
    [ObservableProperty] private LockableProperty<ObservableCollection<string>> _tags = new();
    [ObservableProperty] private int _totalPlayTime; //单位：分钟
    [ObservableProperty] private bool _runAsAdmin; //是否以管理员权限运行
    private bool _isSaveInCloud;
    private RssType _rssType = RssType.None;
    [ObservableProperty] private PlayType _playType;
    // ReSharper disable once MemberCanBePrivate.Global
    // ReSharper disable once FieldCanBeMadeReadOnly.Global
    [JsonInclude] public string?[] Ids = new string?[5]; //magic number: 钦定了一个最大Phraser数目
    [Newtonsoft.Json.JsonIgnore] public ObservableCollection<Category> Categories = new();
    [ObservableProperty] private string _comment = string.Empty; //吐槽（评论）
    [ObservableProperty] private int _myRate; //我的评分
    [ObservableProperty] private bool _privateComment; //是否私密评论

    public static readonly List<SortKeys> SortKeysList = new ();

    [Newtonsoft.Json.JsonIgnore] public string? Id
    {
        get => Ids[(int)RssType];

        set
        {
            if (Ids[(int)RssType] != value)
            {
               Ids[(int)RssType] = value;
               OnPropertyChanged();
               if (_rssType == RssType.Mixed)
                   UpdateIdFromMixed();
            }
        }
    }

    public event GenericDelegate<(Galgame, string)>? GalPropertyChanged;

    public RssType RssType
    {
        get => _rssType;
        set
        {
            if (_rssType != value)
            {
                _rssType = value;
                // OnPropertyChanged(); //信息源是通过Combobox选择的，不需要通知
                OnPropertyChanged(nameof(Id));
            }
        }
    }

    private bool IsSaveInCloud
    {
        set
        {
            _isSaveInCloud = value;
            SavePosition = _isSaveInCloud ? "云端" : "本地";
        }
    }

    public Galgame()
    {
        _tags.Value = new ObservableCollection<string>();
        _developer.OnValueChanged += _ => GalPropertyChanged?.Invoke((this, "developer"));
        
    }

    public Galgame(string path)
    {
        Name = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path + System.IO.Path.DirectorySeparatorChar)) ?? "";
        _tags.Value = new ObservableCollection<string>();
        Path = path;
        _developer.OnValueChanged += _ => GalPropertyChanged?.Invoke((this, "developer"));
    }
    
    /// <summary>
    /// 检查游戏文件夹是否存在
    /// </summary>
    public bool CheckExist()
    {
        return Directory.Exists(Path);
    }

    /// <summary>
    /// 更新游戏存档位置（云端/本地）信息
    /// <returns>如果存档在云端返回true，本地返回false</returns>
    /// </summary>
    public bool CheckSavePosition()
    {
        DirectoryInfo directoryInfo = new(Path);
        if (directoryInfo.GetDirectories().Any(IsSymlink))
        {
            IsSaveInCloud = true;
            return true;
        }
        IsSaveInCloud = false;
        return false;
    }

    /// <summary>
    /// 删除游戏文件夹
    /// </summary>
    public void Delete()
    {
        new DirectoryInfo(Path).Delete(true);
    }

    public int CompareTo(Galgame? other)
    {
        if (other is null) return 1;
        foreach (SortKeys keyValue in SortKeysList)
        {
            var result = 0;
            var take = -1; //默认降序
            switch (keyValue)
            {
                case SortKeys.Developer:
                    result = string.Compare(_developer.Value!, other._developer.Value, StringComparison.Ordinal);
                    break;
                case SortKeys.Name:
                    result = string.Compare(_name.Value!, other._name.Value, StringComparison.CurrentCultureIgnoreCase);
                    take = 1;
                    break;
                case SortKeys.Rating:
                    result = _rating.Value.CompareTo(other._rating.Value);
                    break;
                case SortKeys.LastPlay:
                    result = GetTime(_lastPlay.Value!).CompareTo(GetTime(other._lastPlay.Value!));
                    break;
            }
            if (result != 0)
                return take * result; 
        }
        return 0;
    }

    /// <summary>
    /// 时间转换
    /// </summary>
    /// <param name="time">年/月/日</param>
    /// <returns></returns>
    private long GetTime(string time)
    {
        if (time == DefaultString)
            return 0;
        var tmp = time.Split('/');
        return Convert.ToInt64(tmp[2])+Convert.ToInt64(tmp[1])*31+Convert.ToInt64(tmp[0])*30*12;
    }

    public override bool Equals(object? obj) => obj is Galgame galgame && Path == galgame.Path;
    
    // ReSharper disable once NonReadonlyMemberInGetHashCode
    public override int GetHashCode() => Path.GetHashCode();
    
    private static bool IsSymlink(FileSystemInfo fileInfo)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            const FileAttributes symlinkAttribute = FileAttributes.ReparsePoint;
            return (fileInfo.Attributes & symlinkAttribute) == symlinkAttribute;
        }
        throw new NotSupportedException("Unsupported operating system.");
    }
    
    /// <summary>
    /// 获取游戏文件夹下的所有exe以及bat文件
    /// </summary>
    /// <returns>所有exe以及bat文件地址</returns>
    public List<string> GetExesAndBats()
    {
        List<string> result = Directory.GetFiles(Path).Where(file => file.ToLower().EndsWith(".exe")).ToList();
        result.AddRange(Directory.GetFiles(Path).Where(file => file.ToLower().EndsWith(".bat")));
        return result;
    }
    
    /// <summary>
    /// 获取游戏文件夹下的所有子文件夹
    /// </summary>
    /// <returns>子文件夹地址</returns>
    public List<string> GetSubFolders()
    {
        List<string> result = Directory.GetDirectories(Path).ToList();
        return result;
    }

    /// <summary>
    /// 记录游戏的游玩时间
    /// </summary>
    /// <param name="process">游戏进程</param>
    public async void RecordPlayTime(Process process)
    {
        await Task.Run(() =>
        {
            while (!process.HasExited)
            {
                Thread.Sleep(1000 * 60);
                if (!process.HasExited)
                {
                    _totalPlayTime++;
                    var now = DateTime.Now.ToShortDateString();
                    if (PlayedTime.ContainsKey(now))
                        PlayedTime[now]++;
                    else
                        PlayedTime.Add(now, 1);
                }
            }
        });
    }

    /// <summary>
    /// 获取该游戏信息文件夹地址
    /// </summary>
    /// <returns></returns>
    public string GetMetaPath()
    {
        return System.IO.Path.Combine(Path, MetaPath);
    }

    /// <summary>
    /// 获取用来保存meta信息的galgame，用于序列化
    /// </summary>
    /// <returns></returns>
    public Galgame GetMetaCopy()
    {
        Galgame result = (Galgame)MemberwiseClone();
        if(ExePath != null)
            result.ExePath = "..\\" + System.IO.Path.GetFileName(ExePath);
        result.Path = "..\\";
        result._imagePath = new LockableProperty<string>();
        if (ImagePath.Value == DefaultImagePath)
            result.ImagePath.Value = DefaultImagePath;
        else
            result.ImagePath.Value = ".\\" + System.IO.Path.GetFileName(ImagePath);
        return result;
    }

    /// <summary>
    /// 从meta信息中恢复游戏信息
    /// </summary>
    /// <param name="meta">待恢复的数据</param>
    /// <param name="metaFolderPath">meta文件夹路径</param>
    /// <returns>恢复过后的信息</returns>
    public static Galgame ResolveMeta(Galgame meta,string metaFolderPath)
    {
        meta.Path = SystemPath.GetFullPath(SystemPath.Combine(metaFolderPath, meta.Path));
        if (meta.Path.EndsWith('\\')) meta.Path = meta.Path[..^1];
        if (meta.ImagePath.Value != DefaultImagePath)
            meta.ImagePath.Value = SystemPath.GetFullPath(SystemPath.Combine(metaFolderPath, meta.ImagePath.Value!));
        if (meta.ExePath != null)
            meta.ExePath = SystemPath.GetFullPath(SystemPath.Combine(metaFolderPath, meta.ExePath));
        return meta;
    }

    // ReSharper disable once UnusedParameterInPartialMethod
    partial void OnPlayTypeChanged(PlayType value)
    {
        GalPropertyChanged?.Invoke((this, "playType"));
    }

    /// <summary>
    /// 从混合数据源的id更新其他数据源的id
    /// </summary>
    public void UpdateIdFromMixed()
    {
        (string? bgmId, string? vndbId) tmp = MixedPhraser.TryGetId(Ids[(int)RssType.Mixed]);
        if (tmp.bgmId != null && string.IsNullOrEmpty(Ids[(int)RssType.Bangumi])) 
            Ids[(int)RssType.Bangumi] = tmp.bgmId;
        if (tmp.vndbId != null && string.IsNullOrEmpty(Ids[(int)RssType.Vndb])) 
            Ids[(int)RssType.Vndb] = tmp.vndbId;
    }
}


public enum SortKeys
{
    Name,
    LastPlay,
    Developer,
    Rating
}

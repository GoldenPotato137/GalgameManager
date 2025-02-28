namespace GalgameManager.Enums;

public static class KeyValues
{
    //设置与杂项
    public const string RemoteFolder = "remoteFolder";
    public const string SortKeys = "sortKeys";
    public const string SortKeysAscending = "sortKeysAscending";
    public const string SearchChildFolder = "searchChildFolder";
    public const string SearchChildFolderDepth = "searchChildFolderDepth";
    public const string IgnoreFetchResult = "ignoreFetchResult";
    public const string RegexPattern = "regexPattern";
    public const string RegexIndex = "regexIndex";
    public const string RegexRemoveBorder = "regexRemoveBorder";
    public const string GameFolderMustContain = "gameFolderMustContain";
    public const string GameFolderShouldContain = "gameFolderShouldContain";
    public const string FaqLastUpdate = "faqLastUpdate";
    public const string SaveBackupMetadata = "saveBackupMetadata";
    public const string DisplayedUpdateVersion = "displayedUpdateVersion";
    public const string CustomPasswordSaverName = "PotatoVN";
    public const string CustomPasswordDisplayName = "CustomPassword";
    public const string LastUpdateCheckDate = "lastUpdateCheckDate"; // DateTime,上次检查更新的时间
    public const string LastUpdateCheckResult = "lastUpdateCheckResult"; // bool,上次检查更新的结果
    public const string LastNoticeUpdateVersion = "lastNoticeUpdateVersion"; // string,上次通知更新的版本
    public const string AutoCategory = "autoCategory"; // bool,是否自动分类
    public const string AuthenticationType = "authenticationType"; // AuthenticationType,身份验证类型
    public const string FontInstalled = "fontInstalled"; //bool, 是否安装了Segoe Fluent Icons字体
    public const string SyncGames = "syncGames"; //bool, 是否同步游戏（游玩时长/状态/列表）
    public const string SyncTo = "syncTo"; //map<mac:string, id:int>，每台设备merge到的commit id
    
    //账户相关
    public const string BangumiAccount= "bangumiAccount"; //BgmAccount?, Bangumi账户, 若为null则未登录
    public const string BangumiOAuthStateLastUpdate = "bangumiOAuthStateLastUpdate";
    public const string PvnServerType = "pvnServerType"; //enum: PvnServerType, 服务器类型（官方/自定义）
    public const string PvnServerEndpoint = "pvnServerEndpoint"; //string, 自定义服务器Url
    public const string PvnAccount = "pvnAccount"; //PvnAccount?, PotatoVN账户, 若为null则未登录
    public const string PvnAccountUserName = "pvnAccountUserName"; //string, PotatoVN账户名
    public const string VndbAccount = "vndbAccount"; //VndbAccount?, Vndb账户, 若为null则未登录
    
    //游玩相关
    public const string RecordOnlyWhenForeground = "recordOnlyWhenForeground"; //bool, 是否只在游戏窗口在前台时记录游玩时间
    public const string PlayingWindowMode = "playingWindowMode"; // WindowMode,游玩时窗口模式
    public const string LocaleEmulatorPath = "localeEmulatorPath"; //string?, 本地模拟器路径
    
    //启动与跳转相关
    public const string QuitStart = "quitStart"; //bool, 是否在jump list跳转打开游戏时启动游戏
    public const string CategoryGroup = "categoryGroup"; // string，分类页展示的分类组
    public const string StartPage = "startPage"; // PageEnum,启动时显示的页面
    public const string AutoStartWhenLogin = "autoStartWhenLogin"; //bool, 是否开机自启
    public const string MinToTrayWhenAutoStart = "minToTrayWhenAutoStart"; //bool, 开机自启时是否最小化到托盘
    
    //数据相关
    public const string GalgameSources = "galgameSources";
    ///int, 当前已经用到的最大的galgameSource的id
    public const string GalgameSourcesId = "galgameSourcesId"; 
    public const string Galgames = "galgames";
    public const string Filters = "filters";
    public const string KeepFilters = "keepFilters"; //bool, 离开界面/关闭软件时是否保留筛选器
    public const string CategoryGroups = "categoryGroups"; // List<CategoryGroup>,分类组
    public const string PvnSyncTimestamp = "pvnSyncTimestamp"; //long, 上一次同步时间戳
    public const string ToDeleteGames = "toDeleteGames"; //List<int>, 待删除的游戏id
    public const string MultiStreamPageList = "multiStreamPageList"; //List<IGalgameManager.MultiStreamPage.Lists.IList>, 主页列表
    
    //搜刮设置
    public const string RssType = "rssType";
    public const string OverrideLocalName = "overrideLocalName";
    public const string OverrideLocalNameWithChinese = "overrideLocalNameWithChinese";
    public const string SyncPlayStatusWhenPhrasing = "syncPlayStatusWhenPhrasing"; //bool, 是否在获取游戏信息时同步游玩状态
    public const string DownloadCharacters = "fetchCharacters"; //bool, 搜刮时是否获取角色信息
    public const string MixedPhraserOrder = "mixedPhraserOrder"; //MixedPhraserOrder,混合搜刮器的顺序
    
    //显示相关
    public const string DisplayPlayTypePolygon = "displayPlayTypePolygon"; //bool, 游戏页是否显示游玩状态的小三角形
    public const string FixHorizontalPicture = "fixHorizontalPictrue"; //bool, 游戏页是否裁剪横图
    public const string DisplayVirtualGame = "displayVirtualGame"; //bool, 游戏页是否显示虚拟游戏
    public const string SpecialDisplayVirtualGame = "specialDisplayVirtualGame"; //bool, 游戏页是否特殊显示虚拟游戏（降低透明度）
    public const string MultiStreamPageAllowScroll = "multiStreamPageAllowScroll"; //bool, 主页列表是否允许横向滚动
    public const string TimeAsHour = "timeAsHour"; //bool，时间是否显示为“__h__m"，若为false则显示为“__分钟”
    public const string GalgamePageNewLayout = "galgamePageNewLayout"; //bool, 游戏页是否使用新界面
    
    
    //库页面
    public const string LibraryNavBar = "libraryNavBar"; //bool, 是否显示库页面的导航栏
    public const string LibraryStatistics = "libraryStatistics"; //bool, 是否显示库页面的统计信息（当前页游戏库/游戏数）
    
    //消息通知相关 (最小化到托盘时是否通知/全局消息通知)
    public const string NotifyWhenGetGalgameInFolder = "notifyWhenGetGalgameInFolder"; //bool, 完成获取文件夹内游戏
    public const string NotifyWhenUnpackGame = "notifyWhenUnpackGame"; //bool, 完成解压游戏
    public const string EventPvnSyncNotify = "eventPvnSyncNotify"; //bool, 是否通知PotatoVN同步事件
    public const string EventPvnSyncEmptyNotify = "eventPvnSyncEmptyNotify"; //bool, 是否通知PotatoVN同步空事件（即已是最新）
    
    //软件本体设置相关
    public const string MemoryImprove = "memoryImprove"; //bool, 是否启用内存优化
    public const string UploadData = "uploadData"; // bool,是否将匿名数据上传到AppCenter
    public const string CloseMode = "closeMode"; // WindowMode,关闭模式，Normal（表示未设定）/Close/SystemTray
    public const string DevelopmentMode = "developmentMode"; //bool, 是否开发模式
    public const string LastError = "lastError"; //string, 上次错误信息
    
    //是否执行过某种升级, bool
    public const string DataStatus = "dataStatus"; //LocalSettingStatus, 用于描述某PotatoVN数据的状态
    public const string IdFromMixedUpgraded = "idFromMixedUpgraded"; //其他信息源id从mixed中获取
    public const string SaveFormatUpgraded = "saveFormatUpgraded"; //设置格式升级
    public const string SortKeysUpgraded = "sortKeysUpgraded"; //排序格式升级
    public const string OAuthUpgraded = "OAuthUpgraded"; //BgmOAuth升级1
    public const string OAuthUpgraded2 = "OAuthUpgraded2"; //BgmOAuth升级2
    public const string SavePathUpgraded = "savePathUpgraded"; //存档路径升级
    public const string GameSyncUpgraded = "gameSyncUpgraded"; //游戏同步升级
    public const string MixedPhraserOrderVersion = "mixedPhraserOrderVersion"; //int，当前配置中的混合搜刮器顺序的版本
    
    
    //废弃Key，只读，仅用于升级
    public const string BangumiToken = "bangumiToken";
    public const string BangumiOAuthState= "bangumiOAuthState"; //BgmAccount?, Bangumi账户, 若为null则未登录
    public const string GalgameFolders = "galgameFolders"; //旧游戏文件夹，仅用于升级
}
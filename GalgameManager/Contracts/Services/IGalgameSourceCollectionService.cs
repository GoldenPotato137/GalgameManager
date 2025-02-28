using System.Collections.ObjectModel;
using GalgameManager.Models;
using GalgameManager.Models.BgTasks;
using GalgameManager.Models.Sources;

namespace GalgameManager.Contracts.Services;

public interface IGalgameSourceCollectionService
{
    /// <summary>
    /// 当库被删除时触发
    /// </summary>
    public Action<GalgameSourceBase>? OnSourceDeleted { get; set; }
    
    /// <summary>
    /// 当库列表被修改（添加、删除）时触发
    /// </summary>
    public Action? OnSourceChanged { get; set; }
    
    /// <summary>
    /// 初始化
    /// </summary>
    /// <returns></returns>
    Task InitAsync();
    
    /// <summary>
    /// 应用启动后调用
    /// </summary>
    Task StartAsync();

    /// <summary>
    /// 获取所有Source列表的引用，<b>只读</b>
    /// </summary>
    /// <returns></returns>
    public ObservableCollection<GalgameSourceBase> GetGalgameSources();

    /// <inheritdoc cref="GetGalgameSource"/>
    public GalgameSourceBase? GetGalgameSourceFromUrl(string url);

    /// 尝试获取某个库，若不存在则返回null
    /// <p>
    /// 对于不同的库的类型，匹配规则如下：<br/>
    /// <list type="bullet">
    /// <item>本地文件夹：直接匹配path</item>
    /// <item>对于剩余的库，直接匹配path</item>
    /// </list>
    /// </p>
    /// 
    public GalgameSourceBase? GetGalgameSource(GalgameSourceType type, string path);

    /// <summary>
    /// 试图添加一个galgame库
    /// </summary>
    /// <param name="sourceType"></param>
    /// <param name="path">库路径</param>
    /// <param name="tryGetGalgame">是否自动寻找库里游戏</param>
    /// <exception cref="Exception">库已经添加过了</exception>
    public Task<GalgameSourceBase> AddGalgameSourceAsync(GalgameSourceType sourceType, string path,
        bool tryGetGalgame = true);

    /// <summary>
    /// 删除一个galgame库，其包含弹窗警告，若用户取消则什么都不做
    /// </summary>
    /// <param name="source"></param>
    public Task DeleteGalgameFolderAsync(GalgameSourceBase source);

    /// <summary>
    /// 扫描所有库
    /// </summary>
    public void ScanAll();

    /// <summary>
    /// 扫描某个库
    /// </summary>
    /// <param name="source"></param>
    public void Scan(GalgameSourceBase source);

    /// <summary>
    /// 保存某个库
    /// </summary>
    /// <param name="source"></param>
    public void Save(GalgameSourceBase source);

    /// <summary>
    /// 将一个游戏移入某个库，不进行物理移动操作（如复制文件节、上传游戏等）
    /// </summary>
    /// <param name="target"></param>
    /// <param name="game"></param>
    /// <param name="path">游戏在库中的路径</param>
    public void MoveInNoOperate(GalgameSourceBase target, Galgame game, string path);

    /// <summary>
    /// 将一个游戏移出某个库，不进行物理移动操作（如删除文件节、在云端删除游戏等）
    /// </summary>
    /// <param name="target"></param>
    /// <param name="game"></param>
    public void MoveOutNoOperate(GalgameSourceBase target, Galgame game);

    /// <summary>
    /// 移动游戏，<b>会进行物理操作</b>（如删除文件夹、复制文件夹、上传游戏到云端等）<br/>
    /// 可以组合移入和移出操作，例如可以不移入任何库，只移出；也可以不移出任何库，只移入；也可以同时移入和移出 <br/>
    /// 若不需要物理移动位置，请用<see cref="MoveInNoOperate"/>与<see cref="MoveOutNoOperate"/>>>
    /// </summary>
    /// <param name="moveInSrc">要移入的库，若设为null则表示不移入任何库</param>
    /// <param name="moveInPath">要移入的路径，若设置为null则表示让service自行决定路径</param>
    /// <param name="moveOutSrc">要移出的库</param>
    /// <param name="game">游戏</param>
    /// <returns>一个已经启动的BgTask</returns>
    public BgTaskBase MoveAsync(GalgameSourceBase? moveInSrc, string? moveInPath, GalgameSourceBase? moveOutSrc,
        Galgame game);

    /// <summary>
    /// 从游戏路径获取其源应该在的路径
    /// </summary>
    /// <param name="type"></param>
    /// <param name="gamePath"></param>
    /// <returns></returns>
    public string GetSourcePath(GalgameSourceType type, string gamePath);

    /// <summary>
    /// 备份数据（调用localSettingService导出）
    /// </summary>
    /// <returns></returns>
    public Task ExportAsync(Action<string, int, int>? progress);
}
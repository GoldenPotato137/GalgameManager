﻿using Windows.Storage;
using Newtonsoft.Json;
using LiteDB;

namespace GalgameManager.Contracts.Services;

public interface ILocalSettingsService
{
    /// <summary>
    /// 当设置值改变时触发，<b>从UI线程调用</b>
    /// </summary>
    public event Delegate? OnSettingChanged;
    
    public DirectoryInfo LocalFolder { get; }
    
    public DirectoryInfo TemporaryFolder { get; }

    public LiteDatabase Database { get; }
    
    Task<T?> ReadSettingAsync<T>(string key, bool isLarge = false, List<JsonConverter>? converters = null,
        bool typeNameHandling = false);

    /// <summary>
    /// 用于直接读取旧设置（直接加载json文件而非从内存读），如果文件不存在则返回默认值
    /// </summary>
    /// <param name="key">key</param>
    /// <param name="template">模板</param>
    /// <param name="settings"></param>
    Task<T?> ReadOldSettingAsync<T>(string key, T template, JsonSerializerSettings? settings = null);

    Task SaveSettingAsync<T>(string key, T value, bool isLarge = false, bool triggerEventWhenNull = false,
        List<JsonConverter>? converters = null, bool typeNameHandling = false);

    Task RemoveSettingAsync(string key, bool isLarge = false);
    
    public delegate void Delegate(string key, object? value);

    /// <summary>
    /// 将某个设置添加到备份中<br/>
    /// 若设置中涉及图片，需要调用<see cref="AddImageToExportAsync"/>方法将图片添加到备份，
    /// 并将返回的路径替换原来的图片路径，后续导入时调用<see cref="GetImageFromImportAsync"/>方法获取图片绝对路径
    /// </summary>
    Task AddToExportAsync(string key, object value, List<JsonConverter>? converters = null,
        bool typeNameHandling = false);

    /// <summary>
    /// 直接把某个现有配置（使用外置json保存的）添加到备份中，不会对配置进行任何处理 <br/>
    /// 如果对应的文件不存在则什么都不做
    /// </summary>
    /// <param name="key"></param>
    /// <returns></returns>
    Task AddToExportDirectlyAsync(string key);
    
    /// <summary>
    /// 把图片添加到备份中，返回图片在备份中的路径，若添加失败（图片不存在，没有权限访问等）则返回null
    /// </summary>
    /// <param name="imagePath"></param>
    /// <returns></returns>
    Task<string?> AddImageToExportAsync(string? imagePath);
    
    /// <summary>
    /// 导入时获取备份中的图片绝对路径<br/>
    /// <ul>
    /// <li>若图片不存在或imagePath为null或空则返回null</li>
    /// <li>若图片已经是绝对路径则直接返回</li>
    /// <li>若图片为默认图片则直接返回</li>
    /// </ul>
    /// </summary>
    /// <param name="imagePath"></param>
    /// <returns></returns>
    Task<string?> GetImageFromImportAsync(string? imagePath);

    /// <summary>
    /// 获取临时备份文件夹，若文件夹不存在则创建，若已存在则直接返回该文件夹
    /// </summary>
    /// <returns></returns>
    Task<StorageFolder> GetTmpExportFolder();

    /// <summary>
    /// 将当前无法读取的数据备份至："$LocalFolder/FailData"
    /// </summary>
    /// <returns></returns>
    Task<string> BackupFailedDataAsync();
}

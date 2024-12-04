﻿using GalgameManager.Services;

namespace GalgameManager.Models;

/// <summary>
/// 用于描述某PotatoVN数据的升级情况 <br/>
/// 方便跨版本数据导出与导入
/// </summary>
public class LocalSettingStatus
{
    /// LocalSettingService, v1.8.0, 将原先的一整个巨大的LocalSettings.json按key拆分成多个文件
    public bool LargerFileSeparateUpgraded = false;

    /// GalgameSourceCollectionService, v1.8.0, 修改存储库的结构
    /// <seealso cref="GalgameSourceCollectionService.SourceUpgradeAsync"/>
    public bool GalgameSourceFormatUpgrade = false;
    
    /// CategoryService, v1.8.0, 改变分类中游戏索引格式
    public bool CategoryGameIndexUpgrade = false;   
    
    /// CategoryService, v1.8.0, 给各分类添加LastPlayed字段
    public bool CategoryAddLastPlayed = false;


    /// galgameCollectionService是否已处理过导入
    public bool ImportGalgame = true;
    /// galgameSourceCollectionService是否已处理过导入
    public bool ImportGalgameSource = true;
    /// categoryService是否已处理过导入
    public bool ImportCategory = true;
    public void SetImportToFalse()
    {
        ImportGalgame = false;
        ImportGalgameSource = false;
        ImportCategory = false;
    }

    public LocalSettingStatus Clone() => (LocalSettingStatus)MemberwiseClone();
}
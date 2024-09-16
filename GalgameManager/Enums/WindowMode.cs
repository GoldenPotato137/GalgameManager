﻿namespace GalgameManager.Enums;

public enum WindowMode
{
    /// <summary>
    /// 正常模式
    /// </summary>
    Normal,
    
    /// <summary>
    /// 最小化
    /// </summary>
    Minimize,
    
    /// <summary>
    /// 最小到系统托盘
    /// </summary>
    SystemTray,
    
    /// <summary>
    /// 关闭应用，仅在CloseConfirmDialog中使用
    /// </summary>
    Close,
    
    /// <summary>
    /// 启动中
    /// </summary>
    Booting,
    
    
    /// <summary>
    /// 游玩时pvn状态，None为什么都不做
    /// </summary>
    None,

}
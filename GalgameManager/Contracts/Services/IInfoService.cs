﻿using System.Collections.ObjectModel;
using GalgameManager.Enums;
using GalgameManager.Models;
using Microsoft.UI.Xaml.Controls;

namespace GalgameManager.Contracts.Services;

public interface IInfoService
{
    public event Action<InfoBarSeverity,string?,string?,int> OnInfo; 
    
    public event Action<InfoBarSeverity,string?,string?> OnEvent;
    
    public ObservableCollection<Info> Infos { get; }

    /// <summary>
    /// 使用InfoBar通知信息，若title与msg均为空则关闭InfoBar
    /// </summary>
    /// <param name="infoBarSeverity"></param>
    /// <param name="title"></param>
    /// <param name="msg"></param>
    /// <param name="displayTimeMs"></param>
    public void Info(InfoBarSeverity infoBarSeverity, string? title = null, string? msg = null,int? displayTimeMs = 3000);

    /// <summary>
    /// 记录并通知事件
    /// </summary>
    /// <param name="type">事件</param>
    /// <param name="infoBarSeverity">严重程度</param>
    /// <param name="title">事件名</param>
    /// <param name="exception">与之相关的异常，若不是异常则不填</param>
    /// <param name="msg">事件信息</param>
    public void Event(EventType type, InfoBarSeverity infoBarSeverity, string title, Exception? exception = null, string? msg = null);

    /// <summary>
    /// 不严重的非预期错误，仅在开发模式下通知
    /// </summary>
    /// <param name="msg"></param>
    /// <param name="infoBarSeverity"></param>
    /// <param name="e"></param>
    public void DeveloperEvent(InfoBarSeverity infoBarSeverity = InfoBarSeverity.Warning, string? msg = null,
        Exception? e = null);

    /// <summary>
    /// 手动记录日志，默认只将severity >= InfoBarSeverity.Warning的日志通知，开发者模式下通知所有日志 <br/>
    /// <see cref="Event"/>会自动调用该方法记录日志
    /// </summary>
    /// <param name="severity"></param>
    /// <param name="msg"></param>
    public void Log(InfoBarSeverity severity = InfoBarSeverity.Warning, string msg = "");
}
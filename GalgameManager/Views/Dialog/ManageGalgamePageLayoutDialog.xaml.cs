using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using GalgameManager.Contracts.Services;
using GalgameManager.Enums;
using GalgameManager.Helpers;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace GalgameManager.Views.Dialog;

public sealed partial class ManageGalgamePageLayoutDialog : ContentDialog
{
    private readonly ILocalSettingsService _localSettingsService = App.GetService<ILocalSettingsService>();

    public bool GalgamePageNewLayout_ShowPainter { get; set; }
    public bool GalgamePageNewLayout_ShowSeiyu { get; set; }
    public bool GalgamePageNewLayout_ShowWriter { get; set; }
    public bool GalgamePageNewLayout_ShowMusician { get; set; }

    public ManageGalgamePageLayoutDialog()
    {
        InitializeComponent();
        XamlRoot = App.MainWindow!.Content.XamlRoot;
        Title = "ManageGalgamePageLayoutDialog_Title".GetLocalized();
        PrimaryButtonText = "Yes".GetLocalized();
        SecondaryButtonText = "Cancel".GetLocalized();

        LoadSettings();
        PrimaryButtonClick += ManageGalgamePageLayoutDialog_PrimaryButtonClick;
        SecondaryButtonClick += ManageGalgamePageLayoutDialog_SecondaryButtonClick;
    }

    private async void LoadSettings()
    {
        GalgamePageNewLayout_ShowPainter = await _localSettingsService.ReadSettingAsync<bool>(KeyValues.GalgamePageNewLayout_ShowPainter);
        GalgamePageNewLayout_ShowSeiyu = await _localSettingsService.ReadSettingAsync<bool>(KeyValues.GalgamePageNewLayout_ShowSeiyu);
        GalgamePageNewLayout_ShowWriter = await _localSettingsService.ReadSettingAsync<bool>(KeyValues.GalgamePageNewLayout_ShowWriter);
        GalgamePageNewLayout_ShowMusician = await _localSettingsService.ReadSettingAsync<bool>(KeyValues.GalgamePageNewLayout_ShowMusician);
    }

    private async void ManageGalgamePageLayoutDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();

        await _localSettingsService.SaveSettingAsync(KeyValues.GalgamePageNewLayout_ShowPainter, GalgamePageNewLayout_ShowPainter);
        await _localSettingsService.SaveSettingAsync(KeyValues.GalgamePageNewLayout_ShowSeiyu, GalgamePageNewLayout_ShowSeiyu);
        await _localSettingsService.SaveSettingAsync(KeyValues.GalgamePageNewLayout_ShowWriter, GalgamePageNewLayout_ShowWriter);
        await _localSettingsService.SaveSettingAsync(KeyValues.GalgamePageNewLayout_ShowMusician, GalgamePageNewLayout_ShowMusician);

        deferral.Complete();
    }

    private void ManageGalgamePageLayoutDialog_SecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // 取消按钮不需要执行任何操作，对话框会自动关闭
    }
}


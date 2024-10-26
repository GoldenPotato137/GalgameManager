using Windows.Storage;
using Windows.Storage.Pickers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalgameManager.Contracts.Services;
using GalgameManager.Core.Contracts.Services;
using GalgameManager.Helpers;
using GalgameManager.Models.Sources;
using GalgameManager.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GalgameManager.Views.Dialog;

public sealed partial class UnpackDialog
{
    public string? Password;
    public string PackName => PackNameText.Text;
    public string GameName => GameNameText.Text;
    public StorageFile? StorageFile;
    public GalgameSourceBase? Source = null;
        
    public UnpackDialog()
    {
        InitializeComponent();

        XamlRoot = App.MainWindow!.Content.XamlRoot;
        PrimaryButtonText = "Yes".GetLocalized();
        SecondaryButtonText = "Cancel".GetLocalized();
        Title = "UnpackDialog_Title".GetLocalized();
        
        SecondaryButtonClick += (_, _) => StorageFile = null;
    }

    public async new Task ShowAsync()
    {
        await GetPack();
        if (StorageFile is null) return;
        await base.ShowAsync();
    }
    
    public async Task ShowAsync(StorageFile storageFile,  bool showSources = false)
    {
        StorageFile = storageFile;
        PackNameText.Text = StorageFile?.Name ?? string.Empty;
        GameNameText.Text = StorageFile?.DisplayName ?? string.Empty;
        if (StorageFile is null) return;
        SourceSelect.Visibility = showSources ? Visibility.Visible : Visibility.Collapsed;
        SourceListView.ItemsSource = 
            App.GetService<IGalgameSourceCollectionService>().GetGalgameSources()
            .Where(s=>s.SourceType == GalgameSourceType.LocalFolder);
        await base.ShowAsync();
    }

    [RelayCommand]
    private async Task GetPack()
    {
        FileOpenPicker openPicker = new()
        {
            ViewMode = PickerViewMode.Thumbnail,
            SuggestedStartLocation = PickerLocationId.PicturesLibrary
        };
        WinRT.Interop.InitializeWithWindow.Initialize(openPicker, App.MainWindow!.GetWindowHandle());
        openPicker.FileTypeFilter.Add(".zip");
        openPicker.FileTypeFilter.Add(".7z");
        openPicker.FileTypeFilter.Add(".tar");
        openPicker.FileTypeFilter.Add(".001");
        openPicker.FileTypeFilter.Add(".rar");
        StorageFile = await openPicker.PickSingleFileAsync();
            
        PackNameText.Text = StorageFile?.Name ?? string.Empty;
        GameNameText.Text = StorageFile?.DisplayName ?? string.Empty;
    }

    private void SourceListView_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        Source = SourceListView.SelectedItem as GalgameSourceBase;
    }
}
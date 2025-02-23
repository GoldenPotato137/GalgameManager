#pragma warning disable MVVMTK0049 //警告INotifyPropertyChanged无法NativeAOT，等后续处理
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalgameManager.Contracts.Services;
using GalgameManager.Enums;
using GalgameManager.Helpers;
using GalgameManager.Models;
using Microsoft.UI.Xaml;

namespace GalgameManager.Views.Dialog;

[INotifyPropertyChanged]
public partial class ConfirmGalInfoDialog
{
    public List<RssType> RssTypes { get; }= new() { RssType.Bangumi, RssType.Vndb, RssType.Ymgal, RssType.Cngal };
    [ObservableProperty] private Galgame _galgame = null!;
    [ObservableProperty] private string? _id = string.Empty;
    [ObservableProperty] private RssType _selectedRssType = RssType.Bangumi;
    [ObservableProperty] private string _hint = null!;
    [ObservableProperty] private Visibility _isPhrasing = Visibility.Collapsed;
    private readonly IGalgameCollectionService _service;

    public ConfirmGalInfoDialog(Galgame targetGame, Galgame? fetchedMeta, IGalgameCollectionService service)
    {
        InitializeComponent();
        XamlRoot = App.MainWindow!.Content.XamlRoot;
        
        Galgame = fetchedMeta ?? new Galgame(targetGame.Name.Value ?? string.Empty);
        _service = service;
        
        Update();
        PrimaryButtonText = "Yes".GetLocalized();
        SecondaryButtonText = "Cancel".GetLocalized();
    }

    private void Update()
    {
        Id = Galgame.Ids[(int)SelectedRssType];
        Title = Galgame.Description.Value?.Length > 0
            ? "ConfirmGalInfoDialog_Title_Correct".GetLocalized()
            : "ConfirmGalInfoDialog_Title_NotFound".GetLocalized();
        List<RssType> currentId = RssTypes.Where(rss => !string.IsNullOrWhiteSpace(Galgame.Ids[(int)rss])).ToList();
        Hint = "ConfirmGalInfoDialog_Hint".GetLocalized() + "\n" +
               (currentId.Count == 0
                   ? "ConfirmGalInfoDialog_NoID".GetLocalized()
                   : "ConfirmGalInfoDialog_ID".GetLocalized(string.Join(',', currentId)));
    }

    [RelayCommand]
    private async Task FetchInfo()
    {
        IsPhrasing = Visibility.Visible;
        IsPrimaryButtonEnabled = IsSecondaryButtonEnabled = false;
        await _service.ParseGalInfoOnlyAsync(Galgame);
        IsPhrasing = Visibility.Collapsed;
        IsPrimaryButtonEnabled = IsSecondaryButtonEnabled = true;
        Update();
    }

    partial void OnIdChanged(string? value)
    {
        Galgame.Ids[(int)SelectedRssType] = string.IsNullOrWhiteSpace(value) ? null : value;
        Galgame.UpdateMixedId();
        Update();
    }

    partial void OnSelectedRssTypeChanged(RssType value) => Id = Galgame.Ids[(int)value];
}

#pragma warning restore MVVMTK0049
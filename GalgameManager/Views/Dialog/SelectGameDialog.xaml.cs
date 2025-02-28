using Microsoft.UI.Xaml.Controls;
using GalgameManager.Helpers;
using GalgameManager.Models;
using CommunityToolkit.WinUI.Collections;
using System.Collections.ObjectModel;
using GalgameManager.Contracts.Services;
using GalgameManager.Enums;
using CommunityToolkit.Mvvm.ComponentModel;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace GalgameManager.Views.Dialog;

public sealed partial class SelectGameDialog : ContentDialog
{
    private readonly TaskCompletionSource<(Galgame?, List<Career>)> _selectGameTcs = new();
    private readonly AdvancedCollectionView _games;
    private string _searchKey = string.Empty;
    
    public ObservableCollection<CareerCheckBox> Careers { get; } = new();
    
    public string SearchKey
    {
        get => _searchKey;
        set
        {
            _searchKey = value;
            _games.Filter = item => string.IsNullOrEmpty(_searchKey) || 
                ((item as Galgame)?.Name?.Value?.Contains(_searchKey, StringComparison.OrdinalIgnoreCase) ?? false);
        }
    }


    public SelectGameDialog()
    {
        InitializeComponent();
        
        // 设置本地化文本
        Title = "SelectGameDialog_Title".GetLocalized();
        PrimaryButtonText = "SelectGameDialog_Confirm".GetLocalized();
        CloseButtonText = "SelectGameDialog_Cancel".GetLocalized();
        SearchBox.PlaceholderText = "SelectGameDialog_SearchPlaceholder".GetLocalized();
        
        // 安全地设置 XamlRoot
        if (App.MainWindow?.Content?.XamlRoot != null)
        {
            XamlRoot = App.MainWindow.Content.XamlRoot;
        }
        
        // 初始化职位多选列表
        var validCareers = Enum.GetValues<Career>().Where(c => c != Career.Unknown);
        foreach (var career in validCareers)
        {
            Careers.Add(new CareerCheckBox(career));
        }
        
        var galgames = App.GetService<IGalgameCollectionService>().Galgames;
        _games = new AdvancedCollectionView(galgames, true);
        GamesList.ItemsSource = _games;

        PrimaryButtonClick += (_, _) =>
        {
            var selectedCareers = Careers.Where(c => c.IsChecked).Select(c => c.Career).ToList();
            _selectGameTcs.TrySetResult((GamesList.SelectedItem as Galgame, selectedCareers));
        };
        CloseButtonClick += (_, _) =>
        {
            _selectGameTcs.TrySetResult((null, new List<Career>()));
        };
    }

    public async Task<(Galgame? Game, List<Career> Careers)> ShowAndGetResultAsync()
    {
        await ShowAsync();
        return await _selectGameTcs.Task;
    }
}

public class CareerCheckBox : ObservableObject
{
    private bool _isChecked;
    public Career Career { get; }
    
    public bool IsChecked
    {
        get => _isChecked;
        set => SetProperty(ref _isChecked, value);
    }

    public CareerCheckBox(Career career)
    {
        Career = career;
    }
}

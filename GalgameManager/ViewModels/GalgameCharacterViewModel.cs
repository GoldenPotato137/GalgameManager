using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalgameManager.Contracts.Services;
using GalgameManager.Contracts.ViewModels;
using GalgameManager.Models;
using Microsoft.UI.Xaml;

namespace GalgameManager.ViewModels;

public partial class GalgameCharacterViewModel (
    INavigationService navigationService,
    IGalgameCollectionService galgameService
): ObservableObject, INavigationAware
{
    [ObservableProperty] private GalgameCharacter? _character;
    [ObservableProperty] private Visibility _isSummaryVisible = Visibility.Collapsed;
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private DateTimeOffset? _selectedDate;

    public readonly Gender[] Genders = { Gender.Female, Gender.Male, Gender.Unknown };

    public void OnNavigatedTo(object parameter)
    {
        if (parameter is not GalgameCharacterParameter param) //参数不正确，返回主菜单
        {
            navigationService.NavigateTo(typeof(HomeViewModel).FullName!);
            return;
        }

        Character = param.GalgameCharacter;
        
        // 初始化日期
        if (Character.BirthYear.HasValue && Character.BirthMon.HasValue && Character.BirthDay.HasValue)
        {
            SelectedDate = new DateTimeOffset(Character.BirthYear.Value, Character.BirthMon.Value, Character.BirthDay.Value, 0, 0, 0, TimeSpan.Zero);
        }
        
        UpdateVisibility();
    }

    public void OnNavigatedFrom()
    {
    }
    
    private void UpdateVisibility()
    {
        IsSummaryVisible = Character?.Summary! != string.Empty ? Visibility.Visible : Visibility.Collapsed;
    }

    [RelayCommand]
    private void Edit()
    {
        if (Character is null) return;
        IsEditing = true;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (Character is null) return;
        
        // 遍历所有游戏找到包含该角色的游戏
        var galgame = galgameService.Galgames.FirstOrDefault(g => 
            g.Characters.Contains(Character));
        
        if (galgame is not null)
        {
            await galgameService.SaveGalgameAsync(galgame);
        }
        
        IsEditing = false;
    }

    partial void OnSelectedDateChanged(DateTimeOffset? oldValue, DateTimeOffset? newValue)
    {
        if (Character is null || newValue is null) return;
        
        Character.BirthYear = newValue.Value.Year;
        Character.BirthMon = newValue.Value.Month;
        Character.BirthDay = newValue.Value.Day;
        Character.BirthDate = $"{newValue.Value.Month}月{newValue.Value.Day}日";
    }
}

public class GalgameCharacterParameter
{
    [Required] public GalgameCharacter GalgameCharacter = null!;
}
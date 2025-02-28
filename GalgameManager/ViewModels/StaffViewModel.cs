using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalgameManager.Contracts.Services;
using GalgameManager.Contracts.ViewModels;
using GalgameManager.Helpers;
using GalgameManager.Models;
using GalgameManager.Models.Filters;
using GalgameManager.Enums;
using System.Collections.ObjectModel;
using System.Linq;
using System.Collections.Generic;
using GalgameManager.Views.Dialog;
using Windows.Storage.Pickers;
using Windows.Storage;

namespace GalgameManager.ViewModels;

public partial class StaffViewModel(
    INavigationService navigationService, IFilterService filterService,
    IStaffService staffService
) : ObservableRecipient, INavigationAware
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CareerString))] 
    private Staff? _staff;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string _editableName = string.Empty;


    public string CareerString =>
        Staff?.Career.Count > 0 ? string.Join(", ", Staff.Career.Select(c => c.GetLocalized())) : "-";


    public readonly Gender[] Genders = { Gender.Female, Gender.Male, Gender.Unknown };

    public ObservableCollection<StaffViewModelCareerCheckBox> Careers { get; } = [];

    public void OnNavigatedTo(object parameter)
    {
        if (parameter is not StaffPageNavigationParameter staffPageNavigationParameter)
        {
            Debug.Assert(false, "Invalid navigation parameter");
            return;
        }
        Staff = staffPageNavigationParameter.Staff;
        
        // 使用LINQ简化Career的添加
        var validCareers = Enum.GetValues<Career>().Where(c => c != Career.Unknown);
        foreach (var career in validCareers)
        {
            Careers.Add(new(career, Staff.Career.Contains(career)));
        }
        EditableName = Staff?.Name ?? string.Empty;
    }

    public void OnNavigatedFrom()
    {
    }

    [RelayCommand]
    private void NaviToGame(StaffGame? game)
    {
        if (game is null) return;
        NavigationHelper.NavigateToGalgamePage(navigationService, new GalgamePageParameter{Galgame = game.Game});
    }

    [RelayCommand]
    private void NaviToHome()
    {
        if (Staff is null) return;
        NavigationHelper.NavigateToHomePage(navigationService, filterService, [new StaffFilter(Staff)]);
    }
    
    public class StaffPageNavigationParameter
    {
        public required Staff Staff { get; set; }
    }

    [RelayCommand]
    private void EditStaff()
    {
        IsEditing = true;
    }

    [RelayCommand]
    private void SaveStaff()
    {
        if (Staff != null)
        {
            // 清空原有的Career列表
            Staff.Career.Clear();
            
            // 将选中的Career添加到Staff.Career中
            foreach (var careerCheckBox in Careers)
            {
                if (careerCheckBox.IsChecked)
                {
                    Staff.Career.Add(careerCheckBox.Career);
                }
            }

            // 更新姓名
            var name = EditableName.Trim();
            if (!string.IsNullOrEmpty(name) && name != Staff.Name)
            {
                if (name.IsJapanese())
                    Staff.JapaneseName = name;
                else if (name.IsChinese())
                {
                    Staff.ChineseName = name;
                    Staff.JapaneseName = null;
                }
                else
                {
                    Staff.EnglishName = name;
                    Staff.JapaneseName = null;
                    Staff.ChineseName = null;
                }
            }
            
            staffService.Save(Staff);
        }
        IsEditing = false;

        // 刷新CareerString
        OnPropertyChanged(nameof(CareerString));

    }

    [RelayCommand]
    private void DeleteGame(StaffGame? game)
    {
        if (game is null || Staff is null) return;
        Staff.RemoveGame(game.Game);
        staffService.Save(Staff);
    }

    [RelayCommand]
    private async Task AddGame()
    {
        if (Staff is null) return;
        
        var dialog = new SelectGameDialog();
        var (selectedGame, selectedCareers) = await dialog.ShowAndGetResultAsync();
        if (selectedGame != null && selectedCareers.Count > 0)
        {
            Staff.AddGame(selectedGame, selectedCareers);
            staffService.Save(Staff);
        }
    }

    [RelayCommand]
    private async Task PickImageAsync()
    {
        if (Staff is null) return;
        FileOpenPicker openPicker = new()
        {
            ViewMode = PickerViewMode.Thumbnail,
            SuggestedStartLocation = PickerLocationId.PicturesLibrary
        };
        WinRT.Interop.InitializeWithWindow.Initialize(openPicker, App.MainWindow!.GetWindowHandle());
        openPicker.FileTypeFilter.Add(".jpg");
        openPicker.FileTypeFilter.Add(".jpeg");
        openPicker.FileTypeFilter.Add(".png");
        openPicker.FileTypeFilter.Add(".bmp");
        StorageFile? file = await openPicker.PickSingleFileAsync();
        if (file == null) return;
        StorageFile newFile = await file.CopyAsync(await FileHelper.GetFolderAsync(FileHelper.FolderType.Images),
            $"{Staff.Name}{file.FileType}", NameCollisionOption.ReplaceExisting);
        Staff.ImagePath = string.Empty;
        Staff.ImagePath = newFile.Path;

    }

}


public partial class StaffViewModelCareerCheckBox : ObservableRecipient
{
    public Career Career { get; set; }
    [ObservableProperty] private bool _isChecked;

    public StaffViewModelCareerCheckBox() { }

    public StaffViewModelCareerCheckBox(Career career, bool isChecked)
    {
        Career = career;
        IsChecked = isChecked;
    }
}
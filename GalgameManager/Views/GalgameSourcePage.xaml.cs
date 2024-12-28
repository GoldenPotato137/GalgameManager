﻿using GalgameManager.Contracts.Services;
using GalgameManager.ViewModels;

using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace GalgameManager.Views;

public sealed partial class GalgameSourcePage : Page
{
    public GalgameSourceViewModel ViewModel
    {
        get;
    }

    public GalgameSourcePage()
    {
        ViewModel = App.GetService<GalgameSourceViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }

    protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
    {
        base.OnNavigatingFrom(e);
        if (e.NavigationMode == NavigationMode.Back)
        {
            var navigationService = App.GetService<INavigationService>();

            if (ViewModel.Item != null)
            {
                navigationService.SetListDataItemForNextConnectedAnimation(ViewModel.Item);
            }
        }
    }
}

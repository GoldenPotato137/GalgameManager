﻿using Windows.ApplicationModel.Activation;
using GalgameManager.Contracts.Services;
using GalgameManager.Models;
using GalgameManager.Services;
using GalgameManager.ViewModels;
using Microsoft.Windows.AppLifecycle;

namespace GalgameManager.Activation;

public class JumpListActivationHandler : ActivationHandler<AppActivationArguments>
{
    private readonly GalgameCollectionService _galgameCollectionService;
    private Galgame? _game;
    
    public JumpListActivationHandler(IGalgameCollectionService galgameCollectionService)
    {
        _galgameCollectionService = (galgameCollectionService as GalgameCollectionService)!;
    }
    
    protected override bool CanHandleInternal(AppActivationArguments args)
    {
        if (args.Kind != ExtendedActivationKind.Launch) return false;
        if (args.Data is not LaunchActivatedEventArgs arg) return false;
        if ((arg.Arguments.StartsWith("/j") && arg.Arguments.Length > 2) == false) return false;
        try
        {
            var target = arg.Arguments[3..]; //去掉/j与空格
            target = target[1..^1]; //去掉双引号
            _game = _galgameCollectionService.GetGalgameFromUuid(new Guid(target));
            return _game is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    protected async override Task HandleInternalAsync(AppActivationArguments args)
    {
        App.GetService<INavigationService>().NavigateTo(typeof(GalgameViewModel).FullName!, new GalgamePageParameter
        {
            Galgame = _game!,
            StartGame = true
        });
        await Task.CompletedTask;
    }
}
﻿using GalgameManager.Contracts.Services;
using GalgameManager.Helpers;
using GalgameManager.ViewModels;
using Microsoft.Windows.AppLifecycle;

namespace GalgameManager.Activation;

public class UpdateContentHandler : ActivationHandler<AppActivationArguments>
{
    protected override bool CanHandleInternal(AppActivationArguments args)
    {
        if(args.Kind != ExtendedActivationKind.Launch || !App.GetService<IUpdateService>().ShouldDisplayUpdateContent()) 
            return false;
        if (Utils.IsWebview2Ok()) return false;
        return true;
    }

    protected override Task HandleInternalAsync(AppActivationArguments args)
    {
        App.GetService<INavigationService>().NavigateTo(typeof(UpdateContentViewModel).FullName!, true);
        return Task.CompletedTask;
    }
}
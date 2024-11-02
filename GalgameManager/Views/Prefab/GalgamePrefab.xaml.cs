﻿using System.Diagnostics;
using DependencyPropertyGenerator;
using GalgameManager.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace GalgameManager.Views.Prefab;

[DependencyProperty<Stretch>("ImageStretch", DefaultValue = Stretch.UniformToFill,
    DefaultBindingMode = DefaultBindingMode.OneWay)]
[DependencyProperty<Galgame>("Galgame")]
[DependencyProperty<Visibility>("PlayTypeVisibility", DefaultValue = Visibility.Collapsed,
    DefaultBindingMode = DefaultBindingMode.OneWay)]
[DependencyProperty<FlyoutBase>("Flyout")]
[DependencyProperty<double>("ItemScale", DefaultValue = 1.0f)]
public sealed partial class GalgamePrefab
{
    public GalgamePrefab()
    {
        InitializeComponent();
        Loaded += GalgamePrefab_Loaded;
    }

    private void GalgamePrefab_Loaded(object sender, RoutedEventArgs e)
    {
        Debug.Assert(Galgame != null, "Galgame property should not be null.");
    }

    partial void OnItemScaleChanged(double newValue)
    {
        if (newValue > 0) return;
        ItemScale = 1.0f;
    }
    
    public double CalcValue(double value) => value * ItemScale;
}
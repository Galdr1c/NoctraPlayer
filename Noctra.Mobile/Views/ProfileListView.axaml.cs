using System;
using Avalonia;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Noctra.Models;
using Noctra.ViewModels;
using AddProfileViewModel = Noctra.ViewModels.AddProfileViewModel;

namespace Noctra.Mobile.Views;

public partial class ProfileListView : UserControl
{
    private ProfilesViewModel? _viewModel;
    private AddProfileViewModel? _activeProfileSetupViewModel;

    public ProfileListView()
    {
        InitializeComponent();
        AttachedToVisualTree += async (_, _) =>
        {
            if (DataContext is ProfilesViewModel viewModel)
            {
                await viewModel.RefreshProfilesAsync();
            }
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        BindViewModel(DataContext as ProfilesViewModel);
    }

    private void BindViewModel(ProfilesViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel))
        {
            return;
        }

        if (_viewModel is not null)
        {
            _viewModel.OnProfileAddRequested -= ViewModel_OnProfileAddRequested;
            _viewModel.OnProfileEditRequested -= ViewModel_OnProfileEditRequested;
        }

        _viewModel = viewModel;

        if (_viewModel is not null)
        {
            _viewModel.OnProfileAddRequested += ViewModel_OnProfileAddRequested;
            _viewModel.OnProfileEditRequested += ViewModel_OnProfileEditRequested;
        }
    }

    private void ViewModel_OnProfileAddRequested(Profile profile)
    {
        OpenProfileSetup(null);
    }

    private void ViewModel_OnProfileEditRequested(Profile profile)
    {
        OpenProfileSetup(profile);
    }

    private void OpenProfileSetup(Profile? profile)
    {
        if (Application.Current is not App app || app.Services is null)
        {
            return;
        }

        var viewModel = app.Services.GetRequiredService<AddProfileViewModel>();
        if (profile is not null)
        {
            viewModel.InitializeForEdit(profile);
        }

        _activeProfileSetupViewModel = viewModel;
        _activeProfileSetupViewModel.RequestClose += ProfileSetup_RequestClose;

        ProfileSetupContent.Content = new ProfileSetupView
        {
            DataContext = viewModel
        };
        ProfileSetupHost.IsVisible = true;
    }

    private async void ProfileSetup_RequestClose(object? sender, EventArgs e)
    {
        if (_activeProfileSetupViewModel is not null)
        {
            _activeProfileSetupViewModel.RequestClose -= ProfileSetup_RequestClose;
            _activeProfileSetupViewModel = null;
        }

        ProfileSetupHost.IsVisible = false;
        ProfileSetupContent.Content = null;

        if (_viewModel is not null)
        {
            await _viewModel.RefreshProfilesAsync();
        }
    }
}

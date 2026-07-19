using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using Noctra.ViewModels;

namespace Noctra.Mobile.Views;

public partial class ProfileSetupView : UserControl
{
    private AddProfileViewModel? _viewModel;
    private AvatarPickerViewModel? _avatarPickerViewModel;

    public ProfileSetupView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        BindViewModel(DataContext as AddProfileViewModel);
    }

    private void BindViewModel(AddProfileViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel))
        {
            return;
        }

        if (_viewModel is not null)
        {
            _viewModel.RequestAvatarPicker -= ViewModel_RequestAvatarPicker;
            _viewModel.ValidationErrorOccurred -= ViewModel_ValidationErrorOccurred;
        }

        _viewModel = viewModel;

        if (_viewModel is not null)
        {
            _viewModel.RequestAvatarPicker += ViewModel_RequestAvatarPicker;
            _viewModel.ValidationErrorOccurred += ViewModel_ValidationErrorOccurred;
        }
    }

    private void ViewModel_RequestAvatarPicker(object? sender, EventArgs e)
    {
        if (Avalonia.Application.Current is not App app || app.Services is null)
        {
            return;
        }

        _avatarPickerViewModel = app.Services.GetRequiredService<AvatarPickerViewModel>();
        _avatarPickerViewModel.SelectedAvatar = _viewModel?.SelectedAvatar;
        _avatarPickerViewModel.AvatarSelected += AvatarPicker_AvatarSelected;
        AvatarPickerContent.DataContext = _avatarPickerViewModel;
        AvatarPickerHost.IsVisible = true;
    }

    private void AvatarPicker_AvatarSelected(object? sender, string avatar)
    {
        _viewModel?.SetAvatar(avatar);
        CloseAvatarPicker();
    }

    private void OnCloseAvatarPicker(object? sender, RoutedEventArgs e)
    {
        CloseAvatarPicker();
    }

    private void CloseAvatarPicker()
    {
        if (_avatarPickerViewModel is not null)
        {
            _avatarPickerViewModel.AvatarSelected -= AvatarPicker_AvatarSelected;
            _avatarPickerViewModel = null;
        }

        AvatarPickerHost.IsVisible = false;
        AvatarPickerContent.DataContext = null;
    }

    public bool TryHandleBack()
    {
        if (AvatarPickerHost.IsVisible)
        {
            CloseAvatarPicker();
            return true;
        }

        return false;
    }

    private void ProfileName_LostFocus(object? sender, RoutedEventArgs e)
        => _viewModel?.TouchField("ProfileName");

    private void Url_LostFocus(object? sender, RoutedEventArgs e)
        => _viewModel?.TouchField("Url");

    private void Username_LostFocus(object? sender, RoutedEventArgs e)
        => _viewModel?.TouchField("Username");

    private void Password_LostFocus(object? sender, RoutedEventArgs e)
        => _viewModel?.TouchField("Password");

    private void PinCode_LostFocus(object? sender, RoutedEventArgs e)
        => _viewModel?.TouchField("PinCode");

    private void PinConfirm_LostFocus(object? sender, RoutedEventArgs e)
        => _viewModel?.TouchField("PinConfirm");

    private void ViewModel_ValidationErrorOccurred(object? sender, string fieldName)
    {
        TextBox? textBoxToFocus = fieldName switch
        {
            "ProfileName" => this.FindControl<TextBox>("ProfileNameTextBox"),
            "Url" => this.FindControl<TextBox>("UrlTextBox"),
            "Username" => this.FindControl<TextBox>("UsernameTextBox"),
            "Password" => this.FindControl<TextBox>("PasswordTextBox"),
            "PinCode" => this.FindControl<TextBox>("PinCodeTextBox"),
            "PinConfirm" => this.FindControl<TextBox>("PinConfirmTextBox"),
            _ => null
        };

        if (textBoxToFocus is not null)
        {
            textBoxToFocus.Focus();
        }
    }
}

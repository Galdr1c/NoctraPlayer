using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using Noctra.Core.Services;
using Noctra.Services.Interfaces;
using Noctra.ViewModels;

namespace Noctra.Avalonia.Views;

public partial class AddProfileWindow : Window
{
    private readonly IDialogService _dialogService;
    private AddProfileViewModel? _viewModel;

    public AddProfileWindow()
        : this(
            ((App)Application.Current!).Services.GetRequiredService<AddProfileViewModel>(),
            ((App)Application.Current!).Services.GetRequiredService<IDialogService>())
    {
    }

    public AddProfileWindow(AddProfileViewModel viewModel, IDialogService dialogService)
    {
        InitializeComponent();
        _dialogService = dialogService;
        DataContext = viewModel;
        BindViewModel(viewModel);
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

        if (_viewModel != null)
        {
            _viewModel.RequestClose -= ViewModel_RequestClose;
            _viewModel.RequestAvatarPicker -= ViewModel_RequestAvatarPicker;
        }

        _viewModel = viewModel;

        if (_viewModel != null)
        {
            _viewModel.RequestClose += ViewModel_RequestClose;
            _viewModel.RequestAvatarPicker += ViewModel_RequestAvatarPicker;
        }
    }

    private void DragBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    protected override void OnClosed(EventArgs e)
    {
        BindViewModel(null);
        base.OnClosed(e);
    }

    private void ViewModel_RequestClose(object? sender, EventArgs e)
    {
        Close(true);
    }

    private async void ViewModel_RequestAvatarPicker(object? sender, EventArgs e)
    {
        if (_viewModel == null)
        {
            return;
        }

        try
        {
            var selectedAvatar = await _dialogService.ShowAvatarPickerAsync(_viewModel.SelectedAvatar);
            if (!string.IsNullOrWhiteSpace(selectedAvatar))
            {
                _viewModel.SetAvatar(selectedAvatar);
            }
        }
        catch (Exception ex)
        {
            StartupDiagnostics.LogException("Failed to open AvatarPickerWindow.", ex);

            var dialogService = ((App)Application.Current!).Services.GetService<IDialogService>();
            if (dialogService != null)
            {
                await dialogService.ShowErrorAsync("Hata", "Avatar seçme penceresi açılamadı.", ex);
            }
        }
    }
}

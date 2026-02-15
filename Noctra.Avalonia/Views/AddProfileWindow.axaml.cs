using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using Noctra.Services.Interfaces;
using Noctra.ViewModels;

namespace Noctra.Avalonia.Views;

public partial class AddProfileWindow : Window
{
    private readonly IServiceScopeFactory _scopeFactory;
    private AddProfileViewModel? _viewModel;

    public AddProfileWindow()
        : this(
            ((App)Application.Current!).Services.GetRequiredService<AddProfileViewModel>(),
            ((App)Application.Current!).Services.GetRequiredService<IServiceScopeFactory>())
    {
    }

    public AddProfileWindow(AddProfileViewModel viewModel, IServiceScopeFactory scopeFactory)
    {
        InitializeComponent();
        _scopeFactory = scopeFactory;
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
            using var scope = _scopeFactory.CreateScope();
            var pickerWindow = scope.ServiceProvider.GetRequiredService<AvatarPickerWindow>();
            var pickerVm = pickerWindow.DataContext as AvatarPickerViewModel
                ?? scope.ServiceProvider.GetRequiredService<AvatarPickerViewModel>();

            if (!ReferenceEquals(pickerWindow.DataContext, pickerVm))
            {
                pickerWindow.DataContext = pickerVm;
            }

            string? selectedAvatar = null;
            void OnAvatarSelected(object? _, string avatar)
            {
                selectedAvatar = avatar;
            }

            pickerVm.AvatarSelected += OnAvatarSelected;
            bool? result;
            try
            {
                result = await pickerWindow.ShowDialog<bool?>(this);
            }
            finally
            {
                pickerVm.AvatarSelected -= OnAvatarSelected;
            }

            if (result == true && !string.IsNullOrWhiteSpace(selectedAvatar))
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

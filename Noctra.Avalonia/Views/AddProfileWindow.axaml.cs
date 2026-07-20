using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using Noctra.Core.Services;
using Noctra.Services.Interfaces;
using Noctra.ViewModels;
using System.Linq;

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
        SyncPasswordChar();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        SyncPasswordChar();
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
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }

        _viewModel = viewModel;

        if (_viewModel != null)
        {
            _viewModel.RequestClose += ViewModel_RequestClose;
            _viewModel.RequestAvatarPicker += ViewModel_RequestAvatarPicker;
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        }
    }

    private const char PasswordMask = '\u2022';

    private void SyncPasswordChar()
    {
        if (DesktopPasswordTextBox is null || _viewModel is null) return;
        DesktopPasswordTextBox.PasswordChar = _viewModel.IsPasswordRevealed ? '\0' : PasswordMask;
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AddProfileViewModel.IsPasswordRevealed))
        {
            SyncPasswordChar();
        }
    }

    private bool _isFormattingMac;

    /// <summary>
    /// Stalker modunda MAC adresini otomatik olarak biçimlendirir.
    /// Caret konumunu koruyarak ':' karakterlerini ekler, paste edilen
    /// düz değerleri formatlar ve büyük harfe çevirir.
    /// Ortak StalkerMacFormatter servisini kullanır.
    /// </summary>
    private void UsernameTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isFormattingMac || sender is not TextBox textBox || _viewModel is null)
            return;

        if (!_viewModel.IsStalker)
            return;

        _isFormattingMac = true;
        try
        {
            var caretIndex = textBox.CaretIndex;
            var raw = textBox.Text ?? string.Empty;

            // Caret öncesindeki hex karakter sayısını hesapla
            var rawHexBeforeCaret = new string(raw[..Math.Min(caretIndex, raw.Length)]
                .Where(c => Uri.IsHexDigit(c)).ToArray());
            var hexCount = Math.Min(rawHexBeforeCaret.Length, 12);

            // Ortak formatter'ı kullanarak MAC değerini biçimlendir
            var formatted = StalkerMacFormatter.Normalize(raw);
            var newCaretIndex = StalkerMacFormatter.CalculateCaretPosition(hexCount, formatted.Length);

            if (textBox.Text != formatted)
            {
                textBox.Text = formatted;
                textBox.CaretIndex = newCaretIndex;
            }
        }
        finally
        {
            _isFormattingMac = false;
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
        // Gizlilik: Pencere kapatıldığında parolayı otomatik gizle.
        if (_viewModel is not null)
        {
            _viewModel.IsPasswordRevealed = false;
        }

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
            var dialogService = ((App)Application.Current!).Services.GetService<IDialogService>();
            if (dialogService != null)
            {
                await dialogService.ShowErrorAsync("Hata", "Avatar seçme penceresi açılamadı.", ex);
            }
        }
    }
}

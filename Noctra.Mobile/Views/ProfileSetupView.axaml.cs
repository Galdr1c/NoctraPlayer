using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Noctra.Services.Interfaces;
using Noctra.ViewModels;

namespace Noctra.Mobile.Views;

public partial class ProfileSetupView : UserControl
{
    private AddProfileViewModel? _viewModel;
    private AvatarPickerViewModel? _avatarPickerViewModel;
    private bool _segmentedControlInitialized;
    private bool _indicatorPositionInitialized;
    private bool _isLoaded;
    private double _lastSegmentedWidth;
    private int _currentSegmentIndex;
    private int _providerSyncVersion;

    public ProfileSetupView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        BindViewModel(DataContext as AddProfileViewModel);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _isLoaded = true;
        InitializeSegmentedControl();
        ScheduleProviderSelectionSync();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        _isLoaded = false;
        base.OnUnloaded(e);
    }

    private void InitializeSegmentedControl()
    {
        if (_segmentedControlInitialized)
        {
            return;
        }

        // Click yalnızca gerçek kullanıcı etkileşiminde çalışır. Binding ile
        // düzenlenen profilin provider'ı yüklenirken Click tetiklenmez; böylece
        // başlangıç seçimi animasyonsuz ve doğru konuma kurulabilir.
        SegmentXtream.Click += OnSegmentClicked;
        SegmentM3U.Click += OnSegmentClicked;
        SegmentStalker.Click += OnSegmentClicked;

        SegmentedControlGrid.LayoutUpdated += OnSegmentedLayoutUpdated;
        SegmentedControlGrid.SizeChanged += OnSegmentedControlSizeChanged;

        _segmentedControlInitialized = true;
        _currentSegmentIndex = GetSelectedSegmentIndex();
    }

    private void OnSegmentedLayoutUpdated(object? sender, EventArgs e)
    {
        if (!TryInitializeIndicatorPosition())
        {
            return;
        }

        SegmentedControlGrid.LayoutUpdated -= OnSegmentedLayoutUpdated;
    }

    private void OnSegmentedControlSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width <= 0)
        {
            return;
        }

        if (!_indicatorPositionInitialized)
        {
            TryInitializeIndicatorPosition();
            return;
        }

        // Provider değişince formun yüksekliği değişebilir. Sadece gerçek bir
        // yatay genişlik değişiminde indicator'ı yeniden hesapla; aksi halde
        // devam eden ilk kayma animasyonu iptal edilmesin.
        if (Math.Abs(e.NewSize.Width - _lastSegmentedWidth) < 0.5)
        {
            return;
        }

        _lastSegmentedWidth = e.NewSize.Width;
        UpdateIndicatorPosition(_currentSegmentIndex, animate: false);
    }

    private void OnSegmentClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { IsChecked: true } selectedSegment)
        {
            return;
        }

        int selectedIndex = GetSegmentIndex(selectedSegment);

        // Çok erken bir dokunuş olursa indicator önce mevcut seçimde kurulup
        // ardından yeni seçime kayar. Böylece ilk seçim de animasyonludur.
        if (!_indicatorPositionInitialized && !TryInitializeIndicatorPosition())
        {
            return;
        }

        if (selectedIndex == _currentSegmentIndex)
        {
            return;
        }

        UpdateIndicatorPosition(selectedIndex, animate: true);
        _currentSegmentIndex = selectedIndex;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) ||
            e.PropertyName is nameof(AddProfileViewModel.IsXtream)
                or nameof(AddProfileViewModel.IsM3U)
                or nameof(AddProfileViewModel.IsStalker))
        {
            ScheduleProviderSelectionSync();
            UpdateUsernamePlaceholder();
        }
    }

    private void UpdateUsernamePlaceholder()
    {
        if (_viewModel is null || UsernameTextBox is null) return;

        if (_viewModel.IsStalker)
        {
            UsernameTextBox.PlaceholderText = "00:1A:79:XX:XX:XX";
            return;
        }

        if (Avalonia.Application.Current is App app && app.Services is not null)
        {
            var loc = app.Services.GetService<ILocalizationService>();
            UsernameTextBox.PlaceholderText = loc?.GetString("Profiles.Account.UsernamePlaceholder") ?? "Username";
        }
    }

    private void ScheduleProviderSelectionSync()
    {
        if (!_isLoaded || !_segmentedControlInitialized)
        {
            return;
        }

        int requestedVersion = ++_providerSyncVersion;

        // IsXtream / IsM3U / IsStalker art arda değişebilir. Background
        // önceliğinde tek sefer okuyarak son ve tutarlı seçimi uygula.
        Dispatcher.UIThread.Post(() =>
        {
            if (!_isLoaded || requestedVersion != _providerSyncVersion)
            {
                return;
            }

            int selectedIndex = GetSelectedSegmentIndex();

            if (!_indicatorPositionInitialized)
            {
                _currentSegmentIndex = selectedIndex;
                TryInitializeIndicatorPosition();
                return;
            }

            if (selectedIndex == _currentSegmentIndex)
            {
                return;
            }

            // Profil düzenleme/veri yükleme kaynaklı provider değişimi.
            // Ekran doğrudan doğru provider seçili halde açılmalı.
            _currentSegmentIndex = selectedIndex;
            UpdateIndicatorPosition(selectedIndex, animate: false);
        }, DispatcherPriority.Background);
    }

    private bool TryInitializeIndicatorPosition()
    {
        if (SegmentedControlGrid.Bounds.Width <= 0 ||
            SegmentIndicator.RenderTransform is not TranslateTransform)
        {
            return false;
        }

        _currentSegmentIndex = GetSelectedSegmentIndex();
        _lastSegmentedWidth = SegmentedControlGrid.Bounds.Width;
        UpdateIndicatorPosition(_currentSegmentIndex, animate: false);
        _indicatorPositionInitialized = true;
        return true;
    }

    private int GetSelectedSegmentIndex()
    {
        if (SegmentStalker.IsChecked == true)
        {
            return 2;
        }

        if (SegmentM3U.IsChecked == true)
        {
            return 1;
        }

        return 0;
    }

    private static int GetSegmentIndex(RadioButton segment)
        => segment.Name switch
        {
            "SegmentM3U" => 1,
            "SegmentStalker" => 2,
            _ => 0
        };

    private void UpdateIndicatorPosition(int selectedIndex, bool animate)
    {
        if (SegmentedControlGrid.Bounds.Width <= 0 ||
            SegmentIndicator.RenderTransform is not TranslateTransform translateTransform)
        {
            return;
        }

        selectedIndex = Math.Clamp(selectedIndex, 0, 2);

        RadioButton selectedSegment = selectedIndex switch
        {
            1 => SegmentM3U,
            2 => SegmentStalker,
            _ => SegmentXtream
        };

        double targetX = selectedSegment.Bounds.Width > 0
            ? selectedSegment.Bounds.X
            : selectedIndex * (SegmentedControlGrid.Bounds.Width / 3.0);

        CornerRadius targetRadius = selectedIndex switch
        {
            0 => new CornerRadius(8, 0, 0, 8),
            1 => new CornerRadius(0),
            2 => new CornerRadius(0, 8, 8, 0),
            _ => new CornerRadius(8, 0, 0, 8)
        };

        if (animate)
        {
            // Aynı TranslateTransform nesnesinin yalnızca X değeri değişir;
            // DoubleTransition ilk kullanıcı seçiminde de düzgün çalışır.
            translateTransform.X = targetX;
            SegmentIndicator.CornerRadius = targetRadius;
            return;
        }

        var transformTransitions = translateTransform.Transitions;
        var indicatorTransitions = SegmentIndicator.Transitions;

        translateTransform.Transitions = null;
        SegmentIndicator.Transitions = null;

        translateTransform.X = targetX;
        SegmentIndicator.CornerRadius = targetRadius;

        // Değerler render ağacına anlık olarak yerleştirildikten sonra
        // transition'ları bir sonraki UI turunda geri aç.
        Dispatcher.UIThread.Post(() =>
        {
            translateTransform.Transitions = transformTransitions;
            SegmentIndicator.Transitions = indicatorTransitions;
        }, DispatcherPriority.Loaded);
    }

    private void BindViewModel(AddProfileViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel))
        {
            ScheduleProviderSelectionSync();
            return;
        }

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _viewModel.RequestAvatarPicker -= ViewModel_RequestAvatarPicker;
            _viewModel.ValidationErrorOccurred -= ViewModel_ValidationErrorOccurred;
        }

        _viewModel = viewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            _viewModel.RequestAvatarPicker += ViewModel_RequestAvatarPicker;
            _viewModel.ValidationErrorOccurred += ViewModel_ValidationErrorOccurred;
        }

        ScheduleProviderSelectionSync();
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

    private bool _isFormattingMac;

    /// <summary>
    /// Stalker modunda MAC adresini otomatik olarak biçimlendirir.
    /// Caret konumunu koruyarak ':' karakterlerini ekler, paste edilen
    /// düz değerleri (001A79ABCDEF) formatlar ve büyük harfe çevirir.
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

            // Sadece hex karakterleri al
            var hexOnly = new string(raw.Where(c => Uri.IsHexDigit(c)).ToArray())
                .ToUpperInvariant();

            // Maksimum 12 hex karakter (6 byte)
            if (hexOnly.Length > 12)
                hexOnly = hexOnly[..12];

            // ':' ile biçimlendir: XX:XX:XX:XX:XX:XX
            var formatted = FormatMacWithColons(hexOnly);

            // Caret konumunu hesapla: Formatting öncesi hex-only
            // karakter sayısına göre pozisyon belirle
            var rawHexBeforeCaret = new string(raw[..Math.Min(caretIndex, raw.Length)]
                .Where(c => Uri.IsHexDigit(c)).ToArray());
            var hexCount = Math.Min(rawHexBeforeCaret.Length, 12);

            // Yeni caret konumu: hexCount karaktere kadar ':' ekle
            var newCaretIndex = Math.Min(hexCount + (hexCount / 2), formatted.Length);

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

    private static string FormatMacWithColons(string hex)
    {
        if (string.IsNullOrEmpty(hex))
            return string.Empty;

        var parts = new List<string>();
        for (int i = 0; i < hex.Length; i += 2)
        {
            var len = Math.Min(2, hex.Length - i);
            parts.Add(hex.Substring(i, len));
        }
        return string.Join(":", parts);
    }

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
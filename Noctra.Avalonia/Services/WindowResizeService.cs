using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Noctra.Avalonia.Services;

/// <summary>
/// Handles 8-way proportional window resizing logic for PiP mode.
/// </summary>
public class WindowResizeService
{
    private readonly Window _window;
    
    private bool _isResizing;
    private string? _resizeCorner;
    private Point _resizeStartPoint;
    private Rect _resizeStartBounds;

    public WindowResizeService(Window window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
    }

    public void BeginResize(object? sender, PointerPressedEventArgs e)
    {
        var border = sender as Border;
        if (border == null) return;
        
        // Pencereyi öne getir ve odağı al (Gecikmeyi önlemek için)
        _window.Activate();
        _window.Focus();
        
        _isResizing = true;
        _resizeCorner = border.Tag?.ToString();
        
        // Global (Screen) koordinatları kullanmak titremeyi engeller
        var visualRoot = _window as TopLevel;
        if (visualRoot == null) return;
        
        _resizeStartPoint = visualRoot.PointToScreen(e.GetPosition(_window)).ToPoint(1.0);
        _resizeStartBounds = new Rect(_window.Position.X, _window.Position.Y, _window.Width, _window.Height);
        
        e.Pointer.Capture(border);
        e.Handled = true;
    }

    public void UpdateResize(PointerEventArgs e)
    {
        if (!_isResizing) return;
        
        var visualRoot = _window as TopLevel;
        if (visualRoot == null) return;
        
        var currentPoint = visualRoot.PointToScreen(e.GetPosition(_window)).ToPoint(1.0);
        var deltaX = currentPoint.X - _resizeStartPoint.X;
        var deltaY = currentPoint.Y - _resizeStartPoint.Y;
        
        const double aspectRatio = 16.0 / 9.0;
        
        double newWidth = _resizeStartBounds.Width;
        double newHeight = _resizeStartBounds.Height;
        double newX = _resizeStartBounds.X;
        double newY = _resizeStartBounds.Y;

        switch (_resizeCorner)
        {
            case "BottomRight":
            case "Right":
                newWidth = Math.Max(240, _resizeStartBounds.Width + deltaX);
                newHeight = newWidth / aspectRatio;
                break;
            case "Bottom":
                newHeight = Math.Max(135, _resizeStartBounds.Height + deltaY);
                newWidth = newHeight * aspectRatio;
                break;
            case "BottomLeft":
            case "Left":
                newWidth = Math.Max(240, _resizeStartBounds.Width - deltaX);
                newHeight = newWidth / aspectRatio;
                newX = _resizeStartBounds.Right - newWidth;
                break;
            case "TopRight":
                newWidth = Math.Max(240, _resizeStartBounds.Width + deltaX);
                newHeight = newWidth / aspectRatio;
                newY = _resizeStartBounds.Bottom - newHeight;
                break;
            case "Top":
                newHeight = Math.Max(135, _resizeStartBounds.Height - deltaY);
                newWidth = newHeight * aspectRatio;
                newY = _resizeStartBounds.Bottom - newHeight;
                break;
            case "TopLeft":
                newWidth = Math.Max(240, _resizeStartBounds.Width - deltaX);
                newHeight = newWidth / aspectRatio;
                newX = _resizeStartBounds.Right - newWidth;
                newY = _resizeStartBounds.Bottom - newHeight;
                break;
        }

        // Titremeyi önlemek için pixel snap ve casting optimizasyonu
        int finalWidth = (int)Math.Round(newWidth);
        int finalHeight = (int)Math.Round(newHeight);
        int finalX = (int)Math.Round(newX);
        int finalY = (int)Math.Round(newY);

        bool sizeChanged = (int)_window.Width != finalWidth || (int)_window.Height != finalHeight;
        bool posChanged = _window.Position.X != finalX || _window.Position.Y != finalY;

        if (sizeChanged)
        {
            _window.Width = finalWidth;
            _window.Height = finalHeight;
        }

        if (posChanged)
        {
            _window.Position = new PixelPoint(finalX, finalY);
        }
        
        e.Handled = true;
    }

    public void EndResize(PointerReleasedEventArgs e)
    {
        if (_isResizing)
        {
            _isResizing = false;
            e.Pointer.Capture(null);
        }
    }
}

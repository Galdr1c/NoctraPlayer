using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Noctra.Mobile.Behaviors;

public sealed class MobileTextBoxMenuBehavior
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<MobileTextBoxMenuBehavior, TextBox, bool>("IsEnabled");

    static MobileTextBoxMenuBehavior()
    {
        IsEnabledProperty.Changed.AddClassHandler<TextBox>(OnIsEnabledChanged);
    }

    public static bool GetIsEnabled(AvaloniaObject element)
        => element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(AvaloniaObject element, bool value)
        => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(TextBox textBox, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            textBox.ContextMenu = CreateMenu(textBox);
        }
        else if (ReferenceEquals(textBox.ContextMenu?.Tag, typeof(MobileTextBoxMenuBehavior)))
        {
            textBox.ContextMenu = null;
        }
    }

    private static ContextMenu CreateMenu(TextBox textBox)
    {
        var menu = new ContextMenu
        {
            Tag = typeof(MobileTextBoxMenuBehavior),
            ItemsSource = new[]
            {
                CreateItem("Cut", (_, _) => textBox.Cut()),
                CreateItem("Copy", (_, _) => textBox.Copy()),
                CreateItem("Paste", (_, _) => textBox.Paste()),
                CreateItem("Select All", (_, _) => textBox.SelectAll())
            }
        };

        menu.Opening += (_, _) =>
        {
            foreach (var item in menu.Items.OfType<MenuItem>())
            {
                item.IsEnabled = textBox.IsEnabled && !textBox.IsReadOnly;
            }
        };

        return menu;
    }

    private static MenuItem CreateItem(string header, EventHandler<RoutedEventArgs> click)
    {
        var item = new MenuItem { Header = header };
        item.Click += click;
        return item;
    }
}

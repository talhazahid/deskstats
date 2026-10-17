using System.Windows;
using System.Windows.Controls;

namespace SysDash.App;

/// <summary>
/// WPF allows an element to have exactly one logical parent. When a widget view
/// is re-homed (sidebar ⇄ floating window) the old parent must let go first, or
/// WPF throws "Specified element is already the logical child of another element".
/// Hosts call <see cref="Release"/> right before adopting a view so re-homing can
/// never fail, whatever the previous owner was.
/// </summary>
internal static class ViewParenting
{
    public static void Release(FrameworkElement? view)
    {
        if (view is null)
        {
            return;
        }

        switch (view.Parent)
        {
            case Decorator decorator:
                decorator.Child = null;
                break;
            case Panel panel:
                panel.Children.Remove(view);
                break;
            case ContentControl contentControl:
                contentControl.Content = null;
                break;
            case ContentPresenter presenter:
                presenter.Content = null;
                break;
            case ItemsControl items:
                items.Items.Remove(view);
                break;
        }
    }
}

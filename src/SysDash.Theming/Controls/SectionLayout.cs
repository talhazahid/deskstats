using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SysDash.Theming.Controls;

/// <summary>
/// Vertical stack used inside a widget section. Normally it behaves exactly like
/// a StackPanel. When <see cref="ResponsiveColumns"/> makes the section taller so
/// its column lines up with its neighbours, the section TITLE stays pinned at the
/// top and the rest of the content (rings, rows…) is centred vertically in the
/// extra room — so a stretched card looks intentional, not half-empty.
///
/// The first child counts as the title when it is (or directly contains) a
/// TextBlock using the "SectionHeader" style. Sections without a title centre
/// all of their content.
/// </summary>
public sealed class SectionLayout : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        double width = 0;
        double height = 0;
        var childAvailable = new Size(availableSize.Width, double.PositiveInfinity);

        foreach (UIElement child in InternalChildren)
        {
            child.Measure(childAvailable);
            width = Math.Max(width, child.DesiredSize.Width);
            height += child.DesiredSize.Height;
        }

        return new Size(double.IsInfinity(availableSize.Width) ? width : Math.Min(width, availableSize.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        UIElement? header = null;
        double headerHeight = 0;
        double bodyHeight = 0;

        foreach (UIElement child in InternalChildren)
        {
            if (header is null && headerHeight == 0 && bodyHeight == 0 && IsHeader(child))
            {
                header = child;
                headerHeight = child.DesiredSize.Height;
                continue;
            }

            bodyHeight += child.DesiredSize.Height;
        }

        // Title at the top; the body centred in whatever space is left.
        double spare = Math.Max(0, finalSize.Height - headerHeight - bodyHeight);
        double y = 0;

        foreach (UIElement child in InternalChildren)
        {
            double h = child.DesiredSize.Height;
            if (ReferenceEquals(child, header))
            {
                child.Arrange(new Rect(0, 0, finalSize.Width, h));
                y = h + spare / 2;
                continue;
            }

            if (header is null && y == 0)
            {
                y = spare / 2;
            }

            child.Arrange(new Rect(0, y, finalSize.Width, h));
            y += h;
        }

        return finalSize;
    }

    private bool IsHeader(UIElement element)
    {
        object? headerStyle = TryFindResource("SectionHeader");
        if (headerStyle is null)
        {
            return false;
        }

        // The title itself, or a row (Grid / StackPanel) whose first descendants hold it.
        return Find(element, headerStyle, depth: 0);
    }

    private static bool Find(DependencyObject node, object headerStyle, int depth)
    {
        if (node is TextBlock text && ReferenceEquals(text.Style, headerStyle))
        {
            return true;
        }

        if (depth >= 3 || node is not Panel panel)
        {
            return false;
        }

        foreach (UIElement child in panel.Children)
        {
            if (Find(child, headerStyle, depth + 1))
            {
                return true;
            }
        }

        return false;
    }
}

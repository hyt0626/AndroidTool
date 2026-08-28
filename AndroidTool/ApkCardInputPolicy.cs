using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace AndroidTool;

public static class ApkCardInputPolicy
{
    public static bool ShouldToggle(int clickCount, DependencyObject? source, DependencyObject card)
    {
        if (clickCount != 1 || source is null) return false;

        for (var current = source; current is not null; current = GetParent(current))
        {
            if (ReferenceEquals(current, card)) return true;
            if (current is ButtonBase) return false;
        }
        return false;
    }

    private static DependencyObject? GetParent(DependencyObject element) =>
        element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
}

using System.Windows;
using System.Windows.Media;

namespace Sifra.Desktop;

internal static class VisualTreeExtensions
{
    /// <summary>Walks up the visual tree from this element looking for the first ancestor (or itself) whose DataContext is a T — e.g. finding which list row a context-menu click landed on.</summary>
    public static T? FindAncestorDataContext<T>(this DependencyObject? element) where T : class
    {
        var current = element;
        while (current is not null)
        {
            if (current is FrameworkElement { DataContext: T match })
            {
                return match;
            }
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}

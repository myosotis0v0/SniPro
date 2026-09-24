using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SniPro.App;

internal static class UiMotion
{
    public static void Reveal(FrameworkElement element, int delayMilliseconds = 0)
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            element.Opacity = 1;
            element.RenderTransform = Transform.Identity;
            return;
        }

        var offset = new TranslateTransform(0, 12);
        element.RenderTransform = offset;
        element.Opacity = 0;

        var delay = TimeSpan.FromMilliseconds(delayMilliseconds);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        element.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240))
            {
                BeginTime = delay,
                EasingFunction = easing
            });
        offset.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(320))
            {
                BeginTime = delay,
                EasingFunction = easing
            });
    }

    public static void FadeIn(TextBlock text)
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            text.Opacity = 1;
            return;
        }

        text.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(200)));
    }
}

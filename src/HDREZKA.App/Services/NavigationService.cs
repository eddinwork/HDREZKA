using Microsoft.UI.Xaml.Controls;

namespace HDREZKA.App.Services;

public static class Nav
{
    public static Frame? Frame { get; private set; }

    public static event Action<Type, object?>? Navigated;

    public static void SetFrame(Frame frame) => Frame = frame;

    public static void Go<T>(object? parameter = null) where T : Page
    {
        if (Frame == null) return;
        Frame.Navigate(typeof(T), parameter);
        Navigated?.Invoke(typeof(T), parameter);
    }
}

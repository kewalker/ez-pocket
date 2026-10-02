using System.Runtime.CompilerServices;
using Microsoft.Maui.Platforms.Linux.Gtk4.Handlers;

namespace EzPocket;

internal static class LinuxTextStyles
{
    private static readonly ConditionalWeakTable<Gtk.Widget, Gtk.CssProvider> ColorProviders = new();

    public static void Register()
    {
        LabelHandler.Mapper.AppendToMapping(nameof(ILabel.TextColor),
            (handler, label) => ApplyColor(handler.PlatformView, label.TextColor));
        ButtonHandler.Mapper.AppendToMapping(nameof(ITextButton.Text),
            (handler, button) => ApplyButtonStyles(handler.PlatformView, button as Button));
        ButtonHandler.Mapper.AppendToMapping(nameof(ITextButton.TextColor),
            (handler, button) => ApplyButtonStyles(handler.PlatformView, button as Button));
        ButtonHandler.Mapper.AppendToMapping(nameof(IView.Background),
            (handler, button) => ApplyButtonStyles(handler.PlatformView, button as Button));
    }

    private static void ApplyButtonStyles(Gtk.Button? widget, Button? button)
    {
        if (widget is null || button is null) return;

        ApplyColor(widget, button.TextColor);
        if (widget.GetChild() is Gtk.Widget label)
            ApplyColor(label, button.TextColor);

        if (button.BackgroundColor is Color background)
        {
            Gtk.CssProvider provider = GetProvider(widget);
            provider.LoadFromString($"* {{ color: {ToRgb(button.TextColor)}; background-color: {ToRgb(background)}; background-image: none; }}");
        }
    }

    private static void ApplyColor(Gtk.Widget? widget, Color? color)
    {
        if (widget is null) return;

        Gtk.CssProvider provider = GetProvider(widget);

        if (color is null)
        {
            provider.LoadFromString("");
            return;
        }

        provider.LoadFromString($"* {{ color: {ToRgb(color)}; }}");
    }

    private static Gtk.CssProvider GetProvider(Gtk.Widget widget) =>
        ColorProviders.GetValue(widget, static view =>
        {
            var css = Gtk.CssProvider.New();
            view.GetStyleContext().AddProvider(css, Gtk.Constants.STYLE_PROVIDER_PRIORITY_APPLICATION + 1);
            return css;
        });

    private static string ToRgb(Color color)
    {
        int red = (int)Math.Round(color.Red * 255);
        int green = (int)Math.Round(color.Green * 255);
        int blue = (int)Math.Round(color.Blue * 255);
        return $"rgb({red}, {green}, {blue})";
    }
}

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
        ButtonHandler.Mapper.AppendToMapping(nameof(Button.TextColor),
            (handler, button) => ApplyColor(handler.PlatformView, (button as Button)?.TextColor));
    }

    private static void ApplyColor(Gtk.Widget? widget, Color? color)
    {
        if (widget is null) return;

        Gtk.CssProvider provider = ColorProviders.GetValue(widget, static view =>
        {
            var css = Gtk.CssProvider.New();
            view.GetStyleContext().AddProvider(css, Gtk.Constants.STYLE_PROVIDER_PRIORITY_APPLICATION + 1);
            return css;
        });

        if (color is null)
        {
            provider.LoadFromString("");
            return;
        }

        int red = (int)Math.Round(color.Red * 255);
        int green = (int)Math.Round(color.Green * 255);
        int blue = (int)Math.Round(color.Blue * 255);
        provider.LoadFromString($"* {{ color: rgb({red}, {green}, {blue}); }}");
    }
}

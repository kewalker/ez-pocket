namespace EzPocket.Services;

public interface IColorPickerService
{
    Task<uint?> PickAsync(uint initialColor, Action<uint> preview);
}

/// <summary>Native MAUI controls for platforms without the Windows spectrum picker.</summary>
public sealed class MauiColorPickerService : IColorPickerService
{
    public async Task<uint?> PickAsync(uint initialColor, Action<uint> preview)
    {
        var navigation = Application.Current?.Windows.FirstOrDefault()?.Page?.Navigation
            ?? throw new InvalidOperationException("The app window is not ready.");
        var completion = new TaskCompletionSource<uint?>();
        uint selected = initialColor;
        var sample = new BoxView { HeightRequest = 90, Color = Color.FromArgb($"#{initialColor:X6}") };
        var value = new Label { Text = $"#{initialColor:X6}", FontSize = 20, TextColor = Colors.Black };
        var content = new VerticalStackLayout { Padding = 24, Spacing = 14 };
        content.Children.Add(new Label { Text = "Choose color", FontSize = 24, TextColor = Colors.Black });
        content.Children.Add(sample);
        content.Children.Add(value);
        var sliders = new Slider[3];
        for (int i = 0; i < 3; i++)
        {
            content.Children.Add(new Label { Text = new[] { "RED", "GREEN", "BLUE" }[i], TextColor = Colors.Black });
            sliders[i] = new Slider { Minimum = 0, Maximum = 255, Value = (initialColor >> (16 - i * 8)) & 255 };
            content.Children.Add(sliders[i]);
        }
        foreach (var slider in sliders)
            slider.ValueChanged += (_, _) =>
            {
                selected = (uint)((int)Math.Round(sliders[0].Value) << 16 | (int)Math.Round(sliders[1].Value) << 8 | (int)Math.Round(sliders[2].Value));
                value.Text = $"#{selected:X6}";
                sample.Color = Color.FromArgb(value.Text);
                preview(selected);
            };
        var apply = new Controls.EzButton { Text = "USE COLOR" };
        var cancel = new Controls.EzButton { Text = "CANCEL", ButtonBackgroundColor = Colors.White, ButtonTextColor = Colors.Black };
        content.Children.Add(apply);
        content.Children.Add(cancel);
        var page = new ContentPage { Content = new ScrollView { Content = content }, BackgroundColor = Colors.White, Title = "Choose color" };
        async Task Finish(uint? result)
        {
            if (!apply.IsEnabled) return;
            apply.IsEnabled = cancel.IsEnabled = false;
            try { await navigation.PopModalAsync(); }
            finally { completion.TrySetResult(result); }
        }
        apply.Clicked += async (_, _) => await Finish(selected);
        cancel.Clicked += async (_, _) => await Finish(null);
        page.Disappearing += (_, _) => { if (apply.IsEnabled) completion.TrySetResult(null); };
        await navigation.PushModalAsync(page);
        return await completion.Task;
    }
}

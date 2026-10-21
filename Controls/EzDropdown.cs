using System.Collections;
using Microsoft.Maui.Controls.Shapes;

namespace EzPocket.Controls;

// Keeps the native picker responsible for its menu, keyboard input, and accessibility.
[ContentProperty(nameof(Items))]
public sealed class EzDropdown : ContentView
{
    private readonly Picker picker;
    private readonly Border frame;
    private bool pointerInside;

    public static readonly BindableProperty TitleProperty = BindableProperty.Create(nameof(Title), typeof(string), typeof(EzDropdown), default(string));
    public static readonly BindableProperty ItemsSourceProperty = BindableProperty.Create(nameof(ItemsSource), typeof(IList), typeof(EzDropdown), default(IList));
    public static readonly BindableProperty SelectedIndexProperty = BindableProperty.Create(nameof(SelectedIndex), typeof(int), typeof(EzDropdown), -1, BindingMode.TwoWay);
    public static readonly BindableProperty SelectedItemProperty = BindableProperty.Create(nameof(SelectedItem), typeof(object), typeof(EzDropdown), null, BindingMode.TwoWay);

    public string? Title { get => (string?)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public IList? ItemsSource { get => (IList?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public int SelectedIndex { get => (int)GetValue(SelectedIndexProperty); set => SetValue(SelectedIndexProperty, value); }
    public object? SelectedItem { get => GetValue(SelectedItemProperty); set => SetValue(SelectedItemProperty, value); }
    public IList<string> Items => picker.Items;
    public BindingBase ItemDisplayBinding { get => picker.ItemDisplayBinding; set => picker.ItemDisplayBinding = value; }
    public event EventHandler? SelectedIndexChanged;

    public EzDropdown()
    {
        picker = new Picker
        {
            TextColor = Color.FromArgb("#151515"), TitleColor = Color.FromArgb("#60605C"),
            BackgroundColor = Colors.Transparent, FontSize = 14, HeightRequest = 46,
            HorizontalOptions = LayoutOptions.Fill
        };
        frame = new Border
        {
            BackgroundColor = Colors.White, Stroke = Color.FromArgb("#151515"),
            StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = 2 },
            Padding = new Thickness(10, 0), Content = picker
        };
        Content = frame;
        picker.SetBinding(Picker.TitleProperty, new Binding(nameof(Title), source: this));
        picker.SetBinding(Picker.ItemsSourceProperty, new Binding(nameof(ItemsSource), source: this));
        picker.SetBinding(Picker.SelectedIndexProperty, new Binding(nameof(SelectedIndex), BindingMode.TwoWay, source: this));
        picker.SetBinding(Picker.SelectedItemProperty, new Binding(nameof(SelectedItem), BindingMode.TwoWay, source: this));
        picker.SelectedIndexChanged += (_, e) =>
        {
            // Both values must be current before a page's selection handler runs.
            SelectedIndex = picker.SelectedIndex;
            SelectedItem = picker.SelectedItem;
            SelectedIndexChanged?.Invoke(this, e);
        };
        picker.Focused += (_, _) => UpdateFeedback();
        picker.Unfocused += (_, _) => UpdateFeedback();
        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (_, _) => { pointerInside = true; UpdateFeedback(); };
        pointer.PointerExited += (_, _) => { pointerInside = false; UpdateFeedback(); };
        frame.GestureRecognizers.Add(pointer);
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsEnabled)) UpdateFeedback();
            if (e.PropertyName == SemanticProperties.DescriptionProperty.PropertyName)
                SemanticProperties.SetDescription(picker, SemanticProperties.GetDescription(this));
        };
    }

    private void UpdateFeedback()
    {
        frame.Stroke = Color.FromArgb(!IsEnabled ? "#C9C9C2" : picker.IsFocused ? "#52627A" : "#151515");
        frame.BackgroundColor = Color.FromArgb(IsEnabled && pointerInside ? "#F4F4F0" : "#FFFFFF");
        frame.StrokeThickness = IsEnabled && picker.IsFocused ? 2 : 1;
        frame.Opacity = IsEnabled ? 1 : 0.55;
    }
}
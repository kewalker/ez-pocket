namespace EzPocket.Controls;

public partial class EzButton : ContentView
{
    public static readonly BindableProperty TextProperty = BindableProperty.Create(nameof(Text), typeof(string), typeof(EzButton), string.Empty);
    public static readonly BindableProperty FontAttributesProperty = BindableProperty.Create(nameof(FontAttributes), typeof(FontAttributes), typeof(EzButton), FontAttributes.None);
    public static readonly BindableProperty CornerRadiusProperty = BindableProperty.Create(nameof(CornerRadius), typeof(int), typeof(EzButton), 6);

    public event EventHandler? Clicked;

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public FontAttributes FontAttributes
    {
        get => (FontAttributes)GetValue(FontAttributesProperty);
        set => SetValue(FontAttributesProperty, value);
    }

    public int CornerRadius
    {
        get => (int)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public EzButton()
    {
        InitializeComponent();
    }

    private void OnNativeClicked(object? sender, EventArgs e) => Clicked?.Invoke(this, e);

    private void OnPointerEntered(object? sender, PointerEventArgs e) => SetFeedback(1.02, 0.9);

    private void OnPointerExited(object? sender, PointerEventArgs e) => SetFeedback(1, 1);

    private void OnNativePressed(object? sender, EventArgs e) => SetFeedback(0.96, 0.75);

    private void OnNativeReleased(object? sender, EventArgs e) => SetFeedback(1.02, 0.9);

    private void OnNativeFocused(object? sender, FocusEventArgs e)
    {
        NativeButton.BorderColor = Colors.Gray;
        NativeButton.BorderWidth = 2;
    }

    private void OnNativeUnfocused(object? sender, FocusEventArgs e)
    {
        NativeButton.BorderColor = Colors.Transparent;
        NativeButton.BorderWidth = 0;
    }

    private void SetFeedback(double scale, double opacity)
    {
        NativeButton.Scale = scale;
        NativeButton.Opacity = opacity;
    }
}

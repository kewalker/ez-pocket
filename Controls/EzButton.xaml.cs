namespace EzPocket.Controls;

public partial class EzButton : ContentView
{
    public static readonly BindableProperty TextProperty = BindableProperty.Create(nameof(Text), typeof(string), typeof(EzButton), string.Empty);
    public static readonly BindableProperty FontSizeProperty = BindableProperty.Create(nameof(FontSize), typeof(double), typeof(EzButton), 14d);
    public static readonly BindableProperty FontAttributesProperty = BindableProperty.Create(nameof(FontAttributes), typeof(FontAttributes), typeof(EzButton), FontAttributes.None);
    public static readonly BindableProperty CornerRadiusProperty = BindableProperty.Create(nameof(CornerRadius), typeof(int), typeof(EzButton), 6);
    public static readonly BindableProperty ButtonPaddingProperty = BindableProperty.Create(nameof(ButtonPadding), typeof(Thickness), typeof(EzButton), new Thickness(18, 10));
    public static readonly BindableProperty MinimumHeightProperty = BindableProperty.Create(nameof(MinimumHeight), typeof(double), typeof(EzButton), 44d);
    public static readonly BindableProperty ButtonBackgroundColorProperty = BindableProperty.Create(nameof(ButtonBackgroundColor), typeof(Color), typeof(EzButton), Color.FromArgb("#2563EB"));
    public static readonly BindableProperty ButtonTextColorProperty = BindableProperty.Create(nameof(ButtonTextColor), typeof(Color), typeof(EzButton), Colors.White);

    public event EventHandler? Clicked;

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
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

    public Thickness ButtonPadding
    {
        get => (Thickness)GetValue(ButtonPaddingProperty);
        set => SetValue(ButtonPaddingProperty, value);
    }

    public double MinimumHeight
    {
        get => (double)GetValue(MinimumHeightProperty);
        set => SetValue(MinimumHeightProperty, value);
    }

    public Color ButtonBackgroundColor
    {
        get => (Color)GetValue(ButtonBackgroundColorProperty);
        set => SetValue(ButtonBackgroundColorProperty, value);
    }

    public Color ButtonTextColor
    {
        get => (Color)GetValue(ButtonTextColorProperty);
        set => SetValue(ButtonTextColorProperty, value);
    }

    public EzButton()
    {
        InitializeComponent();
    }

    private void OnNativeClicked(object? sender, EventArgs e) => Clicked?.Invoke(this, e);

    private void OnPointerEntered(object? sender, PointerEventArgs e) => SetFeedback(1.02, 1);

    private void OnPointerExited(object? sender, PointerEventArgs e) => SetFeedback(1, 1);

    private void OnNativePressed(object? sender, EventArgs e) => SetFeedback(0.96, 0.75);

    private void OnNativeReleased(object? sender, EventArgs e) => SetFeedback(1.02, 1);

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

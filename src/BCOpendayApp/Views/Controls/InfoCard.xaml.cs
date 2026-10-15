namespace BCOpendayApp.Views.Controls;

public partial class InfoCard : ContentView
{
    public static readonly BindableProperty HeadingProperty =
        BindableProperty.Create(nameof(Heading), typeof(string), typeof(InfoCard), string.Empty);

    public static readonly BindableProperty BodyProperty =
        BindableProperty.Create(nameof(Body), typeof(string), typeof(InfoCard), string.Empty);

    public static readonly BindableProperty HeadingColorProperty =
        BindableProperty.Create(nameof(HeadingColor), typeof(Color), typeof(InfoCard), Colors.White);

    public InfoCard()
    {
        InitializeComponent();
    }

    public string Heading
    {
        get => (string)GetValue(HeadingProperty);
        set => SetValue(HeadingProperty, value);
    }

    public string Body
    {
        get => (string)GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }

    public Color HeadingColor
    {
        get => (Color)GetValue(HeadingColorProperty);
        set => SetValue(HeadingColorProperty, value);
    }
}

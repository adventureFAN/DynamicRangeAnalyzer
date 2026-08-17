using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DRAnalyzer.App;

public partial class FontSettingsWindow : Window
{
    private const double DipPerPoint =
        96.0 / 72.0;

    private bool _isInitialized;

    public string SelectedFontFamilyName { get; private set; } =
        SystemFonts.MessageFontFamily.Source;

    public double SelectedFontSizeDip { get; private set; } =
        SystemFonts.MessageFontSize;

    public FontSettingsWindow(
        FontFamily currentFontFamily,
        double currentFontSizeDip)
    {
        InitializeComponent();

        FontFamily =
            currentFontFamily;

        FontSize =
            currentFontSizeDip;

        var fontNames =
            Fonts.SystemFontFamilies
                .Select(
                    family =>
                        family.Source)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(
                    name =>
                        name,
                    StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

        FontFamilyComboBox.ItemsSource =
            fontNames;

        var currentFontName =
            fontNames.FirstOrDefault(
                name =>
                    string.Equals(
                        name,
                        currentFontFamily.Source,
                        StringComparison.OrdinalIgnoreCase));

        FontFamilyComboBox.SelectedItem =
            currentFontName ??
            fontNames.FirstOrDefault();

        var pointSizes =
            Enumerable
                .Range(
                    9,
                    10)
                .ToArray();

        FontSizeComboBox.ItemsSource =
            pointSizes;

        var currentPointSize =
            Math.Clamp(
                (int)Math.Round(
                    currentFontSizeDip /
                    DipPerPoint),
                9,
                18);

        FontSizeComboBox.SelectedItem =
            currentPointSize;

        _isInitialized = true;

        UpdatePreview();
    }

    private void FontSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_isInitialized)
            return;

        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (FontFamilyComboBox.SelectedItem is not string fontFamilyName ||
            FontSizeComboBox.SelectedItem is not int pointSize)
        {
            return;
        }

        PreviewText.FontFamily =
            new FontFamily(
                fontFamilyName);

        PreviewText.FontSize =
            pointSize *
            DipPerPoint;
    }

    private void OkButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (FontFamilyComboBox.SelectedItem is not string fontFamilyName ||
            FontSizeComboBox.SelectedItem is not int pointSize)
        {
            return;
        }

        SelectedFontFamilyName =
            fontFamilyName;

        SelectedFontSizeDip =
            pointSize *
            DipPerPoint;

        DialogResult = true;
    }
}

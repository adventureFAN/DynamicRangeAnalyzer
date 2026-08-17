using System.Reflection;
using System.Windows;

namespace DRAnalyzer.App;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        var version =
            Assembly
                .GetExecutingAssembly()
                .GetName()
                .Version;

        VersionText.Text =
            version is null
                ? "Version unknown"
                : $"Version {version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
    }

    private void OkButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }
}

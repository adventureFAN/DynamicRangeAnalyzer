using System.Threading;
using System.Windows;
using System.Windows.Threading;
using DRAnalyzer.App.Settings;

namespace DRAnalyzer.App;

public partial class App : Application
{
    private int _fatalErrorShown;

    public App()
    {
        DispatcherUnhandledException +=
            OnDispatcherUnhandledException;

        AppDomain.CurrentDomain.UnhandledException +=
            OnAppDomainUnhandledException;
    }

    public AppThemePreference ThemePreference { get; private set; } =
        AppThemePreference.System;

    protected override void OnStartup(
        StartupEventArgs e)
    {
        ApplyTheme(
            AppThemeSettings.Load(),
            save: false);

        base.OnStartup(e);
    }

    public void SetThemePreference(
        AppThemePreference preference)
    {
        ApplyTheme(
            preference,
            save: true);
    }

    private void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        ShowFatalError(e.Exception);

        // After an unknown exception, the internal application state
        // can no longer be trusted. Do not continue running.
        e.Handled = true;
        Shutdown(-1);
    }

    private void OnAppDomainUnhandledException(
        object? sender,
        UnhandledExceptionEventArgs e)
    {
        var exception =
            e.ExceptionObject as Exception
            ?? new InvalidOperationException(
                "An unknown fatal error occurred.");

        ShowFatalError(exception);
    }

    private void ShowFatalError(
        Exception exception)
    {
        if (Interlocked.Exchange(
                ref _fatalErrorShown,
                1) != 0)
        {
            return;
        }

        var message =
            "An unexpected error occurred and the application " +
            "must close.\n\n" +
            UserFacingErrorFormatter.ForFatal(exception);

        try
        {
            MessageBox.Show(
                message,
                "Dynamic Range Analyzer - Fatal Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // The last-resort fallback must not trigger another
            // unhandled exception.
        }
    }

#pragma warning disable WPF0001
    private void ApplyTheme(
        AppThemePreference preference,
        bool save)
    {
        ThemeMode =
            preference switch
            {
                AppThemePreference.Light =>
                    System.Windows.ThemeMode.Light,

                AppThemePreference.Dark =>
                    System.Windows.ThemeMode.Dark,

                _ =>
                    System.Windows.ThemeMode.System
            };

        ThemePreference =
            preference;

        if (save)
        {
            AppThemeSettings.Save(
                preference);
        }
    }
#pragma warning restore WPF0001
}

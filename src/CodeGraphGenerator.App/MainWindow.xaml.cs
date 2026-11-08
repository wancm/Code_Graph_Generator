using System.Windows;
using CodeGraphGenerator.App.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CodeGraphGenerator.App;

/// <summary>The one window: a BlazorWebView showing the Blazor pages.</summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        var services = new ServiceCollection();
        services.AddWpfBlazorWebView();
#if DEBUG
        services.AddBlazorWebViewDeveloperTools();
#endif
        services.AddSingleton(new DesktopDialogs(this));
        services.AddSingleton<SettingsStore>();
        services.AddSingleton<GenerationService>();
        Resources.Add("services", services.BuildServiceProvider());

        InitializeComponent();
    }
}

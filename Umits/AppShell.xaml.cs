using System.ComponentModel;

namespace Umits;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute(nameof(SettingsPage), typeof(SettingsPage));
        Routing.RegisterRoute(nameof(InfoPage), typeof(InfoPage));
        
        Routing.RegisterRoute(nameof(LicensesPage), typeof(LicensesPage));
    }
}
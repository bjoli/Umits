
namespace Umits;

public partial class LicensesPage : ContentPage
{
    public LicensesPage()
    {
        InitializeComponent();
    }
    
    private async void OnBackButtonClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
}
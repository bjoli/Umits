namespace Umits;

public partial class InfoPage : ContentPage
{

    public InfoPage()
    {
        InitializeComponent();
    }

    private async void OnBackButtonClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
}
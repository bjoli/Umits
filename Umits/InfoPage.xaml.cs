namespace Umits;

public partial class InfoPage : ContentPage
{

    public InfoPage()
    {
        InitializeComponent();
    }

    private async void OnBackButtonClicked(object? sender, EventArgs e)
    {
        try
        {
            await Shell.Current.GoToAsync("..");
        } 
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }
}
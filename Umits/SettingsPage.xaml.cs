namespace Umits;

public partial class SettingsPage : ContentPage
{
    private const double CollapsedHeight = 100;
    private const double ExpandedHeight = 800;
    private readonly string _entitiesFilePath = Path.Combine(FileSystem.AppDataDirectory, "user_entities.txt");

    private readonly string _macrosFilePath = Path.Combine(FileSystem.AppDataDirectory, "user_macros.txt");

    public SettingsPage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
       
        try
        {
            base.OnAppearing();
            // Load Number Format
            NumberFormatEntry.Text = Preferences.Default.Get("NumberFormat", "G7");

            // Load Macros
            if (File.Exists(_macrosFilePath)) MacrosEditor.Text = await File.ReadAllTextAsync(_macrosFilePath);

            // Load Entities
            if (File.Exists(_entitiesFilePath)) EntitiesEditor.Text = await File.ReadAllTextAsync(_entitiesFilePath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading data: {ex.Message}");
        }
    }

    protected override async void OnDisappearing()
    {
        try
        {
            base.OnDisappearing();

            // Save Number Format
            Preferences.Default.Set("NumberFormat", string.IsNullOrWhiteSpace(NumberFormatEntry.Text) ? "G7" : NumberFormatEntry.Text);

            // Save Macros
            await File.WriteAllTextAsync(_macrosFilePath, MacrosEditor.Text);

            // Save Entities
            await File.WriteAllTextAsync(_entitiesFilePath, EntitiesEditor.Text);

            // Clear all the macros and entities and reload them
            // to avoid any removed entities loitering around.
            Engine.clearMacrosAndEntities();
            ConfigurationLoader.loadAll();
            MacroParser.parseFile(await File.ReadAllTextAsync(_macrosFilePath));
            EntityParser.parseFiles([await File.ReadAllTextAsync(_entitiesFilePath)]);
            Engine.formatString = Preferences.Get("NumberFormat", "G7");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading data: {ex.Message}");
        }
    }

    private void OnEditorFocused(object? sender, FocusEventArgs e)
    {
        if (sender is Editor editor)
            editor.Animate("Expand", new Animation(v => editor.HeightRequest = v, editor.Height, ExpandedHeight), 16,
                250, Easing.CubicOut);
    }

    private void OnEditorUnfocused(object? sender, FocusEventArgs e)
    {
        if (sender is Editor editor)
            editor.Animate("Collapse", new Animation(v => editor.HeightRequest = v, editor.Height, CollapsedHeight), 16,
                250, Easing.CubicIn);
    }

    private async void OnCloseClicked(object? sender, EventArgs e)
    {
        try
        {
            await Navigation.PopAsync();
        }
        catch (Exception ex) 
        {
            Console.WriteLine($"Error loading data: {ex.Message}");
        }
    }

    private async void Licenses_OnClicked(object? sender, EventArgs e)
    {
        try
        {
            await Shell.Current.GoToAsync(nameof(LicensesPage));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading data: {ex.Message}");
        }
    }
}
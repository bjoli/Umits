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
        base.OnAppearing();

        // Load Number Format
        NumberFormatEntry.Text = Preferences.Default.Get("NumberFormat", "G7");

        // Load Macros
        if (File.Exists(_macrosFilePath)) MacrosEditor.Text = await File.ReadAllTextAsync(_macrosFilePath);

        // Load Entities
        if (File.Exists(_entitiesFilePath)) EntitiesEditor.Text = await File.ReadAllTextAsync(_entitiesFilePath);
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();

        // Save Number Format
        Preferences.Default.Set("NumberFormat", NumberFormatEntry.Text ?? "G7");

        // Save Macros
        await File.WriteAllTextAsync(_macrosFilePath, MacrosEditor.Text ?? string.Empty);

        // Save Entities
        await File.WriteAllTextAsync(_entitiesFilePath, EntitiesEditor.Text ?? string.Empty);

        // Clear all the macros and entities and reload them
        // to avoid any removed entities loitering around.
        Engine.clearMacrosAndEntities();
        ConfigurationLoader.loadAll();
        MacroParser.parseFile(await File.ReadAllTextAsync(_macrosFilePath));
        EntityParser.parseFiles([await File.ReadAllTextAsync(_entitiesFilePath)]);
        Engine.formatString = Preferences.Get("NumberFormat", "G7");
    }

    private void OnEditorFocused(object sender, FocusEventArgs e)
    {
        if (sender is Editor editor)
            editor.Animate("Expand", new Animation(v => editor.HeightRequest = v, editor.Height, ExpandedHeight), 16,
                250, Easing.CubicOut);
    }

    private void OnEditorUnfocused(object sender, FocusEventArgs e)
    {
        if (sender is Editor editor)
            editor.Animate("Collapse", new Animation(v => editor.HeightRequest = v, editor.Height, CollapsedHeight), 16,
                250, Easing.CubicIn);
    }

    private async void OnCloseClicked(object sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }
}
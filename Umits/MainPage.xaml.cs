/*
Umits - A unit converter
Copyright (C) 2026 Linus Björnstam

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <https://www.gnu.org/licenses/>.

---

App Store Distribution Exception
(Additional Permission under Section 7 of the GNU General Public
License, version 3)

As a special exception to the GPLv3, the copyright holders grant you
permission to compile and publish this software to a digital
marketplace (such as the Apple App Store) whose Terms of Service or
Digital Rights Management (DRM) requirements would otherwise conflict
with the conditions of the GPLv3.

This exception applies strictly under the following conditions:

* Permitted Modifications: You may only make the technical
  modifications strictly necessary to comply with the digital
  marketplace’s submission requirements (e.g., modifying bundle IDs,
  API keys, or signing certificates).
* Restrictions: You may not modify the software's core functionality
  or create derivative works for other purposes under this exception.
  Any such modifications immediately void this exception, and the
  resulting work becomes subject entirely to the standard terms of
  the GPLv3, including the requirement to release the complete
  corresponding source code.
* No Sublicensing: You may not sublicense the specific rights
  granted by this exception to any third party.

If you modify this program, you may extend this exception to your
version of the program, but you are not obligated to do so. If you
do not wish to do so, delete this exception statement from your
version.
*/

using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Android.Icu.Text;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Core.Platform;
using Indiko.Maui.Controls.Markdown.Theming;

namespace Umits;

// Data model for the CollectionView binding
public class HistoryItem(int id, string query, string result, bool isError)
{
    public int Id { get; set; } = id;
    public string Query { get; set; } = query;
    public string Result { get; set; } = result;
    public bool IsError { get; set; } = isError;


}

public partial class MainPage
{
    private readonly ObservableCollection<HistoryItem> _history = new();
    private string? _lastResult;
    
    private double _historyHeight = -1.0;

    public MainPage()
    {
        InitializeComponent();
        
        // Bind the history list to the UI
        HistoryCollectionView.ItemsSource = _history;
        
        Application.Current!.RequestedThemeChanged += OnRequestedThemeChanged;
        KeyboardService.KeyboardHeightChanged += OnKeyboardHeightChanged!;

        ChangeMarkdownViewTheme();

        InputEntry.Focus();
        this.Loaded += OnPageLoaded!;
        
    }
    
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        InfoMarkdownView.MarkdownText = await LoadMarkdownFileAsync("Documentation.md");

    }
    
    private async void OnPageLoaded(object sender, EventArgs e)
    {
        base.OnAppearing();

        // Ensures the UI is ready before requesting focus
        await Task.Delay(100);
            InputEntry.Focus(); // Or InputEntry if you reverted to the single-line control

    }
    private async void OnConvertClicked(object sender, EventArgs e)
    {
        HideErrorBubbleImmediately();
        string rawQuery = InputEntry.Text;
        
        if (string.IsNullOrWhiteSpace(rawQuery)) return;
        rawQuery = rawQuery.Trim();

        // Replace $1, $2, etc., with the corresponding history result
        rawQuery = Regex.Replace(rawQuery, @"\$(\d+)", match =>
        {
            if (int.TryParse(match.Groups[1].Value, out int index))
            {
                int listIndex = index; 
                if (listIndex >= 0 && listIndex < _history.Count)
                {
                    return _history[listIndex].Result;
                }
            }
            // If the index is out of bounds or invalid, leave the token as is
            return match.Value; 
        });

        string actualQuery = rawQuery;
        if (rawQuery.StartsWith("in ", StringComparison.OrdinalIgnoreCase) && _lastResult != null)
        {
            actualQuery = $"{_lastResult} {rawQuery}";
        }

        string resultStr = Engine.convertQuery(actualQuery);
        bool isError = resultStr.StartsWith("Error") 
                       || resultStr.StartsWith("Syntax Error");

        if (isError)
        {
            await ShowErrorBubbleAsync(resultStr);
            return;
        }

        // Update state
        _history.Add(new HistoryItem(_history.Count, actualQuery, resultStr, isError));


        _lastResult = resultStr;
        InputEntry.Placeholder = $"e.g., in ms (uses {_lastResult})";
        
        InputEntry.Text = string.Empty;

        // Scroll to the bottom
        HistoryCollectionView.ScrollTo(_history.Last(), position: ScrollToPosition.MakeVisible);
    }

    private async void OnInfoClicked(object sender, EventArgs e)
    {
        await InputEntry.HideKeyboardAsync();
        OverlayContainer.IsVisible = !OverlayContainer.IsVisible;
    }

    // When tapping a historyItem we copy the query into the text field
    private void OnHistoryItemTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is not string query) return;
        InputEntry.Text = query; 
        InputEntry.Focus();
    }
    
    // A long press on a historyItem copies the result to the clipboard
    private async void OnItemLongPressed(object sender, LongPressCompletedEventArgs e)
    {
        if (sender is VisualElement { BindingContext: HistoryItem item })
        {
            await Clipboard.Default.SetTextAsync(item.Result);
        }
    }

    private void OnKeyboardHeightChanged(object sender, double keyboardHeight)
    {
        //  less than is just to surpress a warning. 
        // The default when no history is set is -1
        if (_historyHeight < -0.9)
        {
            _historyHeight = HistoryPanel.Height;
        }

        this.Padding = keyboardHeight < 1 
            ? new Thickness(0, 0, 0, keyboardHeight) 
            : new Thickness(0, 0, 0, keyboardHeight -25);

        if (_history.Count > 0)
        {
            HistoryCollectionView.ScrollTo(_history.Last(), position: ScrollToPosition.MakeVisible);
        }
    }

    // Always unsubscribe to prevent memory leaks
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        KeyboardService.KeyboardHeightChanged -= OnKeyboardHeightChanged!;
    }

    private void OnOverlayTapped(object? sender, TappedEventArgs e)
    {
        OverlayContainer.IsVisible = false;
    }
    
    // Error bubble
    private async Task ShowErrorBubbleAsync(string errorMessage)
    {
        ErrorLabel.Text = errorMessage;
        ErrorBubble.IsVisible = true;

        // Fade in
        await ErrorBubble.FadeToAsync(1);
    }

    
     // Handle the error bubble 
    private void HideErrorBubbleImmediately()
    {
        ErrorBubble.Opacity = 0;
        ErrorBubble.IsVisible = false;
    }

    private void OnErrorBubbleTapped(object sender, TappedEventArgs e)
    {
        HideErrorBubbleImmediately();
    }
    
    
    private void OnInputTextChanged(object sender, TextChangedEventArgs e)
    {
        // Only attempt to hide if it is currently visible
        if (ErrorBubble.IsVisible)
        {
            HideErrorBubbleImmediately();
        }
    }
    
    // Set the theme of the markdownView
    private void OnRequestedThemeChanged(object? sender, AppThemeChangedEventArgs e)
    {
        ChangeMarkdownViewTheme();
    }

    private void ChangeMarkdownViewTheme()
    {
        AppTheme? currentTheme = Application.Current?.RequestedTheme;
        if (currentTheme == null)
            return;
        
        
        if (currentTheme == AppTheme.Dark)
        {
            InfoMarkdownView.Theme = MarkdownThemeDefaults.Dracula;
            return;
        }
        
        InfoMarkdownView.Theme = MarkdownThemeDefaults.GitHub;
    }
    
    
    private async void OnDocumentationClicked(object sender, EventArgs e)
    {
    
        InfoScroll.Orientation = ScrollOrientation.Vertical;
        InfoMarkdownView.MarkdownText = await LoadMarkdownFileAsync("Documentation.md");
    }

    private async void OnLicensesClicked(object sender, EventArgs e)
    {
        InfoScroll.Orientation = ScrollOrientation.Both;
        InfoMarkdownView.MarkdownText = "... Loading ...";
        InfoMarkdownView.MarkdownText = await LoadMarkdownFileAsync("LICENSES.md");

    }

    private async Task<string> LoadMarkdownFileAsync(string filename)
    {
        try
        {
            using var stream = await FileSystem.OpenAppPackageFileAsync(filename);
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync();
        }
        catch (Exception ex)
        {
            return $"### Error\nCould not load {filename}: {ex.Message}";
        }
    }


    private void CloseInfoView(object? sender, EventArgs e)
    {
        OverlayContainer.IsVisible = false;
    }
}
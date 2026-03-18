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
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Core.Platform;

namespace Umits;

// Data model for the CollectionView binding
public class HistoryItem(int id, string query, string result)
{
    public int Id { get; set; } = id;
    public string Query { get; set; } = query;
    public string Result { get; set; } = result;
}

public partial class MainPage
{
    private readonly ObservableCollection<HistoryItem> _history = new();

    private double _historyHeight = -1.0;
    private string? _lastResult;


    private string _selectedSwipeParen = string.Empty;

    public MainPage()
    {
        InitializeComponent();

        // Bind the history list to the UI
        HistoryCollectionView.ItemsSource = _history;


        Loaded += OnPageLoaded;
    }

    protected override async void OnAppearing()
    {
        try
        {
            base.OnAppearing();
            InputEntry.Focus();

        
            KeyboardService.KeyboardHeightChanged += OnKeyboardHeightChanged;
            await LoadSettingsIntoEngine();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    private async Task LoadSettingsIntoEngine()
    {
        // 1. Apply Number Format String
        // Assuming you exposed a public static property named 'formatString' on Engine
        try
        {
            Engine.formatString = Preferences.Default.Get("NumberFormat", "G7");
        }
        catch (TypeInitializationException ex)
        {
            // The actual error is hidden inside the InnerException
            System.Diagnostics.Debug.WriteLine($"CRITICAL ERROR: {ex.InnerException?.Message}");
            System.Diagnostics.Debug.WriteLine($"CRITICAL STACK TRACE: {ex.InnerException?.StackTrace}");
            throw; // Re-throw if you want it to still crash after logging
        }

        // 2. Load Macros
        var macrosPath = Path.Combine(FileSystem.AppDataDirectory, "user_macros.txt");
        if (File.Exists(macrosPath))
        {
            var macrosText = await File.ReadAllTextAsync(macrosPath);
            MacroParser.parseFile(macrosText);
        }

        // 3. Load Entities
        var entitiesPath = Path.Combine(FileSystem.AppDataDirectory, "user_entities.txt");
        if (File.Exists(entitiesPath))
        {
            var entitiesText = await File.ReadAllTextAsync(entitiesPath);
            EntityParser.parseFiles([entitiesText]);
        }
    }

    private async void OnPageLoaded(object? sender, EventArgs? e)
    {
        try
        {
            base.OnAppearing();

            // Ensures the UI is ready before requesting focus
            await Task.Delay(100);
            InputEntry.Focus(); // Or InputEntry if you reverted to the single-line control
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }

    private async void OnConvertClicked(object? sender, EventArgs? e)
    {
        try
        {
            HideErrorBubbleImmediately();
            var rawQuery = InputEntry.Text;

            if (string.IsNullOrWhiteSpace(rawQuery)) return;
            rawQuery = rawQuery.Trim();

            // Replace $1, $2, etc., with the corresponding history result
            rawQuery = Regex.Replace(rawQuery, @"\$(\d+)", match =>
            {
                if (int.TryParse(match.Groups[1].Value, out var index))
                {
                    var listIndex = index;
                    if (listIndex >= 0 && listIndex < _history.Count) return _history[listIndex].Result;
                }

                // If the index is out of bounds or invalid, leave the token as is
                return match.Value;
            });

            // Clear the results.
            if (rawQuery == "clear")
            {
                _history.Clear();
                InputEntry.Text = string.Empty;
                return;
            }

            var actualQuery = rawQuery;
            if (rawQuery.StartsWith("in ", StringComparison.OrdinalIgnoreCase) && _lastResult != null)
                actualQuery = $"{_lastResult} {rawQuery}";

            var resultStr = Engine.convertQuery(actualQuery);
            var isError = resultStr.Contains("Error");

            if (isError)
            {
                await ShowErrorBubbleAsync(resultStr);
                return;
            }

            // Update state
            _history.Add(new HistoryItem(_history.Count, actualQuery, resultStr));


            _lastResult = resultStr;
            InputEntry.Placeholder = $"e.g., in ms (uses {_lastResult})";

            InputEntry.Text = string.Empty;

            // Scroll to the bottom
            HistoryCollectionView.ScrollTo(_history.Last(), position: ScrollToPosition.MakeVisible);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }

    private async void OnInfoClicked(object? sender, EventArgs e)
    {
        try
        {
            await InputEntry.HideKeyboardAsync();
            await Shell.Current.GoToAsync(nameof(InfoPage));
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }

    private float _startX;
    private string _selectedOperator = string.Empty;


    private Point GetAbsolutePosition(VisualElement element)
    {
        double x = 0;
        double y = 0;
        Element current = element;

        while (current is VisualElement visual)
        {
            x += visual.X;
            y += visual.Y;
            current = current.Parent;
        }

        return new Point(x, y);
    }

    private void OnOperatorHandlerChanged(object? sender, EventArgs? e)
    {
#if ANDROID
        if (sender is Border { Handler.PlatformView: Android.Views.View nativeView } border)
            // Access the native Android View underlying the MAUI Border
        {
            nativeView.Touch += (_, args) =>
            {
                if (args.Event == null) return;

                // Extract operators dynamically from the Label
                string leftOp = "+", rightOp = "-";
                if (border.Content is Label label)
                {
                    var ops = label.Text.Split(' ');
                    if (ops.Length == 2)
                    {
                        leftOp = ops[0];
                        rightOp = ops[1];
                    }
                }

                switch (args.Event.ActionMasked)
                {
                    case Android.Views.MotionEventActions.Down:
                        _startX = args.Event.GetX();
                        PopupLeftOp.Text = leftOp;
                        PopupRightOp.Text = rightOp;

                        _selectedOperator = leftOp;
                        PopupLeftOp.BackgroundColor = Colors.LightGray;
                        PopupRightOp.BackgroundColor = Colors.Transparent;

                        InlineOperatorPopup.IsVisible = true;
                        Point location = GetAbsolutePosition(border);

                        // The popup is roughly 90px wide. 
                        // X: Center it over the button. Y: Place it 55px above.
                        InlineOperatorPopup.TranslationX = location.X + (border.Width / 2) - 45;
                        InlineOperatorPopup.TranslationY = location.Y - 55;

                        args.Handled = true; // Setting to true kills the 150ms OS delay
                        break;

                    case Android.Views.MotionEventActions.Move:
                        if (!InlineOperatorPopup.IsVisible) break;

                        float deltaX = args.Event.GetX() - _startX;

                        // 30 native pixels threshold for a swipe right
                        if (deltaX > 30)
                        {
                            PopupRightOp.BackgroundColor = Colors.LightGray;
                            PopupLeftOp.BackgroundColor = Colors.Transparent;
                            _selectedOperator = PopupRightOp.Text;
                        }
                        else
                        {
                            PopupLeftOp.BackgroundColor = Colors.LightGray;
                            PopupRightOp.BackgroundColor = Colors.Transparent;
                            _selectedOperator = PopupLeftOp.Text;
                        }

                        args.Handled = true;
                        break;

                    case Android.Views.MotionEventActions.Up:
                    case Android.Views.MotionEventActions.Cancel:
                        ProcessOperatorSelection();
                        args.Handled = true;
                        break;
                }
            };
        }
#endif
    }

    private void ProcessOperatorSelection()
    {
        if (InlineOperatorPopup.IsVisible)
        {
            if (!string.IsNullOrEmpty(_selectedOperator))
            {
                InsertTextAtCursor(_selectedOperator);
            }

            InlineOperatorPopup.IsVisible = false;
            PopupLeftOp.BackgroundColor = Colors.Transparent;
            PopupRightOp.BackgroundColor = Colors.Transparent;
            _selectedOperator = string.Empty;
        }
    }

    private CancellationTokenSource? _parenLongPressCts;
    private bool _isParenPopupOpen;
    private float _parenStartX;
    private float _parenStartY;

    private void OnParenHandlerChanged(object? sender, EventArgs? e)
    {
        // I am only pushing this for android, but if anyone wants to implement this for ios, 
        // well, I hope there is an easier way to handle popups.
#if ANDROID
        if (sender is Border { Handler.PlatformView: Android.Views.View nativeView } border)
        {
            nativeView.Touch += (s, args) =>
            {
                if (args.Event == null) return;

                switch (args.Event.ActionMasked)
                {
                    case Android.Views.MotionEventActions.Down:
                        _parenStartX = args.Event.GetX();
                        _parenStartY = args.Event.GetY();
                        _isParenPopupOpen = false;
                        _parenLongPressCts = new CancellationTokenSource();

                        // Start the 250ms timer
                        _ = TriggerParenLongPressAsync(_parenLongPressCts.Token, border);

                        args.Handled = true;
                        break;

                    case Android.Views.MotionEventActions.Move:
                        if (_isParenPopupOpen)
                        {
                            // Handle the swipe selection if the popup is open
                            float deltaX = args.Event.GetX() - _parenStartX;
                            if (deltaX < -15)
                            {
                                PopupLeftParen.BackgroundColor = Colors.LightGray;
                                PopupRightParen.BackgroundColor = Colors.Transparent;
                                _selectedSwipeParen = "(";
                            }
                            else if (deltaX > 15)
                            {
                                PopupRightParen.BackgroundColor = Colors.LightGray;
                                PopupLeftParen.BackgroundColor = Colors.Transparent;
                                _selectedSwipeParen = ")";
                            }
                            else
                            {
                                PopupLeftParen.BackgroundColor = Colors.LightGray;
                                PopupRightParen.BackgroundColor = Colors.Transparent;
                                _selectedSwipeParen = "("; // Default to left while open
                            }
                        }
                        else
                        {
                            // Cancel the long press if they move their finger too far before it opens
                            if (Math.Abs(args.Event.GetX() - _parenStartX) > 20 ||
                                Math.Abs(args.Event.GetY() - _parenStartY) > 20)
                            {
                                _parenLongPressCts?.Cancel();
                            }
                        }

                        args.Handled = true;
                        break;

                    case Android.Views.MotionEventActions.Up:
                    case Android.Views.MotionEventActions.Cancel:
                        if (_isParenPopupOpen)
                        {
                            ProcessParenSelection();
                        }
                        else
                        {
                            // It was a short tap. Cancel timer and run normal heuristic.
                            _parenLongPressCts?.Cancel();
                            ExecuteAutoParenHeuristic();
                        }

                        _isParenPopupOpen = false;
                        args.Handled = true;
                        break;
                }
            };
        }
#endif
    }

    private async Task TriggerParenLongPressAsync(CancellationToken token, Border border)
    {
        try
        {
            await Task.Delay(250, token);

            // If we get here, the timer finished without being cancelled
            _isParenPopupOpen = true;
            _selectedSwipeParen = "("; // Default selection

            PopupLeftParen.BackgroundColor = Colors.LightGray;
            PopupRightParen.BackgroundColor = Colors.Transparent;

            Point location = GetAbsolutePosition(border);
            InlineParenPopup.TranslationX = location.X + (border.Width / 2) - 45;
            InlineParenPopup.TranslationY = location.Y - 55;

            InlineParenPopup.IsVisible = true;
        }
        catch (TaskCanceledException)
        {
            // Touch was released or moved before 250ms elapsed
        }
    }

    private void ProcessParenSelection()
    {
        if (InlineParenPopup.IsVisible)
        {
            if (!string.IsNullOrEmpty(_selectedSwipeParen))
            {
                InsertTextAtCursor(_selectedSwipeParen);
            }

            InlineParenPopup.IsVisible = false;
            PopupLeftParen.BackgroundColor = Colors.Transparent;
            PopupRightParen.BackgroundColor = Colors.Transparent;
            _selectedSwipeParen = string.Empty;
        }
    }

    private void ExecuteAutoParenHeuristic()
    {
        var text = string.IsNullOrEmpty(InputEntry.Text) ? string.Empty : InputEntry.Text;
        var cursor = Math.Max(0, Math.Min(InputEntry.CursorPosition, text.Length));
        var selLength = InputEntry.SelectionLength;

        if (selLength > 0)
        {
            // Wrap selected text in parentheses
            var before = text.Substring(0, cursor);
            var selected = text.Substring(cursor, selLength);
            var after = text.Substring(cursor + selLength);

            InputEntry.Text = before + "(" + selected + ")" + after;
            InputEntry.CursorPosition = cursor + selLength + 2;
            InputEntry.Focus();
            return;
        }

        var textBeforeCursor = text.Substring(0, cursor);
        var openCount = textBeforeCursor.Count(c => c == '(');
        var closeCount = textBeforeCursor.Count(c => c == ')');

        // HARD RULE: Never close if there are no unmatched open parentheses.
        if (openCount <= closeCount)
        {
            InsertTextAtCursor("(");
            return;
        }

        // Heuristics for when there ARE unmatched open parentheses
        char? leftChar = null;
        for (var i = cursor - 1; i >= 0; i--)
            if (!char.IsWhiteSpace(textBeforeCursor[i]))
            {
                leftChar = textBeforeCursor[i];
                break;
            }

        var parenToInsert = "("; // Default to open

        if (leftChar.HasValue)
        {
            var c = leftChar.Value;

            if (c == '+' || c == '-' || c == '*' || c == '/' || c == '^' || c == '(')
            {
                parenToInsert = "(";
            }
            else if (char.IsDigit(c) || c == ')' || c == '.' || c == ',')
            {
                parenToInsert = ")";
            }
            else if (char.IsLetter(c) || c == '_')
            {
                // Check if the word is a function or macro
                var wordMatch = Regex.Match(textBeforeCursor, @"[a-zA-Z_][a-zA-Z0-9_]*\s*$");
                if (wordMatch.Success)
                {
                    var lastWord = wordMatch.Value.Trim();
                    string[] standardFunctions = ["sqrt", "ln", "sin", "cos", "log", "log10", "tan"];
                    // Todo: fix so that it checks for macros as well
                    var isFunctionOrMacro = standardFunctions.Contains(lastWord);

                    parenToInsert = isFunctionOrMacro ? "(" : ")";
                }
                else
                {
                    parenToInsert = ")";
                }
            }
        }

        InsertTextAtCursor(parenToInsert);
    }

    private void InsertTextAtCursor(string textToInsert)
    {
        var text = InputEntry.Text;
        var cursor = InputEntry.CursorPosition;
        var selLength = InputEntry.SelectionLength;

        var before = text.Substring(0, cursor);
        var after = text.Substring(cursor + selLength);

        InputEntry.Text = before + textToInsert + after;
        InputEntry.CursorPosition = cursor + textToInsert.Length;
        InputEntry.Focus();
    }

    private async void OnSettingsClicked(object? sender, EventArgs? e)
    {
        try
        {
            await InputEntry.HideKeyboardAsync();
            await Shell.Current.GoToAsync(nameof(SettingsPage));
        }
        catch (Exception ex)
        {            
            Console.WriteLine(ex);
        }
    }

    // When tapping a historyItem we copy the query into the text field
    private void OnHistoryItemTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not string query) return;
        InputEntry.Text = query;
        InputEntry.Focus();
    }

    // A long press on a historyItem copies the result to the clipboard
    private async void OnItemLongPressed(object? sender, LongPressCompletedEventArgs e)
    {
        try
        {
            if (sender is VisualElement { BindingContext: HistoryItem item })
                await Clipboard.Default.SetTextAsync(item.Result);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);

        }
    }

    private void OnKeyboardHeightChanged(object? sender, double keyboardHeight)
    {
        //  less than is just to suppress a warning. 
        // The default when no history is set is -1
        if (_historyHeight < -0.9) _historyHeight = HistoryPanel.Height;

        Padding = keyboardHeight < 1
            ? new Thickness(0, 0, 0, keyboardHeight)
            : new Thickness(0, 0, 0, keyboardHeight - 25);

        if (_history.Count > 0) HistoryCollectionView.ScrollTo(_history.Last(), position: ScrollToPosition.MakeVisible);
    }

    // Always unsubscribe to prevent memory leaks
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        KeyboardService.KeyboardHeightChanged -= OnKeyboardHeightChanged;
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

    private void OnErrorBubbleTapped(object? sender, TappedEventArgs e)
    {
        HideErrorBubbleImmediately();
    }


    private void OnInputTextChanged(object? sender, TextChangedEventArgs e)
    {
        // Only attempt to hide if it is currently visible
        if (ErrorBubble.IsVisible) HideErrorBubbleImmediately();
    }

}
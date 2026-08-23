
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Microsoft.Extensions.Logging;
using OfferLens.ViewModels;
using System.Linq;

namespace OfferLens.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
    }

    IInputPane? InputPane;
    TopLevel? toplevel;

    private void MainGrid_Loaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        toplevel = TopLevel.GetTopLevel(MainGrid);

        if (toplevel is not null)
        {

            InputPane = toplevel.InputPane;

            if (InputPane is not null)
                InputPane.StateChanged += InputPane_StateChanged;
        }


    }

    private void InputPane_StateChanged(object? sender, InputPaneStateEventArgs e)
    {
        if (DataContext is MainViewModel model && InputPane is not null)
        {
            var OccludedArea = InputPane.OccludedRect;
            var OkayToOcclude = OffersOverlayTextBlock.Bounds.Height + OffersOverlayTextBlock.Margin.Top + OffersOverlayTextBlock.Margin.Bottom + BadOfferBorder.Bounds.Height + BadOfferBorder.Margin.Top + BadOfferBorder.Margin.Bottom;

            var OccludedHeight = OccludedArea.Height - OkayToOcclude;
            if (OccludedHeight < 0)
                OccludedHeight = 0;

            var Padding = this.Padding;

            var adjustment = OccludedHeight + Padding.Bottom + Padding.Top;

            if (adjustment < 0)
                adjustment = 0;

            MainGrid.Height = this.Bounds.Height - adjustment;

            if (OccludedArea.Height == 0)
            {
                string?
                    PerMileGood = model.PerMileTargetGood_Text,
                    PerMileGreat = model.PerMileTargetGreat_Text,
                    PerHourGood = model.PerHourTargetGood_Text,
                    PerHourGreat = model.PerHourTargetGreat_Text;

                if (PerMileGood is not null)
                    model.PerMileTargetGood_Text = FormatTextNumber(PerMileGood);
                if (PerMileGreat is not null)
                    model.PerMileTargetGreat_Text = FormatTextNumber(PerMileGreat);
                if (PerHourGood is not null)
                    model.PerHourTargetGood_Text = FormatTextNumber(PerHourGood);
                if (PerHourGreat is not null)
                    model.PerHourTargetGreat_Text = FormatTextNumber(PerHourGreat);

                if (toplevel is not null)
                {
                    var element = toplevel.FocusManager?.GetFocusedElement();

                    if (element is not null && element is TextBox txt)
                    {
                        //set cursor to the end of the text
                        var textlength = txt.Text?.Length ?? 0;
                        txt.CaretIndex = textlength;
                    }
                }
            }
        }
    }

    string FormatTextNumber(string text)
    {
        //We will reformat numbers under the following conditions:
        //1. If the number contains a decimal place but no digits after it, we will remove the decimal place.
        //2. If the number contains a single digit after the decimal place, we will add a zero to the end of it.
        if (text.Contains('.'))
        {
            var parts = text.Split('.');
            if (parts.Length == 2)
            {
                var integerPart = parts[0];
                var decimalPart = parts[1];
                if (decimalPart.Length == 0)
                {
                    // Remove the decimal point if there are no digits after it
                    return integerPart;
                }
                else if (decimalPart.Length == 1)
                {
                    // Add a zero to the end if there's only one digit after the decimal
                    return $"{integerPart}.{decimalPart}0";
                }
            }
        }
        return text;
    }

    //private void TextBox_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    //{
    //    //Allowed: digits and decimal point, backspace, delete, left arrow, right arrow
    //    //Block decimal if there is already a decimal point in the text
    //    //block any other keys from being inputed (text, negative sign, etc)
    //    if (sender is TextBox textBox)
    //    {
    //        var text = textBox.Text ?? string.Empty;
    //        var hasDecimal = text.Contains('.');
    //        var key = e.Key;
    //        if (key == Avalonia.Input.Key.Back || key == Avalonia.Input.Key.Delete ||
    //            key == Avalonia.Input.Key.Left || key == Avalonia.Input.Key.Right)
    //        {
    //            // Allow backspace, delete, left arrow, right arrow
    //            return;
    //        }
    //        else if (key >= Avalonia.Input.Key.D0 && key <= Avalonia.Input.Key.D9)
    //        {
    //            // Allow digits 0-9
    //            return;
    //        }
    //        else if (key >= Avalonia.Input.Key.NumPad0 && key <= Avalonia.Input.Key.NumPad9)
    //        {
    //            // Allow numpad digits 0-9
    //            return;
    //        }
    //        else if (key == Avalonia.Input.Key.OemPeriod || key == Avalonia.Input.Key.Decimal)
    //        {
    //            // Allow decimal point only if there isn't one already
    //            if (!hasDecimal)
    //                return;
    //        }
    //        // If we reach here, the key is not allowed, so we mark the event as handled
    //        e.Handled = true;
    //    }

    //}

    private void TextBox_TextChanged(object? sender, Avalonia.Controls.TextChangedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            var text = textBox.Text ?? string.Empty;

            // Break out our trigger checks for readability
            bool hasInvalidChars = System.Text.RegularExpressions.Regex.IsMatch(text, @"[^\d.]");
            bool hasMultipleDecimals = text.Count(c => c == '.') > 1;
            bool hasTooManyDecimalPlaces = text.Contains('.') && (text.Length - text.IndexOf('.') - 1 > 2);

            // If the text contains invalid characters (e.g., from a paste)
            if (hasInvalidChars || hasMultipleDecimals || hasTooManyDecimalPlaces)
            {
                // Strip everything except digits and decimals
                var cleaned = System.Text.RegularExpressions.Regex.Replace(text, @"[^\d.]", "");

                // Prevent multiple decimals
                while (cleaned.IndexOf('.') != cleaned.LastIndexOf('.'))
                {
                    cleaned = cleaned.Remove(cleaned.LastIndexOf('.'), 1);
                }

                // Enforce the 2 decimal place maximum
                int decimalIndex = cleaned.IndexOf('.');
                if (decimalIndex != -1 && (cleaned.Length - decimalIndex - 1 > 2))
                {
                    // Take the substring from the start up to the decimal point + 2 digits
                    cleaned = cleaned.Substring(0, decimalIndex + 3);
                }

                // Update the text and move the cursor to the end so it doesn't jump
                textBox.Text = cleaned;
                textBox.CaretIndex = cleaned.Length;
            }
        }
    }

    private void TextBox_LostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            var txt = textBox.Text;
            txt = FormatTextNumber(txt ?? string.Empty);
            textBox.Text = txt;
        }
    }
}
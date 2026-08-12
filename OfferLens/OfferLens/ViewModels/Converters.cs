using Avalonia.Data;
using Avalonia.Data.Converters;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace OfferLens.ViewModels
{
    public class StringToDecimalConverter : IValueConverter
    {
        public static readonly StringToDecimalConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value?.ToString();
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            //// 1. Intercept the empty string and safely return null
            //if (value is string str && string.IsNullOrWhiteSpace(str))
            //    return null;

            //// 2. Try to parse the number normally
            //if (decimal.TryParse(value as string, out decimal result))
            //    return result;

            //// 3. If they type letters, trigger the red UI error box without crashing the app
            //return null;

            if (value is string str)
            {
                //Remove any non-numeric characters except for the decimal point and negative sign
                str = System.Text.RegularExpressions.Regex.Replace(str, @"[^\d.-]", "");

                //Try to parse the cleaned string to a decimal
                if (decimal.TryParse(str, out decimal result))
                {
                    return result;
                }
                else
                {
                    // If parsing fails, return null to trigger the red UI error box
                    return null;
                }
            }
            return null;
        }
    }
}

namespace SekaniUI.Converters;

using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Sekani.EditorCore;

public class EqualityToBoolConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		// Checks if the bound value matches the ConverterParameter passed in XAML
		var equals = Equals(value, parameter);
		return Equals(value, parameter);
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
	{
		throw new NotImplementedException();
	}
}

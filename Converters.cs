namespace DayOneWin
{
	using System;
	using System.Windows.Data;

	/// <summary>
	/// Flips boolean values to the opposite value.
	/// </summary>
	[ValueConversion( typeof( bool ), typeof( bool ) )]
	public class FlipBooleanConverter : IValueConverter
	{
		public object Convert( object value, Type targetType, object parameter, System.Globalization.CultureInfo culture )
		{
			bool val = (bool)value;
			return !val;
		}

		public object ConvertBack( object value, Type targetType, object parameter, System.Globalization.CultureInfo culture )
		{
			bool val = (bool)value;
			return !val;
		}
	}
}

/*
    Copyright (C) 2014-2026 de4dot@gmail.com

    This file is part of dnSpy

    dnSpy is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    dnSpy is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with dnSpy.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace dnSpy.Avalonia.AsmEditor {
	// true -> "1*" or "<parameter>*", false -> 0
	sealed class BooleanToGridLengthConverter : IValueConverter {
		public static readonly BooleanToGridLengthConverter Instance = new BooleanToGridLengthConverter();

		public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
			double starValue = 1;
			if (parameter is not null)
				starValue = System.Convert.ToDouble(parameter, CultureInfo.InvariantCulture);
			return value is true ? new GridLength(starValue, GridUnitType.Star) : new GridLength(0);
		}

		public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
	}

	// The parameter is "<true thickness>|<false thickness>"
	sealed class BooleanToThicknessConverter : IValueConverter {
		public static readonly BooleanToThicknessConverter Instance = new BooleanToThicknessConverter();

		public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
			var parts = ((string)parameter!).Split('|');
			return Thickness.Parse(value is true ? parts[0] : parts[1]);
		}

		public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
	}

	// Binds a radio button to an enum value, the parameter is the value
	sealed class RadioButtonConverter : IValueConverter {
		public static readonly RadioButtonConverter Instance = new RadioButtonConverter();

		public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => Equals(value, parameter);
		public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? parameter : BindingOperations.DoNothing;
	}
}

/*
    Copyright (C) 2014-2019 de4dot@gmail.com

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
using System.Windows.Data;

namespace dnSpy.AsmEditor.DnlibDialogs.Converters {
	// The parameter is the value or the name of an enum value. XAML can't reference the internal enums in
	// dnSpy.AsmEditor.Core so they use the name.
	sealed class RadioButtonConverter : IValueConverter {
		public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
			value is Enum && parameter is string name ? value.ToString() == name : value.Equals(parameter);

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
			if (!value.Equals(true))
				return Binding.DoNothing;
			if (parameter is string name && targetType.IsEnum)
				return Enum.Parse(targetType, name);
			return parameter;
		}
	}
}

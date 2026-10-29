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
using Avalonia.Data.Converters;
using dnSpy.AsmEditor.MethodBody;
using dnSpy.Avalonia.Controls;
using dnSpy.Contracts.Text;

namespace dnSpy.Avalonia.AsmEditor {
	// Converts an instruction, operand, local, type etc to colorized text shown by a ColoredTextBlock
	sealed class CilObjectConverter : IValueConverter {
		public static readonly CilObjectConverter Instance = new CilObjectConverter(WriteObjectFlags.None);
		// Instructions are written without their operands
		public static readonly CilObjectConverter ShortInstance = new CilObjectConverter(WriteObjectFlags.ShortInstruction);

		readonly WriteObjectFlags flags;

		CilObjectConverter(WriteObjectFlags flags) => this.flags = flags;

		public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
			var text = new ColoredText();
			try {
				BodyUtils.WriteObject(new SingleLineWriter(text), value, flags);
			}
			catch (Exception ex) {
				text = new ColoredText();
				text.Write(BoxedTextColor.Error, ex.Message);
			}
			return text;
		}

		public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();

		// The text is shown on one line
		sealed class SingleLineWriter : ITextColorWriter {
			readonly ColoredText output;
			public SingleLineWriter(ColoredText output) => this.output = output;
			public void Write(TextColor color, string? text) => Write(color.Box(), text);
			public void Write(object color, string? text) => output.Write(color, text?.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' '));
		}
	}
}

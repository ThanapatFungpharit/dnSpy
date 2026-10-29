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

using System.Collections.Generic;
using System.Text;
using dnSpy.Contracts.Text;

namespace dnSpy.Avalonia.Controls {
	// Colorized text created by the decompilers' ITextColorWriter methods
	sealed class ColoredText : ITextColorWriter {
		readonly List<(object color, string text)> parts = new List<(object color, string text)>();

		public IReadOnlyList<(object color, string text)> Parts => parts;

		public void Write(TextColor color, string? text) => Write(color.Box(), text);

		public void Write(object color, string? text) {
			if (!string.IsNullOrEmpty(text))
				parts.Add((color, text));
		}

		public override string ToString() {
			var sb = new StringBuilder();
			foreach (var part in parts)
				sb.Append(part.text);
			return sb.ToString();
		}
	}
}

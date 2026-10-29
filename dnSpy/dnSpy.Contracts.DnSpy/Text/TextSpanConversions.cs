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

using dnSpy.Contracts.Decompiler;
using Microsoft.VisualStudio.Text;

namespace dnSpy.Contracts.Text {
	/// <summary>
	/// Converts between <see cref="TextSpan"/> and the text editor's <see cref="Span"/>
	/// </summary>
	public static class TextSpanConversions {
		/// <summary>
		/// Converts <paramref name="span"/> to a text editor <see cref="Span"/>
		/// </summary>
		/// <param name="span">Span</param>
		/// <returns></returns>
		public static Span ToVsSpan(this TextSpan span) => new Span(span.Start, span.Length);

		/// <summary>
		/// Converts <paramref name="span"/> to a <see cref="TextSpan"/>
		/// </summary>
		/// <param name="span">Span</param>
		/// <returns></returns>
		public static TextSpan ToDsTextSpan(this Span span) => new TextSpan(span.Start, span.Length);
	}
}

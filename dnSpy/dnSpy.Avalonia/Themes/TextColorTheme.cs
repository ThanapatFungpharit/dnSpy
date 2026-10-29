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
using System.Collections.Generic;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using dnSpy.Contracts.Text;

namespace dnSpy.Avalonia.Themes {
	readonly struct TextColorStyle {
		public IBrush? Foreground { get; }
		public IBrush? Background { get; }
		public bool Bold { get; }
		public bool Italic { get; }

		public TextColorStyle(IBrush? foreground, IBrush? background, bool bold, bool italic) {
			Foreground = foreground;
			Background = background;
			Bold = bold;
			Italic = italic;
		}
	}

	// Text colors read from the WPF app's *.dntheme files. The theme color names are the
	// lower case TextColor names.
	sealed class TextColorTheme {
		static TextColorTheme? dark, light;

		public static TextColorTheme Current =>
			Application.Current?.ActualThemeVariant == ThemeVariant.Dark ?
			dark ??= Load("dark.dntheme") :
			light ??= Load("light.dntheme");

		readonly TextColorStyle[] styles;
		readonly Dictionary<string, TextColorStyle> otherStyles;

		public TextColorStyle DefaultText { get; }
		public TextColorStyle LineNumber => GetOther("linenumber");
		public TextColorStyle SelectedText => GetOther("selectedtext");
		public TextColorStyle HighlightedReference => GetOther("highlightedreference");
		public TextColorStyle HighlightedDefinition => GetOther("highlighteddefinition");
		public TextColorStyle CurrentLine => GetOther("currentline");

		TextColorTheme(Dictionary<string, TextColorStyle> allStyles) {
			otherStyles = allStyles;
			allStyles.TryGetValue("defaulttext", out var defaultText);
			DefaultText = defaultText;
			styles = new TextColorStyle[(int)TextColor.Last];
			for (int i = 0; i < styles.Length; i++) {
				var name = ((TextColor)i).ToString().ToLowerInvariant();
				styles[i] = allStyles.TryGetValue(name, out var style) ? style : defaultText;
			}
		}

		TextColorStyle GetOther(string name) => otherStyles.TryGetValue(name, out var style) ? style : default;

		// Colors that aren't TextColor values, eg. the hex editor's colors. The name is the lower case theme color name.
		public TextColorStyle GetByName(string name) => GetOther(name);

		public TextColorStyle Get(object? color) {
			if (color is TextColor textColor && (uint)textColor < (uint)styles.Length)
				return styles[(int)textColor];
			return DefaultText;
		}

		static TextColorTheme Load(string resourceName) {
			var dict = new Dictionary<string, TextColorStyle>(StringComparer.Ordinal);
			using (var stream = typeof(TextColorTheme).Assembly.GetManifestResourceStream(resourceName)) {
				if (stream is not null) {
					var doc = XDocument.Load(stream);
					foreach (var elem in doc.Root!.Element("colors")?.Elements("color") ?? Array.Empty<XElement>()) {
						var name = (string?)elem.Attribute("name");
						if (name is null)
							continue;
						dict[name] = new TextColorStyle(ParseBrush((string?)elem.Attribute("fg")), ParseBrush((string?)elem.Attribute("bg")),
							(bool?)elem.Attribute("bold") ?? false, (bool?)elem.Attribute("italics") ?? false);
					}
				}
			}
			return new TextColorTheme(dict);
		}

		static IBrush? ParseBrush(string? s) {
			if (s is null || !Color.TryParse(s, out var color))
				return null;
			return new ImmutableSolidColorBrush(color);
		}
	}
}

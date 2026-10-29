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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using dnSpy.Avalonia.Themes;

namespace dnSpy.Avalonia.Controls {
	// Shows a ColoredText using the current theme's text colors
	sealed class ColoredTextBlock : TextBlock {
		public static readonly StyledProperty<ColoredText?> ColoredTextProperty =
			AvaloniaProperty.Register<ColoredTextBlock, ColoredText?>(nameof(ColoredText));

		public ColoredText? ColoredText {
			get => GetValue(ColoredTextProperty);
			set => SetValue(ColoredTextProperty, value);
		}

		protected override Type StyleKeyOverride => typeof(TextBlock);

		protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
			base.OnPropertyChanged(change);
			if (change.Property == ColoredTextProperty)
				UpdateInlines();
		}

		protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
			base.OnAttachedToVisualTree(e);
			if (Application.Current is Application app)
				app.ActualThemeVariantChanged += Application_ActualThemeVariantChanged;
			UpdateInlines();
		}

		protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
			base.OnDetachedFromVisualTree(e);
			if (Application.Current is Application app)
				app.ActualThemeVariantChanged -= Application_ActualThemeVariantChanged;
		}

		void Application_ActualThemeVariantChanged(object? sender, EventArgs e) => UpdateInlines();

		void UpdateInlines() {
			var inlines = Inlines ??= new InlineCollection();
			inlines.Clear();
			var coloredText = ColoredText;
			if (coloredText is null)
				return;
			var theme = TextColorTheme.Current;
			foreach (var (color, text) in coloredText.Parts) {
				var style = theme.Get(color);
				var run = new Run(text);
				if (style.Foreground is not null)
					run.Foreground = style.Foreground;
				if (style.Bold)
					run.FontWeight = FontWeight.Bold;
				if (style.Italic)
					run.FontStyle = FontStyle.Italic;
				inlines.Add(run);
			}
		}
	}
}

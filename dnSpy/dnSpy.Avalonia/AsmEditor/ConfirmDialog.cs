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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace dnSpy.Avalonia.AsmEditor {
	// A Yes/No question shown modally like the other AsmEditor dialogs
	sealed class ConfirmDialog : DialogWindow {
		public ConfirmDialog(string message, string title) {
			Title = title;
			SizeToContent = SizeToContent.WidthAndHeight;
			MinWidth = 300;
			MaxWidth = 600;
			CanResize = false;
			var yesButton = new Button { Content = "_Yes", IsDefault = true, Margin = new Thickness(0, 0, 5, 0) };
			yesButton.Classes.Add("dialog");
			yesButton.Click += (s, e) => CloseDialog(true);
			var noButton = new Button { Content = "_No", IsCancel = true };
			noButton.Classes.Add("dialog");
			noButton.Click += (s, e) => CloseDialog(false);
			Content = new StackPanel {
				Margin = new Thickness(16),
				Spacing = 16,
				Children = {
					new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
					new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { yesButton, noButton } },
				},
			};
			Opened += (s, e) => yesButton.Focus();
		}
	}
}

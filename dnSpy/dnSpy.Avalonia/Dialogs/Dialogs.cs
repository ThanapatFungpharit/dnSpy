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

using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace dnSpy.Avalonia.Dialogs {
	enum MessageBoxResult {
		None,
		OK,
		Yes,
		No,
		Cancel,
	}

	enum MessageBoxButtons {
		OK,
		YesNo,
		YesNoCancel,
	}

	// Avalonia doesn't have a message box
	static class MessageBox {
		public static async Task<MessageBoxResult> ShowAsync(Window owner, string message, string title, MessageBoxButtons buttons = MessageBoxButtons.OK) {
			var window = new Window {
				Title = title,
				SizeToContent = SizeToContent.WidthAndHeight,
				MinWidth = 300,
				MaxWidth = 800,
				CanResize = false,
				WindowStartupLocation = WindowStartupLocation.CenterOwner,
				ShowInTaskbar = false,
			};
			var result = MessageBoxResult.None;
			var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
			void AddButton(string text, MessageBoxResult buttonResult, bool isDefault = false, bool isCancel = false) {
				var button = new Button { Content = text, MinWidth = 75, IsDefault = isDefault, IsCancel = isCancel };
				button.Click += (s, e) => {
					result = buttonResult;
					window.Close();
				};
				buttonPanel.Children.Add(button);
			}
			switch (buttons) {
			case MessageBoxButtons.OK:
				AddButton("OK", MessageBoxResult.OK, isDefault: true, isCancel: true);
				break;
			case MessageBoxButtons.YesNo:
				AddButton("Yes", MessageBoxResult.Yes, isDefault: true);
				AddButton("No", MessageBoxResult.No, isCancel: true);
				break;
			case MessageBoxButtons.YesNoCancel:
				AddButton("Yes", MessageBoxResult.Yes, isDefault: true);
				AddButton("No", MessageBoxResult.No);
				AddButton("Cancel", MessageBoxResult.Cancel, isCancel: true);
				break;
			}
			window.Content = new StackPanel {
				Margin = new Thickness(16),
				Spacing = 16,
				Children = {
					new SelectableTextBlock { Text = message, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap },
					buttonPanel,
				},
			};
			await window.ShowDialog(owner);
			return result;
		}
	}

	static class InputBox {
		// Returns null if it was canceled
		public static async Task<string?> ShowAsync(Window owner, string title, string label, string text) {
			var window = new Window {
				Title = title,
				SizeToContent = SizeToContent.WidthAndHeight,
				MinWidth = 350,
				CanResize = false,
				WindowStartupLocation = WindowStartupLocation.CenterOwner,
				ShowInTaskbar = false,
			};
			string? result = null;
			var textBox = new TextBox { Text = text };
			var okButton = new Button { Content = "OK", MinWidth = 75, IsDefault = true };
			var cancelButton = new Button { Content = "Cancel", MinWidth = 75, IsCancel = true };
			okButton.Click += (s, e) => {
				result = textBox.Text ?? string.Empty;
				window.Close();
			};
			cancelButton.Click += (s, e) => window.Close();
			window.Content = new StackPanel {
				Margin = new Thickness(16),
				Spacing = 8,
				Children = {
					new TextBlock { Text = label },
					textBox,
					new StackPanel {
						Orientation = Orientation.Horizontal,
						HorizontalAlignment = HorizontalAlignment.Right,
						Spacing = 8,
						Margin = new Thickness(0, 8, 0, 0),
						Children = { okButton, cancelButton },
					},
				},
			};
			window.Opened += (s, e) => {
				textBox.Focus(NavigationMethod.Tab);
				textBox.SelectAll();
			};
			await window.ShowDialog(owner);
			return result;
		}
	}
}

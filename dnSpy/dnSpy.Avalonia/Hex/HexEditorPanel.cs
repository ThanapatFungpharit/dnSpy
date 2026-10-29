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
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using dnSpy.Avalonia.Dialogs;

namespace dnSpy.Avalonia.Hex {
	// A hex editor tab's content: a toolbar, the hex editor, its scroll bar and a status line
	sealed class HexEditorPanel : DockPanel {
		readonly ScrollBar scrollBar;
		readonly TextBlock statusText;
		bool updatingScrollBar;

		public HexEditor Editor { get; }
		public HexDocument Document { get; }

		// Raised before the file is written (the arg is the filename) so the owner can stop reading it lazily,
		// and after the file was saved so the owner can reload the assembly
		public event EventHandler<string>? Saving;
		public event EventHandler<string>? Saved;

		public HexEditorPanel(HexDocument document) {
			Document = document;
			Editor = new HexEditor { Document = document };

			var saveButton = new Button { Content = "Save", Margin = new global::Avalonia.Thickness(2) };
			saveButton.Click += async (s, e) => await SaveAsync(saveAs: false);
			var saveAsButton = new Button { Content = "Save As...", Margin = new global::Avalonia.Thickness(2) };
			saveAsButton.Click += async (s, e) => await SaveAsync(saveAs: true);
			var goToButton = new Button { Content = "Go to Offset...", Margin = new global::Avalonia.Thickness(2) };
			goToButton.Click += async (s, e) => await GoToOffsetAsync();
			var undoButton = new Button { Content = "Undo", Margin = new global::Avalonia.Thickness(2) };
			undoButton.Click += (s, e) => { Editor.Undo(); Editor.Focus(); };
			var redoButton = new Button { Content = "Redo", Margin = new global::Avalonia.Thickness(2) };
			redoButton.Click += (s, e) => { Editor.Redo(); Editor.Focus(); };
			var toolbar = new StackPanel {
				Orientation = Orientation.Horizontal,
				Children = { saveButton, saveAsButton, goToButton, undoButton, redoButton },
			};
			SetDock(toolbar, Dock.Top);
			Children.Add(toolbar);

			statusText = new TextBlock { Margin = new global::Avalonia.Thickness(4, 2) };
			SetDock(statusText, Dock.Bottom);
			Children.Add(statusText);

			scrollBar = new ScrollBar { Orientation = Orientation.Vertical, AllowAutoHide = false, SmallChange = 1 };
			scrollBar.ValueChanged += ScrollBar_ValueChanged;
			SetDock(scrollBar, Dock.Right);
			Children.Add(scrollBar);

			Children.Add(Editor);

			Editor.ScrollChanged += (s, e) => UpdateScrollBar();
			Editor.CaretMoved += (s, e) => UpdateStatus();
			document.Changed += (s, e) => UpdateStatus();
			UpdateStatus();
		}

		public string Title => (Document.IsModified ? "*" : string.Empty) + Document.Name + " (hex)";
		public event EventHandler? TitleChanged;

		void UpdateScrollBar() {
			updatingScrollBar = true;
			try {
				// The scroll bar uses doubles, the line count is small enough (file size / 16)
				scrollBar.Maximum = Editor.MaxTopLine;
				scrollBar.ViewportSize = Editor.VisibleLineCount;
				scrollBar.LargeChange = Math.Max(1, Editor.VisibleLineCount - 1);
				scrollBar.Value = Editor.TopLine;
			}
			finally {
				updatingScrollBar = false;
			}
		}

		void ScrollBar_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e) {
			if (!updatingScrollBar)
				Editor.TopLine = (long)Math.Round(e.NewValue);
		}

		void UpdateStatus() {
			var text = $"Offset: 0x{Editor.CaretOffset:X8}   Length: 0x{Document.Length:X} ({Document.Length} bytes)";
			if (Editor.Selection is { } sel)
				text += $"   Selection: 0x{sel.start:X8}-0x{sel.end - 1:X8} (0x{sel.end - sel.start:X} bytes)";
			if (Document.Length > 0) {
				byte b = Document[Editor.CaretOffset];
				text += $"   Byte: 0x{b:X2} ({b})";
			}
			statusText.Text = text;
			TitleChanged?.Invoke(this, EventArgs.Empty);
		}

		protected override void OnKeyDown(KeyEventArgs e) {
			base.OnKeyDown(e);
			if (e.Handled)
				return;
			if (e.Key == Key.S && e.KeyModifiers == KeyModifiers.Control) {
				_ = SaveAsync(saveAs: false);
				e.Handled = true;
			}
			else if (e.Key == Key.G && e.KeyModifiers == KeyModifiers.Control) {
				_ = GoToOffsetAsync();
				e.Handled = true;
			}
		}

		public async Task<bool> SaveAsync(bool saveAs) {
			if (TopLevel.GetTopLevel(this) is not Window window)
				return false;
			var filename = Document.Filename;
			if (saveAs || filename is null) {
				var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions {
					Title = "Save",
					SuggestedFileName = Document.Name,
				});
				filename = file?.TryGetLocalPath();
				if (filename is null)
					return false;
			}
			try {
				Saving?.Invoke(this, filename);
				Document.Save(filename);
			}
			catch (Exception ex) {
				await MessageBox.ShowAsync(window, $"Couldn't save '{filename}':\n\n{ex.Message}", "Save");
				return false;
			}
			Saved?.Invoke(this, filename);
			return true;
		}

		async Task GoToOffsetAsync() {
			if (TopLevel.GetTopLevel(this) is not Window window)
				return;
			var text = await InputBox.ShowAsync(window, "Go to Offset", "Offset (hex, or decimal with a 0n prefix):", $"0x{Editor.CaretOffset:X}");
			if (text is null)
				return;
			if (TryParseOffset(text, out long offset) && offset >= 0 && offset < Document.Length)
				Editor.MoveCaret(offset);
			else
				await MessageBox.ShowAsync(window, $"Invalid offset: {text}", "Go to Offset");
			Editor.Focus();
		}

		internal static bool TryParseOffset(string text, out long offset) {
			text = text.Trim();
			if (text.StartsWith("0n", StringComparison.OrdinalIgnoreCase))
				return long.TryParse(text.Substring(2), NumberStyles.None, CultureInfo.InvariantCulture, out offset);
			if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
				text = text.Substring(2);
			return long.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out offset);
		}
	}
}

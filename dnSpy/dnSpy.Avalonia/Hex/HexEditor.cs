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
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using dnSpy.Avalonia.Themes;

namespace dnSpy.Avalonia.Hex {
	enum HexColumn {
		Values,
		Ascii,
	}

	// Shows a HexDocument as offset, hex values and ASCII columns, 16 bytes per line. Bytes are
	// overwritten by typing hex digits in the values column or characters in the ASCII column.
	// Only the visible lines are rendered so big files are fine. The owner shows a scroll bar
	// (see TopLine, LineCount, VisibleLineCount and ScrollChanged).
	sealed class HexEditor : Control {
		public const int BytesPerLine = 16;
		// Values column: "XX XX XX XX XX XX XX XX  XX XX XX XX XX XX XX XX"
		const int ValuesColumnChars = BytesPerLine * 3;
		const double TextPadding = 4;

		HexDocument? document;
		long caretOffset;
		// 0 = high nibble, 1 = low nibble. Only used in the values column.
		int caretNibble;
		HexColumn caretColumn;
		long? selectionAnchor;
		long topLine;
		bool isSelectingWithMouse;

		Typeface typeface;
		double fontSize = 13;
		double charWidth, lineHeight;

		public event EventHandler? ScrollChanged;
		public event EventHandler? CaretMoved;

		public HexEditor() {
			Focusable = true;
			ClipToBounds = true;
			Cursor = new Cursor(StandardCursorType.Ibeam);
			typeface = new Typeface(new FontFamily("Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, Liberation Mono, Noto Sans Mono, monospace"));
			UpdateFontMetrics();
		}

		public HexDocument? Document {
			get => document;
			set {
				if (document == value)
					return;
				if (document is not null)
					document.Changed -= Document_Changed;
				document = value;
				if (document is not null)
					document.Changed += Document_Changed;
				caretOffset = 0;
				caretNibble = 0;
				selectionAnchor = null;
				topLine = 0;
				OnScrollChanged();
				OnCaretMoved();
				InvalidateVisual();
			}
		}

		void Document_Changed(object? sender, EventArgs e) => InvalidateVisual();

		public long CaretOffset => caretOffset;
		public HexColumn CaretColumn => caretColumn;
		public long LineCount => document is null ? 0 : (document.Length + BytesPerLine - 1) / BytesPerLine;
		public int VisibleLineCount => Math.Max(1, (int)Math.Floor(Math.Max(0, Bounds.Height - TextPadding) / lineHeight));
		public long MaxTopLine => Math.Max(0, LineCount - VisibleLineCount);

		public long TopLine {
			get => topLine;
			set {
				value = Math.Clamp(value, 0, MaxTopLine);
				if (topLine == value)
					return;
				topLine = value;
				OnScrollChanged();
				InvalidateVisual();
			}
		}

		// Start and end (exclusive) of the selection, or null if nothing is selected
		public (long start, long end)? Selection {
			get {
				if (selectionAnchor is not long anchor || document is null)
					return null;
				long start = Math.Min(anchor, caretOffset);
				long end = Math.Min(Math.Max(anchor, caretOffset) + 1, document.Length);
				return start < end ? (start, end) : null;
			}
		}

		void OnScrollChanged() => ScrollChanged?.Invoke(this, EventArgs.Empty);
		void OnCaretMoved() => CaretMoved?.Invoke(this, EventArgs.Empty);

		void UpdateFontMetrics() {
			var text = new FormattedText("0", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, fontSize, Brushes.Black);
			charWidth = text.WidthIncludingTrailingWhitespace;
			lineHeight = Math.Ceiling(text.Height);
			if (charWidth <= 0)
				charWidth = fontSize * 0.6;
			if (lineHeight <= 0)
				lineHeight = fontSize * 1.3;
		}

		int OffsetDigits => document is not null && document.Length > 0x1_0000_0000 ? 16 : 8;
		double ValuesX => TextPadding + (OffsetDigits + 2) * charWidth;
		double AsciiX => ValuesX + (ValuesColumnChars + 1) * charWidth;

		// Index of the first char of a byte in the values column. There's an extra space after the 8th byte.
		static int GetValuesCharIndex(int byteIndex) => byteIndex * 3 + (byteIndex >= BytesPerLine / 2 ? 1 : 0);

		protected override Size MeasureOverride(Size availableSize) {
			double width = AsciiX + BytesPerLine * charWidth + TextPadding;
			double height = double.IsInfinity(availableSize.Height) ? 20 * lineHeight : availableSize.Height;
			return new Size(width, height);
		}

		protected override Size ArrangeOverride(Size finalSize) {
			var size = base.ArrangeOverride(finalSize);
			// More lines can be visible now
			if (topLine > MaxTopLine)
				TopLine = MaxTopLine;
			OnScrollChanged();
			return size;
		}

		public override void Render(DrawingContext context) {
			var theme = TextColorTheme.Current;
			var textStyle = theme.GetByName("hextext");
			var background = textStyle.Background ?? theme.DefaultText.Background ?? Brushes.White;
			var foreground = textStyle.Foreground ?? theme.DefaultText.Foreground ?? Brushes.Black;
			var offsetBrush = theme.GetByName("hexoffset").Foreground ?? foreground;
			var asciiBrush = theme.GetByName("hexascii").Foreground ?? foreground;
			var modifiedBrush = theme.GetByName("hexbyteerror").Foreground ?? Brushes.Red;
			var selectionBrush = theme.GetByName("hexselection").Background ?? theme.SelectedText.Background ?? Brushes.LightBlue;
			var caretBrush = theme.GetByName("hexcaret").Background ?? new ImmutableSolidColorBrush(Color.FromArgb(0x66, 0, 0, 0));
			var inactiveCaretBrush = theme.GetByName("hexinactivecaret").Background ?? Brushes.Red;
			var currentLineBrush = theme.GetByName("hexcurrentline").Foreground ?? Brushes.Gray;

			context.FillRectangle(background, new Rect(Bounds.Size));
			var doc = document;
			if (doc is null || doc.Length == 0)
				return;

			int offsetDigits = OffsetDigits;
			double valuesX = ValuesX, asciiX = AsciiX;
			var selection = Selection;
			long caretLine = caretOffset / BytesPerLine;
			var sb = new StringBuilder();
			int visibleLines = VisibleLineCount + 1;
			for (int i = 0; i < visibleLines; i++) {
				long line = topLine + i;
				long lineOffset = line * BytesPerLine;
				if (lineOffset >= doc.Length)
					break;
				int count = (int)Math.Min(BytesPerLine, doc.Length - lineOffset);
				double y = TextPadding / 2 + i * lineHeight;

				if (line == caretLine) {
					var pen = new Pen(currentLineBrush, 1);
					context.DrawRectangle(null, pen, new Rect(valuesX - charWidth / 2, y, asciiX + BytesPerLine * charWidth - valuesX + charWidth / 2, lineHeight));
				}

				if (selection is { } sel) {
					var (selStart, selEnd) = sel;
					for (int b = 0; b < count; b++) {
						long offset = lineOffset + b;
						if (offset < selStart || offset >= selEnd)
							continue;
						// Also fill the space between selected bytes
						bool nextSelected = b + 1 < count && offset + 1 < selEnd;
						double valueWidth = nextSelected ? (GetValuesCharIndex(b + 1) - GetValuesCharIndex(b)) * charWidth : 2 * charWidth;
						context.FillRectangle(selectionBrush, new Rect(valuesX + GetValuesCharIndex(b) * charWidth, y, valueWidth, lineHeight));
						context.FillRectangle(selectionBrush, new Rect(asciiX + b * charWidth, y, charWidth, lineHeight));
					}
				}

				var offsetText = new FormattedText(lineOffset.ToString(offsetDigits == 8 ? "X8" : "X16", CultureInfo.InvariantCulture),
					CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, fontSize, offsetBrush);
				context.DrawText(offsetText, new Point(TextPadding, y));

				sb.Clear();
				for (int b = 0; b < count; b++) {
					if (b == BytesPerLine / 2)
						sb.Append(' ');
					sb.Append(doc[lineOffset + b].ToString("X2", CultureInfo.InvariantCulture));
					if (b + 1 < count)
						sb.Append(' ');
				}
				var valuesText = new FormattedText(sb.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, fontSize, foreground);

				sb.Clear();
				for (int b = 0; b < count; b++) {
					byte value = doc[lineOffset + b];
					sb.Append(value >= 0x20 && value <= 0x7E ? (char)value : '.');
				}
				var asciiText = new FormattedText(sb.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, fontSize, asciiBrush);

				for (int b = 0; b < count; b++) {
					if (doc.IsByteModified(lineOffset + b)) {
						valuesText.SetForegroundBrush(modifiedBrush, GetValuesCharIndex(b), 2);
						asciiText.SetForegroundBrush(modifiedBrush, b, 1);
					}
				}
				context.DrawText(valuesText, new Point(valuesX, y));
				context.DrawText(asciiText, new Point(asciiX, y));

				if (line == caretLine) {
					int b = (int)(caretOffset - lineOffset);
					var valueRect = new Rect(valuesX + (GetValuesCharIndex(b) + (caretColumn == HexColumn.Values ? caretNibble : 0)) * charWidth, y,
						caretColumn == HexColumn.Values ? charWidth : 2 * charWidth, lineHeight);
					var asciiRect = new Rect(asciiX + b * charWidth, y, charWidth, lineHeight);
					bool isFocused = IsFocused;
					if (caretColumn == HexColumn.Values) {
						context.FillRectangle(isFocused ? caretBrush : inactiveCaretBrush, valueRect);
						context.DrawRectangle(null, new Pen(inactiveCaretBrush, 1), asciiRect);
					}
					else {
						context.FillRectangle(isFocused ? caretBrush : inactiveCaretBrush, asciiRect);
						context.DrawRectangle(null, new Pen(inactiveCaretBrush, 1), valueRect);
					}
				}
			}
		}

		protected override void OnGotFocus(FocusChangedEventArgs e) {
			base.OnGotFocus(e);
			InvalidateVisual();
		}

		protected override void OnLostFocus(FocusChangedEventArgs e) {
			base.OnLostFocus(e);
			InvalidateVisual();
		}

		public void MoveCaret(long offset, bool extendSelection = false, int nibble = 0) {
			if (document is null || document.Length == 0)
				return;
			offset = Math.Clamp(offset, 0, document.Length - 1);
			if (extendSelection)
				selectionAnchor ??= caretOffset;
			else
				selectionAnchor = null;
			caretOffset = offset;
			caretNibble = caretColumn == HexColumn.Values ? nibble : 0;
			EnsureCaretVisible();
			OnCaretMoved();
			InvalidateVisual();
		}

		// Selects the bytes and moves the caret to the first byte, eg. a method body
		public void Select(long offset, long length) {
			if (document is null || document.Length == 0)
				return;
			MoveCaret(offset + Math.Max(0, length - 1));
			if (length > 1) {
				selectionAnchor = caretOffset;
				caretOffset = Math.Clamp(offset, 0, document.Length - 1);
			}
			// Show the start of the selection
			long line = caretOffset / BytesPerLine;
			if (line < topLine || line >= topLine + VisibleLineCount)
				TopLine = line - VisibleLineCount / 3;
			OnCaretMoved();
			InvalidateVisual();
		}

		public void EnsureCaretVisible() {
			long line = caretOffset / BytesPerLine;
			if (line < topLine)
				TopLine = line;
			else if (line >= topLine + VisibleLineCount)
				TopLine = line - VisibleLineCount + 1;
		}

		public void SelectAll() {
			if (document is null || document.Length == 0)
				return;
			selectionAnchor = 0;
			caretOffset = document.Length - 1;
			EnsureCaretVisible();
			OnCaretMoved();
			InvalidateVisual();
		}

		public void SwitchColumn() {
			caretColumn = caretColumn == HexColumn.Values ? HexColumn.Ascii : HexColumn.Values;
			caretNibble = 0;
			OnCaretMoved();
			InvalidateVisual();
		}

		protected override void OnKeyDown(KeyEventArgs e) {
			base.OnKeyDown(e);
			if (e.Handled || document is null)
				return;
			bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
			bool ctrl = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
			bool alt = (e.KeyModifiers & KeyModifiers.Alt) != 0;
			if (alt)
				return;
			long pageBytes = (long)VisibleLineCount * BytesPerLine;
			e.Handled = true;
			switch (e.Key) {
			case Key.Left:
				if (caretColumn == HexColumn.Values && !shift && caretNibble == 1)
					MoveCaret(caretOffset, nibble: 0);
				else
					MoveCaret(caretOffset - 1, shift, caretColumn == HexColumn.Values && !shift ? 1 : 0);
				break;
			case Key.Right:
				if (caretColumn == HexColumn.Values && !shift && caretNibble == 0)
					MoveCaret(caretOffset, nibble: 1);
				else
					MoveCaret(caretOffset + 1, shift);
				break;
			case Key.Up:
				if (ctrl)
					TopLine--;
				else
					MoveCaret(caretOffset - BytesPerLine, shift, caretNibble);
				break;
			case Key.Down:
				if (ctrl)
					TopLine++;
				else if (caretOffset + BytesPerLine < document.Length)
					MoveCaret(caretOffset + BytesPerLine, shift, caretNibble);
				break;
			case Key.PageUp:
				TopLine -= VisibleLineCount;
				MoveCaret(caretOffset - pageBytes, shift, caretNibble);
				break;
			case Key.PageDown:
				TopLine += VisibleLineCount;
				MoveCaret(Math.Min(caretOffset + pageBytes, document.Length - 1), shift, caretNibble);
				break;
			case Key.Home:
				MoveCaret(ctrl ? 0 : caretOffset - caretOffset % BytesPerLine, shift);
				break;
			case Key.End:
				MoveCaret(ctrl ? document.Length - 1 : caretOffset - caretOffset % BytesPerLine + BytesPerLine - 1, shift);
				break;
			case Key.Tab:
				SwitchColumn();
				break;
			case Key.A when ctrl:
				SelectAll();
				break;
			case Key.C when ctrl:
				_ = CopyAsync();
				break;
			case Key.V when ctrl:
				_ = PasteAsync();
				break;
			case Key.Z when ctrl && !shift:
				Undo();
				break;
			case Key.Y when ctrl:
			case Key.Z when ctrl && shift:
				Redo();
				break;
			default:
				e.Handled = false;
				break;
			}
		}

		public void Undo() {
			if (document?.Undo() is long offset)
				MoveCaret(offset);
		}

		public void Redo() {
			if (document?.Redo() is long offset)
				MoveCaret(offset);
		}

		protected override void OnTextInput(TextInputEventArgs e) {
			base.OnTextInput(e);
			if (e.Handled || document is null || document.Length == 0 || string.IsNullOrEmpty(e.Text))
				return;
			foreach (var c in e.Text)
				TypeChar(c);
			e.Handled = true;
		}

		void TypeChar(char c) {
			var doc = document!;
			if (caretColumn == HexColumn.Values) {
				int value = HexDigitValue(c);
				if (value < 0)
					return;
				byte b = doc[caretOffset];
				b = caretNibble == 0 ? (byte)((b & 0x0F) | (value << 4)) : (byte)((b & 0xF0) | value);
				selectionAnchor = null;
				doc.Write(caretOffset, new[] { b }, merge: true);
				if (caretNibble == 0)
					MoveCaret(caretOffset, nibble: 1);
				else if (caretOffset + 1 < doc.Length)
					MoveCaret(caretOffset + 1, nibble: 0);
			}
			else {
				if (c < 0x20)
					return;
				selectionAnchor = null;
				doc.Write(caretOffset, new[] { c <= 0x7E ? (byte)c : (byte)'?' }, merge: true);
				if (caretOffset + 1 < doc.Length)
					MoveCaret(caretOffset + 1);
				else
					InvalidateVisual();
			}
		}

		static int HexDigitValue(char c) {
			if (c >= '0' && c <= '9')
				return c - '0';
			if (c >= 'a' && c <= 'f')
				return c - 'a' + 10;
			if (c >= 'A' && c <= 'F')
				return c - 'A' + 10;
			return -1;
		}

		// The selected bytes (or the byte at the caret) as hex values or as ASCII text
		public string GetSelectedText() {
			if (document is null || document.Length == 0)
				return string.Empty;
			var (start, end) = Selection ?? (caretOffset, caretOffset + 1);
			var bytes = document.Read(start, (int)Math.Min(end - start, 16 * 1024 * 1024));
			if (caretColumn == HexColumn.Ascii) {
				var chars = new char[bytes.Length];
				for (int i = 0; i < bytes.Length; i++)
					chars[i] = bytes[i] >= 0x20 && bytes[i] <= 0x7E ? (char)bytes[i] : '.';
				return new string(chars);
			}
			return Convert.ToHexString(bytes);
		}

		async System.Threading.Tasks.Task CopyAsync() {
			var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
			if (clipboard is not null)
				await global::Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, GetSelectedText());
		}

		async System.Threading.Tasks.Task PasteAsync() {
			var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
			if (clipboard is null)
				return;
			var text = await global::Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(clipboard);
			if (!string.IsNullOrEmpty(text))
				Paste(text);
		}

		// Overwrites the bytes at the caret with hex values (values column) or with the text (ASCII column)
		public void Paste(string text) {
			if (document is null || document.Length == 0)
				return;
			byte[]? bytes = caretColumn == HexColumn.Values ? TryParseHex(text) : Encoding.ASCII.GetBytes(text);
			if (bytes is null || bytes.Length == 0)
				return;
			long start = Selection?.start ?? caretOffset;
			document.Write(start, bytes);
			MoveCaret(Math.Min(start + bytes.Length, document.Length - 1));
		}

		// Accepts eg. "4D5A90", "4D 5A 90", "0x4D, 0x5A"
		static byte[]? TryParseHex(string text) {
			var digits = new StringBuilder();
			for (int i = 0; i < text.Length; i++) {
				char c = text[i];
				if (c == '0' && i + 1 < text.Length && (text[i + 1] == 'x' || text[i + 1] == 'X')) {
					i++;
					continue;
				}
				if (HexDigitValue(c) >= 0)
					digits.Append(c);
				else if (!char.IsWhiteSpace(c) && c != ',' && c != '-')
					return null;
			}
			if (digits.Length == 0 || digits.Length % 2 != 0)
				return null;
			return Convert.FromHexString(digits.ToString());
		}

		(long offset, HexColumn column, int nibble)? HitTest(Point point) {
			if (document is null || document.Length == 0)
				return null;
			long line = topLine + (long)Math.Floor(Math.Max(0, point.Y - TextPadding / 2) / lineHeight);
			line = Math.Clamp(line, 0, LineCount - 1);
			int byteIndex;
			int nibble = 0;
			HexColumn column;
			if (point.X >= AsciiX - charWidth / 2) {
				column = HexColumn.Ascii;
				byteIndex = (int)Math.Floor((point.X - AsciiX) / charWidth);
			}
			else {
				column = HexColumn.Values;
				int c = (int)Math.Floor((point.X - ValuesX) / charWidth);
				if (c >= GetValuesCharIndex(BytesPerLine / 2))
					c--;
				c = Math.Max(0, c);
				byteIndex = c / 3;
				nibble = c % 3 == 0 ? 0 : 1;
			}
			byteIndex = Math.Clamp(byteIndex, 0, BytesPerLine - 1);
			long offset = Math.Min(line * BytesPerLine + byteIndex, document.Length - 1);
			return (offset, column, nibble);
		}

		protected override void OnPointerPressed(PointerPressedEventArgs e) {
			base.OnPointerPressed(e);
			Focus();
			var point = e.GetCurrentPoint(this);
			var hit = HitTest(point.Position);
			if (!point.Properties.IsLeftButtonPressed || hit is null)
				return;
			var (offset, column, nibble) = hit.Value;
			bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
			caretColumn = column;
			MoveCaret(offset, shift, nibble);
			if (!shift)
				selectionAnchor = offset;
			isSelectingWithMouse = true;
			e.Pointer.Capture(this);
			e.Handled = true;
		}

		protected override void OnPointerMoved(PointerEventArgs e) {
			base.OnPointerMoved(e);
			if (!isSelectingWithMouse)
				return;
			var hit = HitTest(e.GetPosition(this));
			if (hit is not null && hit.Value.offset != caretOffset)
				MoveCaret(hit.Value.offset, extendSelection: true);
		}

		protected override void OnPointerReleased(PointerReleasedEventArgs e) {
			base.OnPointerReleased(e);
			if (!isSelectingWithMouse)
				return;
			isSelectingWithMouse = false;
			e.Pointer.Capture(null);
			// A click without dragging doesn't select anything
			if (selectionAnchor == caretOffset)
				selectionAnchor = null;
			InvalidateVisual();
		}

		protected override void OnPointerWheelChanged(PointerWheelEventArgs e) {
			base.OnPointerWheelChanged(e);
			TopLine -= (long)Math.Round(e.Delta.Y * 3);
			e.Handled = true;
		}
	}
}

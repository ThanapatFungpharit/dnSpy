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
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using dnSpy.Avalonia.Themes;
using dnSpy.Contracts.Documents.Tabs.DocViewer;
using dnSpy.Contracts.Text;

namespace dnSpy.Avalonia.Controls {
	// Read-only text editor that shows decompiled code. References can be followed by clicking
	// them and all references to the one at the caret are highlighted.
	sealed class CodeViewer : TextEditor {
		readonly DecompiledTextColorizer colorizer = new DecompiledTextColorizer();
		DocumentViewerContent? content;
		Point? pointerPressedPosition;

		public event EventHandler<ReferenceInfo>? ReferenceClicked;

		public DocumentViewerContent? Content => content;

		protected override Type StyleKeyOverride => typeof(TextEditor);

		public CodeViewer() {
			IsReadOnly = true;
			ShowLineNumbers = true;
			LineNumbersMargin = new Thickness(4, 0, 8, 0);
			FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, Liberation Mono, Noto Sans Mono, monospace");
			FontSize = 13;
			Options.EnableHyperlinks = false;
			Options.EnableEmailHyperlinks = false;
			Options.HighlightCurrentLine = true;
			TextArea.TextView.LineTransformers.Add(colorizer);
			TextArea.Caret.PositionChanged += Caret_PositionChanged;
			TextArea.AddHandler(PointerPressedEvent, TextArea_PointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
			TextArea.AddHandler(PointerReleasedEvent, TextArea_PointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
			TextArea.AddHandler(PointerMovedEvent, TextArea_PointerMoved, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
			UpdateThemeColors();
		}

		protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
			base.OnAttachedToVisualTree(e);
			if (Application.Current is Application app)
				app.ActualThemeVariantChanged += Application_ActualThemeVariantChanged;
			UpdateThemeColors();
		}

		protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
			base.OnDetachedFromVisualTree(e);
			if (Application.Current is Application app)
				app.ActualThemeVariantChanged -= Application_ActualThemeVariantChanged;
		}

		void Application_ActualThemeVariantChanged(object? sender, EventArgs e) {
			UpdateThemeColors();
			TextArea.TextView.Redraw();
		}

		void UpdateThemeColors() {
			var theme = TextColorTheme.Current;
			if (theme.DefaultText.Background is IBrush bg)
				Background = bg;
			if (theme.DefaultText.Foreground is IBrush fg)
				Foreground = fg;
			if (theme.LineNumber.Foreground is IBrush lineNumber)
				LineNumbersForeground = lineNumber;
			if (theme.SelectedText.Background is IBrush selection)
				TextArea.SelectionBrush = selection;
			if (theme.CurrentLine.Foreground is IBrush currentLine)
				TextArea.TextView.CurrentLineBorder = new Pen(currentLine, 2);
		}

		public void SetContent(DocumentViewerContent newContent) {
			content = newContent;
			colorizer.Content = newContent;
			colorizer.HighlightedReference = null;
			Document = new TextDocument(newContent.Text) { UndoStack = { SizeLimit = 0 } };
		}

		public void SetText(string text) {
			content = null;
			colorizer.Content = null;
			colorizer.HighlightedReference = null;
			Document = new TextDocument(text) { UndoStack = { SizeLimit = 0 } };
		}

		public void MoveCaret(int offset) {
			CaretOffset = Math.Clamp(offset, 0, Document.TextLength);
			// The new document hasn't been measured yet
			var document = Document;
			Dispatcher.UIThread.Post(() => {
				if (Document == document)
					TextArea.Caret.BringCaretToView();
			}, DispatcherPriority.Loaded);
		}

		// Moves the caret to the definition of a member, eg. the method's name when a method was decompiled
		public bool MoveCaretToDefinition(object? reference) {
			if (content is null || reference is null)
				return false;
			foreach (var spanData in content.ReferenceCollection) {
				if (spanData.Data.IsDefinition && Equals(spanData.Data.Reference, reference)) {
					MoveCaret(spanData.TextSpan.Start);
					return true;
				}
			}
			return false;
		}

		SpanData<ReferenceInfo>? GetReferenceAt(PointerEventArgs e) {
			if (content is null)
				return null;
			var pos = GetPositionFromPoint(e.GetPosition(this));
			if (pos is null || pos.Value.IsAtEndOfLine)
				return null;
			int offset = Document.GetOffset(pos.Value.Location);
			var spanData = content.ReferenceCollection.Find(offset, allowIntersection: false);
			if (spanData is null || spanData.Value.Data.Reference is null)
				return null;
			return spanData;
		}

		void TextArea_PointerPressed(object? sender, PointerPressedEventArgs e) =>
			pointerPressedPosition = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ? e.GetPosition(this) : null;

		void TextArea_PointerReleased(object? sender, PointerReleasedEventArgs e) {
			var pressedPosition = pointerPressedPosition;
			pointerPressedPosition = null;
			if (pressedPosition is null || e.InitialPressMouseButton != MouseButton.Left || SelectionLength != 0)
				return;
			var delta = e.GetPosition(this) - pressedPosition.Value;
			if (Math.Abs(delta.X) > 4 || Math.Abs(delta.Y) > 4)
				return;
			if (GetReferenceAt(e) is SpanData<ReferenceInfo> spanData && CanFollow(spanData.Data))
				ReferenceClicked?.Invoke(this, spanData.Data);
		}

		void TextArea_PointerMoved(object? sender, PointerEventArgs e) =>
			TextArea.TextView.Cursor = GetReferenceAt(e) is SpanData<ReferenceInfo> spanData && CanFollow(spanData.Data) ? new Cursor(StandardCursorType.Hand) : null;

		// Locals and definitions are only highlighted
		static bool CanFollow(in ReferenceInfo info) => !info.IsLocal && !info.IsDefinition && !info.NoFollow;

		void Caret_PositionChanged(object? sender, EventArgs e) {
			object? reference = null;
			if (content is not null) {
				var spanData = content.ReferenceCollection.Find(CaretOffset);
				if (spanData is not null && !spanData.Value.Data.IsHidden)
					reference = spanData.Value.Data.Reference;
			}
			if (Equals(reference, colorizer.HighlightedReference))
				return;
			colorizer.HighlightedReference = reference;
			TextArea.TextView.Redraw();
		}

		sealed class DecompiledTextColorizer : DocumentColorizingTransformer {
			public DocumentViewerContent? Content { get; set; }
			public object? HighlightedReference { get; set; }

			protected override void ColorizeLine(DocumentLine line) {
				var content = Content;
				if (content is null || line.Length == 0)
					return;
				int lineStart = line.Offset;
				int lineEnd = line.EndOffset;
				var theme = TextColorTheme.Current;

				var colors = content.ColorCollection;
				int index = colors.GetStartIndex(lineStart);
				if (index >= 0) {
					for (; index < colors.Count; index++) {
						var spanData = colors[index];
						if (spanData.TextSpan.Start >= lineEnd)
							break;
						int start = Math.Max(lineStart, spanData.TextSpan.Start);
						int end = Math.Min(lineEnd, spanData.TextSpan.End);
						if (start >= end)
							continue;
						var style = theme.Get(spanData.Data);
						if (style.Foreground is null && !style.Bold && !style.Italic)
							continue;
						ChangeLinePart(start, end, element => {
							var props = element.TextRunProperties;
							if (style.Foreground is not null)
								props.SetForegroundBrush(style.Foreground);
							if (style.Bold || style.Italic) {
								var typeface = props.Typeface;
								props.SetTypeface(new Typeface(typeface.FontFamily,
									style.Italic ? FontStyle.Italic : typeface.Style,
									style.Bold ? FontWeight.Bold : typeface.Weight));
							}
						});
					}
				}

				var highlighted = HighlightedReference;
				if (highlighted is not null) {
					foreach (var spanData in content.ReferenceCollection.Find(lineStart, lineEnd - lineStart)) {
						if (!Equals(spanData.Data.Reference, highlighted))
							continue;
						var brush = (spanData.Data.IsDefinition ? theme.HighlightedDefinition : theme.HighlightedReference).Background;
						if (brush is null)
							continue;
						int start = Math.Max(lineStart, spanData.TextSpan.Start);
						int end = Math.Min(lineEnd, spanData.TextSpan.End);
						if (start < end)
							ChangeLinePart(start, end, element => element.TextRunProperties.SetBackgroundBrush(brush));
					}
				}
			}
		}
	}
}

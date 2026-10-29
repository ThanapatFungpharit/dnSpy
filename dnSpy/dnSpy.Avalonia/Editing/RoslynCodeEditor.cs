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
using System.Collections.Immutable;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using dnSpy.Avalonia.Images;
using dnSpy.Avalonia.Themes;
using dnSpy.Contracts.Images;
using dnSpy.Contracts.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Text;
using RoslynCompletionList = Microsoft.CodeAnalysis.Completion.CompletionList;
using RoslynDocument = Microsoft.CodeAnalysis.Document;
using RoslynTextSpan = Microsoft.CodeAnalysis.Text.TextSpan;
using TextDocument = AvaloniaEdit.Document.TextDocument;

namespace dnSpy.Avalonia.Editing {
	// Code editor used by the edit code window. The text is kept in sync with a document in a RoslynProject
	// which is used for syntax highlighting, error squiggles, completion (Ctrl+Space or when typing),
	// signature help (when typing '(' or ',', or Ctrl+Shift+Space) and quick info (hover).
	sealed class RoslynCodeEditor : TextEditor {
		readonly RoslynProject project;
		readonly DocumentId documentId;
		readonly ClassificationColorizer colorizer = new ClassificationColorizer();
		readonly DiagnosticRenderer diagnosticRenderer = new DiagnosticRenderer();
		readonly DispatcherTimer updateTimer;
		CancellationTokenSource? updateCancellation;
		CancellationTokenSource? completionCancellation;
		CancellationTokenSource? signatureHelpCancellation;
		CancellationTokenSource? quickInfoCancellation;
		CompletionWindow? completionWindow;
		OverloadInsightWindow? insightWindow;
		int textVersion;

		protected override Type StyleKeyOverride => typeof(TextEditor);

		public DocumentId DocumentId => documentId;

		// Raised when new diagnostics have been calculated
		public event EventHandler? DiagnosticsUpdated;
		// Used by the smoke test
		internal CompletionWindow? ActiveCompletionWindow => completionWindow;
		internal int DiagnosticsUpdateCount { get; private set; }
		public ImmutableArray<Diagnostic> Diagnostics => diagnosticRenderer.Diagnostics;

		public RoslynCodeEditor(RoslynProject project, DocumentId documentId, string text) {
			this.project = project;
			this.documentId = documentId;
			ShowLineNumbers = true;
			LineNumbersMargin = new Thickness(4, 0, 8, 0);
			FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, Liberation Mono, Noto Sans Mono, monospace");
			FontSize = 13;
			Options.EnableHyperlinks = false;
			Options.EnableEmailHyperlinks = false;
			Options.HighlightCurrentLine = true;
			Options.ConvertTabsToSpaces = false;
			Options.IndentationSize = 4;
			Document = new TextDocument(text);
			TextArea.TextView.LineTransformers.Add(colorizer);
			TextArea.TextView.BackgroundRenderers.Add(diagnosticRenderer);
			Document.Changed += Document_Changed;
			TextArea.TextEntering += TextArea_TextEntering;
			TextArea.TextEntered += TextArea_TextEntered;
			TextArea.Caret.PositionChanged += Caret_PositionChanged;
			TextArea.AddHandler(KeyDownEvent, TextArea_KeyDown, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
			TextArea.TextView.PointerHover += TextView_PointerHover;
			TextArea.TextView.PointerHoverStopped += TextView_PointerHoverStopped;
			updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
			updateTimer.Tick += (s, e) => {
				updateTimer.Stop();
				_ = UpdateClassificationAndDiagnosticsAsync();
			};
			UpdateThemeColors();
			_ = UpdateClassificationAndDiagnosticsAsync();
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
			updateTimer.Stop();
			updateCancellation?.Cancel();
			completionCancellation?.Cancel();
			signatureHelpCancellation?.Cancel();
			quickInfoCancellation?.Cancel();
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

		RoslynDocument CurrentDocument => project.GetDocument(documentId);

		void Document_Changed(object? sender, DocumentChangeEventArgs e) {
			textVersion++;
			project.UpdateText(documentId, Document.Text);
			// Keep the old colors and squiggles where the text didn't change until the new ones have been calculated
			colorizer.OnTextChanged(e.Offset, e.RemovalLength, e.InsertionLength);
			diagnosticRenderer.OnTextChanged(e.Offset, e.RemovalLength, e.InsertionLength);
			updateTimer.Stop();
			updateTimer.Start();
		}

		async Task UpdateClassificationAndDiagnosticsAsync() {
			updateCancellation?.Cancel();
			var cancellation = updateCancellation = new CancellationTokenSource();
			var document = CurrentDocument;
			int version = textVersion;
			try {
				var (spans, diagnostics) = await Task.Run(async () => {
					var text = await document.GetTextAsync(cancellation.Token).ConfigureAwait(false);
					var classified = await RoslynProject.GetClassifiedSpansAsync(document, new RoslynTextSpan(0, text.Length), cancellation.Token).ConfigureAwait(false);
					var colors = ClassificationColorizer.ToColorSpans(classified);
					var diags = await RoslynProject.GetDiagnosticsAsync(document, cancellation.Token).ConfigureAwait(false);
					return (colors, diags);
				}, cancellation.Token);
				if (cancellation.IsCancellationRequested || version != textVersion)
					return;
				colorizer.SetSpans(spans);
				diagnosticRenderer.SetDiagnostics(diagnostics);
				TextArea.TextView.Redraw();
				DiagnosticsUpdateCount++;
				DiagnosticsUpdated?.Invoke(this, EventArgs.Empty);
			}
			catch (OperationCanceledException) {
			}
		}

		void TextArea_KeyDown(object? sender, KeyEventArgs e) {
			if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.Control) {
				_ = ShowCompletionAsync(triggerChar: null);
				e.Handled = true;
			}
			else if (e.Key == Key.Space && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift)) {
				_ = ShowSignatureHelpAsync();
				e.Handled = true;
			}
			else if (e.Key == Key.Escape && insightWindow is not null) {
				insightWindow.Close();
				e.Handled = true;
			}
		}

		void TextArea_TextEntering(object? sender, TextInputEventArgs e) {
			// Commit the selected completion item when a char that can't be part of an identifier is typed
			if (completionWindow is not null && !string.IsNullOrEmpty(e.Text) && !IsIdentifierChar(e.Text[0]))
				completionWindow.CompletionList.RequestInsertion(e);
		}

		void TextArea_TextEntered(object? sender, TextInputEventArgs e) {
			if (string.IsNullOrEmpty(e.Text) || e.Text.Length != 1)
				return;
			char c = e.Text[0];
			if (completionWindow is null) {
				var document = CurrentDocument;
				if (document.TryGetText(out var sourceText) && RoslynProject.ShouldTriggerCompletion(document, sourceText, CaretOffset, c))
					_ = ShowCompletionAsync(c);
			}
			if (c == '(' || c == ',' || c == '[' || c == '<' && insightWindow is not null)
				_ = ShowSignatureHelpAsync();
			else if ((c == ')' || c == ']') && insightWindow is not null)
				_ = ShowSignatureHelpAsync();
		}

		static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

		async Task ShowCompletionAsync(char? triggerChar) {
			completionCancellation?.Cancel();
			var cancellation = completionCancellation = new CancellationTokenSource();
			var document = CurrentDocument;
			int position = CaretOffset;
			int version = textVersion;
			RoslynCompletionList? list;
			try {
				list = await Task.Run(() => RoslynProject.GetCompletionsAsync(document, position, triggerChar, cancellation.Token), cancellation.Token);
			}
			catch (OperationCanceledException) {
				return;
			}
			if (cancellation.IsCancellationRequested || list is null || list.ItemsList.Count == 0)
				return;
			// The user could've typed more chars, that's fine, the completion window filters the items
			if (CaretOffset < list.Span.Start || CaretOffset > Document.TextLength)
				return;

			completionWindow?.Close();
			var window = new CompletionWindow(TextArea) {
				StartOffset = list.Span.Start,
				EndOffset = CaretOffset,
				CloseWhenCaretAtBeginning = triggerChar is null,
				MinWidth = 300,
			};
			var data = window.CompletionList.CompletionData;
			foreach (var item in list.ItemsList.OrderBy(a => a.SortText, StringComparer.OrdinalIgnoreCase))
				data.Add(new RoslynCompletionData(this, document, item));
			window.Closed += (s, e) => {
				if (completionWindow == window)
					completionWindow = null;
			};
			completionWindow = window;
			window.Show();
			// Select the best match of the text that's already been typed, eg. after Ctrl+Space
			if (CaretOffset > list.Span.Start && version == textVersion)
				window.CompletionList.SelectItem(Document.GetText(list.Span.Start, CaretOffset - list.Span.Start));
		}

		// Called by the completion item when it's committed. Most items are just inserted but some items
		// (eg. override completion) change other text so Roslyn must create the change.
		internal void Commit(RoslynDocument document, CompletionItem item, ISegment segment) {
			if (!item.IsComplexTextEdit) {
				Document.Replace(segment, item.DisplayText);
				return;
			}
			int version = textVersion;
			_ = CommitComplexAsync(document, item, version);
		}

		async Task CommitComplexAsync(RoslynDocument document, CompletionItem item, int version) {
			CompletionChange? change;
			try {
				change = await Task.Run(() => RoslynProject.GetCompletionChangeAsync(document, item, CancellationToken.None));
			}
			catch (Exception) {
				return;
			}
			if (change is null || version != textVersion)
				return;
			// Apply the changes from the end so the earlier offsets stay valid
			Document.BeginUpdate();
			try {
				foreach (var textChange in change.TextChanges.OrderByDescending(a => a.Span.Start))
					Document.Replace(textChange.Span.Start, textChange.Span.Length, textChange.NewText ?? string.Empty);
			}
			finally {
				Document.EndUpdate();
			}
			if (change.NewPosition is int newPosition && newPosition <= Document.TextLength)
				CaretOffset = newPosition;
		}

		async Task ShowSignatureHelpAsync() {
			signatureHelpCancellation?.Cancel();
			var cancellation = signatureHelpCancellation = new CancellationTokenSource();
			var document = CurrentDocument;
			int position = CaretOffset;
			SignatureHelpInfo? info;
			try {
				info = await Task.Run(() => RoslynProject.GetSignatureHelpAsync(document, position, cancellation.Token), cancellation.Token);
			}
			catch (OperationCanceledException) {
				return;
			}
			if (cancellation.IsCancellationRequested)
				return;
			if (info is null) {
				insightWindow?.Close();
				return;
			}
			if (insightWindow is not null && insightWindow.Provider is SignatureOverloadProvider provider && insightWindow.StartOffset == info.ArgumentListStart) {
				provider.Update(info);
				return;
			}
			insightWindow?.Close();
			var window = new OverloadInsightWindow(TextArea) {
				StartOffset = info.ArgumentListStart,
				Provider = new SignatureOverloadProvider(info),
			};
			window.Closed += (s, e) => {
				if (insightWindow == window)
					insightWindow = null;
			};
			insightWindow = window;
			window.Show();
		}

		void Caret_PositionChanged(object? sender, EventArgs e) {
			// Update the current parameter or close it if the caret left the argument list
			if (insightWindow is not null) {
				if (CaretOffset < insightWindow.StartOffset)
					insightWindow.Close();
				else
					_ = ShowSignatureHelpAsync();
			}
		}

		async void TextView_PointerHover(object? sender, PointerEventArgs e) {
			var pos = GetPositionFromPoint(e.GetPosition(this));
			if (pos is null)
				return;
			int offset = Document.GetOffset(pos.Value.Location);
			string? text = diagnosticRenderer.GetMessage(offset);
			if (text is null) {
				quickInfoCancellation?.Cancel();
				var cancellation = quickInfoCancellation = new CancellationTokenSource();
				var document = CurrentDocument;
				try {
					text = await Task.Run(() => RoslynProject.GetQuickInfoAsync(document, offset, cancellation.Token), cancellation.Token);
				}
				catch (OperationCanceledException) {
					return;
				}
				catch (Exception) {
					return;
				}
				if (cancellation.IsCancellationRequested)
					return;
			}
			if (text is null)
				return;
			var textView = TextArea.TextView;
			ToolTip.SetTip(textView, new TextBlock { Text = text, MaxWidth = 700, TextWrapping = TextWrapping.Wrap, FontFamily = FontFamily });
			ToolTip.SetPlacement(textView, PlacementMode.Pointer);
			ToolTip.SetIsOpen(textView, true);
		}

		void TextView_PointerHoverStopped(object? sender, PointerEventArgs e) {
			quickInfoCancellation?.Cancel();
			var textView = TextArea.TextView;
			ToolTip.SetIsOpen(textView, false);
			ToolTip.SetTip(textView, null);
		}

		public void MoveCaretTo(int line, int column) {
			if (Document.LineCount == 0)
				return;
			var docLine = Document.GetLineByNumber(Math.Clamp(line + 1, 1, Document.LineCount));
			CaretOffset = docLine.Offset + Math.Clamp(column, 0, docLine.Length);
			TextArea.ClearSelection();
			var document = Document;
			Dispatcher.UIThread.Post(() => {
				if (Document == document)
					TextArea.Caret.BringCaretToView();
			}, DispatcherPriority.Loaded);
			TextArea.Focus();
		}

		sealed class RoslynCompletionData : ICompletionData {
			readonly RoslynCodeEditor editor;
			readonly RoslynDocument document;
			readonly CompletionItem item;
			TextBlock? description;

			public RoslynCompletionData(RoslynCodeEditor editor, RoslynDocument document, CompletionItem item) {
				this.editor = editor;
				this.document = document;
				this.item = item;
			}

			public IImage? Image => ImageCache.Get(GetImageName(item.Tags));
			public string Text => item.FilterText;
			public object Content => item.DisplayTextPrefix + item.DisplayText + item.DisplayTextSuffix;
			public double Priority => item.Rules.MatchPriority;

			// Calculated when the item is selected for the first time
			public object Description {
				get {
					if (description is null) {
						description = new TextBlock { MaxWidth = 600, TextWrapping = TextWrapping.Wrap };
						_ = LoadDescriptionAsync(description);
					}
					return description;
				}
			}

			async Task LoadDescriptionAsync(TextBlock textBlock) {
				try {
					var text = await Task.Run(() => RoslynProject.GetCompletionDescriptionAsync(document, item, CancellationToken.None));
					textBlock.Text = text;
					textBlock.IsVisible = text is not null;
				}
				catch (Exception) {
					textBlock.IsVisible = false;
				}
			}

			public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs) =>
				editor.Commit(document, item, completionSegment);
		}

		static string GetImageName(ImmutableArray<string> tags) {
			string access = "Public";
			foreach (var tag in tags) {
				switch (tag) {
				case "Private": access = "Private"; break;
				case "Protected": access = "Protected"; break;
				case "Internal": access = "Internal"; break;
				}
			}
			foreach (var tag in tags) {
				switch (tag) {
				case "Class":			return "Class" + access;
				case "Structure":		return "Structure" + access;
				case "Interface":		return "Interface" + access;
				case "Enum":			return "Enumeration" + access;
				case "Delegate":		return "Delegate" + access;
				case "Module":			return "Module" + access;
				case "EnumMember":		return DsImageStrings.EnumerationItemPublic;
				case "Constant":		return "Constant" + access;
				case "Field":			return "Field" + access;
				case "Event":			return "Event" + access;
				case "Method":			return tags.Contains("Extension") ? DsImageStrings.ExtensionMethod : "Method" + access;
				case "ExtensionMethod":	return DsImageStrings.ExtensionMethod;
				case "Operator":		return "Operator" + access;
				case "Property":		return access == "Public" ? DsImageStrings.Property : "Property" + access;
				case "Namespace":		return DsImageStrings.Namespace;
				case "Local":
				case "Parameter":
				case "RangeVariable":	return DsImageStrings.LocalVariable;
				case "TypeParameter":	return DsImageStrings.Type;
				case "Intrinsic":		return DsImageStrings.Type;
				case "Keyword":			return DsImageStrings.IntellisenseKeyword;
				case "Label":			return DsImageStrings.Label;
				case "Snippet":			return DsImageStrings.Snippet;
				case "Assembly":		return DsImageStrings.Assembly;
				}
			}
			return DsImageStrings.IntellisenseKeyword;
		}

		sealed class SignatureOverloadProvider : IOverloadProvider {
			SignatureHelpInfo info;
			int selectedIndex;

			public event PropertyChangedEventHandler? PropertyChanged;

			public SignatureOverloadProvider(SignatureHelpInfo info) {
				this.info = info;
				selectedIndex = info.SelectedIndex;
			}

			public void Update(SignatureHelpInfo newInfo) {
				// Keep the overload the user selected with the arrow keys if it's still there
				var selectedText = selectedIndex < info.Items.Count ? info.Items[selectedIndex].Text : null;
				info = newInfo;
				int index = newInfo.Items.ToList().FindIndex(a => a.Text == selectedText);
				selectedIndex = index >= 0 && index != newInfo.SelectedIndex && selectedIndex != info.SelectedIndex ? index : newInfo.SelectedIndex;
				OnPropertyChanged(nameof(Count));
				OnPropertyChanged(nameof(SelectedIndex));
				OnAllChanged();
			}

			public int SelectedIndex {
				get => selectedIndex;
				set {
					if (selectedIndex == value)
						return;
					selectedIndex = value;
					OnPropertyChanged(nameof(SelectedIndex));
					OnAllChanged();
				}
			}

			public int Count => info.Items.Count;
			public string CurrentIndexText => $"{selectedIndex + 1} of {info.Items.Count}";
			public object CurrentHeader => info.Items[Math.Clamp(selectedIndex, 0, info.Items.Count - 1)].Text;
			public object CurrentContent => info.Items[Math.Clamp(selectedIndex, 0, info.Items.Count - 1)].Documentation ?? string.Empty;

			void OnAllChanged() {
				OnPropertyChanged(nameof(CurrentIndexText));
				OnPropertyChanged(nameof(CurrentHeader));
				OnPropertyChanged(nameof(CurrentContent));
			}

			void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
		}

		// Colors the text with Roslyn's classifications using the same colors as the decompiled code
		sealed class ClassificationColorizer : DocumentColorizingTransformer {
			// Sorted by start offset and don't overlap
			List<(int start, int length, TextColor color)> spans = new List<(int start, int length, TextColor color)>();

			public void SetSpans(List<(int start, int length, TextColor color)> newSpans) => spans = newSpans;

			public void OnTextChanged(int offset, int removalLength, int insertionLength) {
				int delta = insertionLength - removalLength;
				var newSpans = new List<(int start, int length, TextColor color)>(spans.Count);
				foreach (var span in spans) {
					int end = span.start + span.length;
					if (end <= offset)
						newSpans.Add(span);
					else if (span.start >= offset + removalLength)
						newSpans.Add((span.start + delta, span.length, span.color));
					// else it overlaps the changed text, it's colored again later
				}
				spans = newSpans;
			}

			public static List<(int start, int length, TextColor color)> ToColorSpans(IEnumerable<ClassifiedSpan> classifiedSpans) {
				var result = new List<(int start, int length, TextColor color)>();
				// The same span can have more than one classification, eg. 'method name' and 'static symbol'
				foreach (var group in classifiedSpans.GroupBy(a => a.TextSpan).OrderBy(a => a.Key.Start)) {
					bool isStatic = group.Any(a => a.ClassificationType == ClassificationTypeNames.StaticSymbol);
					TextColor? color = null;
					foreach (var classified in group) {
						color = GetColor(classified.ClassificationType, isStatic);
						if (color is not null)
							break;
					}
					if (color is TextColor c && group.Key.Length > 0) {
						if (result.Count > 0 && result[result.Count - 1].start + result[result.Count - 1].length > group.Key.Start)
							continue;
						result.Add((group.Key.Start, group.Key.Length, c));
					}
				}
				return result;
			}

			static TextColor? GetColor(string classification, bool isStatic) {
				switch (classification) {
				case ClassificationTypeNames.Comment:					return TextColor.Comment;
				case ClassificationTypeNames.ExcludedCode:				return TextColor.ExcludedCode;
				case ClassificationTypeNames.Keyword:
				case ClassificationTypeNames.ControlKeyword:			return TextColor.Keyword;
				case ClassificationTypeNames.NumericLiteral:			return TextColor.Number;
				case ClassificationTypeNames.Operator:
				case ClassificationTypeNames.OperatorOverloaded:		return TextColor.Operator;
				case ClassificationTypeNames.Punctuation:				return TextColor.Punctuation;
				case ClassificationTypeNames.PreprocessorKeyword:		return TextColor.PreprocessorKeyword;
				case ClassificationTypeNames.PreprocessorText:			return TextColor.PreprocessorText;
				case ClassificationTypeNames.StringLiteral:				return TextColor.String;
				case ClassificationTypeNames.VerbatimStringLiteral:		return TextColor.VerbatimString;
				case ClassificationTypeNames.StringEscapeCharacter:		return TextColor.String;
				case ClassificationTypeNames.ClassName:
				case ClassificationTypeNames.RecordClassName:			return isStatic ? TextColor.StaticType : TextColor.Type;
				case ClassificationTypeNames.StructName:
				case ClassificationTypeNames.RecordStructName:			return TextColor.ValueType;
				case ClassificationTypeNames.InterfaceName:				return TextColor.Interface;
				case ClassificationTypeNames.EnumName:					return TextColor.Enum;
				case ClassificationTypeNames.DelegateName:				return TextColor.Delegate;
				case ClassificationTypeNames.ModuleName:				return TextColor.Module;
				case ClassificationTypeNames.TypeParameterName:			return TextColor.TypeGenericParameter;
				case ClassificationTypeNames.MethodName:				return isStatic ? TextColor.StaticMethod : TextColor.InstanceMethod;
				case ClassificationTypeNames.ExtensionMethodName:		return TextColor.ExtensionMethod;
				case ClassificationTypeNames.FieldName:					return isStatic ? TextColor.StaticField : TextColor.InstanceField;
				case ClassificationTypeNames.ConstantName:				return TextColor.LiteralField;
				case ClassificationTypeNames.EnumMemberName:			return TextColor.EnumField;
				case ClassificationTypeNames.PropertyName:				return isStatic ? TextColor.StaticProperty : TextColor.InstanceProperty;
				case ClassificationTypeNames.EventName:					return isStatic ? TextColor.StaticEvent : TextColor.InstanceEvent;
				case ClassificationTypeNames.LocalName:					return TextColor.Local;
				case ClassificationTypeNames.ParameterName:				return TextColor.Parameter;
				case ClassificationTypeNames.NamespaceName:				return TextColor.Namespace;
				case ClassificationTypeNames.LabelName:					return TextColor.Label;
				case ClassificationTypeNames.XmlDocCommentAttributeName:	return TextColor.XmlDocCommentAttributeName;
				case ClassificationTypeNames.XmlDocCommentAttributeQuotes:	return TextColor.XmlDocCommentAttributeQuotes;
				case ClassificationTypeNames.XmlDocCommentAttributeValue:	return TextColor.XmlDocCommentAttributeValue;
				case ClassificationTypeNames.XmlDocCommentCDataSection:		return TextColor.XmlDocCommentCDataSection;
				case ClassificationTypeNames.XmlDocCommentComment:			return TextColor.XmlDocCommentComment;
				case ClassificationTypeNames.XmlDocCommentDelimiter:		return TextColor.XmlDocCommentDelimiter;
				case ClassificationTypeNames.XmlDocCommentEntityReference:	return TextColor.XmlDocCommentEntityReference;
				case ClassificationTypeNames.XmlDocCommentName:				return TextColor.XmlDocCommentName;
				case ClassificationTypeNames.XmlDocCommentProcessingInstruction: return TextColor.XmlDocCommentProcessingInstruction;
				case ClassificationTypeNames.XmlDocCommentText:				return TextColor.XmlDocCommentText;
				default:												return null;
				}
			}

			protected override void ColorizeLine(DocumentLine line) {
				if (spans.Count == 0 || line.Length == 0)
					return;
				int lineStart = line.Offset;
				int lineEnd = line.EndOffset;
				var theme = TextColorTheme.Current;
				int index = FindFirstSpan(lineStart);
				for (; index < spans.Count; index++) {
					var span = spans[index];
					if (span.start >= lineEnd)
						break;
					int start = Math.Max(lineStart, span.start);
					int end = Math.Min(lineEnd, span.start + span.length);
					if (start >= end)
						continue;
					var style = theme.Get(span.color);
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

			// Index of the first span that ends after offset
			int FindFirstSpan(int offset) {
				int lo = 0, hi = spans.Count - 1;
				while (lo <= hi) {
					int mid = (lo + hi) / 2;
					if (spans[mid].start + spans[mid].length <= offset)
						lo = mid + 1;
					else
						hi = mid - 1;
				}
				return lo;
			}
		}

		// Draws squiggles under errors (red) and warnings (green)
		sealed class DiagnosticRenderer : IBackgroundRenderer {
			List<(int start, int length, DiagnosticSeverity severity, string message)> spans = new List<(int start, int length, DiagnosticSeverity severity, string message)>();

			public ImmutableArray<Diagnostic> Diagnostics { get; private set; } = ImmutableArray<Diagnostic>.Empty;

			public KnownLayer Layer => KnownLayer.Selection;

			public void SetDiagnostics(ImmutableArray<Diagnostic> diagnostics) {
				Diagnostics = diagnostics;
				var newSpans = new List<(int start, int length, DiagnosticSeverity severity, string message)>();
				foreach (var d in diagnostics) {
					if (!d.Location.IsInSource)
						continue;
					var span = d.Location.SourceSpan;
					newSpans.Add((span.Start, span.Length, d.Severity, $"{d.Id}: {d.GetMessage()}"));
				}
				spans = newSpans;
			}

			public void OnTextChanged(int offset, int removalLength, int insertionLength) {
				int delta = insertionLength - removalLength;
				var newSpans = new List<(int start, int length, DiagnosticSeverity severity, string message)>(spans.Count);
				foreach (var span in spans) {
					if (span.start + span.length <= offset)
						newSpans.Add(span);
					else if (span.start >= offset + removalLength)
						newSpans.Add((span.start + delta, span.length, span.severity, span.message));
				}
				spans = newSpans;
			}

			public string? GetMessage(int offset) {
				var messages = spans.Where(a => a.start <= offset && offset <= a.start + Math.Max(1, a.length)).Select(a => a.message).Distinct().ToArray();
				return messages.Length == 0 ? null : string.Join(Environment.NewLine, messages);
			}

			public void Draw(TextView textView, DrawingContext drawingContext) {
				if (spans.Count == 0 || !textView.VisualLinesValid)
					return;
				var visualLines = textView.VisualLines;
				if (visualLines.Count == 0)
					return;
				int viewStart = visualLines[0].FirstDocumentLine.Offset;
				int viewEnd = visualLines[visualLines.Count - 1].LastDocumentLine.EndOffset;
				var errorPen = new Pen(TextColorTheme.Current.Get(TextColor.Error).Foreground ?? Brushes.Red, 1);
				var warningPen = new Pen(Brushes.Green, 1);
				int textLength = textView.Document?.TextLength ?? 0;
				foreach (var span in spans) {
					int start = span.start, length = span.length;
					if (start > textLength)
						continue;
					// Missing tokens have no length, show the squiggle under the previous char
					if (length == 0) {
						if (start > 0)
							start--;
						length = 1;
					}
					length = Math.Min(length, textLength - start);
					if (length <= 0 || start + length < viewStart || start > viewEnd)
						continue;
					var pen = span.severity == DiagnosticSeverity.Error ? errorPen : warningPen;
					var segment = new TextSegment { StartOffset = start, Length = length };
					foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
						DrawSquiggle(drawingContext, pen, rect);
				}
			}

			static void DrawSquiggle(DrawingContext drawingContext, IPen pen, Rect rect) {
				const double amplitude = 1.5, step = 2;
				double y = rect.Bottom - amplitude;
				var geometry = new StreamGeometry();
				using (var ctx = geometry.Open()) {
					ctx.BeginFigure(new Point(rect.Left, y), false);
					bool up = true;
					for (double x = rect.Left + step; x <= rect.Right + step / 2; x += step) {
						ctx.LineTo(new Point(x, up ? y - amplitude : y + amplitude));
						up = !up;
					}
					ctx.EndFigure(false);
				}
				drawingContext.DrawGeometry(null, pen, geometry);
			}
		}
	}
}

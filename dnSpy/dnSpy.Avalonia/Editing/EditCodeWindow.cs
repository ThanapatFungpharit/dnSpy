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
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using dnlib.DotNet;
using dnSpy.AsmEditor.Compiler;
using dnSpy.Avalonia.Dialogs;
using dnSpy.Avalonia.Images;
using dnSpy.Contracts.AsmEditor.Compiler;
using dnSpy.Contracts.Images;

namespace dnSpy.Avalonia.Editing {
	sealed class DiagnosticItem {
		public CompilerDiagnostic Diagnostic { get; }
		public string Location { get; }

		public DiagnosticItem(CompilerDiagnostic diagnostic) {
			Diagnostic = diagnostic;
			Location = diagnostic.LineLocationSpan is LineLocationSpan span ? $"{diagnostic.Filename}{span.StartLinePosition}" : diagnostic.Filename ?? string.Empty;
		}

		public string ImageName {
			get {
				switch (Diagnostic.Severity) {
				case CompilerDiagnosticSeverity.Error:		return DsImageStrings.StatusError;
				case CompilerDiagnosticSeverity.Warning:	return DsImageStrings.StatusWarning;
				default:									return DsImageStrings.StatusInformation;
				}
			}
		}
	}

	// The Avalonia version of the WPF EditCodeDlg. Shows the decompiled code in editors, compiles it and
	// returns the ModuleImporter (see ShowDialog<T>()) if it compiled. The caller applies the changes.
	sealed class EditCodeWindow : Window {
		readonly EditCodeSession session;
		readonly Func<string, ModuleDef?> openModule;
		readonly CancellationTokenSource cancellation = new CancellationTokenSource();
		readonly TabControl documentsTabControl;
		readonly ListBox diagnosticsListBox;
		readonly ObservableCollection<DiagnosticItem> diagnostics = new ObservableCollection<DiagnosticItem>();
		readonly Button compileButton;
		readonly Button addReferenceButton;
		readonly Button addDocumentButton;
		readonly TextBlock statusText;
		readonly List<RoslynCodeEditor> editors = new List<RoslynCodeEditor>();
		bool isCompiling;

		// openModule opens an assembly that the user picked, used to add a reference
		public EditCodeWindow(EditCodeSession session, Func<string, ModuleDef?> openModule) {
			this.session = session;
			this.openModule = openModule;
			Title = session.Title;
			Width = 1000;
			Height = 750;
			WindowStartupLocation = WindowStartupLocation.CenterOwner;
			ShowInTaskbar = false;

			compileButton = new Button { Content = "Compile", IsEnabled = false, IsDefault = false };
			compileButton.Click += async (s, e) => await CompileAsync();
			var cancelButton = new Button { Content = "Cancel" };
			cancelButton.Click += (s, e) => Close(null);
			addReferenceButton = new Button { Content = "Add Assembly Reference...", IsEnabled = false };
			addReferenceButton.Click += async (s, e) => await AddReferenceAsync();
			addDocumentButton = new Button { Content = "Add Document...", IsEnabled = false };
			addDocumentButton.Click += async (s, e) => await AddDocumentAsync();
			ToolTip.SetTip(compileButton, "Compile and replace the code (Ctrl+B or F6)");
			ToolTip.SetTip(addReferenceButton, "Add an assembly reference (Ctrl+O)");

			var toolbar = new StackPanel {
				Orientation = Orientation.Horizontal,
				Spacing = 4,
				Margin = new Thickness(4),
				Children = { addReferenceButton, addDocumentButton },
			};
			var buttons = new StackPanel {
				Orientation = Orientation.Horizontal,
				HorizontalAlignment = HorizontalAlignment.Right,
				Spacing = 8,
				Margin = new Thickness(4),
				Children = { compileButton, cancelButton },
			};
			statusText = new TextBlock { Text = "Decompiling...", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4) };

			documentsTabControl = new TabControl { Padding = new Thickness(0) };
			diagnosticsListBox = new ListBox {
				ItemsSource = diagnostics,
				ItemTemplate = new FuncDataTemplate<DiagnosticItem>((item, _) => new StackPanel {
					Orientation = Orientation.Horizontal,
					Spacing = 6,
					Children = {
						new Image { Source = item is null ? null : ImageCache.Get(item.ImageName), Width = 16, Height = 16 },
						new TextBlock { Text = item?.Diagnostic.Id, MinWidth = 60 },
						new TextBlock { Text = item?.Diagnostic.Description, TextWrapping = TextWrapping.NoWrap },
						new TextBlock { Text = item?.Location, Foreground = Brushes.Gray },
					},
				}),
			};
			diagnosticsListBox.DoubleTapped += (s, e) => GoToSelectedDiagnostic();
			diagnosticsListBox.KeyDown += (s, e) => {
				if (e.Key == Key.Enter) {
					GoToSelectedDiagnostic();
					e.Handled = true;
				}
			};

			var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,4,150,Auto") };
			Grid.SetRow(toolbar, 0);
			Grid.SetRow(documentsTabControl, 1);
			var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Rows };
			Grid.SetRow(splitter, 2);
			Grid.SetRow(diagnosticsListBox, 3);
			var bottom = new DockPanel { LastChildFill = true };
			DockPanel.SetDock(buttons, Dock.Right);
			bottom.Children.Add(buttons);
			bottom.Children.Add(statusText);
			Grid.SetRow(bottom, 4);
			grid.Children.Add(toolbar);
			grid.Children.Add(documentsTabControl);
			grid.Children.Add(splitter);
			grid.Children.Add(diagnosticsListBox);
			grid.Children.Add(bottom);
			Content = grid;

			Opened += async (s, e) => await InitializeAsync();
			Closed += (s, e) => cancellation.Cancel();
		}

		// For the smoke test
		internal IReadOnlyList<RoslynCodeEditor> Editors => editors;
		internal IReadOnlyList<DiagnosticItem> DiagnosticItems => diagnostics;
		internal bool HasDecompiled { get; private set; }
		internal bool IsCompiling => isCompiling;

		async Task InitializeAsync() {
			try {
				await session.InitializeAsync(cancellation.Token);
			}
			catch (OperationCanceledException) {
				return;
			}
			catch (Exception ex) {
				statusText.Text = "Couldn't decompile the code";
				// Show the exception in an editor like the WPF code editor does
				var tab = new TabItem { Header = "error", Content = new TextBox { Text = ex.ToString(), IsReadOnly = true, AcceptsReturn = true } };
				documentsTabControl.Items.Add(tab);
				return;
			}
			foreach (var doc in session.Documents)
				AddEditor(doc);
			documentsTabControl.SelectedIndex = 0;
			statusText.Text = string.Empty;
			compileButton.IsEnabled = true;
			addReferenceButton.IsEnabled = true;
			addDocumentButton.IsEnabled = true;
			HasDecompiled = true;
		}

		RoslynCodeEditor AddEditor(EditCodeDocument doc) {
			var editor = new RoslynCodeEditor(session.Project!, doc.Id, doc.InitialText);
			editors.Add(editor);
			documentsTabControl.Items.Add(new TabItem { Header = doc.Name, Content = editor });
			if (doc.InitialCaretOffset is int caret) {
				editor.CaretOffset = Math.Min(caret, editor.Document.TextLength);
				// The editor hasn't been measured yet
				editor.AttachedToVisualTree += (s, e) => global::Avalonia.Threading.Dispatcher.UIThread.Post(() => {
					editor.TextArea.Caret.BringCaretToView();
					editor.TextArea.Focus();
				}, global::Avalonia.Threading.DispatcherPriority.Loaded);
			}
			return editor;
		}

		protected override void OnKeyDown(KeyEventArgs e) {
			base.OnKeyDown(e);
			if (e.Handled)
				return;
			if ((e.Key == Key.B && e.KeyModifiers == KeyModifiers.Control) || (e.Key == Key.F6 && e.KeyModifiers == KeyModifiers.None)) {
				_ = CompileAsync();
				e.Handled = true;
			}
			else if (e.Key == Key.O && e.KeyModifiers == KeyModifiers.Control) {
				_ = AddReferenceAsync();
				e.Handled = true;
			}
			else if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None) {
				Close(null);
				e.Handled = true;
			}
		}

		// Compiles the code and closes the window if it compiled
		internal async Task CompileAsync() {
			if (!compileButton.IsEnabled || isCompiling)
				return;
			isCompiling = true;
			compileButton.IsEnabled = false;
			statusText.Text = "Compiling...";
			diagnostics.Clear();
			EditCodeCompileResult result;
			try {
				result = await session.CompileAsync(cancellation.Token);
			}
			finally {
				isCompiling = false;
				compileButton.IsEnabled = true;
			}
			statusText.Text = string.Empty;
			foreach (var d in result.Diagnostics.Where(a => a.Severity != CompilerDiagnosticSeverity.Hidden).OrderBy(a => a, CompilerDiagnosticComparer.Instance))
				diagnostics.Add(new DiagnosticItem(d));
			if (result.Importer is ModuleImporter importer) {
				Close(importer);
				return;
			}
			statusText.Text = $"{diagnostics.Count(a => a.Diagnostic.Severity == CompilerDiagnosticSeverity.Error)} error(s)";
			if (diagnostics.Count > 0) {
				diagnosticsListBox.SelectedIndex = 0;
				GoToSelectedDiagnostic();
			}
		}

		void GoToSelectedDiagnostic() {
			if (diagnosticsListBox.SelectedItem is not DiagnosticItem item || item.Diagnostic.Filename is null)
				return;
			int index = session.Documents.ToList().FindIndex(a => a.Name == item.Diagnostic.Filename);
			if (index < 0 || index >= editors.Count)
				return;
			documentsTabControl.SelectedIndex = index;
			if (item.Diagnostic.LineLocationSpan is LineLocationSpan span) {
				var editor = editors[index];
				global::Avalonia.Threading.Dispatcher.UIThread.Post(() => editor.MoveCaretTo(span.StartLinePosition.Line, span.StartLinePosition.Character),
					global::Avalonia.Threading.DispatcherPriority.Loaded);
			}
		}

		async Task AddReferenceAsync() {
			if (!addReferenceButton.IsEnabled)
				return;
			var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
				Title = "Add Assembly Reference",
				AllowMultiple = true,
				FileTypeFilter = new[] {
					new FilePickerFileType(".NET Assemblies") { Patterns = new[] { "*.dll", "*.exe", "*.netmodule", "*.winmd" } },
					FilePickerFileTypes.All,
				},
			});
			var modules = files.Select(a => a.TryGetLocalPath()).OfType<string>().Select(openModule).OfType<ModuleDef>().ToArray();
			if (modules.Length == 0)
				return;
			if (!session.AddReferences(modules))
				await MessageBox.ShowAsync(this, "Couldn't add the assembly references (they might've already been added)", "Add Assembly Reference");
		}

		async Task AddDocumentAsync() {
			var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
				Title = "Add Document",
				AllowMultiple = true,
			});
			foreach (var filename in files.Select(a => a.TryGetLocalPath()).OfType<string>()) {
				string text;
				try {
					text = File.ReadAllText(filename);
				}
				catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) {
					await MessageBox.ShowAsync(this, ex.Message, "Add Document");
					continue;
				}
				AddEditor(session.AddDocument(Path.GetFileName(filename), text));
			}
		}
	}
}

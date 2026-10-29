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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using dnSpy.Avalonia.TreeNodes;
using dnSpy.Contracts.Decompiler;
using dnSpy.Contracts.Documents;
using dnSpy.Contracts.Documents.Tabs.DocViewer;
using dnSpy.Documents.Tabs.DocViewer;

namespace dnSpy.Avalonia {
	sealed partial class MainWindow : Window {
		readonly DnSpyHost host;
		readonly DocumentTreeModel treeModel;
		readonly List<(TreeNode node, int caret)> backStack = new List<(TreeNode node, int caret)>();
		readonly List<(TreeNode node, int caret)> forwardStack = new List<(TreeNode node, int caret)>();
		TreeNode? currentNode;
		CancellationTokenSource? decompileCancellation;
		// Set when the tree view selection is changed by code, eg. when going back
		bool ignoreSelectionChanged;

		public MainWindow(DnSpyHost host, string[] args) {
			this.host = host;
			InitializeComponent();

			var context = new TreeNodeContext(host.DocumentService, () => host.DecompilerService.Decompiler);
			treeModel = new DocumentTreeModel(context);
			treeView.ItemsSource = treeModel.Roots;

			var decompilers = host.DecompilerService.AllDecompilers.OrderBy(a => a.OrderUI).ToArray();
			decompilerComboBox.ItemsSource = decompilers;
			decompilerComboBox.SelectedItem = host.DecompilerService.Decompiler;
			host.DecompilerService.DecompilerChanged += DecompilerService_DecompilerChanged;

			codeViewer.ReferenceClicked += CodeViewer_ReferenceClicked;
			InitializeEditing();
			AddHandler(DragDrop.DropEvent, Window_Drop);
			AddHandler(DragDrop.DragOverEvent, Window_DragOver);
			AddHandler(PointerPressedEvent, Window_PointerPressed, RoutingStrategies.Tunnel);

			using (host.DocumentService.DisableAssemblyLoad()) {
				foreach (var info in host.GetSavedDocuments())
					host.DocumentService.TryGetOrCreate(info);
			}
			OpenFiles(args, select: true);
			UpdateTitle();
		}

		void UpdateTitle() {
			var version = typeof(MainWindow).Assembly.GetName().Version;
			var name = $"dnSpy {version?.ToString(3)} (Avalonia preview)";
			Title = currentNode is null ? name : $"{currentNode} - {name}";
			// Edited modules that haven't been saved
			if (editService is not null && editService.ModifiedModules.Any())
				Title = "*" + Title;
		}

		void OpenFiles(IEnumerable<string> filenames, bool select) {
			DocumentNode? lastNode = null;
			foreach (var filename in filenames) {
				// The filename is saved in the settings file so it can't be relative to the current dir
				string fullPath;
				try {
					fullPath = Path.GetFullPath(filename);
				}
				catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is NotSupportedException) {
					continue;
				}
				var document = host.DocumentService.TryGetOrCreate(DsDocumentInfo.CreateDocument(fullPath));
				if (document is null)
					continue;
				document.IsAutoLoaded = false;
				lastNode = treeModel.GetOrAddNode(document);
			}
			if (select && lastNode is not null)
				SelectNode(lastNode);
		}

		async void Open_Click(object? sender, RoutedEventArgs e) => await OpenAsync();

		async Task OpenAsync() {
			var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
				Title = "Open",
				AllowMultiple = true,
				FileTypeFilter = new[] {
					new FilePickerFileType(".NET Executables") { Patterns = new[] { "*.exe", "*.dll", "*.netmodule", "*.winmd" } },
					FilePickerFileTypes.All,
				},
			});
			OpenFiles(files.Select(a => a.TryGetLocalPath()).OfType<string>(), select: true);
		}

		void Close_Click(object? sender, RoutedEventArgs e) => CloseSelectedDocuments();
		async void CloseAll_Click(object? sender, RoutedEventArgs e) {
			if (!await ConfirmCloseModifiedAsync(host.DocumentService.GetDocuments()))
				return;
			host.DocumentService.Clear();
			ClearHistory();
		}

		// Edits are lost when an edited module is closed
		async Task<bool> ConfirmCloseModifiedAsync(IEnumerable<IDsDocument> documents) {
			var modified = documents.SelectMany(a => a.GetAllChildrenAndSelf()).Select(a => a.ModuleDef).OfType<dnlib.DotNet.ModuleDef>().Where(editService.IsModified).ToArray();
			if (modified.Length == 0)
				return true;
			var result = await Dialogs.MessageBox.ShowAsync(this, $"{string.Join(", ", modified.Select(a => a.Name.String))} has unsaved changes. Close it anyway?", "dnSpy", Dialogs.MessageBoxButtons.YesNo);
			return result == Dialogs.MessageBoxResult.Yes;
		}
		void Exit_Click(object? sender, RoutedEventArgs e) => Close();

		async void CloseSelectedDocuments() {
			var nodes = treeView.SelectedItems.OfType<TreeNode>().Select(GetRootNode).OfType<DocumentNode>().Distinct().ToArray();
			if (nodes.Length == 0)
				return;
			if (!await ConfirmCloseModifiedAsync(nodes.Select(a => a.Document)))
				return;
			treeModel.Remove(nodes);
			ClearHistory();
		}

		static DocumentNode? GetRootNode(TreeNode node) {
			while (node.Parent is not null)
				node = node.Parent;
			return node as DocumentNode;
		}

		void ClearHistory() {
			backStack.Clear();
			forwardStack.Clear();
			editService.RemoveClosedModules();
			if (currentNode is not null && GetRootNode(currentNode) is DocumentNode root && !treeModel.Roots.Contains(root)) {
				currentNode = null;
				codeViewer.SetText(string.Empty);
				UpdateTitle();
			}
		}

		void TreeView_KeyDown(object? sender, KeyEventArgs e) {
			if (e.Key == Key.Delete && e.KeyModifiers == KeyModifiers.None) {
				// Types, members and namespaces are deleted from the module, assemblies and modules are closed
				// The confirmation is modal so it's shown after the key event has been handled
				if (CanDelete(fromCodeViewer: false))
					Dispatcher.UIThread.Post(() => Delete(fromCodeViewer: false));
				else
					CloseSelectedDocuments();
				e.Handled = true;
			}
		}

		protected override void OnKeyDown(KeyEventArgs e) {
			base.OnKeyDown(e);
			if (e.Handled)
				return;
			if (e.Key == Key.O && e.KeyModifiers == KeyModifiers.Control) {
				_ = OpenAsync();
				e.Handled = true;
			}
			else if (e.Key == Key.Left && e.KeyModifiers == KeyModifiers.Alt) {
				GoBack();
				e.Handled = true;
			}
			else if (e.Key == Key.Right && e.KeyModifiers == KeyModifiers.Alt) {
				GoForward();
				e.Handled = true;
			}
		}

		void Window_PointerPressed(object? sender, PointerPressedEventArgs e) {
			var props = e.GetCurrentPoint(this).Properties;
			if (props.IsXButton1Pressed) {
				GoBack();
				e.Handled = true;
			}
			else if (props.IsXButton2Pressed) {
				GoForward();
				e.Handled = true;
			}
		}

		void Window_DragOver(object? sender, DragEventArgs e) =>
			e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;

		void Window_Drop(object? sender, DragEventArgs e) {
			var files = e.DataTransfer.TryGetFiles();
			if (files is null)
				return;
			OpenFiles(files.Select(a => a.TryGetLocalPath()).OfType<string>(), select: true);
		}

		void ThemeSystem_Click(object? sender, RoutedEventArgs e) => SetTheme(ThemeVariant.Default);
		void ThemeDark_Click(object? sender, RoutedEventArgs e) => SetTheme(ThemeVariant.Dark);
		void ThemeLight_Click(object? sender, RoutedEventArgs e) => SetTheme(ThemeVariant.Light);
		static void SetTheme(ThemeVariant theme) {
			if (Application.Current is Application app)
				app.RequestedThemeVariant = theme;
		}

		void DecompilerComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e) {
			if (decompilerComboBox.SelectedItem is IDecompiler decompiler)
				host.DecompilerService.Decompiler = decompiler;
		}

		void DecompilerService_DecompilerChanged(object? sender, EventArgs e) {
			decompilerComboBox.SelectedItem = host.DecompilerService.Decompiler;
			foreach (var root in treeModel.Roots)
				root.RefreshText();
			// The old caret offset means nothing in the other language's output so move it to the definition
			if (currentNode is not null)
				Decompile(currentNode, caret: null);
		}

		void TreeView_SelectionChanged(object? sender, SelectionChangedEventArgs e) {
			if (ignoreSelectionChanged)
				return;
			if (treeView.SelectedItem is TreeNode node && node != currentNode && node is not PlaceholderNode) {
				// Show the code if a hex editor tab is active
				tabControl.SelectedIndex = 0;
				NavigateTo(node, caret: null);
			}
		}

		void CodeViewer_ReferenceClicked(object? sender, ReferenceInfo e) {
			var node = treeModel.FindNode(e.Reference);
			if (node is not null)
				SelectNode(node);
		}

		// Selects the node in the tree view which also decompiles it
		void SelectNode(TreeNode node) {
			for (var parent = node.Parent; parent is not null; parent = parent.Parent)
				parent.IsExpanded = true;
			treeView.SelectedItem = node;
			// The containers of the expanded nodes are created later
			Dispatcher.UIThread.Post(() => {
				if (treeView.TreeContainerFromItem(node) is Control container)
					container.BringIntoView();
			}, DispatcherPriority.Loaded);
		}

		void NavigateTo(TreeNode node, int? caret) {
			if (currentNode is not null) {
				backStack.Add((currentNode, codeViewer.CaretOffset));
				forwardStack.Clear();
			}
			Decompile(node, caret);
		}

		void Back_Click(object? sender, RoutedEventArgs e) => GoBack();
		void Forward_Click(object? sender, RoutedEventArgs e) => GoForward();
		void GoBack() => GoBackOrForward(backStack, forwardStack);
		void GoForward() => GoBackOrForward(forwardStack, backStack);

		void GoBackOrForward(List<(TreeNode node, int caret)> from, List<(TreeNode node, int caret)> to) {
			if (from.Count == 0)
				return;
			var (node, caret) = from[from.Count - 1];
			from.RemoveAt(from.Count - 1);
			if (currentNode is not null)
				to.Add((currentNode, codeViewer.CaretOffset));
			ignoreSelectionChanged = true;
			try {
				SelectNode(node);
			}
			finally {
				ignoreSelectionChanged = false;
			}
			Decompile(node, caret);
		}

		async void Decompile(TreeNode node, int? caret) {
			currentNode = node;
			UpdateTitle();
			decompileCancellation?.Cancel();
			var cancellation = decompileCancellation = new CancellationTokenSource();
			var decompiler = host.DecompilerService.Decompiler;
			var documentService = host.DocumentService;

			var task = Task.Run(() => {
				var output = new DocumentViewerOutputBase();
				var decompilationContext = new DecompilationContext {
					CancellationToken = cancellation.Token,
					CalculateILSpans = true,
					GetDisableAssemblyLoad = () => documentService.DisableAssemblyLoad(),
				};
				try {
					node.Decompile(new DecompileContext(decompiler, output, decompilationContext));
				}
				catch (OperationCanceledException) {
					throw;
				}
				catch (Exception ex) {
					output.WriteLine();
					decompiler.WriteCommentLine(output, "An exception occurred while decompiling:");
					foreach (var line in ex.ToString().Split('\n'))
						decompiler.WriteCommentLine(output, line.TrimEnd('\r'));
				}
				return output.CreateContent();
			}, cancellation.Token);

			// Don't show the old code while a slow node (eg. a big namespace) is being decompiled
			if (await Task.WhenAny(task, Task.Delay(250)) != task && cancellation == decompileCancellation)
				codeViewer.SetText("Decompiling...");

			DocumentViewerContent content;
			try {
				content = await task;
			}
			catch (OperationCanceledException) {
				return;
			}
			if (cancellation != decompileCancellation)
				return;
			decompileCancellation = null;
			cancellation.Dispose();

			codeViewer.SetContent(content);
			if (caret is int offset)
				codeViewer.MoveCaret(offset);
			else if (!codeViewer.MoveCaretToDefinition(node.Reference))
				codeViewer.MoveCaret(0);
		}
	}
}

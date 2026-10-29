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
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using dnlib.DotNet;
using dnlib.DotNet.Writer;
using dnSpy.AsmEditor.Compiler;
using dnSpy.AsmEditor.SaveModule;
using dnSpy.Avalonia.AsmEditor;
using dnSpy.Avalonia.Dialogs;
using dnSpy.Avalonia.Editing;
using dnSpy.Avalonia.Hex;
using dnSpy.Avalonia.TreeNodes;
using dnSpy.Contracts.Decompiler;
using dnSpy.Contracts.Documents;

namespace dnSpy.Avalonia {
	// Code editing (edit method/class, add class/members, edit assembly attributes), undo/redo,
	// saving modules and the hex editor tabs
	sealed partial class MainWindow {
		ModuleEditService editService = null!;
		readonly List<(TabItem tab, HexEditorPanel panel)> hexTabs = new List<(TabItem tab, HexEditorPanel panel)>();
		bool closeConfirmed;

		void InitializeEditing() {
			editService = new ModuleEditService(host.DocumentService, host.MethodAnnotations);
			editService.ModuleChanged += EditService_ModuleChanged;
			editService.ModifiedStateChanged += (s, e) => UpdateTitle();
			InitializeAsmEditor();

			treeView.ContextMenu = CreateEditContextMenu(fromCodeViewer: false);
			codeViewer.ContextMenu = CreateEditContextMenu(fromCodeViewer: true);
			editMenu.SubmenuOpened += (s, e) => UpdateEditMenuItems();
			AddHandler(KeyDownEvent, Window_PreviewKeyDown, RoutingStrategies.Tunnel);
			Closing += MainWindow_Closing;
		}

		bool HasUnsavedChanges => editService.ModifiedModules.Any() || hexTabs.Any(a => a.panel.Document.IsModified);

		ContextMenu CreateEditContextMenu(bool fromCodeViewer) {
			var editMethod = new MenuItem { Header = "Edit Method Body..." };
			editMethod.Click += (s, e) => _ = EditCodeAsync(EditCodeKind.EditMethod, fromCodeViewer);
			var editClass = new MenuItem { Header = "Edit Class..." };
			editClass.Click += (s, e) => _ = EditCodeAsync(EditCodeKind.EditClass, fromCodeViewer);
			var addMembers = new MenuItem { Header = "Add Class Members..." };
			addMembers.Click += (s, e) => _ = EditCodeAsync(EditCodeKind.AddMembers, fromCodeViewer);
			var addClass = new MenuItem { Header = "Add Class..." };
			addClass.Click += (s, e) => _ = EditCodeAsync(EditCodeKind.AddClass, fromCodeViewer);
			var editAssembly = new MenuItem { Header = "Edit Assembly Attributes..." };
			editAssembly.Click += (s, e) => _ = EditCodeAsync(EditCodeKind.EditAssembly, fromCodeViewer);
			var openHex = new MenuItem { Header = "Open Hex Editor" };
			openHex.Click += (s, e) => OpenHexEditorForSelection(fromCodeViewer, showMember: false);
			var showInHex = new MenuItem { Header = "Show in Hex Editor" };
			showInHex.Click += (s, e) => OpenHexEditorForSelection(fromCodeViewer, showMember: true);
			var editSettings = CreateEditSettingsMenuItem(fromCodeViewer);
			var editMethodBody = new MenuItem { Header = dnSpy.AsmEditor.Properties.dnSpy_AsmEditor_Core_Resources.EditILInstructionsCommand };
			editMethodBody.Click += (s, e) => EditMethodBody(fromCodeViewer);
			var replaceWithStub = new MenuItem { Header = dnSpy.AsmEditor.Properties.dnSpy_AsmEditor_Core_Resources.ReplaceMethodBodyWithStubCommand };
			replaceWithStub.Click += (s, e) => ReplaceMethodBodyWithStub(fromCodeViewer);
			var saveModule = new MenuItem { Header = "Save Module..." };
			saveModule.Click += (s, e) => _ = SaveModuleAsync(fromCodeViewer);

			var menu = new ContextMenu {
				Items = {
					editMethod, editClass, addMembers, addClass, editAssembly,
					new Separator(),
					editSettings, editMethodBody, replaceWithStub,
					new Separator(),
					openHex, showInHex,
					new Separator(),
					saveModule,
				},
			};
			menu.Items.Add(new Separator());
			var updateCreateDeleteItems = AddCreateDeleteMenuItems(menu.Items, fromCodeViewer);
			menu.Opening += (s, e) => {
				updateCreateDeleteItems();
				var target = GetEditTarget(fromCodeViewer);
				editMethod.IsEnabled = CanEditCode(EditCodeKind.EditMethod, target);
				editClass.IsEnabled = CanEditCode(EditCodeKind.EditClass, target);
				addMembers.IsEnabled = CanEditCode(EditCodeKind.AddMembers, target);
				addClass.IsEnabled = CanEditCode(EditCodeKind.AddClass, target);
				editAssembly.IsEnabled = CanEditCode(EditCodeKind.EditAssembly, target);
				openHex.IsEnabled = GetHexFilename(target.document) is not null;
				showInHex.IsEnabled = GetHexRange(target.member) is not null;
				saveModule.IsEnabled = target.module is not null;
				UpdateEditSettingsMenuItem(editSettings, fromCodeViewer);
				editMethodBody.IsEnabled = replaceWithStub.IsEnabled = GetEditMethodBodyTarget(fromCodeViewer) is not null;
			};
			return menu;
		}

		void UpdateEditMenuItems() {
			bool fromCodeViewer = codeViewer.IsKeyboardFocusWithin;
			var target = GetEditTarget(fromCodeViewer);
			var hex = GetActiveHexPanel();
			undoMenuItem.IsEnabled = hex?.Document.CanUndo ?? editService.CanUndo;
			redoMenuItem.IsEnabled = hex?.Document.CanRedo ?? editService.CanRedo;
			editMethodMenuItem.IsEnabled = CanEditCode(EditCodeKind.EditMethod, target);
			editClassMenuItem.IsEnabled = CanEditCode(EditCodeKind.EditClass, target);
			addMembersMenuItem.IsEnabled = CanEditCode(EditCodeKind.AddMembers, target);
			addClassMenuItem.IsEnabled = CanEditCode(EditCodeKind.AddClass, target);
			editAssemblyMenuItem.IsEnabled = CanEditCode(EditCodeKind.EditAssembly, target);
			openHexEditorMenuItem.IsEnabled = GetHexFilename(target.document) is not null;
			showInHexEditorMenuItem.IsEnabled = GetHexRange(target.member) is not null;
			UpdateEditSettingsMenuItem(editSettingsMenuItem, fromCodeViewer);
			updateEditMenuCreateDeleteItems?.Invoke();
			editMethodBodyMenuItem.IsEnabled = replaceMethodBodyWithStubMenuItem.IsEnabled = GetEditMethodBodyTarget(fromCodeViewer) is not null;
		}

		readonly struct EditTarget {
			public readonly IDsDocument? document;
			public readonly ModuleDef? module;
			public readonly IMemberDef? member;

			public EditTarget(IDsDocument? document, ModuleDef? module, IMemberDef? member) {
				this.document = document;
				this.module = module;
				this.member = member;
			}
		}

		// What the edit commands use: the member at the caret in the code viewer or the selected tree node
		EditTarget GetEditTarget(bool fromCodeViewer) {
			object? reference = null;
			TreeNode? node = treeView.SelectedItem as TreeNode;
			if (fromCodeViewer) {
				node = currentNode;
				var content = codeViewer.Content;
				var spanData = content?.ReferenceCollection.Find(codeViewer.CaretOffset);
				if (spanData is not null && !spanData.Value.Data.IsLocal)
					reference = spanData.Value.Data.Reference;
			}
			reference ??= node?.Reference;

			var member = reference switch {
				IMemberDef def => def,
				IMemberRef memberRef => ResolveDef(memberRef),
				_ => null,
			};
			// Only members in a loaded module can be edited, not eg. a reference to a BCL method
			ModuleDef? module = member?.Module;
			if (member is not null && FindDocument(member.Module) is null) {
				member = null;
				module = null;
			}

			IDsDocument? document = null;
			if (module is null && node is not null) {
				for (var n = node; n is not null; n = n.Parent) {
					if (n is DocumentNode docNode) {
						document = docNode.Document;
						break;
					}
				}
				module = document?.ModuleDef;
			}
			document ??= FindDocument(module);
			return new EditTarget(document, module, member);
		}

		static IMemberDef? ResolveDef(IMemberRef memberRef) {
			switch (memberRef) {
			case MethodSpec ms:
				return ResolveDef(ms.Method);
			case ITypeDefOrRef tdr:
				return tdr.ResolveTypeDef();
			case MemberRef mr:
				return mr.Resolve() as IMemberDef;
			default:
				return null;
			}
		}

		IDsDocument? FindDocument(ModuleDef? module) {
			if (module is null)
				return null;
			foreach (var document in host.DocumentService.GetDocuments().SelectMany(a => a.GetAllChildrenAndSelf())) {
				if (document.ModuleDef == module)
					return document;
			}
			return null;
		}

		static IMemberDef? GetDefToEdit(EditCodeKind kind, EditTarget target) {
			switch (kind) {
			case EditCodeKind.EditMethod:
				return target.member is MethodDef method && !method.IsAbstract && method.HasBody ? method : null;
			case EditCodeKind.EditClass:
			case EditCodeKind.AddMembers:
				var type = target.member as TypeDef ?? target.member?.DeclaringType;
				// Can't edit the <Module> type
				return type is null || type.IsGlobalModuleType && type.DeclaringType is null ? null : target.member;
			default:
				return null;
			}
		}

		IDecompiler? GetEditDecompiler(EditCodeKind kind) {
			var decompiler = host.DecompilerService.Decompiler;
			if (EditCodeSession.CanEdit(kind, decompiler))
				return decompiler;
			// Eg. IL is shown, use C#
			return host.DecompilerService.AllDecompilers.Where(a => a.GenericGuid == DecompilerConstants.LANGUAGE_CSHARP).OrderBy(a => a.OrderUI).FirstOrDefault(a => EditCodeSession.CanEdit(kind, a));
		}

		bool CanEditCode(EditCodeKind kind, EditTarget target) {
			if (target.module is null || GetEditDecompiler(kind) is null)
				return false;
			switch (kind) {
			case EditCodeKind.EditMethod:
			case EditCodeKind.EditClass:
			case EditCodeKind.AddMembers:
				return GetDefToEdit(kind, target) is not null;
			case EditCodeKind.EditAssembly:
				return target.module.Assembly is not null;
			default:
				return true;
			}
		}

		void EditMethod_Click(object? sender, RoutedEventArgs e) => _ = EditCodeAsync(EditCodeKind.EditMethod, codeViewer.IsKeyboardFocusWithin);
		void EditClass_Click(object? sender, RoutedEventArgs e) => _ = EditCodeAsync(EditCodeKind.EditClass, codeViewer.IsKeyboardFocusWithin);
		void AddMembers_Click(object? sender, RoutedEventArgs e) => _ = EditCodeAsync(EditCodeKind.AddMembers, codeViewer.IsKeyboardFocusWithin);
		void AddClass_Click(object? sender, RoutedEventArgs e) => _ = EditCodeAsync(EditCodeKind.AddClass, codeViewer.IsKeyboardFocusWithin);
		void EditAssembly_Click(object? sender, RoutedEventArgs e) => _ = EditCodeAsync(EditCodeKind.EditAssembly, codeViewer.IsKeyboardFocusWithin);

		// Used by the smoke test
		internal EditCodeWindow? LastEditCodeWindow { get; private set; }
		internal ModuleEditService EditService => editService;
		internal Task<bool> SaveModuleToAsync(ModuleDef module, string filename) => SaveModuleAsync(module, filename);

		internal async Task EditCodeAsync(EditCodeKind kind, bool fromCodeViewer) {
			var target = GetEditTarget(fromCodeViewer);
			var decompiler = GetEditDecompiler(kind);
			if (!CanEditCode(kind, target) || decompiler is null || target.module is not ModuleDef module)
				return;
			var defToEdit = GetDefToEdit(kind, target);

			ModuleImporter? importer;
			using (var session = new EditCodeSession(kind, decompiler, host.SettingsService, host.RawModuleBytesProvider, module, defToEdit)) {
				var window = new EditCodeWindow(session, OpenModuleForReference);
				LastEditCodeWindow = window;
				importer = await window.ShowDialog<ModuleImporter?>(this);
				if (importer is null)
					return;
				try {
					editService.Apply(importer, module);
				}
				catch (Exception ex) {
					await MessageBox.ShowAsync(this, $"Couldn't update the module:\n\n{ex}", "Edit Code");
					return;
				}
			}

			// Show the edited member, or the type that has the new members
			object? toSelect = kind switch {
				EditCodeKind.EditMethod => defToEdit,
				EditCodeKind.EditClass => defToEdit,
				EditCodeKind.AddMembers => defToEdit as TypeDef ?? defToEdit?.DeclaringType,
				EditCodeKind.AddClass => importer.NewNonNestedTypes.Select(a => a.TargetType).FirstOrDefault() ?? (object)module,
				_ => module.Assembly ?? (object)module,
			};
			if (treeModel.FindNode(toSelect) is TreeNode node && node != currentNode)
				SelectNode(node);
		}

		ModuleDef? OpenModuleForReference(string filename) {
			var document = host.DocumentService.TryGetOrCreate(DsDocumentInfo.CreateDocument(Path.GetFullPath(filename)));
			if (document is not null)
				document.IsAutoLoaded = false;
			return document?.ModuleDef;
		}

		void EditService_ModuleChanged(object? sender, ModuleEdit changes) {
			// Assembly and netmodule commands add and remove documents
			treeModel.SyncWithDocumentService();
			// Show the new and removed members and modules and refresh the edited nodes' text (eg. a renamed member)
			foreach (var root in changes.Modules.Select(a => treeModel.FindNode(a)).OfType<TreeNode>().Select(GetRootNode).OfType<DocumentNode>().Distinct().ToArray())
				root.ReloadChildren();
			// The decompiled code is cached nowhere so just decompile it again
			if (currentNode is not null && IsInTree(currentNode))
				Decompile(currentNode, codeViewer.CaretOffset);
			UpdateTitle();
		}

		bool IsInTree(TreeNode node) {
			var root = GetRootNode(node);
			if (root is null || !treeModel.Roots.Contains(root))
				return false;
			for (var n = node; n.Parent is not null; n = n.Parent) {
				if (!n.Parent.Children.Contains(n))
					return false;
			}
			return true;
		}

		void Undo_Click(object? sender, RoutedEventArgs e) => Undo();
		void Redo_Click(object? sender, RoutedEventArgs e) => Redo();

		void Undo() {
			if (GetActiveHexPanel() is HexEditorPanel hex)
				hex.Editor.Undo();
			else
				editService.Undo();
		}

		void Redo() {
			if (GetActiveHexPanel() is HexEditorPanel hex)
				hex.Editor.Redo();
			else
				editService.Redo();
		}

		// Ctrl+Z/Y undo the code edits, except in the hex editor which has its own undo. The read only
		// code viewer would otherwise handle them.
		void Window_PreviewKeyDown(object? sender, KeyEventArgs e) {
			if (e.Source is Visual source && source.FindAncestorOfType<HexEditor>(includeSelf: true) is not null)
				return;
			if (e.Source is Visual visual && visual.FindAncestorOfType<TextBox>(includeSelf: true) is not null)
				return;
			if (e.Key == Key.Z && e.KeyModifiers == KeyModifiers.Control) {
				editService.Undo();
				e.Handled = true;
			}
			else if ((e.Key == Key.Y && e.KeyModifiers == KeyModifiers.Control) || (e.Key == Key.Z && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))) {
				editService.Redo();
				e.Handled = true;
			}
			else if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.Alt) {
				// The dialog is modal so it's shown after the key event has been handled
				bool fromCodeViewer = codeViewer.IsKeyboardFocusWithin;
				global::Avalonia.Threading.Dispatcher.UIThread.Post(() => EditSettings(fromCodeViewer));
				e.Handled = true;
			}
			else if (e.Key == Key.S && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift)) {
				_ = SaveModuleAsync(codeViewer.IsKeyboardFocusWithin);
				e.Handled = true;
			}
			else if (e.Key == Key.S && e.KeyModifiers == KeyModifiers.Control && GetActiveHexPanel() is null) {
				_ = SaveAllAsync();
				e.Handled = true;
			}
		}

		void SaveModule_Click(object? sender, RoutedEventArgs e) => _ = SaveModuleAsync(codeViewer.IsKeyboardFocusWithin);
		void SaveAll_Click(object? sender, RoutedEventArgs e) => _ = SaveAllAsync();

		// Shows the save module dialog (filename and the module writer options) and saves the module
		internal async Task SaveModuleAsync(bool fromCodeViewer) {
			var target = GetEditTarget(fromCodeViewer);
			var module = target.module;
			var document = target.document ?? FindDocument(module);
			if (module is null || document is null)
				return;
			var vm = new SaveModuleOptionsVM(document);
			if (!new SaveModuleOptionsDlg { DataContext = vm }.ShowModal(this))
				return;
			await SaveModuleAsync(module, vm.FileName, vm.CreateWriterOptions());
		}

		async Task<bool> SaveModuleAsync(ModuleDef module, string filename, ModuleWriterOptionsBase? options = null) {
			try {
				await Task.Run(() => editService.Save(module, filename, options));
				return true;
			}
			catch (Exception ex) {
				await MessageBox.ShowAsync(this, $"Couldn't save '{filename}':\n\n{ex.Message}", "Save Module");
				return false;
			}
		}

		// Saves all modified modules to their files and all modified hex documents
		async Task<bool> SaveAllAsync() {
			foreach (var module in editService.ModifiedModules) {
				if (string.IsNullOrEmpty(module.Location))
					continue;
				if (!await SaveModuleAsync(module, module.Location))
					return false;
			}
			foreach (var (_, panel) in hexTabs.ToArray()) {
				if (panel.Document.IsModified && !await panel.SaveAsync(saveAs: false))
					return false;
			}
			return true;
		}

		async void MainWindow_Closing(object? sender, WindowClosingEventArgs e) {
			if (closeConfirmed || !HasUnsavedChanges)
				return;
			e.Cancel = true;
			var result = await MessageBox.ShowAsync(this, "There are unsaved changes. Save them before closing?", "dnSpy", MessageBoxButtons.YesNoCancel);
			if (result == MessageBoxResult.Cancel || result == MessageBoxResult.None)
				return;
			if (result == MessageBoxResult.Yes && !await SaveAllAsync())
				return;
			closeConfirmed = true;
			Close();
		}

		// Hex editor

		HexEditorPanel? GetActiveHexPanel() =>
			tabControl.SelectedItem is TabItem tab ? hexTabs.FirstOrDefault(a => a.tab == tab).panel : null;

		internal IReadOnlyList<HexEditorPanel> HexEditorPanels => hexTabs.Select(a => a.panel).ToArray();

		void OpenHexEditor_Click(object? sender, RoutedEventArgs e) => OpenHexEditorForSelection(codeViewer.IsKeyboardFocusWithin, showMember: false);
		void ShowInHexEditor_Click(object? sender, RoutedEventArgs e) => OpenHexEditorForSelection(codeViewer.IsKeyboardFocusWithin, showMember: true);

		async void OpenInHexEditor_Click(object? sender, RoutedEventArgs e) {
			var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
				Title = "Open in Hex Editor",
				AllowMultiple = true,
			});
			foreach (var filename in files.Select(a => a.TryGetLocalPath()).OfType<string>())
				OpenHexEditor(filename);
		}

		static string? GetHexFilename(IDsDocument? document) {
			var filename = document?.Filename;
			return !string.IsNullOrEmpty(filename) && File.Exists(filename) ? filename : null;
		}

		// The file offset and length of a method body or a field's initial value
		(string filename, long offset, long length)? GetHexRange(IMemberDef? member) {
			if (member?.Module is not ModuleDefMD module || GetHexFilename(FindDocument(module)) is not string filename)
				return null;
			var peImage = module.Metadata.PEImage;
			if (!peImage.IsFileImageLayout)
				return null;
			switch (member) {
			case MethodDef method when method.RVA != 0:
				long offset = (long)peImage.ToFileOffset(method.RVA);
				var reader = peImage.CreateReader();
				if (offset >= reader.Length)
					return null;
				reader.Position = (uint)offset;
				byte b = reader.ReadByte();
				// Tiny header: 1 byte, else a fat header (code size is at offset 4)
				if ((b & 3) == 2)
					return (filename, offset, 1 + (b >> 2));
				if (offset + 12 > reader.Length)
					return (filename, offset, 1);
				reader.Position = (uint)offset + 4;
				return (filename, offset, 12 + reader.ReadUInt32());
			case FieldDef field when field.RVA != 0:
				return (filename, (long)peImage.ToFileOffset(field.RVA), Math.Max(1, field.InitialValue?.Length ?? 1));
			default:
				return null;
			}
		}

		void OpenHexEditorForSelection(bool fromCodeViewer, bool showMember) {
			var target = GetEditTarget(fromCodeViewer);
			if (showMember) {
				var range = GetHexRange(target.member);
				if (range is not null)
					OpenHexEditor(range.Value.filename)?.Editor.Select(range.Value.offset, range.Value.length);
			}
			else {
				var filename = GetHexFilename(target.document);
				if (filename is not null)
					OpenHexEditor(filename);
			}
		}

		internal HexEditorPanel? OpenHexEditor(string filename) {
			filename = Path.GetFullPath(filename);
			foreach (var (tab, existing) in hexTabs) {
				if (existing.Document.Filename is string f && StringComparer.OrdinalIgnoreCase.Equals(f, filename)) {
					tabControl.SelectedItem = tab;
					return existing;
				}
			}

			HexDocument document;
			try {
				document = HexDocument.Load(filename);
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) {
				_ = MessageBox.ShowAsync(this, $"Couldn't open '{filename}':\n\n{ex.Message}", "Hex Editor");
				return null;
			}
			var panel = new HexEditorPanel(document);
			var title = new TextBlock { Text = panel.Title, VerticalAlignment = VerticalAlignment.Center };
			var closeButton = new Button { Content = "✕", Padding = new global::Avalonia.Thickness(4, 0), Margin = new global::Avalonia.Thickness(6, 0, 0, 0), FontSize = 10 };
			var newTab = new TabItem {
				Header = new StackPanel { Orientation = Orientation.Horizontal, Children = { title, closeButton } },
				Content = panel,
			};
			ToolTip.SetTip(newTab, filename);
			closeButton.Click += async (s, e) => await CloseHexTabAsync(newTab, panel);
			panel.TitleChanged += (s, e) => title.Text = panel.Title;
			panel.Saving += (s, savedFilename) => DisableMemoryMappedIO(savedFilename);
			panel.Saved += HexPanel_Saved;
			hexTabs.Add((newTab, panel));
			tabControl.Items.Add(newTab);
			tabControl.SelectedItem = newTab;
			global::Avalonia.Threading.Dispatcher.UIThread.Post(() => panel.Editor.Focus(), global::Avalonia.Threading.DispatcherPriority.Loaded);
			return panel;
		}

		async Task CloseHexTabAsync(TabItem tab, HexEditorPanel panel) {
			if (panel.Document.IsModified) {
				var result = await MessageBox.ShowAsync(this, $"Save the changes to '{panel.Document.Name}'?", "Hex Editor", MessageBoxButtons.YesNoCancel);
				if (result == MessageBoxResult.Cancel || result == MessageBoxResult.None)
					return;
				if (result == MessageBoxResult.Yes && !await panel.SaveAsync(saveAs: false))
					return;
			}
			hexTabs.RemoveAll(a => a.tab == tab);
			tabControl.Items.Remove(tab);
			tabControl.SelectedIndex = 0;
		}

		void DisableMemoryMappedIO(string filename) {
			foreach (var document in host.DocumentService.GetDocuments().SelectMany(a => a.GetAllChildrenAndSelf())) {
				if (document.PEImage is not null && StringComparer.OrdinalIgnoreCase.Equals(document.PEImage.Filename, filename))
					MemoryMappedIOHelper.DisableMemoryMappedIO(document.PEImage);
			}
		}

		// The file was changed so reload the assembly if it's loaded and hasn't been edited
		void HexPanel_Saved(object? sender, string filename) {
			var roots = treeModel.Roots.Where(a => StringComparer.OrdinalIgnoreCase.Equals(a.Document.Filename, filename)).ToArray();
			if (roots.Length == 0)
				return;
			if (roots.Any(a => a.Document.GetAllChildrenAndSelf().Any(d => d.ModuleDef is ModuleDef m && editService.IsModified(m))))
				return;
			treeModel.Remove(roots);
			ClearHistory();
			OpenFiles(new[] { filename }, select: false);
		}
	}
}

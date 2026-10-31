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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.Resources;
using dnSpy.AsmEditor.DnlibDialogs;
using dnSpy.AsmEditor.Method;
using dnSpy.AsmEditor.MethodBody;
using dnSpy.AsmEditor.Types;
using dnSpy.AsmEditor.SaveModule;
using dnSpy.Avalonia.AsmEditor;
using dnSpy.Avalonia.Controls;
using dnSpy.Avalonia.Editing;
using dnSpy.Avalonia.TreeNodes;
using dnSpy.Contracts.AsmEditor.Compiler;
using dnSpy.Contracts.Decompiler;
using dnSpy.Contracts.Documents.Tabs.DocViewer;
using dnSpy.Documents.Tabs.DocViewer;

namespace dnSpy.Avalonia.SmokeTest {
	// Drives the real MainWindow with the headless platform (simulated mouse/keyboard input, Skia
	// rendering) and checks that the browser works: tree, decompilation, following references,
	// history, decompiler switching and closing documents. dnlib.dll (which is in the output dir)
	// is the test input so the checks depend on its types.
	//
	//	dnSpy.Avalonia.SmokeTest [--screenshots dir]
	//
	// The exit code is 1 if any check failed.
	sealed class Program {
		static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

		readonly string? screenshotDir;
		// Skips the edit code and hex editor tests (they're slow)
		static bool skipCodeEditor;
		readonly MainWindow window;
		readonly TreeView treeView;
		readonly CodeViewer code;
		readonly ComboBox decompilerComboBox;
		int failures;

		static int Main(string[] args) {
			string? screenshotDir = null;
			for (int i = 0; i < args.Length; i++) {
				if (args[i] == "--screenshots" && i + 1 < args.Length)
					screenshotDir = args[++i];
				else if (args[i] == "--skip-code-editor")
					skipCodeEditor = true;
				else {
					Console.Error.WriteLine("Usage: dnSpy.Avalonia.SmokeTest [--screenshots dir] [--skip-code-editor]");
					return 1;
				}
			}

			// Debug builds of the ILSpy decompiler have asserts that would kill the process
			Trace.Listeners.Clear();
			Trace.Listeners.Add(new AssertLogger());

			AppBuilder.Configure<App>()
				.UseSkia()
				.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
				.SetupWithoutStarting();
			AvaloniaSynchronizationContext.InstallIfNeeded();

			try {
				return new Program(screenshotDir).Run();
			}
			catch (Exception ex) {
				Console.WriteLine($"FAIL: unhandled exception: {ex}");
				return 1;
			}
		}

		Program(string? screenshotDir) {
			this.screenshotDir = screenshotDir;
			if (screenshotDir is not null)
				Directory.CreateDirectory(screenshotDir);
			var dnlibPath = typeof(ModuleDefMD).Assembly.Location;
			window = new MainWindow(new DnSpyHost(useUserFiles: false), new[] { dnlibPath }) {
				Width = 1280,
				Height = 800,
			};
			window.Show();
			treeView = window.FindControl<TreeView>("treeView")!;
			code = window.FindControl<CodeViewer>("codeViewer")!;
			decompilerComboBox = window.FindControl<ComboBox>("decompilerComboBox")!;
		}

		int Run() {
			Check("assembly from the command line is decompiled", WaitFor(() => code.Text.Contains("[assembly: AssemblyVersion")));
			Screenshot("01-start");

			ExpandAndSelect("dnlib (", "dnlib.dll");
			ExpandAndSelect("dnlib.dll", "dnlib.DotNet");
			ExpandAndSelect("dnlib.DotNet", "ModuleDefMD");
			Check("type is decompiled", WaitFor(() => code.Text.Contains("class ModuleDefMD ")));
			Check("caret is at the type definition", code.Document.GetLineByOffset(code.CaretOffset).LineNumber > 1);
			Screenshot("02-type");

			// public sealed class ModuleDefMD : ModuleDefMD2, ...
			const string classDecl = "class ModuleDefMD : ";
			ClickCode(code.Text.IndexOf(classDecl, StringComparison.Ordinal) + classDecl.Length + 2);
			Check("clicking a reference decompiles it", WaitFor(() => code.Text.Contains("class ModuleDefMD2 ")));
			Check("clicking a reference selects its tree node", WaitFor(() => SelectedNodeText.StartsWith("ModuleDefMD2", StringComparison.Ordinal)));
			Screenshot("03-followed");

			PressKey(Key.Left, RawInputModifiers.Alt, PhysicalKey.ArrowLeft);
			Check("Alt+Left goes back", WaitFor(() => code.Text.Contains("class ModuleDefMD ") && SelectedNodeText == "ModuleDefMD"));
			PressKey(Key.Right, RawInputModifiers.Alt, PhysicalKey.ArrowRight);
			Check("Alt+Right goes forward", WaitFor(() => code.Text.Contains("class ModuleDefMD2 ")));

			// The UI independent custom data providers in dnSpy.Core run on headless content too
			var content = code.Content;
			Check("content has method debug info", content?.MethodDebugInfos.Count > 0);
			int braceIndex = code.Text.IndexOf('{');
			Check("content has brace pairs", braceIndex >= 0 && content?.GetCustomData<BracePairCollection>(DocumentViewerContentDataIds.BracePair)?.GetBracePairs(braceIndex) is not null);
			Check("content has block structure", content?.GetCustomData<LazyBlockStructureCollection>(DocumentViewerContentDataIds.BlockStructure)?.Collection.GetRanges(new TextSpan(braceIndex, 1)).Any() == true);

			// The assembly resolver finds the BCL in the installed .NET runtime and it gets its own root node
			var stringRef = code.Content?.ReferenceCollection.FirstOrDefault(a => a.Data.Reference is ITypeDefOrRef t && t.FullName == "System.String" && !a.Data.IsDefinition);
			Check("code has a System.String reference", stringRef?.Data.Reference is not null);
			if (stringRef?.Data.Reference is not null) {
				ClickCode(stringRef.Value.TextSpan.Start + 1);
				Check("following a BCL reference decompiles it", WaitFor(() => code.Text.Contains("class String ")));
				Check("following a BCL reference selects it in the BCL assembly", WaitFor(() => SelectedNodeText == "String" && GetRoot(treeView.SelectedItem as TreeNode)?.ToString()?.StartsWith("System.Private.CoreLib", StringComparison.Ordinal) == true));
				Screenshot("04-bcl");
			}

			SelectDecompiler("IL");
			Check("switching to IL re-decompiles", WaitFor(() => code.Text.Contains(".class ")));
			Check("switching to IL updates the tree text", WaitFor(() => FindItem("Predicate`1") is not null));
			Screenshot("05-il");

			SelectDecompiler("C#");
			Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
			Check("switching back to C# re-decompiles", WaitFor(() => code.Text.Contains("class String ") && !code.Text.Contains(".class ")));
			Screenshot("06-dark");

			SelectTreeItem("dnlib.DotNet.Pdb");
			Check("namespace lists its types", WaitFor(() => code.Text.StartsWith("// dnlib.DotNet.Pdb", StringComparison.Ordinal) && code.Text.Contains("// PdbConstant")));
			Screenshot("07-namespace");

			if (!skipCodeEditor) {
				TestEditCode();
				TestHexEditor();
			}
			TestEditSettings();
			TestEditMethodBody();
			TestCreateDelete();
			TestAssemblyAndNetModuleCommands();
			TestResources();
			TestSaveModuleDialog();

			SelectTreeItem("dnlib (");
			int rootCount = RootCount;
			PressKey(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete);
			Check("Delete closes the assembly", WaitFor(() => RootCount == rootCount - 1));
			Screenshot("08-closed");

			Console.WriteLine(failures == 0 ? "All checks passed" : $"{failures} check(s) failed");
			return failures == 0 ? 0 : 1;
		}

		const string EditMarker = "dnSpy edit test";
		// Its decompiled code compiles as-is (IsNullOrEmpty's 'utf8 == null' is ambiguous between UTF8String's == operators)
		const string EditedMethod = "Contains(string";
		string? savedModuleFilename;

		// Edit Method: decompile into the editor, completion, a compile error, a successful compile, undo/redo and save
		void TestEditCode() {
			ExpandAndSelect("UTF8String", EditedMethod);
			Check("method is decompiled", WaitFor(() => code.Text.Contains(EditedMethod)));
			var method = (treeView.SelectedItem as TreeNode)?.Reference as MethodDef;
			Check("a method is selected", method is not null);
			if (method is null)
				return;

			var editTask = window.EditCodeAsync(EditCodeKind.EditMethod, fromCodeViewer: false);
			Check("edit code window decompiles the method", WaitFor(() => window.LastEditCodeWindow?.HasDecompiled == true, TimeSpan.FromSeconds(60)));
			var editWindow = window.LastEditCodeWindow;
			if (editWindow is null || editWindow.Editors.Count == 0)
				return;
			var editor = editWindow.Editors[0];
			Check("editor has the method", editor.Text.Contains(EditedMethod) && editor.Text.Contains("class UTF8String"));
			Check("editor has a .g file with the other members", editWindow.Editors.Count == 2);
			Check("editor gets diagnostics and colors", WaitFor(() => editor.DiagnosticsUpdateCount > 0, TimeSpan.FromSeconds(60)));
			Screenshot("09-edit-method");

			// Insert code at the start of the method body
			int nameIndex = editor.Text.IndexOf(EditedMethod, StringComparison.Ordinal);
			int bodyStart = editor.Text.IndexOf('{', nameIndex) + 1;
			editor.Document.Insert(bodyStart, "\n\t\t\tSystem.String.Conc");
			editor.CaretOffset = bodyStart + "\n\t\t\tSystem.String.Conc".Length;
			editor.TextArea.Focus();
			Settle();
			editWindow.KeyPress(Key.Space, RawInputModifiers.Control, PhysicalKey.Space, null);
			editWindow.KeyRelease(Key.Space, RawInputModifiers.Control, PhysicalKey.Space, null);
			Check("Ctrl+Space shows completion items", WaitFor(() => editor.ActiveCompletionWindow?.CompletionList.CompletionData.Any(a => a.Text == "Concat") == true, TimeSpan.FromSeconds(60)));
			if (editor.ActiveCompletionWindow is null)
				Console.WriteLine($"  focused: {editWindow.FocusManager?.GetFocusedElement()?.GetType().Name ?? "(none)"}, editor focused: {editor.TextArea.IsKeyboardFocusWithin}");
			Screenshot("10-completion");
			editor.ActiveCompletionWindow?.Close();

			// A compile error keeps the window open and shows the error
			editor.Document.Replace(bodyStart, "\n\t\t\tSystem.String.Conc".Length, "\n\t\t\tint dnSpyError = \"\";");
			_ = editWindow.CompileAsync();
			if (!Check("compile error is reported", WaitFor(() => !editWindow.IsCompiling && editWindow.DiagnosticItems.Any(a => a.Diagnostic.Id == "CS0029"), TimeSpan.FromSeconds(60))))
				PrintDiagnostics(editWindow);
			Check("window stays open after a compile error", !editTask.IsCompleted);
			Screenshot("11-compile-error");

			int errorStart = editor.Text.IndexOf("int dnSpyError", StringComparison.Ordinal);
			editor.Document.Replace(errorStart, "int dnSpyError = \"\";".Length, $"System.String.Concat(\"{EditMarker}\", \"\");");
			_ = editWindow.CompileAsync();
			if (!Check("successful compile closes the window", WaitFor(() => editTask.IsCompleted, TimeSpan.FromSeconds(60))))
				PrintDiagnostics(editWindow);
			Check("edited method is decompiled with the new code", WaitFor(() => code.Text.Contains(EditMarker)));
			Check("method body was replaced", method.Body?.Instructions.Any(a => a.Operand is string str && str == EditMarker) == true);
			Check("module is marked as modified", window.EditService.IsModified(method.Module) && window.Title?.StartsWith("*", StringComparison.Ordinal) == true);
			Screenshot("12-edited");

			treeView.Focus();
			PressKey(Key.Z, RawInputModifiers.Control, PhysicalKey.Z);
			Check("Ctrl+Z undoes the edit", WaitFor(() => !code.Text.Contains(EditMarker) && code.Text.Contains(EditedMethod)));
			Check("undo restores the module", !window.EditService.IsModified(method.Module));
			PressKey(Key.Y, RawInputModifiers.Control, PhysicalKey.Y);
			Check("Ctrl+Y redoes the edit", WaitFor(() => code.Text.Contains(EditMarker)));

			var tempDir = Path.Combine(Path.GetTempPath(), "dnSpy.Avalonia.SmokeTest-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(tempDir);
			savedModuleFilename = Path.Combine(tempDir, "dnlib.dll");
			var saveTask = window.SaveModuleToAsync(method.Module, savedModuleFilename);
			Check("module is saved", WaitFor(() => saveTask.IsCompleted, TimeSpan.FromSeconds(60)) && saveTask.Result);
			if (File.Exists(savedModuleFilename)) {
				using var saved = ModuleDefMD.Load(savedModuleFilename);
				var savedMethod = saved.Find(method.DeclaringType.FullName, isReflectionName: false)?.FindMethod(method.Name, method.MethodSig);
				Check("saved module has the edited method", savedMethod?.Body?.Instructions.Any(a => a.Operand is string str && str == EditMarker) == true);
			}
		}

		// Hex editor: open a file, overwrite a byte, undo/redo, save
		void TestHexEditor() {
			var filename = savedModuleFilename;
			if (filename is null || !File.Exists(filename)) {
				Check("hex editor test file exists", false);
				return;
			}
			var panel = window.OpenHexEditor(filename);
			Check("hex editor opens", panel is not null);
			if (panel is null)
				return;
			var doc = panel.Document;
			Check("hex editor has the file bytes", doc.Length == new FileInfo(filename).Length && doc[0] == (byte)'M' && doc[1] == (byte)'Z');
			Settle();
			panel.Editor.Focus();
			panel.Editor.MoveCaret(0);
			window.KeyTextInput("41");
			Check("typing hex digits overwrites a byte", WaitFor(() => doc[0] == 0x41 && doc.IsModified));
			Screenshot("13-hex");
			PressKey(Key.Z, RawInputModifiers.Control, PhysicalKey.Z);
			Check("Ctrl+Z in the hex editor undoes the byte", WaitFor(() => doc[0] == (byte)'M' && !doc.IsModified));
			PressKey(Key.Y, RawInputModifiers.Control, PhysicalKey.Y);
			Check("Ctrl+Y in the hex editor redoes the byte", WaitFor(() => doc[0] == 0x41));
			Check("the module edit wasn't undone by the hex editor's Ctrl+Z", code.Text.Contains(EditMarker) || window.EditService.CanUndo);
			var saveTask = panel.SaveAsync(saveAs: false);
			Check("hex editor saves the file", WaitFor(() => saveTask.IsCompleted) && saveTask.Result && File.ReadAllBytes(filename)[0] == 0x41 && !doc.IsModified);
			try {
				Directory.Delete(Path.GetDirectoryName(filename)!, recursive: true);
			}
			catch (IOException) {
			}
			// The rest of the test uses the code view
			treeView.Focus();
			// Undo the Edit Method change so closing dnlib at the end doesn't ask to save it
			while (window.EditService.CanUndo)
				window.EditService.Undo();
			Check("undoing the edits restores dnlib", WaitFor(() => !window.EditService.ModifiedModules.Any()));
		}

		TypeDef? utf8StringType;

		// Edit settings dialogs: rename a type (with validation), undo/redo, a nested dialog and the assembly dialog
		void TestEditSettings() {
			SelectTreeItem("UTF8String");
			var type = (treeView.SelectedItem as TreeNode)?.Reference as TypeDef;
			Check("UTF8String is selected", type?.Name == "UTF8String");
			utf8StringType = type;
			if (type is null)
				return;
			const string origName = "UTF8String";
			const string newName = "UTF8StringRenamed";
			bool shown = RunDialog(() => window.EditSettings(fromCodeViewer: false), dialog => {
				Check("Edit Type Settings shows the type dialog", dialog is TypeOptionsDlg && dialog.DataContext is TypeOptionsVM);
				var nameTextBox = dialog.FindControl<TextBox>("nameTextBox");
				var packingSizeTextBox = dialog.FindControl<TextBox>("packingSizeTextBox");
				var okButton = dialog.GetVisualDescendants().OfType<Button>().FirstOrDefault(a => a.IsDefault);
				Check("type dialog has the type name", nameTextBox?.Text == origName);
				Screenshot(dialog, "14-type-settings");
				if (nameTextBox is null || packingSizeTextBox is null || okButton is null)
					return;
				var origPackingSize = packingSizeTextBox.Text;
				packingSizeTextBox.Text = "x";
				Check("an invalid packing size is an error", WaitFor(() => DataValidationErrors.GetHasErrors(packingSizeTextBox) && !okButton.IsEffectivelyEnabled));
				Screenshot(dialog, "15-type-settings-error");
				packingSizeTextBox.Text = origPackingSize;
				Check("a valid packing size enables OK", WaitFor(() => !DataValidationErrors.GetHasErrors(packingSizeTextBox) && okButton.IsEffectivelyEnabled));
				nameTextBox.Text = newName;
				Click(dialog, okButton, new Point(5, 5));
			});
			Check("type dialog was shown", shown);
			Check("OK renames the type", type.Name == newName);
			Check("the renamed type's node is updated", WaitFor(() => SelectedNodeText == newName));
			Check("the renamed type is decompiled", WaitFor(() => code.Text.Contains("class " + newName)));
			Check("the module is marked as modified", window.EditService.IsModified(type.Module));
			treeView.Focus();
			PressKey(Key.Z, RawInputModifiers.Control, PhysicalKey.Z);
			Check("Ctrl+Z undoes the rename", WaitFor(() => type.Name == origName));
			Check("undo updates the type's node", WaitFor(() => FindItem(origName) is not null && FindItem(newName) is null));
			PressKey(Key.Y, RawInputModifiers.Control, PhysicalKey.Y);
			Check("Ctrl+Y redoes the rename", WaitFor(() => type.Name == newName));
			PressKey(Key.Z, RawInputModifiers.Control, PhysicalKey.Z);
			WaitFor(() => type.Name == origName);

			// A method's custom attributes tab opens the custom attribute dialog (a dialog in a dialog)
			var method = type.FindMethod("IsNullOrEmpty");
			Check("a method is selected", method is not null && window.SelectReference(method) && WaitFor(() => (treeView.SelectedItem as TreeNode)?.Reference == method));
			int caCount = method?.CustomAttributes.Count ?? -1;
			shown = RunDialog(() => window.EditSettings(fromCodeViewer: false), dialog => {
				var vm = dialog.DataContext as MethodOptionsVM;
				Check("Edit Method Settings shows the method dialog", dialog is MethodOptionsDlg && vm is not null);
				Screenshot(dialog, "16-method-settings");
				if (vm is null)
					return;
				var tabControl = dialog.GetVisualDescendants().OfType<TabControl>().First();
				tabControl.SelectedIndex = tabControl.Items.Count - 2;
				Settle();
				Screenshot(dialog, "17-method-custom-attributes");
				bool nestedShown = RunDialog(() => vm.CustomAttributesVM.AddCommand.Execute(null), nested => {
					Check("Add shows the custom attribute dialog", nested is CustomAttributeDlg && nested.DataContext is CustomAttributeVM);
					Screenshot(nested, "18-custom-attribute");
				});
				Check("custom attribute dialog was shown", nestedShown);
				Check("canceling the custom attribute dialog adds nothing", vm.CustomAttributesVM.Collection.Count == caCount);
			});
			Check("method dialog was shown", shown);
			Check("canceling the method dialog changes nothing", method?.CustomAttributes.Count == caCount);

			SelectTreeItem("dnlib (");
			shown = RunDialog(() => window.EditSettings(fromCodeViewer: false), dialog => {
				Check("Edit Assembly Settings shows the assembly dialog", dialog is AssemblyOptionsDlg);
				Check("assembly dialog has the name and version", dialog.FindControl<TextBox>("nameTextBox")?.Text == "dnlib" && dialog.FindControl<TextBox>("majorVersionTextBox")?.Text == type.Module.Assembly.Version.Major.ToString());
				Screenshot(dialog, "19-assembly-settings");
			});
			Check("assembly dialog was shown", shown);
		}

		// Method body (IL) editor: the instructions are listed, a shortcut key edits them, an operand's menu, OK,
		// undo, and replacing the body with a stub
		void TestEditMethodBody() {
			var method = utf8StringType?.FindMethod("IsNullOrEmpty");
			Check("IsNullOrEmpty is selected", method?.Body is not null && window.SelectReference(method) && WaitFor(() => (treeView.SelectedItem as TreeNode)?.Reference == method));
			if (method?.Body is null)
				return;
			var origCode = method.Body.Instructions[0].OpCode.Code;
			int instrCount = method.Body.Instructions.Count;
			bool shown = RunDialog(() => window.EditMethodBody(fromCodeViewer: false), dialog => {
				var vm = dialog.DataContext as MethodBodyVM;
				var control = (dialog as MethodBodyDlg)?.MethodBodyControl;
				Check("Edit IL Instructions shows the method body dialog", vm is not null && control is not null && vm.IsCilBody);
				if (vm is null || control is null)
					return;
				var listBox = control.InstructionsListBox;
				Check("the instructions are listed", WaitFor(() => listBox.ItemCount == instrCount && listBox.GetVisualDescendants().OfType<InstructionOperandControl>().Any()));
				Screenshot(dialog, "20-il-editor");

				// N replaces the selected instructions with nops
				dialog.Activate();
				listBox.SelectedIndex = 0;
				(listBox.ContainerFromIndex(0) as InputElement ?? listBox).Focus();
				Check("the instructions list has the keyboard focus", WaitFor(() => listBox.IsKeyboardFocusWithin));
				dialog.KeyPress(Key.N, RawInputModifiers.None, PhysicalKey.N, "n");
				dialog.KeyRelease(Key.N, RawInputModifiers.None, PhysicalKey.N, "n");
				Check("N replaces the instruction with a nop", WaitFor(() => vm.CilBodyVM.InstructionsListVM[0].Code == Code.Nop));

				// A field operand's button shows a menu with the pickers
				var callIndex = vm.CilBodyVM.InstructionsListVM.ToList().FindIndex(a => a.InstructionOperandVM.InstructionOperandType == InstructionOperandType.Field);
				Check("the method has a field operand", callIndex >= 0);
				if (callIndex >= 0) {
					listBox.ScrollIntoView(callIndex);
					Settle();
					var operandButton = (listBox.ContainerFromIndex(callIndex) as Visual)?.GetVisualDescendants().OfType<InstructionOperandControl>().FirstOrDefault()?.Content as Button;
					Check("a field operand is a button", operandButton is not null);
					if (operandButton is not null) {
						Click(dialog, operandButton, new Point(5, 5));
						Check("clicking a field operand shows the operand menu", WaitFor(() => InstructionsListHelper.LastOperandMenu?.IsOpen == true));
						Check("the field operand menu has the field and member reference pickers", InstructionsListHelper.LastOperandMenu?.ItemCount == 2);
						Screenshot(dialog, "21-il-operand-menu");
						InstructionsListHelper.LastOperandMenu?.Close();
					}
				}
				var okButton = dialog.GetVisualDescendants().OfType<Button>().FirstOrDefault(a => a.IsDefault);
				Check("the edited body has no errors", okButton?.IsEffectivelyEnabled == true);
				if (okButton is not null)
					Click(dialog, okButton, new Point(5, 5));
			});
			Check("method body dialog was shown", shown);
			Check("OK updates the method body", method.Body?.Instructions[0].OpCode.Code == Code.Nop && method.Body.Instructions.Count == instrCount);
			treeView.Focus();
			PressKey(Key.Z, RawInputModifiers.Control, PhysicalKey.Z);
			Check("Ctrl+Z restores the method body", WaitFor(() => method.Body?.Instructions[0].OpCode.Code == origCode));

			var origBody = method.Body;
			window.ReplaceMethodBodyWithStub(fromCodeViewer: false);
			Check("Replace Method Body with Stub replaces the body", method.Body != origBody && method.Body?.Instructions.Any(a => a.OpCode.Code == Code.Throw || a.OpCode.Code == Code.Ret) == true);
			PressKey(Key.Z, RawInputModifiers.Control, PhysicalKey.Z);
			Check("Ctrl+Z restores the replaced body", WaitFor(() => method.Body == origBody));
		}

		// Create a method and a field, delete them (with the confirmation), delete a property and its accessors,
		// create a type in a namespace and delete it with the Delete key. Everything is undone.
		void TestCreateDelete() {
			var type = utf8StringType;
			if (type is null || !window.SelectReference(type)) {
				Check("UTF8String can be selected", false);
				return;
			}
			var module = type.Module;
			WaitFor(() => (treeView.SelectedItem as TreeNode)?.Reference == type);

			const string methodName = "SmokeTestMethod";
			bool shown = RunDialog(() => window.Create(MainWindow.CreateKind.Method, fromCodeViewer: false), dialog => {
				Check("Create Method shows the method dialog", dialog is MethodOptionsDlg);
				var nameTextBox = dialog.FindControl<TextBox>("nameTextBox");
				Check("the new method has the default name", nameTextBox?.Text == "MyMethod");
				if (nameTextBox is not null)
					nameTextBox.Text = methodName;
				Screenshot(dialog, "22-create-method");
				ClickOK(dialog);
			});
			var newMethod = type.FindMethod(methodName);
			Check("Create Method adds the method", shown && newMethod is not null && newMethod.DeclaringType == type);
			Check("the new method is selected", WaitFor(() => (treeView.SelectedItem as TreeNode)?.Reference == newMethod));

			shown = RunDialog(() => window.Create(MainWindow.CreateKind.Field, fromCodeViewer: false), dialog => {
				Check("Create Field shows the field dialog", dialog is FieldOptionsDlg);
				ClickOK(dialog);
			});
			var newField = type.FindField("MyField");
			Check("Create Field adds the field", shown && newField is not null);

			// Delete asks first
			if (newMethod is not null && window.SelectReference(newMethod) && WaitFor(() => (treeView.SelectedItem as TreeNode)?.Reference == newMethod)) {
				shown = RunDialog(() => window.Delete(fromCodeViewer: false), dialog => {
					Check("deleting a method asks first", dialog is ConfirmDialog);
					Screenshot(dialog, "23-confirm-delete");
					ClickOK(dialog);
				});
				Check("Delete Method deletes the method", shown && type.FindMethod(methodName) is null);
				Check("the method's type is selected after the delete", WaitFor(() => (treeView.SelectedItem as TreeNode)?.Reference == type));
				treeView.Focus();
				PressKey(Key.Z, RawInputModifiers.Control, PhysicalKey.Z);
				Check("Ctrl+Z restores the deleted method", WaitFor(() => type.FindMethod(methodName) == newMethod));
			}
			else
				Check("the new method can be selected", false);

			// A property is deleted without asking and its accessors are deleted too
			var property = type.Properties.FirstOrDefault(a => a.GetMethod is not null);
			if (property is not null && window.SelectReference(property) && WaitFor(() => (treeView.SelectedItem as TreeNode)?.Reference == property)) {
				var getter = property.GetMethod!;
				treeView.Focus();
				PressKey(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete);
				Check("Delete key deletes the property and its getter", WaitFor(() => !type.Properties.Contains(property) && !type.Methods.Contains(getter)));
				PressKey(Key.Z, RawInputModifiers.Control, PhysicalKey.Z);
				Check("Ctrl+Z restores the property and its getter", WaitFor(() => type.Properties.Contains(property) && type.Methods.Contains(getter) && property.GetMethod == getter));
			}
			else
				Check("a property can be selected", false);

			// A new type in a namespace, deleted with the Delete key
			Check("a namespace is selected", window.SelectReference(new NamespaceReference(module.Assembly, "dnlib.DotNet.Pdb")) && WaitFor(() => treeView.SelectedItem is NamespaceNode nsNode && nsNode.Name == "dnlib.DotNet.Pdb"));
			shown = RunDialog(() => window.Create(MainWindow.CreateKind.Type, fromCodeViewer: false), dialog => {
				Check("Create Type shows the type dialog", dialog is TypeOptionsDlg && dialog.FindControl<TextBox>("nsTextBox")?.Text == "dnlib.DotNet.Pdb");
				ClickOK(dialog);
			});
			var newType = FindNewType(module);
			Check("Create Type adds the type to the namespace", shown && newType is not null);
			Check("the new type is selected", WaitFor(() => (treeView.SelectedItem as TreeNode)?.Reference == newType));
			if (newType is not null) {
				treeView.Focus();
				// The Delete key posts this command, the nested dispatcher frame of the confirmation can't run inside the
				// test's RunJobs() so the command is called directly
				shown = RunDialog(() => window.Delete(fromCodeViewer: false), ClickOK);
				Check("Delete Type deletes the type after asking", shown && FindNewType(module) is null);
			}

			// Undo the remaining changes: create type, create field, create method
			treeView.Focus();
			for (int i = 0; i < 4; i++)
				PressKey(Key.Z, RawInputModifiers.Control, PhysicalKey.Z);
			Check("Ctrl+Z undoes the created members", WaitFor(() => type.FindMethod(methodName) is null && type.FindField("MyField") is null && FindNewType(module) is null));
		}

		// Create an assembly, add a new netmodule to it, remove it, convert the assembly to a netmodule and back,
		// create a netmodule, and undo everything
		void TestAssemblyAndNetModuleCommands() {
			int origRootCount = RootCount;
			bool shown = RunDialog(() => window.CreateAssembly(), dialog => {
				Check("Create Assembly shows the assembly dialog", dialog is AssemblyOptionsDlg && dialog.FindControl<TextBox>("nameTextBox")?.Text == "MyAssembly");
				ClickOK(dialog);
			});
			var asmNode = treeView.SelectedItem as AssemblyDocumentNode;
			var asm = asmNode?.Document.AssemblyDef;
			Check("Create Assembly adds and selects the new assembly", shown && RootCount == origRootCount + 1 && asm?.Name == "MyAssembly");
			if (asmNode is null || asm is null)
				return;
			var module = asm.ManifestModule;
			Check("the new assembly is marked as modified", window.EditService.IsModified(module));

			shown = RunDialog(() => window.AddNewNetModuleToAssembly(), dialog => {
				Check("Add New NetModule shows the netmodule dialog", dialog is NetModuleOptionsDlg);
				ClickOK(dialog);
			});
			var netModule = asm.Modules.Count == 2 ? asm.Modules[1] : null;
			Check("Add New NetModule adds the netmodule to the assembly", shown && netModule is not null && netModule.Kind == ModuleKind.NetModule);
			Check("the assembly node shows the netmodule", WaitFor(() => asmNode.GetChildren().OfType<ModuleDocumentNode>().Count() == 2));
			Check("the new netmodule is marked as modified", netModule is not null && window.EditService.IsModified(netModule));
			if (netModule is not null && window.SelectReference(netModule) && WaitFor(() => (treeView.SelectedItem as ModuleDocumentNode)?.Module == netModule)) {
				window.RemoveNetModuleFromAssembly();
				Check("Remove NetModule removes it from the assembly", asm.Modules.Count == 1 && WaitFor(() => asmNode.GetChildren().OfType<ModuleDocumentNode>().Count() == 1));
				treeView.Focus();
				PressKey(Key.Z, RawInputModifiers.Control, PhysicalKey.Z);
				Check("Ctrl+Z adds the netmodule again", WaitFor(() => asm.Modules.Count == 2 && asmNode.GetChildren().OfType<ModuleDocumentNode>().Count() == 2));
				PressKey(Key.Z, RawInputModifiers.Control, PhysicalKey.Z);
				Check("Ctrl+Z removes the added netmodule", WaitFor(() => asm.Modules.Count == 1));
			}
			else
				Check("the netmodule can be selected", false);

			Check("the new assembly can be selected", window.SelectReference(asm) && WaitFor(() => treeView.SelectedItem == asmNode));
			window.ConvertAssemblyToNetModule();
			Check("Convert Assembly to NetModule makes it a netmodule", module.Assembly is null && module.Kind == ModuleKind.NetModule &&
				WaitFor(() => treeView.ItemsSource!.Cast<object>().OfType<ModuleDocumentNode>().Any(a => a.Module == module) && (treeView.SelectedItem as ModuleDocumentNode)?.Module == module));
			window.ConvertNetModuleToAssembly();
			Check("Convert NetModule to Assembly makes it an assembly", module.Assembly is not null && module.Kind == ModuleKind.Dll &&
				WaitFor(() => treeView.ItemsSource!.Cast<object>().OfType<AssemblyDocumentNode>().Any(a => a.Document.ModuleDef == module) && RootCount == origRootCount + 1));
			Screenshot("24-new-assembly");

			shown = RunDialog(() => window.CreateNetModule(), ClickOK);
			Check("Create NetModule adds a netmodule", shown && WaitFor(() => RootCount == origRootCount + 2 && treeView.SelectedItem is ModuleDocumentNode modNode && modNode.Parent is null));

			treeView.Focus();
			for (int i = 0; i < 4; i++)
				PressKey(Key.Z, RawInputModifiers.Control, PhysicalKey.Z);
			Check("Ctrl+Z removes the created assembly and netmodule", WaitFor(() => RootCount == origRootCount));
			// The rest of the test uses dnlib
			SelectTreeItem("dnlib (");
		}

		// Create a .resources file in dnlib (it has no resources), add, edit and delete elements, rename and delete
		// the resource, and undo everything
		void TestResources() {
			var module = utf8StringType?.Module;
			if (module is null || module.Resources.Count != 0 || !window.SelectReference(module) || !WaitFor(() => (treeView.SelectedItem as ModuleDocumentNode)?.Module == module)) {
				Check("the dnlib module without resources can be selected", false);
				return;
			}
			bool shown = RunDialog(() => window.CreateResource(ResourceType.Embedded), dialog => {
				Check("Create Multi-File Resource shows the resource dialog", dialog is ResourceDlg && FindTextBox(dialog, "nameTextBox")?.Text == "my.resources");
				Screenshot(dialog, "25-create-resource");
				ClickOK(dialog);
			});
			var resourceNode = treeView.SelectedItem as ResourceNode;
			Check("the new .resources file is added and selected", shown && module.Resources.Count == 1 && resourceNode?.Resource.Name == "my.resources" && resourceNode.IsResourceSet);
			if (resourceNode is null)
				return;

			shown = RunDialog(() => window.CreateResourceElement(), dialog => {
				Check("Create Resource shows the resource element dialog", dialog is ResourceElementDlg);
				var nameTextBox = FindTextBox(dialog, "nameTextBox");
				var valueTextBox = dialog.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(a => a.Name == "multiLineTextBox" && a.IsEffectivelyVisible) ??
					dialog.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(a => a.Name == "valueTextBox" && a.IsEffectivelyVisible);
				Check("the resource element dialog has a value text box", nameTextBox is not null && valueTextBox is not null);
				if (nameTextBox is null || valueTextBox is null)
					return;
				nameTextBox.Text = "Greeting";
				valueTextBox.Text = "Hello";
				Screenshot(dialog, "26-create-resource-element");
				ClickOK(dialog);
			});
			string? GetString(string name) {
				var er = module.Resources.FirstOrDefault(a => a.Name == "my.resources" || a.Name == "renamed.resources") as EmbeddedResource;
				var element = er is null ? null : ResourceReader.Read(module, er.CreateReader()).ResourceElements.FirstOrDefault(a => a.Name == name);
				return (element?.ResourceData as BuiltInResourceData)?.Data as string;
			}
			Check("Create Resource adds the string to the .resources file", shown && GetString("Greeting") == "Hello");
			Check("the new resource element is selected", WaitFor(() => (treeView.SelectedItem as ResourceElementNode)?.ResourceElement.Name == "Greeting"));

			shown = RunDialog(() => window.EditSettings(fromCodeViewer: false), dialog => {
				Check("Edit Resource shows the resource element dialog", dialog is ResourceElementDlg);
				var valueTextBox = dialog.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(a => (a.Name == "multiLineTextBox" || a.Name == "valueTextBox") && a.IsEffectivelyVisible);
				if (valueTextBox is not null)
					valueTextBox.Text = "Hello again";
				ClickOK(dialog);
			});
			Check("editing a resource element updates the .resources file", shown && GetString("Greeting") == "Hello again");

			if (treeView.SelectedItem is ResourceElementNode elementNode) {
				window.AddResourceElements(elementNode.ResourceNode, (EmbeddedResource)elementNode.ResourceNode.Resource, new[] {
					new ResourceElement { Name = "Data", ResourceData = new BuiltInResourceData(ResourceTypeCode.ByteArray, new byte[] { 1, 2, 3 }) },
				});
				Check("a byte array element is added", WaitFor(() => (treeView.SelectedItem as ResourceElementNode)?.ResourceElement.Name == "Data" && ((ResourceNode)((TreeNode)treeView.SelectedItem!).Parent!).GetChildren().Count == 2));
				window.Delete(fromCodeViewer: false);
				Check("Delete removes the resource element", WaitFor(() => treeView.SelectedItem is ResourceNode rn && rn.GetChildren().Count == 1) && GetString("Greeting") == "Hello again");
			}
			else
				Check("a resource element is selected", false);

			if (window.SelectReference(module) && WaitFor(() => treeView.SelectedItem is ModuleDocumentNode) && FindResourceNode(module) is ResourceNode rsrcNode) {
				treeView.SelectedItem = rsrcNode;
				shown = RunDialog(() => window.EditSettings(fromCodeViewer: false), dialog => {
					Check("Edit Resource shows the resource dialog", dialog is ResourceDlg);
					var nameTextBox = FindTextBox(dialog, "nameTextBox");
					if (nameTextBox is not null)
						nameTextBox.Text = "renamed.resources";
					ClickOK(dialog);
				});
				Check("Edit Resource renames the resource", shown && module.Resources.Count == 1 && module.Resources[0].Name == "renamed.resources");
				Screenshot("27-resources");
				window.Delete(fromCodeViewer: false);
				Check("Delete removes the resource", module.Resources.Count == 0);
			}
			else
				Check("the resource can be selected", false);

			treeView.Focus();
			PressKey(Key.Z, RawInputModifiers.Control, PhysicalKey.Z);
			Check("Ctrl+Z restores the deleted resource", WaitFor(() => module.Resources.Count == 1 && module.Resources[0].Name == "renamed.resources"));
			PressKey(Key.Z, RawInputModifiers.Control, PhysicalKey.Z);
			Check("Ctrl+Z undoes the rename", WaitFor(() => module.Resources.Count == 1 && module.Resources[0].Name == "my.resources" && GetString("Greeting") == "Hello again"));
			for (int i = 0; i < 5; i++)
				PressKey(Key.Z, RawInputModifiers.Control, PhysicalKey.Z);
			Check("Ctrl+Z undoes all resource changes", WaitFor(() => module.Resources.Count == 0));
		}

		// FindControl() doesn't find controls in a UserControl's name scope
		static TextBox? FindTextBox(Visual root, string name) =>
			root.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(a => a.Name == name);

		// Save Module shows the options dialog, the file is written to the filename in the dialog
		void TestSaveModuleDialog() {
			var module = utf8StringType?.Module;
			if (module is null || !window.SelectReference(module) || !WaitFor(() => (treeView.SelectedItem as ModuleDocumentNode)?.Module == module)) {
				Check("the dnlib module can be selected", false);
				return;
			}
			var dir = Path.Combine(Path.GetTempPath(), "dnSpy.Avalonia.SmokeTest-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(dir);
			var filename = Path.Combine(dir, "saved.dll");
			Task? saveTask = null;
			bool shown = RunDialog(() => saveTask = window.SaveModuleAsync(fromCodeViewer: false), dialog => {
				Check("Save Module shows the save options dialog", dialog is SaveModuleOptionsDlg && dialog.DataContext is SaveModuleOptionsVM);
				var filenameTextBox = FindTextBox(dialog, "filenameTextBox");
				Check("the save dialog has the module's filename", filenameTextBox?.Text == module.Location);
				Screenshot(dialog, "28-save-module");
				var tabControl = dialog.GetVisualDescendants().OfType<TabControl>().First();
				tabControl.SelectedIndex = 1;
				Settle();
				Screenshot(dialog, "29-save-module-writer-options");
				tabControl.SelectedIndex = 0;
				Settle();
				if (filenameTextBox is not null)
					filenameTextBox.Text = filename;
				if (dialog.DataContext is SaveModuleOptionsVM vm)
					vm.WritePdb = false;
				ClickOK(dialog);
			});
			Check("save dialog was shown", shown);
			Check("Save Module writes the module", saveTask is not null && WaitFor(() => saveTask.IsCompleted, TimeSpan.FromSeconds(60)) && File.Exists(filename));
			if (File.Exists(filename)) {
				using (var saved = ModuleDefMD.Load(File.ReadAllBytes(filename)))
					Check("the saved module can be read", saved.Types.Count == module.Types.Count && saved.Name == module.Name);
			}
			try {
				Directory.Delete(dir, recursive: true);
			}
			catch (IOException) {
			}
		}

		ResourceNode? FindResourceNode(ModuleDef module) =>
			(treeView.SelectedItem as ModuleDocumentNode)?.GetChildren().OfType<ResourcesFolderNode>().FirstOrDefault()?.GetChildren().OfType<ResourceNode>().FirstOrDefault();

		// Not ModuleDef.Find(), its cache throws when the types list was modified
		static TypeDef? FindNewType(ModuleDef module) => module.Types.FirstOrDefault(a => a.Namespace == "dnlib.DotNet.Pdb" && a.Name == "MyType");

		static void ClickOK(DialogWindow dialog) {
			var okButton = dialog.GetVisualDescendants().OfType<Button>().FirstOrDefault(a => a.IsDefault);
			if (okButton is not null && okButton.IsEffectivelyEnabled)
				Click(dialog, okButton, new Point(5, 5));
		}

		// Calls 'show' which shows a modal dialog (it runs a nested dispatcher frame until it's closed) and runs
		// 'interact' while it's open. The dialog is canceled afterwards if it's still open. Returns false if no
		// dialog was shown.
		bool RunDialog(Action show, Action<DialogWindow> interact) {
			int depth = DialogWindow.OpenDialogs.Count;
			bool ran = false, done = false;
			void Job() {
				if (done)
					return;
				if (DialogWindow.OpenDialogs.Count <= depth) {
					Dispatcher.UIThread.Post(Job, DispatcherPriority.Background);
					return;
				}
				done = ran = true;
				var dialog = DialogWindow.OpenDialogs[DialogWindow.OpenDialogs.Count - 1];
				try {
					Settle();
					interact(dialog);
				}
				catch (Exception ex) {
					Check($"dialog interaction: {ex}", false);
				}
				finally {
					if (DialogWindow.OpenDialogs.Contains(dialog))
						dialog.CloseDialog(false);
				}
			}
			Dispatcher.UIThread.Post(Job, DispatcherPriority.Background);
			show();
			done = true;
			Settle();
			return ran;
		}

		int RootCount => treeView.ItemsSource!.Cast<object>().Count();
		string SelectedNodeText => treeView.SelectedItem?.ToString() ?? string.Empty;

		static TreeNode? GetRoot(TreeNode? node) {
			while (node?.Parent is not null)
				node = node.Parent;
			return node;
		}

		// Shows why a compile check failed
		static void PrintDiagnostics(EditCodeWindow editWindow) {
			Console.WriteLine($"  compiling: {editWindow.IsCompiling}, {editWindow.DiagnosticItems.Count} diagnostic(s)");
			foreach (var item in editWindow.DiagnosticItems.Take(10)) {
				Console.WriteLine($"  {item.Diagnostic.Severity} {item.Diagnostic.Id}: {item.Diagnostic.Description} {item.Location}");
				if (item.Diagnostic.Severity == CompilerDiagnosticSeverity.Error && item.Diagnostic.LineLocationSpan is LineLocationSpan span && item.Diagnostic.Filename == "main.cs") {
					var doc = editWindow.Editors[0].Document;
					if (span.StartLinePosition.Line < doc.LineCount)
						Console.WriteLine("    " + doc.GetText(doc.GetLineByNumber(span.StartLinePosition.Line + 1)).Trim());
				}
			}
		}

		bool Check(string name, bool ok) {
			Console.WriteLine($"{(ok ? "PASS" : "FAIL")}: {name}");
			if (!ok)
				failures++;
			return ok;
		}

		// Runs the dispatcher until the condition is true. Decompilation happens on another thread.
		static bool WaitFor(Func<bool> condition) => WaitFor(condition, Timeout);

		static bool WaitFor(Func<bool> condition, TimeSpan timeout) {
			var sw = Stopwatch.StartNew();
			do {
				Dispatcher.UIThread.RunJobs();
				AvaloniaHeadlessPlatform.ForceRenderTimerTick();
				if (condition())
					return true;
				Thread.Sleep(20);
			} while (sw.Elapsed < timeout);
			return false;
		}

		// Lets layout, deferred dispatcher work and rendering catch up
		static void Settle() => WaitFor(() => false, TimeSpan.FromMilliseconds(500));

		void Screenshot(string name) => Screenshot(window, name);

		void Screenshot(TopLevel topLevel, string name) {
			if (screenshotDir is null)
				return;
			Settle();
			topLevel.CaptureRenderedFrame()?.Save(Path.Combine(screenshotDir, name + ".png"), new PngBitmapEncoderOptions());
		}

		void PressKey(Key key, RawInputModifiers modifiers, PhysicalKey physicalKey) {
			window.KeyPress(key, modifiers, physicalKey, null);
			window.KeyRelease(key, modifiers, physicalKey, null);
		}

		void Click(Visual visual, Point point) => Click(window, visual, point);

		static void Click(TopLevel topLevel, Visual visual, Point point) {
			var p = visual.TranslatePoint(point, topLevel) ?? throw new InvalidOperationException("Not in the window");
			topLevel.MouseMove(p);
			topLevel.MouseDown(p, MouseButton.Left);
			topLevel.MouseUp(p, MouseButton.Left);
		}

		// Text is a prefix of the node's text
		TreeViewItem? FindItem(string text) =>
			treeView.GetVisualDescendants().OfType<TreeViewItem>().FirstOrDefault(a => a.DataContext?.ToString()?.StartsWith(text, StringComparison.Ordinal) == true);

		void SelectTreeItem(string text) {
			WaitFor(() => FindItem(text) is not null);
			var item = FindItem(text) ?? throw new InvalidOperationException($"Tree node '{text}' wasn't found");
			item.BringIntoView();
			Settle();
			// The header is to the right of the expander
			var header = item.GetVisualDescendants().OfType<ContentPresenter>().First(a => a.Name == "PART_HeaderPresenter");
			Click(header, new Point(30, header.Bounds.Height / 2));
			Settle();
		}

		void ExpandAndSelect(string parent, string child) {
			SelectTreeItem(parent);
			PressKey(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight);
			Check($"Right arrow expands '{parent}'", WaitFor(() => FindItem(child) is not null));
			SelectTreeItem(child);
		}

		void ClickCode(int offset) {
			code.ScrollTo(code.Document.GetLineByOffset(offset).LineNumber, 1);
			Settle();
			var textView = code.TextArea.TextView;
			var location = code.Document.GetLocation(offset);
			var point = textView.GetVisualPosition(new TextViewPosition(location), VisualYPosition.LineMiddle) - textView.ScrollOffset;
			Click(textView, point);
		}

		void SelectDecompiler(string name) =>
			decompilerComboBox.SelectedItem = decompilerComboBox.ItemsSource!.Cast<IDecompiler>().First(a => a.UniqueNameUI == name);

		sealed class AssertLogger : TraceListener {
			public override void Fail(string? message, string? detailMessage) => Console.WriteLine($"  Debug.Assert failed: {message}");
			public override void Write(string? message) { }
			public override void WriteLine(string? message) { }
		}
	}
}

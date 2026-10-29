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

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using dnlib.DotNet;
using dnSpy.AsmEditor.Assembly;
using dnSpy.AsmEditor.Commands;
using dnSpy.Contracts.Documents;
using dnSpy.Contracts.MVVM;
using dnSpy.AsmEditor.Event;
using dnSpy.AsmEditor.Field;
using dnSpy.AsmEditor.Method;
using dnSpy.AsmEditor.MethodBody;
using dnSpy.AsmEditor.Module;
using dnSpy.AsmEditor.Namespace;
using dnSpy.AsmEditor.Properties;
using dnSpy.AsmEditor.Property;
using dnSpy.AsmEditor.Types;
using dnSpy.Avalonia.AsmEditor;
using dnSpy.Avalonia.Dialogs;
using dnSpy.Avalonia.Editing;
using dnSpy.Avalonia.TreeNodes;

namespace dnSpy.Avalonia {
	// The AsmEditor 'edit settings' dialogs (assembly, module, namespace, type and member properties)
	sealed partial class MainWindow {
		// A namespace in a module, the namespace tree nodes don't have a reference
		sealed class NamespaceTarget {
			public ModuleDef Module { get; }
			public string Name { get; }
			public NamespaceTarget(ModuleDef module, string name) {
				Module = module;
				Name = name;
			}
		}

		Action? updateEditMenuCreateDeleteItems;

		void InitializeAsmEditor() {
			DnlibTypePicker.DocumentService = host.DocumentService;
			DialogUtils.MainWindow = this;
			// The Edit menu's create and delete commands use the tree view's selection like WPF
			editMenu.Items.Add(new Separator());
			updateEditMenuCreateDeleteItems = AddCreateDeleteMenuItems(editMenu.Items, fromCodeViewer: false);
		}

		MenuItem CreateEditSettingsMenuItem(bool fromCodeViewer) {
			var menuItem = new MenuItem { InputGesture = new KeyGesture(Key.Enter, KeyModifiers.Alt) };
			menuItem.Click += (s, e) => EditSettings(fromCodeViewer);
			return menuItem;
		}

		// Updates the header and IsEnabled of a menu item created by CreateEditSettingsMenuItem()
		void UpdateEditSettingsMenuItem(MenuItem menuItem, bool fromCodeViewer) {
			var target = GetEditSettingsTarget(fromCodeViewer);
			menuItem.Header = GetEditSettingsHeader(target);
			menuItem.IsEnabled = target is not null;
		}

		static string GetEditSettingsHeader(object? target) => target switch {
			ResourceNode or ResourceElementNode => dnSpy_AsmEditor_Core_Resources.EditResourceCommand,
			AssemblyDef => "Edit Assembly Settings...",
			ModuleDef => "Edit Module Settings...",
			NamespaceTarget => "Rename Namespace...",
			TypeDef => "Edit Type Settings...",
			MethodDef => "Edit Method Settings...",
			FieldDef => "Edit Field Settings...",
			PropertyDef => "Edit Property Settings...",
			EventDef => "Edit Event Settings...",
			_ => "Edit Settings...",
		};

		// The selected node's assembly, module, namespace or member, or in the code viewer, the member at the caret
		object? GetEditSettingsTarget(bool fromCodeViewer) {
			if (fromCodeViewer) {
				var member = GetEditTarget(fromCodeViewer: true).member;
				return member is TypeDef || member is MethodDef || member is FieldDef || member is PropertyDef || member is EventDef ? member : null;
			}
			switch (treeView.SelectedItem) {
			case ResourceNode resourceNode:
				return resourceNode;
			case ResourceElementNode elementNode when elementNode.ResourceNode.Resource is dnlib.DotNet.EmbeddedResource:
				return elementNode;
			case AssemblyDocumentNode asmNode:
				return asmNode.Document.AssemblyDef;
			case ModuleDocumentNode modNode:
				return modNode.Module;
			case NamespaceNode nsNode when nsNode.Parent is ModuleDocumentNode modNode:
				return new NamespaceTarget(modNode.Module, nsNode.Name);
			case TypeNode typeNode:
				return typeNode.TypeDef;
			case MethodNode methodNode:
				return methodNode.MethodDef;
			case FieldNode fieldNode:
				return fieldNode.FieldDef;
			case PropertyNode propertyNode:
				return propertyNode.PropertyDef;
			case EventNode eventNode:
				return eventNode.EventDef;
			default:
				return null;
			}
		}

		void EditSettings_Click(object? sender, RoutedEventArgs e) => EditSettings(codeViewer.IsKeyboardFocusWithin);

		internal void EditSettings(bool fromCodeViewer) {
			var target = GetEditSettingsTarget(fromCodeViewer);
			if (target is null)
				return;
			var decompilerService = host.DecompilerService;
			ModuleDef? module;
			SettingsChange? change;
			object? toSelect = target;
			switch (target) {
			case ResourceNode resourceNode:
				EditResource(resourceNode);
				return;
			case ResourceElementNode elementNode:
				EditResourceElement(elementNode);
				return;
			case AssemblyDef asm: {
				module = asm.ManifestModule;
				var vm = new AssemblyOptionsVM(new AssemblyOptions(asm), module, decompilerService);
				change = ShowSettingsDialog(new AssemblyOptionsDlg(), vm) ? SettingsChanges.Create(asm, vm.CreateAssemblyOptions()) : null;
				break;
			}
			case ModuleDef mod: {
				module = mod;
				var vm = new ModuleOptionsVM(mod, new ModuleOptions(mod), decompilerService);
				change = ShowSettingsDialog(new ModuleOptionsDlg(), vm) ? SettingsChanges.Create(mod, vm.CreateModuleOptions()) : null;
				break;
			}
			case NamespaceTarget ns: {
				module = ns.Module;
				var vm = new NamespaceVM(ns.Name);
				change = ShowSettingsDialog(new NamespaceDlg(), vm) && vm.Name != ns.Name ? SettingsChanges.RenameNamespace(ns.Module, ns.Name, vm.Name) : null;
				toSelect = new NamespaceTarget(ns.Module, vm.Name);
				break;
			}
			case TypeDef type: {
				module = type.Module;
				var vm = new TypeOptionsVM(new TypeDefOptions(type), module, decompilerService, type);
				change = ShowSettingsDialog(new TypeOptionsDlg(), vm) ? SettingsChanges.Create(type, module, vm.CreateTypeDefOptions()) : null;
				break;
			}
			case MethodDef method: {
				module = method.Module;
				var vm = new MethodOptionsVM(new MethodDefOptions(method), module, decompilerService, method.DeclaringType, method);
				change = ShowSettingsDialog(new MethodOptionsDlg(), vm) ? SettingsChanges.Create(method, vm.CreateMethodDefOptions()) : null;
				break;
			}
			case FieldDef field: {
				module = field.Module;
				var vm = new FieldOptionsVM(new FieldDefOptions(field), module, decompilerService, field.DeclaringType);
				change = ShowSettingsDialog(new FieldOptionsDlg(), vm) ? SettingsChanges.Create(field, vm.CreateFieldDefOptions()) : null;
				break;
			}
			case PropertyDef property: {
				module = property.Module;
				var vm = new PropertyOptionsVM(new PropertyDefOptions(property), module, decompilerService, property.DeclaringType);
				change = ShowSettingsDialog(new PropertyOptionsDlg(), vm) ? SettingsChanges.Create(property, vm.CreatePropertyDefOptions()) : null;
				break;
			}
			case EventDef @event: {
				module = @event.Module;
				var vm = new EventOptionsVM(new EventDefOptions(@event), module, decompilerService, @event.DeclaringType);
				change = ShowSettingsDialog(new EventOptionsDlg(), vm) ? SettingsChanges.Create(@event, vm.CreateEventDefOptions()) : null;
				break;
			}
			default:
				return;
			}
			if (change is null || module is null)
				return;
			ApplyChange(module, change, toSelect);
		}

		void ApplyChange(ModuleDef module, SettingsChange change, object? toSelect, IEnumerable<ModuleDef>? otherModules = null) {
			try {
				editService.Apply(new DelegateModuleEdit(module, change.Apply, change.Undo, otherModules));
			}
			catch (Exception ex) {
				_ = MessageBox.ShowAsync(this, $"Couldn't update the module:\n\n{ex}", "dnSpy");
				return;
			}
			var node = toSelect is NamespaceTarget nsTarget ?
				(treeModel.FindNode(nsTarget.Module) as ModuleDocumentNode)?.FindNamespace(nsTarget.Name) :
				treeModel.FindNode(toSelect);
			if (node is not null && node != treeView.SelectedItem)
				SelectNode(node);
		}

		// The method whose body is edited: the selected method node or the method at the caret in the code viewer
		MethodDef? GetEditMethodBodyTarget(bool fromCodeViewer) => GetEditTarget(fromCodeViewer).member as MethodDef;

		void EditMethodBody_Click(object? sender, RoutedEventArgs e) => EditMethodBody(codeViewer.IsKeyboardFocusWithin);
		void ReplaceMethodBodyWithStub_Click(object? sender, RoutedEventArgs e) => ReplaceMethodBodyWithStub(codeViewer.IsKeyboardFocusWithin);

		internal void EditMethodBody(bool fromCodeViewer) {
			if (GetEditMethodBodyTarget(fromCodeViewer) is not MethodDef method)
				return;
			var vm = new MethodBodyVM(new MethodBodyOptions(method), method.Module, host.DecompilerService, method.DeclaringType, method);
			var dialog = new MethodBodyDlg();
			dialog.Title = $"{dialog.Title} - {treeModel.FindNode(method)?.ToString() ?? method.Name.String}";
			if (!ShowSettingsDialog(dialog, vm))
				return;
			ApplyChange(method.Module, SettingsChanges.Create(method, host.MethodAnnotations, vm.CreateMethodBodyOptions()), method);
		}

		internal void ReplaceMethodBodyWithStub(bool fromCodeViewer) {
			if (GetEditMethodBodyTarget(fromCodeViewer) is not MethodDef method)
				return;
			var options = new DefaultCilBodyBuilder(method).CreateDefaultCilMethodBody();
			ApplyChange(method.Module, SettingsChanges.Create(method, host.MethodAnnotations, options), method);
		}

		internal enum CreateKind {
			Type,
			NestedType,
			Method,
			Field,
			Property,
			Event,
		}

		static string GetCreateHeader(CreateKind kind) => kind switch {
			CreateKind.Type => dnSpy_AsmEditor_Core_Resources.CreateTypeCommand,
			CreateKind.NestedType => dnSpy_AsmEditor_Core_Resources.CreateNestedTypeCommand,
			CreateKind.Method => dnSpy_AsmEditor_Core_Resources.CreateMethodCommand,
			CreateKind.Field => dnSpy_AsmEditor_Core_Resources.CreateFieldCommand,
			CreateKind.Property => dnSpy_AsmEditor_Core_Resources.CreatePropertyCommand,
			_ => dnSpy_AsmEditor_Core_Resources.CreateEventCommand,
		};

		// The module and namespace of a new type: the selected namespace or module node
		NamespaceTarget? GetCreateTypeTarget(bool fromCodeViewer) {
			if (fromCodeViewer)
				return null;
			return treeView.SelectedItem switch {
				NamespaceNode nsNode when nsNode.Parent is ModuleDocumentNode modNode => new NamespaceTarget(modNode.Module, nsNode.Name),
				ModuleDocumentNode modNode => new NamespaceTarget(modNode.Module, string.Empty),
				_ => null,
			};
		}

		// The type that gets a new nested type or member: the selected type, the selected member's type, or the
		// type or member at the caret in the code viewer
		TypeDef? GetCreateMemberTarget(bool fromCodeViewer) {
			if (fromCodeViewer) {
				var member = GetEditTarget(fromCodeViewer: true).member;
				return member as TypeDef ?? member?.DeclaringType;
			}
			return treeView.SelectedItem switch {
				TypeNode typeNode => typeNode.TypeDef,
				MethodNode methodNode => methodNode.MethodDef.DeclaringType,
				FieldNode fieldNode => fieldNode.FieldDef.DeclaringType,
				PropertyNode propertyNode => propertyNode.PropertyDef.DeclaringType,
				EventNode eventNode => eventNode.EventDef.DeclaringType,
				_ => null,
			};
		}

		bool CanCreate(CreateKind kind, bool fromCodeViewer) =>
			kind == CreateKind.Type ? GetCreateTypeTarget(fromCodeViewer) is not null : GetCreateMemberTarget(fromCodeViewer) is not null;

		internal void Create(CreateKind kind, bool fromCodeViewer) {
			var decompilerService = host.DecompilerService;
			if (kind == CreateKind.Type) {
				if (GetCreateTypeTarget(fromCodeViewer) is not NamespaceTarget target)
					return;
				var vm = new TypeOptionsVM(MemberChanges.CreateTypeOptions(target.Module, target.Name, isNestedType: false), target.Module, decompilerService, null);
				if (!ShowCreateDialog(new TypeOptionsDlg(), vm, dnSpy_AsmEditor_Core_Resources.CreateTypeCommand2))
					return;
				var type = vm.CreateTypeDefOptions().CreateTypeDef(target.Module);
				ApplyChange(target.Module, MemberChanges.AddType(target.Module, null, type), type);
				return;
			}

			if (GetCreateMemberTarget(fromCodeViewer) is not TypeDef ownerType)
				return;
			var module = ownerType.Module;
			IMemberDef? newDef = null;
			SettingsChange? change = null;
			switch (kind) {
			case CreateKind.NestedType: {
				var vm = new TypeOptionsVM(MemberChanges.CreateTypeOptions(module, string.Empty, isNestedType: true), module, decompilerService, null);
				if (ShowCreateDialog(new TypeOptionsDlg(), vm, dnSpy_AsmEditor_Core_Resources.CreateNestedTypeCommand2)) {
					var type = vm.CreateTypeDefOptions().CreateTypeDef(module);
					(newDef, change) = (type, MemberChanges.AddType(module, ownerType, type));
				}
				break;
			}
			case CreateKind.Method: {
				var vm = new MethodOptionsVM(MemberChanges.CreateMethodOptions(ownerType), module, decompilerService, ownerType, null);
				if (ShowCreateDialog(new MethodOptionsDlg(), vm, dnSpy_AsmEditor_Core_Resources.CreateMethodCommand2)) {
					var method = vm.CreateMethodDefOptions().CreateMethodDef(module);
					(newDef, change) = (method, MemberChanges.AddMethod(ownerType, method));
				}
				break;
			}
			case CreateKind.Field: {
				var vm = new FieldOptionsVM(MemberChanges.CreateFieldOptions(ownerType), module, decompilerService, ownerType);
				if (ShowCreateDialog(new FieldOptionsDlg(), vm, dnSpy_AsmEditor_Core_Resources.CreateFieldCommand2)) {
					var field = vm.CreateFieldDefOptions().CreateFieldDef(module);
					(newDef, change) = (field, MemberChanges.AddField(ownerType, field));
				}
				break;
			}
			case CreateKind.Property: {
				var vm = new PropertyOptionsVM(MemberChanges.CreatePropertyOptions(ownerType), module, decompilerService, ownerType);
				if (ShowCreateDialog(new PropertyOptionsDlg(), vm, dnSpy_AsmEditor_Core_Resources.CreatePropertyCommand2)) {
					var property = vm.CreatePropertyDefOptions().CreatePropertyDef(module);
					(newDef, change) = (property, MemberChanges.AddProperty(ownerType, property));
				}
				break;
			}
			case CreateKind.Event: {
				var vm = new EventOptionsVM(MemberChanges.CreateEventOptions(ownerType), module, decompilerService, ownerType);
				if (ShowCreateDialog(new EventOptionsDlg(), vm, dnSpy_AsmEditor_Core_Resources.CreateEventCommand2)) {
					var @event = vm.CreateEventDefOptions().CreateEventDef(module);
					(newDef, change) = (@event, MemberChanges.AddEvent(ownerType, @event));
				}
				break;
			}
			}
			if (change is not null)
				ApplyChange(module, change, newDef);
		}

		bool ShowCreateDialog(DialogWindow dialog, object vm, string title) {
			dialog.Title = title;
			return ShowSettingsDialog(dialog, vm);
		}

		// What Delete deletes: the selected types and members, the selected namespaces, or the member at the
		// caret in the code viewer
		(IMemberDef[] defs, NamespaceTarget[] namespaces) GetDeleteTargets(bool fromCodeViewer) {
			if (fromCodeViewer) {
				var member = GetEditTarget(fromCodeViewer: true).member;
				return (member is null ? Array.Empty<IMemberDef>() : new[] { member }, Array.Empty<NamespaceTarget>());
			}
			var nodes = treeView.SelectedItems.OfType<TreeNode>().ToArray();
			var defs = nodes.Select(a => a switch {
				TypeNode typeNode => typeNode.TypeDef,
				MethodNode methodNode => methodNode.MethodDef,
				FieldNode fieldNode => fieldNode.FieldDef,
				PropertyNode propertyNode => propertyNode.PropertyDef,
				EventNode eventNode => (IMemberDef)eventNode.EventDef,
				_ => null,
			}).ToArray();
			if (nodes.Length > 0 && defs.All(a => a is not null))
				return (defs!, Array.Empty<NamespaceTarget>());
			var namespaces = nodes.Select(a => a is NamespaceNode nsNode && nsNode.Parent is ModuleDocumentNode modNode ? new NamespaceTarget(modNode.Module, nsNode.Name) : null).ToArray();
			if (nodes.Length > 0 && namespaces.All(a => a is not null))
				return (Array.Empty<IMemberDef>(), namespaces!);
			return (Array.Empty<IMemberDef>(), Array.Empty<NamespaceTarget>());
		}

		static string GetDeleteHeader(IMemberDef[] defs, NamespaceTarget[] namespaces) {
			if (namespaces.Length > 0)
				return namespaces.Length == 1 ? dnSpy_AsmEditor_Core_Resources.DeleteNamespaceCommand : string.Format(dnSpy_AsmEditor_Core_Resources.DeleteNamespacesCommand, namespaces.Length);
			return defs.FirstOrDefault() switch {
				TypeDef => dnSpy_AsmEditor_Core_Resources.DeleteTypeCommand,
				MethodDef => dnSpy_AsmEditor_Core_Resources.DeleteMethodCommand,
				FieldDef => dnSpy_AsmEditor_Core_Resources.DeleteFieldCommand,
				PropertyDef => dnSpy_AsmEditor_Core_Resources.DeletePropertyCommand,
				EventDef => dnSpy_AsmEditor_Core_Resources.DeleteEventCommand,
				_ => "Delete",
			};
		}

		bool CanDelete(bool fromCodeViewer) {
			if (!fromCodeViewer && CanDeleteResources())
				return true;
			var (defs, namespaces) = GetDeleteTargets(fromCodeViewer);
			return defs.Length > 0 || namespaces.Length > 0;
		}

		internal ConfirmDialog? LastConfirmDialog { get; private set; }

		internal bool Delete(bool fromCodeViewer) {
			if (!fromCodeViewer && CanDeleteResources()) {
				DeleteResources();
				return true;
			}
			var (defs, namespaces) = GetDeleteTargets(fromCodeViewer);
			// All selected items are in the same module unless several namespaces/members were selected in different modules
			var module = defs.FirstOrDefault()?.Module ?? namespaces.FirstOrDefault()?.Module;
			if (module is null || defs.Any(a => a.Module != module) || namespaces.Any(a => a.Module != module))
				return false;

			// Same as WPF: ask before deleting definitions that could be referenced by other assemblies
			var question = defs.FirstOrDefault() switch {
				TypeDef => dnSpy_AsmEditor_Core_Resources.AskDeleteType,
				MethodDef => dnSpy_AsmEditor_Core_Resources.AskDeleteMethod,
				FieldDef => dnSpy_AsmEditor_Core_Resources.AskDeleteField,
				_ => null,
			};
			if (question is not null) {
				var dialog = new ConfirmDialog(question, GetDeleteHeader(defs, namespaces));
				LastConfirmDialog = dialog;
				if (!dialog.ShowModal(this))
					return false;
			}

			// Select the parent of the deleted node afterwards
			var parent = (treeView.SelectedItem as TreeNode)?.Parent;
			var change = namespaces.Length > 0 ?
				MemberChanges.Delete(namespaces.SelectMany(a => a.Module.Types.Where(t => t.Namespace == a.Name)).ToArray()) :
				MemberChanges.Delete(defs);
			ApplyChange(module, change, null);
			if (!fromCodeViewer && parent is not null && IsInTree(parent))
				SelectNode(parent);
			else if (treeModel.FindNode(module) is TreeNode modNode)
				SelectNode(modNode);
			return true;
		}

		NamespaceTarget? GetMoveTypesToEmptyNamespaceTarget() =>
			treeView.SelectedItem is NamespaceNode nsNode && nsNode.Name.Length != 0 && nsNode.Parent is ModuleDocumentNode modNode ? new NamespaceTarget(modNode.Module, nsNode.Name) : null;

		internal void MoveTypesToEmptyNamespace() {
			if (GetMoveTypesToEmptyNamespaceTarget() is not NamespaceTarget ns)
				return;
			ApplyChange(ns.Module, SettingsChanges.RenameNamespace(ns.Module, ns.Name, string.Empty), new NamespaceTarget(ns.Module, string.Empty));
		}

		// The assembly that the netmodule commands use: the selected assembly or the assembly of the selected module
		IDsDocument? GetNetModuleTargetAssembly() => treeView.SelectedItem switch {
			AssemblyDocumentNode asmNode => asmNode.Document,
			ModuleDocumentNode modNode when modNode.Parent is AssemblyDocumentNode asmNode => asmNode.Document,
			_ => null,
		};

		internal void CreateAssembly() {
			var newModule = new ModuleDefUser();
			var vm = new AssemblyOptionsVM(AssemblyOptions.Create("MyAssembly"), newModule, host.DecompilerService) { CanShowClrVersion = true };
			if (!ShowCreateDialog(new AssemblyOptionsDlg(), vm, dnSpy_AsmEditor_Core_Resources.CreateAssemblyCommand2))
				return;
			var change = DocumentChanges.CreateAssembly(host.DocumentService, vm.CreateAssemblyOptions(), newModule, out var document);
			ApplyChange(document.ModuleDef!, change, document.AssemblyDef);
		}

		internal void CreateNetModule() {
			var vm = new NetModuleOptionsVM();
			if (!ShowCreateDialog(new NetModuleOptionsDlg(), vm, dnSpy_AsmEditor_Core_Resources.CreateNetModuleCommand2))
				return;
			var change = DocumentChanges.CreateNetModule(host.DocumentService, vm.CreateNetModuleOptions(), out var document);
			ApplyChange(document.ModuleDef!, change, document.ModuleDef);
		}

		internal void AddNewNetModuleToAssembly() {
			if (GetNetModuleTargetAssembly() is not IDsDocument asmDocument || asmDocument.ModuleDef is not ModuleDef manifestModule)
				return;
			var vm = new NetModuleOptionsVM(manifestModule);
			if (!ShowCreateDialog(new NetModuleOptionsDlg(), vm, dnSpy_AsmEditor_Core_Resources.AddNewNetModuleToAssemblyCommand2))
				return;
			var modDocument = DocumentChanges.CreateNetModuleDocument(vm.CreateNetModuleOptions());
			// The new netmodule hasn't been saved
			ApplyChange(manifestModule, DocumentChanges.AddNetModule(asmDocument, modDocument), modDocument.ModuleDef, new[] { modDocument.ModuleDef! });
		}

		void AddExistingNetModuleToAssembly() {
			if (GetNetModuleTargetAssembly() is not IDsDocument asmDocument || asmDocument.ModuleDef is not ModuleDef manifestModule)
				return;
			var filename = DialogUtils.PickOpenFilename(this, PickFilenameConstants.NetModuleFilter);
			if (string.IsNullOrEmpty(filename))
				return;
			var document = host.DocumentService.CreateDocument(DsDocumentInfo.CreateDocument(filename), filename, isModule: true);
			if (document.ModuleDef is null || document.AssemblyDef is not null || document is not IDsDotNetDocument) {
				(document as IDisposable)?.Dispose();
				DialogUtils.ShowMessage(this, string.Format(dnSpy_AsmEditor_Core_Resources.Error_NotNetModule, filename));
				return;
			}
			AddNetModuleToAssembly(asmDocument, document);
		}

		// Used by AddExistingNetModuleToAssembly() and the smoke test
		internal void AddNetModuleToAssembly(IDsDocument asmDocument, IDsDocument modDocument) =>
			ApplyChange(asmDocument.ModuleDef!, DocumentChanges.AddNetModule(asmDocument, modDocument), modDocument.ModuleDef);

		(IDsDocument asmDocument, IDsDocument modDocument)? GetRemoveNetModuleTarget() =>
			treeView.SelectedItem is ModuleDocumentNode modNode && modNode.Parent is AssemblyDocumentNode asmNode &&
			DocumentChanges.CanRemoveNetModule(asmNode.Document, modNode.Document) ? (asmNode.Document, modNode.Document) : null;

		internal void RemoveNetModuleFromAssembly() {
			if (GetRemoveNetModuleTarget() is not var (asmDocument, modDocument))
				return;
			ApplyChange(asmDocument.ModuleDef!, DocumentChanges.RemoveNetModule(asmDocument, modDocument), asmDocument.AssemblyDef);
		}

		IDsDocument? GetConvertNetModuleToAssemblyTarget() =>
			treeView.SelectedItem is ModuleDocumentNode modNode && modNode.Parent is null && DocumentChanges.CanConvertNetModuleToAssembly(modNode.Document) ? modNode.Document : null;

		internal void ConvertNetModuleToAssembly() {
			if (GetConvertNetModuleToAssemblyTarget() is not IDsDocument modDocument)
				return;
			var change = DocumentChanges.ConvertNetModuleToAssembly(host.DocumentService, modDocument, out _);
			ApplyChange(modDocument.ModuleDef!, change, modDocument.ModuleDef);
		}

		IDsDocument? GetConvertAssemblyToNetModuleTarget() =>
			treeView.SelectedItem is AssemblyDocumentNode asmNode && DocumentChanges.CanConvertAssemblyToNetModule(asmNode.Document) ? asmNode.Document : null;

		internal void ConvertAssemblyToNetModule() {
			if (GetConvertAssemblyToNetModuleTarget() is not IDsDocument asmDocument)
				return;
			var change = DocumentChanges.ConvertAssemblyToNetModule(host.DocumentService, asmDocument, out var modDocument);
			ApplyChange(asmDocument.ModuleDef!, change, modDocument.ModuleDef);
		}

		void AddDocumentMenuItems(ItemCollection items, List<Action> updaters) {
			void Add(string header, Action action, Func<bool> canExecute) {
				var menuItem = new MenuItem { Header = header };
				menuItem.Click += (s, e) => action();
				items.Add(menuItem);
				updaters.Add(() => menuItem.IsEnabled = canExecute());
			}
			Add(dnSpy_AsmEditor_Core_Resources.CreateAssemblyCommand, CreateAssembly, () => true);
			Add(dnSpy_AsmEditor_Core_Resources.CreateNetModuleCommand, CreateNetModule, () => true);
			Add(dnSpy_AsmEditor_Core_Resources.AddNewNetModuleToAssemblyCommand, AddNewNetModuleToAssembly, () => GetNetModuleTargetAssembly() is not null);
			Add(dnSpy_AsmEditor_Core_Resources.AddExistingNetModuleToAssemblyCommand, AddExistingNetModuleToAssembly, () => GetNetModuleTargetAssembly() is not null);
			Add(dnSpy_AsmEditor_Core_Resources.RemoveNetModuleCommand, RemoveNetModuleFromAssembly, () => GetRemoveNetModuleTarget() is not null);
			Add(dnSpy_AsmEditor_Core_Resources.ConvNetModuleToAssemblyCommand, ConvertNetModuleToAssembly, () => GetConvertNetModuleToAssemblyTarget() is not null);
			Add(dnSpy_AsmEditor_Core_Resources.ConvAssemblyToNetModuleCommand, ConvertAssemblyToNetModule, () => GetConvertAssemblyToNetModuleTarget() is not null);
			items.Add(new Separator());
		}

		// Adds the create, delete and move types menu items. The headers and IsEnabled are updated by the returned action.
		Action AddCreateDeleteMenuItems(ItemCollection items, bool fromCodeViewer) {
			var updaters = new List<Action>();
			// The assembly and netmodule commands use the tree view's selection
			if (!fromCodeViewer) {
				AddDocumentMenuItems(items, updaters);
				AddResourceMenuItems(items, updaters);
			}
			foreach (var kind in new[] { CreateKind.Type, CreateKind.NestedType, CreateKind.Method, CreateKind.Field, CreateKind.Property, CreateKind.Event }) {
				// The code viewer has no namespace or module to add types to
				if (fromCodeViewer && kind == CreateKind.Type)
					continue;
				var menuItem = new MenuItem { Header = GetCreateHeader(kind) };
				var tmp = kind;
				menuItem.Click += (s, e) => Create(tmp, fromCodeViewer);
				items.Add(menuItem);
				updaters.Add(() => menuItem.IsEnabled = CanCreate(tmp, fromCodeViewer));
			}
			items.Add(new Separator());
			var delete = new MenuItem { InputGesture = fromCodeViewer ? null : new KeyGesture(Key.Delete) };
			delete.Click += (s, e) => Delete(fromCodeViewer);
			items.Add(delete);
			updaters.Add(() => {
				if (!fromCodeViewer && CanDeleteResources()) {
					delete.Header = dnSpy_AsmEditor_Core_Resources.DeleteResourceCommand;
					delete.IsEnabled = true;
					return;
				}
				var (defs, namespaces) = GetDeleteTargets(fromCodeViewer);
				delete.Header = GetDeleteHeader(defs, namespaces);
				delete.IsEnabled = defs.Length > 0 || namespaces.Length > 0;
			});
			if (!fromCodeViewer) {
				var move = new MenuItem { Header = dnSpy_AsmEditor_Core_Resources.MoveTypesToEmptyNamespaceCommand };
				move.Click += (s, e) => MoveTypesToEmptyNamespace();
				items.Add(move);
				updaters.Add(() => move.IsEnabled = GetMoveTypesToEmptyNamespaceTarget() is not null);
			}
			return () => {
				foreach (var updater in updaters)
					updater();
			};
		}

		// Used by the smoke test
		internal bool SelectReference(object reference) {
			if (treeModel.FindNode(reference) is not TreeNode node)
				return false;
			SelectNode(node);
			return true;
		}

		internal DialogWindow? LastSettingsDialog { get; private set; }

		bool ShowSettingsDialog(DialogWindow dialog, object vm) {
			dialog.DataContext = vm;
			LastSettingsDialog = dialog;
			return dialog.ShowModal(this);
		}
	}
}

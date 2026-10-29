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
using System.IO;
using System.Linq;
using Avalonia.Controls;
using dnlib.DotNet;
using dnlib.DotNet.Resources;
using dnSpy.AsmEditor.Commands;
using dnSpy.AsmEditor.Properties;
using dnSpy.AsmEditor.Resources;
using dnSpy.Avalonia.AsmEditor;
using dnSpy.Avalonia.TreeNodes;

namespace dnSpy.Avalonia {
	// The AsmEditor resource commands: create, edit and delete resources and the elements of .resources files.
	// Images and image lists aren't supported, WPF's dialogs use System.Drawing and WinForms.
	sealed partial class MainWindow {
		// The module whose resources are changed: the selected Resources folder or resource, or like WPF, a module
		// without resources (it has no Resources folder)
		ModuleDef? GetResourcesTarget() => treeView.SelectedItem switch {
			ResourcesFolderNode folderNode => folderNode.Module,
			ResourceNode resourceNode => resourceNode.Module,
			ModuleDocumentNode modNode when modNode.Module.Resources.Count == 0 => modNode.Module,
			_ => null,
		};

		// The .resources file that gets new elements: the selected .resources file or element
		ResourceNode? GetResourceSetTarget() => treeView.SelectedItem switch {
			ResourceNode resourceNode when resourceNode.IsResourceSet && resourceNode.Resource is EmbeddedResource => resourceNode,
			ResourceElementNode elementNode => elementNode.ResourceNode,
			_ => null,
		};

		// Applies a resource change and selects the resource or element
		void ApplyResourceChange(ModuleDef module, SettingsChange change, string? resourceName, string? elementName) {
			ApplyChange(module, change, null);
			if (resourceName is not null && FindResourceNode(module, resourceName, elementName) is TreeNode node)
				SelectNode(node);
		}

		TreeNode? FindResourceNode(ModuleDef module, string resourceName, string? elementName) {
			if (treeModel.FindNode(module) is not ModuleDocumentNode modNode)
				return null;
			var folder = modNode.GetChildren().OfType<ResourcesFolderNode>().FirstOrDefault();
			var resourceNode = folder?.GetChildren().OfType<ResourceNode>().FirstOrDefault(a => a.Resource.Name == resourceName);
			if (resourceNode is null || elementName is null)
				return resourceNode;
			return resourceNode.GetChildren().OfType<ResourceElementNode>().FirstOrDefault(a => a.ResourceElement.Name == elementName);
		}

		internal void CreateFileResources() {
			if (GetResourcesTarget() is not ModuleDef module)
				return;
			var filenames = DialogUtils.PickOpenFilenames(this, null);
			if (filenames.Length == 0)
				return;
			Resource[] resources;
			try {
				resources = filenames.Select(a => (Resource)new EmbeddedResource(Path.GetFileName(a), File.ReadAllBytes(a), ManifestResourceAttributes.Public)).ToArray();
			}
			catch (Exception ex) {
				DialogUtils.ShowMessage(this, string.Format(dnSpy_AsmEditor_Core_Resources.Error_ReadingFiles, ex.Message));
				return;
			}
			ApplyResourceChange(module, ResourceChanges.AddResources(module, resources), resources[0].Name, null);
		}

		internal void CreateResource(ResourceType resourceType) {
			if (GetResourcesTarget() is not ModuleDef module)
				return;
			var options = new ResourceOptions {
				ResourceType = resourceType,
				Attributes = ManifestResourceAttributes.Public,
			};
			string title;
			switch (resourceType) {
			case ResourceType.Embedded:
				options.Name = "my.resources";
				title = dnSpy_AsmEditor_Core_Resources.CreateMultiFileResourceCommand2;
				break;
			case ResourceType.AssemblyLinked:
				options.Name = "asmlinked";
				options.Assembly = module.CorLibTypes.AssemblyRef;
				title = dnSpy_AsmEditor_Core_Resources.CreateAssemblyLinkedResourceCommand2;
				break;
			default:
				options.Name = "filelinked";
				options.File = new FileDefUser("somefile", dnlib.DotNet.FileAttributes.ContainsNoMetadata, Array.Empty<byte>());
				title = dnSpy_AsmEditor_Core_Resources.CreateFileLinkedResourceCommand2;
				break;
			}
			var vm = new ResourceVM(options, module, host.DecompilerService);
			if (!ShowCreateDialog(new ResourceDlg(), vm, title))
				return;
			var opts = vm.CreateResourceOptions();
			Resource resource = resourceType switch {
				ResourceType.Embedded => ResourceChanges.CreateEmptyResourceSet(module, opts.Name, opts.Attributes),
				ResourceType.AssemblyLinked => new AssemblyLinkedResource(opts.Name, opts.Assembly, opts.Attributes),
				_ => new LinkedResource(opts.Name, opts.File, opts.Attributes),
			};
			foreach (var ca in opts.CustomAttributes)
				resource.CustomAttributes.Add(ca);
			ApplyResourceChange(module, ResourceChanges.AddResources(module, new[] { resource }), resource.Name, null);
		}

		void EditResource(ResourceNode resourceNode) {
			var vm = new ResourceVM(new ResourceOptions(resourceNode.Resource), resourceNode.Module, host.DecompilerService);
			if (!ShowSettingsDialog(new ResourceDlg(), vm))
				return;
			var options = vm.CreateResourceOptions();
			ApplyResourceChange(resourceNode.Module, ResourceChanges.Edit(resourceNode.Resource, options), options.Name, null);
		}

		void EditResourceElement(ResourceElementNode elementNode) {
			var resourceNode = elementNode.ResourceNode;
			if (resourceNode.Resource is not EmbeddedResource resource)
				return;
			var vm = new ResourceElementVM(new ResourceElementOptions(elementNode.ResourceElement), resourceNode.Module) { CanChangeType = false };
			var dialog = new ResourceElementDlg();
			dialog.Title = dnSpy_AsmEditor_Core_Resources.EditResourceCommand2;
			if (!ShowSettingsDialog(dialog, vm))
				return;
			var newElement = vm.CreateResourceElementOptions().Create();
			SettingsChange change;
			try {
				change = ResourceChanges.ReplaceElement(resourceNode.Module, resource, elementNode.ResourceElement.Name, newElement, out _);
			}
			catch (Exception ex) {
				DialogUtils.ShowMessage(this, string.Format(dnSpy_AsmEditor_Core_Resources.Error_InvalidResourceData, ex.Message));
				return;
			}
			ApplyResourceChange(resourceNode.Module, change, resource.Name, newElement.Name);
		}

		internal void CreateResourceElement() {
			if (GetResourceSetTarget() is not ResourceNode resourceNode || resourceNode.Resource is not EmbeddedResource resource)
				return;
			var vm = new ResourceElementVM(new ResourceElementOptions(new ResourceElement {
				Name = string.Empty,
				ResourceData = new BuiltInResourceData(ResourceTypeCode.String, string.Empty),
			}), resourceNode.Module);
			if (!ShowCreateDialog(new ResourceElementDlg(), vm, dnSpy_AsmEditor_Core_Resources.CreateResourceCommand2))
				return;
			AddResourceElements(resourceNode, resource, new[] { vm.CreateResourceElementOptions().Create() });
		}

		void CreateFileResourceElements(ResourceTypeCode typeCode) {
			if (GetResourceSetTarget() is not ResourceNode resourceNode || resourceNode.Resource is not EmbeddedResource resource)
				return;
			var filenames = DialogUtils.PickOpenFilenames(this, null);
			if (filenames.Length == 0)
				return;
			ResourceElement[] elements;
			try {
				elements = filenames.Select(a => new ResourceElement {
					Name = Path.GetFileName(a),
					ResourceData = new BuiltInResourceData(typeCode, File.ReadAllBytes(a)),
				}).ToArray();
			}
			catch (Exception ex) {
				DialogUtils.ShowMessage(this, string.Format(dnSpy_AsmEditor_Core_Resources.Error_ReadingFiles, ex.Message));
				return;
			}
			AddResourceElements(resourceNode, resource, elements);
		}

		// Used by the create commands and the smoke test
		internal void AddResourceElements(ResourceNode resourceNode, EmbeddedResource resource, ResourceElement[] elements) {
			SettingsChange change;
			try {
				change = ResourceChanges.AddElements(resourceNode.Module, resource, elements, out _);
			}
			catch (Exception ex) {
				DialogUtils.ShowMessage(this, string.Format(dnSpy_AsmEditor_Core_Resources.Error_InvalidResourceData, ex.Message));
				return;
			}
			ApplyResourceChange(resourceNode.Module, change, resource.Name, elements[0].Name);
		}

		bool CanDeleteResources() => GetDeleteResourceTargets() is not null;

		// The selected resources, or the selected elements of one .resources file
		(ModuleDef module, Resource[]? resources, ResourceNode? resourceNode, string[]? elementNames)? GetDeleteResourceTargets() {
			var nodes = treeView.SelectedItems.OfType<TreeNode>().ToArray();
			if (nodes.Length == 0)
				return null;
			if (nodes.All(a => a is ResourceNode)) {
				var resourceNodes = nodes.Cast<ResourceNode>().ToArray();
				var module = resourceNodes[0].Module;
				if (resourceNodes.All(a => a.Module == module))
					return (module, resourceNodes.Select(a => a.Resource).ToArray(), null, null);
			}
			if (nodes.All(a => a is ResourceElementNode)) {
				var elementNodes = nodes.Cast<ResourceElementNode>().ToArray();
				var resourceNode = elementNodes[0].ResourceNode;
				if (elementNodes.All(a => a.ResourceNode == resourceNode) && resourceNode.Resource is EmbeddedResource)
					return (resourceNode.Module, null, resourceNode, elementNodes.Select(a => a.ResourceElement.Name).ToArray());
			}
			return null;
		}

		internal void DeleteResources() {
			if (GetDeleteResourceTargets() is not var (module, resources, resourceNode, elementNames))
				return;
			var parent = (treeView.SelectedItem as TreeNode)?.Parent;
			if (resources is not null)
				ApplyChange(module, ResourceChanges.DeleteResources(module, resources), null);
			else if (resourceNode?.Resource is EmbeddedResource resource && elementNames is not null)
				ApplyResourceChange(module, ResourceChanges.DeleteElements(module, resource, elementNames, out _), resource.Name, null);
			if (resources is not null && parent is not null && IsInTree(parent))
				SelectNode(parent);
		}

		// Adds the resource commands to a menu, they're only visible if they can be used
		void AddResourceMenuItems(ItemCollection items, List<Action> updaters) {
			void Add(string header, Action action, Func<bool> canExecute) {
				var menuItem = new MenuItem { Header = header };
				menuItem.Click += (s, e) => action();
				items.Add(menuItem);
				updaters.Add(() => menuItem.IsVisible = menuItem.IsEnabled = canExecute());
			}
			Add(dnSpy_AsmEditor_Core_Resources.CreateFileResourceCommand, CreateFileResources, () => GetResourcesTarget() is not null);
			Add(dnSpy_AsmEditor_Core_Resources.CreateMultiFileResourceCommand, () => CreateResource(ResourceType.Embedded), () => GetResourcesTarget() is not null);
			Add(dnSpy_AsmEditor_Core_Resources.CreateAssemblyLinkedResourceCommand, () => CreateResource(ResourceType.AssemblyLinked), () => GetResourcesTarget() is not null);
			Add(dnSpy_AsmEditor_Core_Resources.CreateFileLinkedResourceCommand, () => CreateResource(ResourceType.Linked), () => GetResourcesTarget() is not null);
			Add(dnSpy_AsmEditor_Core_Resources.CreateResourceCommand, CreateResourceElement, () => GetResourceSetTarget() is not null);
			Add(dnSpy_AsmEditor_Core_Resources.CreateByteArrayResourceCommand, () => CreateFileResourceElements(ResourceTypeCode.ByteArray), () => GetResourceSetTarget() is not null);
			Add(dnSpy_AsmEditor_Core_Resources.CreateStreamResourceCommand, () => CreateFileResourceElements(ResourceTypeCode.Stream), () => GetResourceSetTarget() is not null);
		}
	}
}

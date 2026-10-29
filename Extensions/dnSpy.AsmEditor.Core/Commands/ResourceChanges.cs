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
using dnlib.DotNet;
using dnlib.DotNet.Resources;
using dnSpy.AsmEditor.Resources;

namespace dnSpy.AsmEditor.Commands {
	/// <summary>
	/// The changes of the resource commands. Resource elements are stored in a .resources file (an embedded resource)
	/// so changing an element writes a new embedded resource that replaces the old one, like the WPF commands.
	/// </summary>
	static class ResourceChanges {
		/// <summary>
		/// Checks whether the resource is a .resources file with resource elements
		/// </summary>
		public static bool IsResourceSet(Resource resource) {
			if (resource is not EmbeddedResource er)
				return false;
			try {
				return ResourceReader.CouldBeResourcesFile(er.CreateReader());
			}
			catch (Exception ex) when (ex is IOException || ex is InvalidOperationException || ex is ArgumentException) {
				return false;
			}
		}

		public static ResourceElementSet ReadResourceSet(ModuleDef module, EmbeddedResource resource) => ResourceReader.Read(module, resource.CreateReader());

		/// <summary>
		/// Creates a .resources file without any elements
		/// </summary>
		public static EmbeddedResource CreateEmptyResourceSet(ModuleDef module, string name, ManifestResourceAttributes attributes) {
			var outStream = new MemoryStream();
			ResourceWriter.Write(module, outStream, ResourceElementSet.CreateForResourceReader(module));
			return new EmbeddedResource(name, outStream.ToArray(), attributes);
		}

		public static SettingsChange AddResources(ModuleDef module, Resource[] resources) =>
			new SettingsChange(() => {
				foreach (var resource in resources)
					module.Resources.Add(resource);
			}, () => {
				for (int i = resources.Length - 1; i >= 0; i--) {
					if (!module.Resources.Remove(resources[i]))
						throw new InvalidOperationException();
				}
			});

		public static SettingsChange DeleteResources(ModuleDef module, Resource[] resources) {
			var removed = new List<(int index, Resource resource)>();
			return new SettingsChange(() => {
				removed.Clear();
				foreach (var resource in resources) {
					int index = module.Resources.IndexOf(resource);
					if (index < 0)
						continue;
					module.Resources.RemoveAt(index);
					removed.Add((index, resource));
				}
			}, () => {
				for (int i = removed.Count - 1; i >= 0; i--)
					module.Resources.Insert(removed[i].index, removed[i].resource);
			});
		}

		/// <summary>
		/// Updates the name, attributes, linked assembly/file and custom attributes of a resource
		/// </summary>
		public static SettingsChange Edit(Resource resource, ResourceOptions newOptions) {
			var origOptions = new ResourceOptions(resource);
			return new SettingsChange(() => newOptions.CopyTo(resource), () => origOptions.CopyTo(resource));
		}

		/// <summary>
		/// Adds resource elements to a .resources file
		/// </summary>
		/// <param name="module">Module</param>
		/// <param name="resource">The .resources file</param>
		/// <param name="newElements">New elements</param>
		/// <param name="newResource">The new embedded resource that replaces <paramref name="resource"/></param>
		public static SettingsChange AddElements(ModuleDef module, EmbeddedResource resource, ResourceElement[] newElements, out EmbeddedResource newResource) =>
			UpdateElements(module, resource, list => list.AddRange(newElements), out newResource);

		public static SettingsChange DeleteElements(ModuleDef module, EmbeddedResource resource, string[] names, out EmbeddedResource newResource) {
			var set = new HashSet<string>(names, StringComparer.Ordinal);
			return UpdateElements(module, resource, list => list.RemoveAll(a => set.Contains(a.Name)), out newResource);
		}

		/// <summary>
		/// Replaces a resource element, eg. with a new name or value
		/// </summary>
		public static SettingsChange ReplaceElement(ModuleDef module, EmbeddedResource resource, string name, ResourceElement newElement, out EmbeddedResource newResource) =>
			UpdateElements(module, resource, list => {
				int index = list.FindIndex(a => a.Name == name);
				if (index < 0)
					throw new InvalidOperationException();
				list[index] = newElement;
			}, out newResource);

		static SettingsChange UpdateElements(ModuleDef module, EmbeddedResource resource, Action<List<ResourceElement>> update, out EmbeddedResource newResource) {
			int index = module.Resources.IndexOf(resource);
			if (index < 0)
				throw new ArgumentException();
			var origSet = ReadResourceSet(module, resource);
			var elements = origSet.ResourceElements.ToList();
			update(elements);
			var newSet = origSet.Clone();
			foreach (var element in elements)
				newSet.Add(element);
			var outStream = new MemoryStream();
			ResourceWriter.Write(module, outStream, newSet);
			var res = new EmbeddedResource(resource.Name, outStream.ToArray(), resource.Attributes);
			foreach (var ca in resource.CustomAttributes)
				res.CustomAttributes.Add(ca);
			newResource = res;
			return new SettingsChange(() => module.Resources[index] = res, () => module.Resources[index] = resource);
		}
	}
}

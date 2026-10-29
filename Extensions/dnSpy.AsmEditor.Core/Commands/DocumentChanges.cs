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
using System.Linq;
using dnlib.DotNet;
using dnlib.PE;
using dnSpy.AsmEditor.Assembly;
using dnSpy.AsmEditor.Module;
using dnSpy.Contracts.Documents;

namespace dnSpy.AsmEditor.Commands {
	/// <summary>
	/// The changes of the assembly and netmodule commands: new documents, adding netmodules to assemblies and
	/// converting between assemblies and netmodules. The documents are added to or removed from the document
	/// service, a UI shows the document service's documents.
	/// </summary>
	static class DocumentChanges {
		/// <summary>
		/// Creates a new assembly with one module
		/// </summary>
		/// <param name="documentService">Document service</param>
		/// <param name="options">Assembly options</param>
		/// <param name="newModule">The module that was passed to the assembly options view model</param>
		/// <param name="document">The new document</param>
		public static SettingsChange CreateAssembly(IDsDocumentService documentService, AssemblyOptions options, ModuleDef newModule, out IDsDocument document) {
			var module = ModuleUtils.CreateModule(options.Name, Guid.NewGuid(), options.ClrVersion, ModuleKind.Dll, newModule);
			options.CreateAssemblyDef(module).Modules.Add(module);
			var doc = DsDotNetDocument.CreateAssembly(DsDocumentInfo.CreateDocument(string.Empty), module, true);
			document = doc;
			return AddRootDocument(documentService, doc);
		}

		public static SettingsChange CreateNetModule(IDsDocumentService documentService, NetModuleOptions options, out IDsDocument document) {
			var module = ModuleUtils.CreateNetModule(options.Name, options.Mvid, options.ClrVersion);
			var doc = DsDotNetDocument.CreateModule(DsDocumentInfo.CreateDocument(string.Empty), module, true);
			document = doc;
			return AddRootDocument(documentService, doc);
		}

		static SettingsChange AddRootDocument(IDsDocumentService documentService, IDsDocument document) =>
			new SettingsChange(() => documentService.ForceAdd(document, false, null), () => documentService.Remove(new[] { document }));

		/// <summary>
		/// Creates a new netmodule document that can be added to an assembly with <see cref="AddNetModule"/>
		/// </summary>
		public static IDsDocument CreateNetModuleDocument(NetModuleOptions options) =>
			DsDotNetDocument.CreateModule(DsDocumentInfo.CreateDocument(string.Empty), ModuleUtils.CreateNetModule(options.Name, options.Mvid, options.ClrVersion), true);

		/// <summary>
		/// Adds a netmodule to an assembly
		/// </summary>
		/// <param name="assemblyDocument">Assembly</param>
		/// <param name="moduleDocument">The netmodule, it has no assembly</param>
		public static SettingsChange AddNetModule(IDsDocument assemblyDocument, IDsDocument moduleDocument) {
			var asm = assemblyDocument.AssemblyDef ?? throw new ArgumentException();
			var module = moduleDocument.ModuleDef ?? throw new ArgumentException();
			return new SettingsChange(() => {
				asm.Modules.Add(module);
				lock (assemblyDocument.Children.SyncRoot)
					assemblyDocument.Children.Add(moduleDocument);
			}, () => {
				asm.Modules.Remove(module);
				lock (assemblyDocument.Children.SyncRoot)
					assemblyDocument.Children.Remove(moduleDocument);
			});
		}

		/// <summary>
		/// Checks whether a module can be removed from its assembly (it's not the manifest module)
		/// </summary>
		public static bool CanRemoveNetModule(IDsDocument assemblyDocument, IDsDocument moduleDocument) =>
			assemblyDocument.AssemblyDef is AssemblyDef asm && moduleDocument.ModuleDef is ModuleDef module &&
			asm.Modules.IndexOf(module) > 0 && assemblyDocument.Children.Contains(moduleDocument);

		public static SettingsChange RemoveNetModule(IDsDocument assemblyDocument, IDsDocument moduleDocument) {
			if (!CanRemoveNetModule(assemblyDocument, moduleDocument))
				throw new ArgumentException();
			var asm = assemblyDocument.AssemblyDef!;
			var module = moduleDocument.ModuleDef!;
			int moduleIndex = asm.Modules.IndexOf(module);
			int documentIndex = assemblyDocument.Children.IndexOf(moduleDocument);
			return new SettingsChange(() => {
				asm.Modules.RemoveAt(moduleIndex);
				lock (assemblyDocument.Children.SyncRoot)
					assemblyDocument.Children.RemoveAt(documentIndex);
			}, () => {
				asm.Modules.Insert(moduleIndex, module);
				lock (assemblyDocument.Children.SyncRoot)
					assemblyDocument.Children.Insert(documentIndex, moduleDocument);
			});
		}

		/// <summary>
		/// Checks whether it's a netmodule that isn't in an assembly
		/// </summary>
		public static bool CanConvertNetModuleToAssembly(IDsDocument document) =>
			document.ModuleDef is ModuleDef module && module.Assembly is null && document.AssemblyDef is null && document is IDsDotNetDocument;

		/// <summary>
		/// Converts a netmodule document (a root document) to an assembly document
		/// </summary>
		/// <param name="documentService">Document service</param>
		/// <param name="moduleDocument">Netmodule document</param>
		/// <param name="getAssemblyDocument">Returns the new assembly document after the change was applied</param>
		public static SettingsChange ConvertNetModuleToAssembly(IDsDocumentService documentService, IDsDocument moduleDocument, out Func<IDsDocument?> getAssemblyDocument) {
			if (!CanConvertNetModuleToAssembly(moduleDocument))
				throw new ArgumentException();
			var module = moduleDocument.ModuleDef!;
			var origKind = module.Kind;
			Characteristics origCharacteristics = module.Characteristics;
			AssemblyDef? asm = null;
			IDsDocument? asmDocument = null;
			getAssemblyDocument = () => asmDocument;
			return new SettingsChange(() => {
				origKind = module.Kind;
				if (asm is null)
					asm = ModuleUtils.AddToNewAssemblyDef(module, ModuleKind.Dll, out origCharacteristics);
				else {
					asm.Modules.Add(module);
					ModuleUtils.WriteNewModuleKind(module, ModuleKind.Dll, out origCharacteristics);
				}
				asmDocument ??= DsDotNetDocument.CreateAssembly((IDsDotNetDocument)moduleDocument);
				documentService.Remove(new[] { moduleDocument });
				documentService.ForceAdd(asmDocument, false, null);
			}, () => {
				documentService.Remove(new[] { asmDocument! });
				asm!.Modules.Remove(module);
				module.Kind = origKind;
				module.Characteristics = origCharacteristics;
				documentService.ForceAdd(moduleDocument, false, null);
			});
		}

		/// <summary>
		/// Checks whether it's an assembly with only one module
		/// </summary>
		public static bool CanConvertAssemblyToNetModule(IDsDocument document) =>
			document.AssemblyDef is AssemblyDef asm && asm.Modules.Count == 1 && document.ModuleDef is ModuleDef module &&
			asm.ManifestModule == module && document.Children.Any(a => a.ModuleDef == module);

		/// <summary>
		/// Converts an assembly document (a root document) to a netmodule document
		/// </summary>
		/// <param name="documentService">Document service</param>
		/// <param name="assemblyDocument">Assembly document</param>
		/// <param name="moduleDocument">The new root document</param>
		public static SettingsChange ConvertAssemblyToNetModule(IDsDocumentService documentService, IDsDocument assemblyDocument, out IDsDocument moduleDocument) {
			if (!CanConvertAssemblyToNetModule(assemblyDocument))
				throw new ArgumentException();
			var asm = assemblyDocument.AssemblyDef!;
			var module = assemblyDocument.ModuleDef!;
			var modDocument = assemblyDocument.Children.First(a => a.ModuleDef == module);
			moduleDocument = modDocument;
			var origKind = module.Kind;
			Characteristics origCharacteristics = module.Characteristics;
			return new SettingsChange(() => {
				documentService.Remove(new[] { assemblyDocument });
				asm.Modules.Remove(module);
				origKind = module.Kind;
				ModuleUtils.WriteNewModuleKind(module, ModuleKind.NetModule, out origCharacteristics);
				documentService.ForceAdd(modDocument, false, null);
			}, () => {
				documentService.Remove(new[] { modDocument });
				module.Kind = origKind;
				module.Characteristics = origCharacteristics;
				asm.Modules.Add(module);
				documentService.ForceAdd(assemblyDocument, false, null);
			});
		}
	}
}

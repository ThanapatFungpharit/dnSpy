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
using dnlib.DotNet;
using dnlib.DotNet.Writer;
using dnSpy.AsmEditor.Compiler;
using dnSpy.Contracts.Documents;

namespace dnSpy.Avalonia.Editing {
	// A change to a module that can be undone, eg. a compiled code edit or new assembly settings
	abstract class ModuleEdit {
		public ModuleDef Module { get; }
		// The modified modules: Module and eg. a new netmodule that was added to Module's assembly
		public IReadOnlyList<ModuleDef> Modules { get; }
		protected ModuleEdit(ModuleDef module, IEnumerable<ModuleDef>? otherModules = null) {
			Module = module ?? throw new ArgumentNullException(nameof(module));
			Modules = new[] { module }.Concat(otherModules ?? Array.Empty<ModuleDef>()).Distinct().ToArray();
		}
		public abstract void Apply();
		public abstract void Undo();
	}

	sealed class DelegateModuleEdit : ModuleEdit {
		readonly Action apply;
		readonly Action undo;

		public DelegateModuleEdit(ModuleDef module, Action apply, Action undo, IEnumerable<ModuleDef>? otherModules = null)
			: base(module, otherModules) {
			this.apply = apply ?? throw new ArgumentNullException(nameof(apply));
			this.undo = undo ?? throw new ArgumentNullException(nameof(undo));
		}

		public override void Apply() => apply();
		public override void Undo() => undo();
	}

	sealed class ImportedChangesEdit : ModuleEdit {
		public ImportedChanges Changes { get; }
		public ImportedChangesEdit(ImportedChanges changes) : base(changes.Module) => Changes = changes;
		public override void Apply() => Changes.Apply();
		public override void Undo() => Changes.Undo();
	}

	// Applied edits (undo/redo) and which modules have unsaved changes
	sealed class ModuleEditService {
		readonly IDsDocumentService documentService;
		readonly IMethodAnnotations methodAnnotations;
		readonly List<ModuleEdit> undoList = new List<ModuleEdit>();
		readonly List<ModuleEdit> redoList = new List<ModuleEdit>();
		// Number of applied (not undone) changes per module since it was saved. Can be negative if a change
		// that was saved is undone.
		readonly Dictionary<ModuleDef, int> modifiedCounts = new Dictionary<ModuleDef, int>();

		// Raised after changes were applied or undone
		public event EventHandler<ModuleEdit>? ModuleChanged;
		public event EventHandler? ModifiedStateChanged;

		public bool CanUndo => undoList.Count > 0;
		public bool CanRedo => redoList.Count > 0;

		public ModuleEditService(IDsDocumentService documentService, IMethodAnnotations methodAnnotations) {
			this.documentService = documentService;
			this.methodAnnotations = methodAnnotations;
		}

		public ImportedChanges Apply(ModuleImporter importer, ModuleDef module) {
			var changes = new ImportedChanges(importer, module, methodAnnotations);
			Apply(new ImportedChangesEdit(changes));
			return changes;
		}

		public void Apply(ModuleEdit edit) {
			edit.Apply();
			undoList.Add(edit);
			redoList.Clear();
			AddModified(edit, 1);
			ModuleChanged?.Invoke(this, edit);
		}

		public void Undo() {
			if (undoList.Count == 0)
				return;
			var changes = undoList[undoList.Count - 1];
			undoList.RemoveAt(undoList.Count - 1);
			changes.Undo();
			redoList.Add(changes);
			AddModified(changes, -1);
			ModuleChanged?.Invoke(this, changes);
		}

		public void Redo() {
			if (redoList.Count == 0)
				return;
			var changes = redoList[redoList.Count - 1];
			redoList.RemoveAt(redoList.Count - 1);
			changes.Apply();
			undoList.Add(changes);
			AddModified(changes, 1);
			ModuleChanged?.Invoke(this, changes);
		}

		void AddModified(ModuleEdit edit, int count) {
			foreach (var module in edit.Modules) {
				modifiedCounts.TryGetValue(module, out int old);
				modifiedCounts[module] = old + count;
			}
			ModifiedStateChanged?.Invoke(this, EventArgs.Empty);
		}

		public bool IsModified(ModuleDef module) => modifiedCounts.TryGetValue(module, out int count) && count != 0;

		public IEnumerable<ModuleDef> ModifiedModules => modifiedCounts.Where(a => a.Value != 0).Select(a => a.Key).ToArray();

		// Forgets the edits of modules that were closed
		public void RemoveClosedModules() {
			var loaded = new HashSet<ModuleDef>(documentService.GetDocuments().SelectMany(a => a.GetAllChildrenAndSelf()).Select(a => a.ModuleDef).OfType<ModuleDef>());
			undoList.RemoveAll(a => !loaded.Contains(a.Module));
			redoList.RemoveAll(a => !loaded.Contains(a.Module));
			foreach (var module in modifiedCounts.Keys.Where(a => !loaded.Contains(a)).ToArray())
				modifiedCounts.Remove(module);
			ModifiedStateChanged?.Invoke(this, EventArgs.Empty);
		}

		// Writes the module to a file. If options is null, the WPF save module dialog's defaults are used.
		public void Save(ModuleDef module, string filename, ModuleWriterOptionsBase? options = null) {
			filename = Path.GetFullPath(filename);
			// dnlib reads the file lazily (memory mapped I/O) so the file can't be overwritten until that's disabled
			foreach (var document in documentService.GetDocuments().SelectMany(a => a.GetAllChildrenAndSelf())) {
				if (document.PEImage is not null && StringComparer.OrdinalIgnoreCase.Equals(document.PEImage.Filename, filename))
					MemoryMappedIOHelper.DisableMemoryMappedIO(document.PEImage);
			}

			if (options is null) {
				if (module is ModuleDefMD moduleMD && !module.IsILOnly)
					options = new NativeModuleWriterOptions(moduleMD, optimizeImageSize: true);
				else
					options = new ModuleWriterOptions(module);
				options.WritePdb = module.PdbState is not null;
				// Make sure the order of the interfaces don't change, see https://github.com/dotnet/roslyn/issues/3905
				options.MetadataOptions.Flags |= MetadataFlags.RoslynSortInterfaceImpl;
			}

			// Write to a temp file first so a failed write doesn't destroy the original file
			var tempFilename = filename + ".tmp" + Guid.NewGuid().ToString("N").Substring(0, 8);
			var tempPdb = tempFilename + ".pdb";
			if (options.WritePdb) {
				options.PdbFileName = tempPdb;
				// The debug directory must have the final name, not the temp file's name
				options.PdbFileNameInDebugDirectory = Path.ChangeExtension(filename, ".pdb");
			}
			try {
				if (options is NativeModuleWriterOptions nativeOptions)
					((ModuleDefMD)module).NativeWrite(tempFilename, nativeOptions);
				else
					module.Write(tempFilename, (ModuleWriterOptions)options);
				File.Move(tempFilename, filename, overwrite: true);
				if (options.WritePdb && File.Exists(tempPdb))
					File.Move(tempPdb, Path.ChangeExtension(filename, ".pdb"), overwrite: true);
			}
			finally {
				if (File.Exists(tempFilename))
					File.Delete(tempFilename);
				if (File.Exists(tempPdb))
					File.Delete(tempPdb);
			}

			if (StringComparer.OrdinalIgnoreCase.Equals(module.Location, filename) || string.IsNullOrEmpty(module.Location)) {
				modifiedCounts.Remove(module);
				ModifiedStateChanged?.Invoke(this, EventArgs.Empty);
			}
		}
	}
}

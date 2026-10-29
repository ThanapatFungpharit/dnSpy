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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using dnlib.DotNet;
using dnSpy.AsmEditor;
using dnSpy.AsmEditor.Compiler;
using dnSpy.Contracts.AsmEditor.Compiler;
using dnSpy.Contracts.Decompiler;

namespace dnSpy.Avalonia.Editing {
	enum EditCodeKind {
		// Edit a method body, the other members in the type are stubs
		EditMethod,
		// Edit a whole (non-nested) type
		EditClass,
		// Add new members to a type, the existing members are stubs
		AddMembers,
		// Add new types to the module
		AddClass,
		// Edit the assembly and module attributes
		EditAssembly,
	}

	sealed class EditCodeDocument {
		public string Name { get; }
		public Microsoft.CodeAnalysis.DocumentId Id { get; }
		public string InitialText { get; }
		// Where the caret is put, eg. the edited method's name
		public int? InitialCaretOffset { get; }

		public EditCodeDocument(string name, Microsoft.CodeAnalysis.DocumentId id, string initialText, int? initialCaretOffset) {
			Name = name;
			Id = id;
			InitialText = initialText;
			InitialCaretOffset = initialCaretOffset;
		}
	}

	sealed class EditCodeCompileResult {
		// Not null if it compiled and the result could be imported into the edited module
		public ModuleImporter? Importer { get; }
		public CompilerDiagnostic[] Diagnostics { get; }

		public EditCodeCompileResult(ModuleImporter? importer, CompilerDiagnostic[] diagnostics) {
			Importer = importer;
			Diagnostics = diagnostics;
		}
	}

	// The UI independent part of the WPF EditCodeVM and its subclasses (EditMethodCodeVM, EditClassVM, AddMembersCodeVM,
	// AddClassVM, EditAssemblyVM): decompiles the code that's edited, finds the references, compiles the code and
	// imports the compiled code into the edited module.
	sealed class EditCodeSession : IDisposable {
		readonly EditCodeKind kind;
		readonly IDecompiler decompiler;
		readonly ModuleDef sourceModule;
		readonly IMemberDef? defToEdit;
		readonly RoslynLanguageOptions languageOptions;
		readonly AssemblyNameInfo tempAssembly;
		readonly AssemblyReferenceResolver assemblyReferenceResolver;
		readonly HashSet<string> currentReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		readonly List<EditCodeDocument> documents = new List<EditCodeDocument>();
		RoslynProject? project;

		public EditCodeKind Kind => kind;
		public ModuleDef SourceModule => sourceModule;
		public IDecompiler Decompiler => decompiler;
		public RoslynProject? Project => project;
		public IReadOnlyList<EditCodeDocument> Documents => documents;

		string MainCodeName => "main" + languageOptions.FileExtension;
		string MainGeneratedCodeName => "main.g" + languageOptions.FileExtension;

		public static bool CanEdit(EditCodeKind kind, IDecompiler decompiler) =>
			IsCompilableLanguage(decompiler) &&
			decompiler.CanDecompile(kind == EditCodeKind.EditAssembly ? DecompilationType.AssemblyInfo : DecompilationType.TypeMethods);

		// The decompiler's language must be compilable, ie. C# or Visual Basic
		static bool IsCompilableLanguage(IDecompiler decompiler) =>
			decompiler.GenericGuid == DecompilerConstants.LANGUAGE_CSHARP || decompiler.GenericGuid == DecompilerConstants.LANGUAGE_VISUALBASIC;

		static RoslynLanguageOptions? GetLanguageOptions(IDecompiler decompiler, Contracts.Settings.ISettingsService settingsService) {
			if (decompiler.GenericGuid == DecompilerConstants.LANGUAGE_CSHARP)
				return new CSharpLanguageOptions(new CSharpCompilerSettings(settingsService));
			if (decompiler.GenericGuid == DecompilerConstants.LANGUAGE_VISUALBASIC)
				return new VisualBasicLanguageOptions(new VisualBasicCompilerSettings(settingsService));
			return null;
		}

		// defToEdit is the method (EditMethod), the type or member (EditClass, AddMembers) or null (AddClass, EditAssembly)
		public EditCodeSession(EditCodeKind kind, IDecompiler decompiler, Contracts.Settings.ISettingsService settingsService, RawModuleBytesProvider rawModuleBytesProvider, ModuleDef sourceModule, IMemberDef? defToEdit) {
			this.kind = kind;
			this.decompiler = decompiler;
			this.sourceModule = sourceModule;
			this.defToEdit = defToEdit;
			languageOptions = GetLanguageOptions(decompiler, settingsService) ?? throw new ArgumentException("The decompiler's language can't be compiled");
			if ((kind == EditCodeKind.EditMethod || kind == EditCodeKind.EditClass || kind == EditCodeKind.AddMembers) && defToEdit is null)
				throw new ArgumentNullException(nameof(defToEdit));
			if (kind == EditCodeKind.EditMethod && defToEdit is not MethodDef)
				throw new ArgumentException("A method must be edited");

			var typeToEdit = GetNonNestedType(defToEdit);
			tempAssembly = new AssemblyNameInfo {
				HashAlgId = AssemblyHashAlgorithm.SHA1,
				Version = new Version(0, 0, 0, 0),
				Attributes = AssemblyAttributes.None,
				Name = Guid.NewGuid().ToString(),
				Culture = string.Empty,
			};
			if (!PublicKeyBase.IsNullOrEmpty2(sourceModule.Assembly?.PublicKeyOrToken)) {
				tempAssembly.PublicKeyOrToken = new PublicKey(publicKeyData);
				tempAssembly.Attributes |= AssemblyAttributes.PublicKey;
			}
			assemblyReferenceResolver = new AssemblyReferenceResolver(rawModuleBytesProvider, sourceModule.Context.AssemblyResolver, tempAssembly, sourceModule, typeToEdit);
		}
		// Same key as the WPF code editor (EditCodeVM)
		static readonly byte[] publicKeyData = new byte[] {
			0x00, 0x24, 0x00, 0x00, 0x04, 0x80, 0x00, 0x00, 0x94, 0x00, 0x00, 0x00, 0x06, 0x02, 0x00, 0x00,
			0x00, 0x24, 0x00, 0x00, 0x52, 0x53, 0x41, 0x31, 0x00, 0x04, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00,
			0x9D, 0xE2, 0xB3, 0x41, 0x2F, 0xA8, 0x0C, 0x88, 0x40, 0x78, 0x7D, 0xBA, 0x09, 0xDB, 0x02, 0x5D,
			0xFA, 0x33, 0x08, 0xF2, 0xE6, 0x68, 0xC4, 0x87, 0x1C, 0x62, 0xF3, 0xCA, 0xDF, 0x74, 0xC5, 0x4B,
			0x05, 0x97, 0x73, 0xE1, 0x3B, 0x1B, 0x79, 0x52, 0x86, 0x89, 0x85, 0xD6, 0x58, 0xD3, 0xC6, 0x9E,
			0x35, 0x7E, 0xF5, 0x41, 0xF8, 0x1C, 0xE2, 0x18, 0x23, 0xB9, 0xDA, 0xDB, 0x32, 0x1F, 0xB2, 0xF3,
			0x27, 0x3C, 0xE5, 0x76, 0x4E, 0x49, 0x4C, 0x05, 0xD7, 0x91, 0xBA, 0x1E, 0x3F, 0x12, 0xCF, 0x99,
			0xFC, 0xA1, 0x55, 0x4A, 0x67, 0x4B, 0xB9, 0xD8, 0x4A, 0x77, 0x1D, 0x1E, 0x2A, 0x16, 0x89, 0x3B,
			0x55, 0x7C, 0x66, 0xCD, 0x00, 0x44, 0x5A, 0x7B, 0xB3, 0xB7, 0xAE, 0x4C, 0xC2, 0xBE, 0x4E, 0x1D,
			0x5F, 0x28, 0x48, 0x34, 0x5A, 0x63, 0xD3, 0xB3, 0xF7, 0xEA, 0xDD, 0x01, 0xF4, 0x60, 0xF4, 0xBF,
		};

		static TypeDef? GetNonNestedType(IMemberDef? def) {
			var type = def as TypeDef ?? def?.DeclaringType;
			while (type?.DeclaringType is not null)
				type = type.DeclaringType;
			return type;
		}

		CompilationKind CompilationKind {
			get {
				switch (kind) {
				case EditCodeKind.EditMethod:	return CompilationKind.EditMethod;
				case EditCodeKind.EditClass:	return CompilationKind.EditClass;
				case EditCodeKind.AddMembers:	return CompilationKind.AddMembers;
				case EditCodeKind.AddClass:		return CompilationKind.AddClass;
				default:						return CompilationKind.EditAssembly;
				}
			}
		}

		public string Title {
			get {
				switch (kind) {
				case EditCodeKind.EditMethod:	return $"Edit Method - {defToEdit}";
				case EditCodeKind.EditClass:	return $"Edit Class - {GetNonNestedType(defToEdit)}";
				case EditCodeKind.AddMembers:	return $"Add Class Members - {GetNonNestedType(defToEdit)}";
				case EditCodeKind.AddClass:		return $"Add Class - {sourceModule.Name}";
				default:						return $"Edit Assembly Attributes - {sourceModule.Assembly?.Name ?? sourceModule.Name}";
				}
			}
		}

		// Decompiles the code and creates the Roslyn project. Must be called on the UI thread, the slow parts run on other threads.
		public async Task InitializeAsync(CancellationToken cancellationToken) {
			if (project is not null)
				throw new InvalidOperationException();
			bool editingCorLib = sourceModule.Assembly?.IsCorLib() == true;
			var requiredReferences = languageOptions.GetRequiredAssemblyReferences(sourceModule, editingCorLib).ToArray();
			var (decompiledDocuments, references) = await Task.Run(() => {
				var docs = Decompile(cancellationToken);
				cancellationToken.ThrowIfCancellationRequested();
				return (docs, CreateCompilerMetadataReferences(requiredReferences, cancellationToken));
			}, cancellationToken);

			foreach (var r in references) {
				if (!string.IsNullOrEmpty(r.Filename))
					currentReferences.Add(r.Filename);
			}
			var publicKey = (tempAssembly.PublicKeyOrToken as PublicKey)?.Data;
			project = new RoslynProject(languageOptions, CompilationKind, tempAssembly.Name, publicKey, PlatformHelper.GetPlatform(sourceModule), references);
			foreach (var (name, text, caret) in decompiledDocuments)
				documents.Add(new EditCodeDocument(name, project.AddDocument(name, text), text, caret));
		}

		List<(string name, string text, int? caret)> Decompile(CancellationToken cancellationToken) {
			var result = new List<(string name, string text, int? caret)>();
			var decompilationContext = new DecompilationContext {
				CancellationToken = cancellationToken,
				AsyncMethodBodyDecompilation = false,
			};
			var nonNestedType = GetNonNestedType(defToEdit);
			switch (kind) {
			case EditCodeKind.EditMethod: {
				var method = (MethodDef)defToEdit!;
				var mainOutput = new ReferenceDecompilerOutput(method);
				decompilationContext.CalculateILSpans = true;
				var options = new DecompileTypeMethods(mainOutput, decompilationContext, nonNestedType!);
				options.Methods.Add(method);
				options.DecompileHidden = false;
				decompiler.Decompile(DecompilationType.TypeMethods, options);
				cancellationToken.ThrowIfCancellationRequested();

				var hiddenOutput = new StringBuilderDecompilerOutput();
				decompilationContext.CalculateILSpans = false;
				options = new DecompileTypeMethods(hiddenOutput, decompilationContext, nonNestedType!);
				options.Methods.Add(method);
				options.DecompileHidden = true;
				decompiler.Decompile(DecompilationType.TypeMethods, options);

				result.Add((MainCodeName, mainOutput.ToString(), mainOutput.ReferenceStart));
				result.Add((MainGeneratedCodeName, hiddenOutput.ToString(), null));
				break;
			}

			case EditCodeKind.EditClass: {
				decompilationContext.AsyncMethodBodyDecompilation = true;
				decompilationContext.CalculateILSpans = true;
				var mainOutput = new ReferenceDecompilerOutput(defToEdit!);
				var options = new DecompileTypeMethods(mainOutput, decompilationContext, nonNestedType!);
				options.DecompileHidden = false;
				options.ShowAll = true;
				decompiler.Decompile(DecompilationType.TypeMethods, options);
				result.Add((MainCodeName, mainOutput.ToString(), mainOutput.ReferenceStart));
				break;
			}

			case EditCodeKind.AddMembers: {
				var mainOutput = new ReferenceDecompilerOutput(defToEdit as TypeDef ?? defToEdit!.DeclaringType);
				var options = new DecompileTypeMethods(mainOutput, decompilationContext, nonNestedType!);
				options.Types.Add(nonNestedType!);
				options.Types.AddRange(nonNestedType!.GetTypes());
				options.DecompileHidden = false;
				decompiler.Decompile(DecompilationType.TypeMethods, options);
				cancellationToken.ThrowIfCancellationRequested();

				var hiddenOutput = new StringBuilderDecompilerOutput();
				options = new DecompileTypeMethods(hiddenOutput, decompilationContext, nonNestedType);
				options.Types.Add(nonNestedType);
				options.Types.AddRange(nonNestedType.GetTypes());
				options.DecompileHidden = true;
				decompiler.Decompile(DecompilationType.TypeMethods, options);

				result.Add((MainCodeName, mainOutput.ToString(), mainOutput.ReferenceStart));
				result.Add((MainGeneratedCodeName, hiddenOutput.ToString(), null));
				break;
			}

			case EditCodeKind.AddClass:
				result.Add((MainCodeName, string.Empty, null));
				break;

			case EditCodeKind.EditAssembly: {
				var mainOutput = new StringBuilderDecompilerOutput();
				var options = new DecompileAssemblyInfo(mainOutput, decompilationContext, sourceModule);
				options.KeepAllAttributes = true;
				decompiler.Decompile(DecompilationType.AssemblyInfo, options);
				result.Add((MainCodeName, mainOutput.ToString(), null));
				break;
			}

			default:
				throw new InvalidOperationException();
			}
			return result;
		}

		CompilerMetadataReference[] CreateCompilerMetadataReferences(IEnumerable<string> extraAssemblyReferences, CancellationToken cancellationToken) {
			var modules = new HashSet<ModuleDef>(new MetadataReferenceFinder(sourceModule, cancellationToken).Find(extraAssemblyReferences));
			var mdRefs = new List<CompilerMetadataReference>();
			foreach (var module in modules) {
				cancellationToken.ThrowIfCancellationRequested();
				if (CreateReference(module) is CompilerMetadataReference cmr)
					mdRefs.Add(cmr);
			}
			return mdRefs.ToArray();
		}

		CompilerMetadataReference? CreateReference(ModuleDef module) =>
			module.IsManifestModule ? assemblyReferenceResolver.Create(module.Assembly) : assemblyReferenceResolver.Create(module);

		// Adds references the user picked, eg. when the compiler can't find a type. Returns false if nothing was added.
		public bool AddReferences(IEnumerable<ModuleDef> modules) {
			if (project is null)
				return false;
			var mdRefs = new List<CompilerMetadataReference>();
			foreach (var module in modules) {
				if (!string.IsNullOrEmpty(module.Location) && currentReferences.Contains(module.Location))
					continue;
				if (CreateReference(module) is not CompilerMetadataReference cmr)
					continue;
				mdRefs.Add(cmr);
				if (!string.IsNullOrEmpty(module.Location))
					currentReferences.Add(module.Location);
			}
			return mdRefs.Count != 0 && project.AddMetadataReferences(mdRefs);
		}

		public EditCodeDocument AddDocument(string name, string text) {
			if (project is null)
				throw new InvalidOperationException();
			var doc = new EditCodeDocument(name, project.AddDocument(name, text), text, null);
			documents.Add(doc);
			return doc;
		}

		// Compiles the code (the documents' text must've been updated with RoslynProject.UpdateText()) and
		// imports it. The edited module isn't changed, the caller applies the importer's result with ImportedChanges.
		public async Task<EditCodeCompileResult> CompileAsync(CancellationToken cancellationToken) {
			if (project is null)
				throw new InvalidOperationException();
			var project1 = project;
			CompilationResult result;
			try {
				result = await Task.Run(() => project1.CompileAsync(cancellationToken), cancellationToken);
			}
			catch (OperationCanceledException) {
				return new EditCodeCompileResult(null, new[] { new CompilerDiagnostic(CompilerDiagnosticSeverity.Error, "The task was canceled", "DSWTF!", null, null, null) });
			}
			catch (Exception ex) {
				return new EditCodeCompileResult(null, new[] { ToCompilerDiagnostic(ex) });
			}
			if (!result.Success)
				return new EditCodeCompileResult(null, result.Diagnostics);

			return await Task.Run(() => Import(result), cancellationToken);
		}

		EditCodeCompileResult Import(CompilationResult result) {
			ModuleImporter? importer = null;
			ModuleImporterAssemblyResolver? asmResolver = null;
			try {
				asmResolver = new ModuleImporterAssemblyResolver(assemblyReferenceResolver.GetReferences());
				importer = new ModuleImporter(sourceModule, asmResolver);
				var rawFile = result.RawFile!;
				switch (kind) {
				case EditCodeKind.EditMethod:
					importer.Import(rawFile, result.DebugFile, (MethodDef)defToEdit!);
					break;
				case EditCodeKind.EditClass:
					importer.Import(rawFile, result.DebugFile, GetNonNestedType(defToEdit)!);
					break;
				case EditCodeKind.AddMembers:
					importer.ImportNewMembers(rawFile, result.DebugFile, GetNonNestedType(defToEdit)!);
					break;
				case EditCodeKind.AddClass:
					importer.Import(rawFile, result.DebugFile, ModuleImporterOptions.None);
					break;
				case EditCodeKind.EditAssembly:
					importer.Import(rawFile, result.DebugFile, ModuleImporterOptions.ReplaceModuleAssemblyAttributes | ModuleImporterOptions.ReplaceAssemblyDeclSecurities | ModuleImporterOptions.ReplaceExportedTypes);
					break;
				}
				var diagnostics = importer.Diagnostics;
				if (diagnostics.Any(a => a.Severity == CompilerDiagnosticSeverity.Error))
					return new EditCodeCompileResult(null, diagnostics);
				return new EditCodeCompileResult(importer, result.Diagnostics.Concat(diagnostics).ToArray());
			}
			catch (ModuleImporterAbortedException) {
				return new EditCodeCompileResult(null, importer!.Diagnostics);
			}
			catch (Exception ex) {
				return new EditCodeCompileResult(null, new[] { ToCompilerDiagnostic(ex) });
			}
			finally {
				asmResolver?.Dispose();
			}
		}

		static CompilerDiagnostic ToCompilerDiagnostic(Exception ex) =>
			new CompilerDiagnostic(CompilerDiagnosticSeverity.Error, $"Exception: {ex.GetType()}: {ex.Message}", "DSBUG1", null, null, null);

		public void Dispose() {
			project?.Dispose();
			assemblyReferenceResolver.Dispose();
		}

		// Remembers where the edited member's definition was written so the caret can be moved there
		sealed class ReferenceDecompilerOutput : StringBuilderDecompilerOutput {
			readonly object reference;
			public int? ReferenceStart { get; private set; }

			public ReferenceDecompilerOutput(object reference) => this.reference = reference;

			public override void Write(string text, object? reference, DecompilerReferenceFlags flags, object color) =>
				Write(text, 0, text.Length, reference, flags, color);

			public override void Write(string text, int index, int length, object? reference, DecompilerReferenceFlags flags, object color) {
				if (reference == this.reference && (flags & DecompilerReferenceFlags.Definition) != 0 && ReferenceStart is null)
					ReferenceStart = NextPosition;
				base.Write(text, index, length, reference, flags, color);
			}
		}
	}
}

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
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using dnSpy.Contracts.AsmEditor.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.QuickInfo;
using Microsoft.CodeAnalysis.Text;
using CS = Microsoft.CodeAnalysis.CSharp;
using CSS = Microsoft.CodeAnalysis.CSharp.Syntax;

namespace dnSpy.Avalonia.Editing {
	// One overload shown by the signature help popup
	sealed class SignatureItem {
		public string Text { get; }
		public string? Documentation { get; }
		public SignatureItem(string text, string? documentation) {
			Text = text;
			Documentation = documentation;
		}
	}

	sealed class SignatureHelpInfo {
		public IReadOnlyList<SignatureItem> Items { get; }
		public int SelectedIndex { get; }
		// Start of the argument list (after the open paren), used to close the popup when the caret leaves it
		public int ArgumentListStart { get; }
		public SignatureHelpInfo(IReadOnlyList<SignatureItem> items, int selectedIndex, int argumentListStart) {
			Items = items;
			SelectedIndex = selectedIndex;
			ArgumentListStart = argumentListStart;
		}
	}

	// A Roslyn workspace with one project that has the code that's edited, the same as dnSpy.Roslyn's
	// RoslynLanguageCompiler but without the VS editor. Its methods are called on the UI thread, the
	// returned Documents are immutable and can be used by other threads.
	sealed class RoslynProject : IDisposable {
		readonly AdhocWorkspace workspace;
		readonly ProjectId projectId;
		readonly RoslynLanguageOptions languageOptions;

		public RoslynLanguageOptions LanguageOptions => languageOptions;
		public bool IsCSharp => languageOptions.LanguageName == LanguageNames.CSharp;

		public RoslynProject(RoslynLanguageOptions languageOptions, CompilationKind kind, string assemblyName, byte[]? publicKey, TargetPlatform platform, IEnumerable<CompilerMetadataReference> references) {
			this.languageOptions = languageOptions;
			workspace = new AdhocWorkspace(MefHostServices.DefaultHost);
			projectId = ProjectId.CreateNewId();

			var compilationOptions = languageOptions.CreateCompilationOptions(GetDefaultOutputKind(kind))
				.WithPlatform(GetPlatform(platform))
				.WithAssemblyIdentityComparer(DesktopAssemblyIdentityComparer.Default);
			if (publicKey is not null) {
				compilationOptions = compilationOptions
					.WithCryptoPublicKey(ImmutableArray.Create<byte>(publicKey))
					.WithDelaySign(true);
			}
			var projectInfo = ProjectInfo.Create(projectId, VersionStamp.Create(), "compilecodeproj", assemblyName, languageOptions.LanguageName,
				compilationOptions: compilationOptions,
				parseOptions: languageOptions.ParseOptions,
				metadataReferences: references.Select(CreateMetadataReference).ToArray(),
				isSubmission: false, hostObjectType: null);
			workspace.AddProject(projectInfo);
		}

		OutputKind GetDefaultOutputKind(CompilationKind kind) {
			if (!languageOptions.SupportsNetModule)
				return OutputKind.DynamicallyLinkedLibrary;
			switch (kind) {
			case CompilationKind.EditAssembly:
				// We can't use netmodule when editing assembly attributes since the compiler won't add an assembly for obvious reasons
				return OutputKind.DynamicallyLinkedLibrary;
			default:
				// Use a netmodule to prevent the compiler from adding assembly attributes. Sometimes the compiler must
				// add assembly attributes but the attributes have missing members and the compiler can't compile the code.
				return OutputKind.NetModule;
			}
		}

		static Platform GetPlatform(TargetPlatform platform) {
			switch (platform) {
			case TargetPlatform.X86:		return Platform.X86;
			case TargetPlatform.X64:		return Platform.X64;
			case TargetPlatform.Itanium:	return Platform.Itanium;
			case TargetPlatform.Arm:		return Platform.Arm;
			case TargetPlatform.Arm64:		return Platform.Arm64;
			// AnyCpu32BitPreferred can only be used when creating executables (we create a dll)
			default:						return Platform.AnyCpu;
			}
		}

		static unsafe MetadataReference CreateMetadataReference(CompilerMetadataReference mdRef) {
			// Quick info and completion show the XML doc comments if there's an xml file next to the assembly
			DocumentationProvider? docProvider = null;
			if (mdRef.Filename is string filename) {
				var xmlFilename = Path.ChangeExtension(filename, ".xml");
				if (File.Exists(xmlFilename))
					docProvider = XmlDocumentationProvider.CreateFromFile(xmlFilename);
			}
			var moduleMetadata = ModuleMetadata.CreateFromImage((IntPtr)mdRef.Data, mdRef.Size);
			if (mdRef.IsAssemblyReference)
				return AssemblyMetadata.Create(moduleMetadata).GetReference(docProvider, filePath: mdRef.Filename);
			return moduleMetadata.GetReference(docProvider, mdRef.Filename);
		}

		Project Project => workspace.CurrentSolution.GetProject(projectId)!;

		// The compiler can't emit the PDB if the source text has no encoding (CS8055)
		static SourceText CreateSourceText(string text) => SourceText.From(text, Encoding.UTF8);

		public DocumentId AddDocument(string name, string text) {
			var info = DocumentInfo.Create(DocumentId.CreateNewId(projectId), name, null, SourceCodeKind.Regular,
				TextLoader.From(TextAndVersion.Create(CreateSourceText(text), VersionStamp.Create())), filePath: name);
			return workspace.AddDocument(info).Id;
		}

		public void UpdateText(DocumentId documentId, string text) {
			var solution = workspace.CurrentSolution.WithDocumentText(documentId, CreateSourceText(text));
			workspace.TryApplyChanges(solution);
		}

		public Document GetDocument(DocumentId documentId) => workspace.CurrentSolution.GetDocument(documentId)!;

		public bool AddMetadataReferences(IEnumerable<CompilerMetadataReference> references) {
			var project = Project.AddMetadataReferences(references.Select(CreateMetadataReference));
			return workspace.TryApplyChanges(project.Solution);
		}

		public async Task<CompilationResult> CompileAsync(CancellationToken cancellationToken) {
			var project = Project;
			var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
			if (compilation is null)
				throw new InvalidOperationException("Project returned a null Compilation");

			var result = Compile(compilation, cancellationToken);
			if (result.Success)
				return result;

			// We allow unsafe code but the compiler tries to add extra attributes to the assembly. Sometimes
			// the corlib doesn't have the required members and the compiler fails to compile the code.
			// Let's try again but without unsafe code.
			var noAttrOptions = languageOptions.CreateCompilationOptionsNoAttributes(compilation.Options);
			if (noAttrOptions != compilation.Options) {
				var result2 = Compile(compilation.WithOptions(noAttrOptions), cancellationToken);
				if (result2.Success)
					return result2;
			}

			return result;
		}

		CompilationResult Compile(Compilation compilation, CancellationToken cancellationToken) {
			var peStream = new MemoryStream();
			var pdbStream = new MemoryStream();
			var emitOpts = new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb);
			var emitResult = compilation.Emit(peStream, pdbStream, options: emitOpts, cancellationToken: cancellationToken);
			var diagnostics = ToCompilerDiagnostics(emitResult.Diagnostics).ToArray();
			if (!emitResult.Success)
				return new CompilationResult(diagnostics);
			return new CompilationResult(peStream.ToArray(), new DebugFileResult(DebugFileFormat.PortablePdb, pdbStream.ToArray()), diagnostics);
		}

		IEnumerable<CompilerDiagnostic> ToCompilerDiagnostics(IEnumerable<Diagnostic> diagnostics) {
			foreach (var d in diagnostics) {
				string? filename = null;
				LineLocationSpan? lineLocationSpan = null;
				if (d.Location.IsInSource) {
					var pos = d.Location.GetLineSpan();
					filename = pos.Path;
					lineLocationSpan = new LineLocationSpan(new LineLocation(pos.StartLinePosition.Line, pos.StartLinePosition.Character),
						new LineLocation(pos.EndLinePosition.Line, pos.EndLinePosition.Character));
				}
				yield return new CompilerDiagnostic(ToSeverity(d.Severity), d.GetMessage(), d.Id, languageOptions.GetHelpUri(d), filename, lineLocationSpan);
			}
		}

		static CompilerDiagnosticSeverity ToSeverity(DiagnosticSeverity severity) {
			switch (severity) {
			case DiagnosticSeverity.Info:		return CompilerDiagnosticSeverity.Info;
			case DiagnosticSeverity.Warning:	return CompilerDiagnosticSeverity.Warning;
			case DiagnosticSeverity.Error:		return CompilerDiagnosticSeverity.Error;
			default:							return CompilerDiagnosticSeverity.Hidden;
			}
		}

		// The squiggles shown while editing: syntax and semantic errors and warnings in the document
		public static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(Document document, CancellationToken cancellationToken) {
			var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
			if (model is null)
				return ImmutableArray<Diagnostic>.Empty;
			return model.GetDiagnostics(cancellationToken: cancellationToken).Where(a => a.Severity >= DiagnosticSeverity.Warning).ToImmutableArray();
		}

		public static async Task<IEnumerable<ClassifiedSpan>> GetClassifiedSpansAsync(Document document, TextSpan span, CancellationToken cancellationToken) {
			var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
			if (model is null)
				return Array.Empty<ClassifiedSpan>();
			return Classifier.GetClassifiedSpans(model, span, document.Project.Solution.Workspace, cancellationToken);
		}

		public static bool ShouldTriggerCompletion(Document document, SourceText text, int position, char c) {
			var service = CompletionService.GetService(document);
			return service is not null && service.ShouldTriggerCompletion(text, position, CompletionTrigger.CreateInsertionTrigger(c));
		}

		public static async Task<CompletionList?> GetCompletionsAsync(Document document, int position, char? triggerChar, CancellationToken cancellationToken) {
			var service = CompletionService.GetService(document);
			if (service is null)
				return null;
			var trigger = triggerChar is char c ? CompletionTrigger.CreateInsertionTrigger(c) : CompletionTrigger.Invoke;
			return await service.GetCompletionsAsync(document, position, trigger, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		public static async Task<CompletionChange?> GetCompletionChangeAsync(Document document, CompletionItem item, CancellationToken cancellationToken) {
			var service = CompletionService.GetService(document);
			if (service is null)
				return null;
			return await service.GetChangeAsync(document, item, commitCharacter: null, cancellationToken).ConfigureAwait(false);
		}

		public static async Task<string?> GetCompletionDescriptionAsync(Document document, CompletionItem item, CancellationToken cancellationToken) {
			var service = CompletionService.GetService(document);
			if (service is null)
				return null;
			var description = await service.GetDescriptionAsync(document, item, cancellationToken).ConfigureAwait(false);
			return string.IsNullOrEmpty(description?.Text) ? null : description!.Text;
		}

		public static async Task<string?> GetQuickInfoAsync(Document document, int position, CancellationToken cancellationToken) {
			var service = QuickInfoService.GetService(document);
			if (service is null)
				return null;
			var info = await service.GetQuickInfoAsync(document, position, cancellationToken).ConfigureAwait(false);
			if (info is null || info.Sections.Length == 0)
				return null;
			var text = string.Join(Environment.NewLine, info.Sections.Select(a => a.Text).Where(a => !string.IsNullOrWhiteSpace(a)));
			return string.IsNullOrWhiteSpace(text) ? null : text;
		}

		// Signature help for C# method calls and object creations. Roslyn's signature help service is
		// internal so this uses the semantic model.
		public static async Task<SignatureHelpInfo?> GetSignatureHelpAsync(Document document, int position, CancellationToken cancellationToken) {
			if (document.Project.Language != LanguageNames.CSharp)
				return null;
			var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
			var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
			if (root is null || model is null || position <= 0)
				return null;
			var token = root.FindToken(position - 1);
			CSS.BaseArgumentListSyntax? argList = null;
			for (var node = token.Parent; node is not null; node = node.Parent) {
				if (node is CSS.BaseArgumentListSyntax list && position > list.SpanStart && (position < list.Span.End || list.Arguments.Count == 0 && IsMissingCloseToken(list))) {
					argList = list;
					break;
				}
				if (node is CSS.StatementSyntax || node is CSS.MemberDeclarationSyntax)
					break;
			}
			if (argList is null)
				return null;
			// The caret must be after '(' and before ')'
			if (!IsMissingCloseToken(argList) && position >= argList.Span.End)
				return null;

			IEnumerable<IMethodSymbol> methods;
			switch (argList.Parent) {
			case CSS.InvocationExpressionSyntax invocation:
				methods = model.GetMemberGroup(invocation.Expression, cancellationToken).OfType<IMethodSymbol>();
				break;
			case CSS.BaseObjectCreationExpressionSyntax creation:
				var type = model.GetTypeInfo(creation, cancellationToken).Type as INamedTypeSymbol;
				methods = type?.InstanceConstructors ?? Enumerable.Empty<IMethodSymbol>();
				break;
			case CSS.ConstructorInitializerSyntax initializer:
				var symbolInfo = model.GetSymbolInfo(initializer, cancellationToken);
				methods = (symbolInfo.Symbol is IMethodSymbol m ? new[] { m } : symbolInfo.CandidateSymbols.OfType<IMethodSymbol>())
					.SelectMany(a => a.ContainingType.InstanceConstructors).Distinct<IMethodSymbol>(SymbolEqualityComparer.Default);
				break;
			case CSS.ElementAccessExpressionSyntax elementAccess:
				var indexedType = model.GetTypeInfo(elementAccess.Expression, cancellationToken).Type;
				var indexers = indexedType?.GetMembers().OfType<IPropertySymbol>().Where(a => a.IsIndexer).Select(a => a.GetMethod).OfType<IMethodSymbol>() ?? Enumerable.Empty<IMethodSymbol>();
				methods = indexers;
				break;
			default:
				return null;
			}
			var within = model.GetEnclosingSymbol(position, cancellationToken);
			var methodList = methods.Where(a => within is null || model.Compilation.IsSymbolAccessibleWithin(a, within.ContainingType as ISymbol ?? model.Compilation.Assembly)).ToList();
			if (methodList.Count == 0)
				return null;

			int argIndex = 0;
			foreach (var separator in argList.Arguments.GetSeparators()) {
				if (separator.SpanStart < position)
					argIndex++;
			}
			var current = (argList.Parent is not null ? model.GetSymbolInfo(argList.Parent, cancellationToken).Symbol : null) as IMethodSymbol;
			int selectedIndex = current is null ? -1 : methodList.FindIndex(a => SymbolEqualityComparer.Default.Equals(a, current));
			if (selectedIndex < 0)
				selectedIndex = Math.Max(0, methodList.FindIndex(a => a.Parameters.Length > argIndex || a.Parameters.LastOrDefault()?.IsParams == true));

			var items = methodList.Select(a => new SignatureItem(FormatSignature(a, argIndex), GetDocumentation(a, argIndex, cancellationToken))).ToList();
			return new SignatureHelpInfo(items, selectedIndex, argList.SpanStart + 1);
		}

		static bool IsMissingCloseToken(CSS.BaseArgumentListSyntax list) =>
			list switch {
				CSS.ArgumentListSyntax a => a.CloseParenToken.IsMissing,
				CSS.BracketedArgumentListSyntax b => b.CloseBracketToken.IsMissing,
				_ => false,
			};

		static readonly SymbolDisplayFormat signatureFormat = SymbolDisplayFormat.MinimallyQualifiedFormat
			.WithMemberOptions(SymbolDisplayMemberOptions.IncludeParameters | SymbolDisplayMemberOptions.IncludeType | SymbolDisplayMemberOptions.IncludeContainingType)
			.WithParameterOptions(SymbolDisplayParameterOptions.IncludeType | SymbolDisplayParameterOptions.IncludeName | SymbolDisplayParameterOptions.IncludeDefaultValue | SymbolDisplayParameterOptions.IncludeParamsRefOut);

		static string FormatSignature(IMethodSymbol method, int argIndex) {
			var s = method.ToDisplayString(signatureFormat);
			if (method.MethodKind == MethodKind.PropertyGet && method.AssociatedSymbol is IPropertySymbol indexer)
				s = indexer.ToDisplayString(signatureFormat);
			if (argIndex < method.Parameters.Length)
				s += Environment.NewLine + "    " + method.Parameters[argIndex].ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
			return s;
		}

		static string? GetDocumentation(IMethodSymbol method, int argIndex, CancellationToken cancellationToken) {
			var xml = method.GetDocumentationCommentXml(cancellationToken: cancellationToken);
			if (string.IsNullOrEmpty(xml))
				return null;
			try {
				var doc = System.Xml.Linq.XElement.Parse("<r>" + xml + "</r>");
				var summary = doc.Descendants("summary").FirstOrDefault()?.Value.Trim();
				string? paramDoc = null;
				if (argIndex < method.Parameters.Length) {
					var name = method.Parameters[argIndex].Name;
					paramDoc = doc.Descendants("param").FirstOrDefault(a => (string?)a.Attribute("name") == name)?.Value.Trim();
				}
				var parts = new[] { summary, paramDoc is null ? null : $"{method.Parameters[argIndex].Name}: {paramDoc}" }.Where(a => !string.IsNullOrEmpty(a));
				var text = string.Join(Environment.NewLine, parts);
				return text.Length == 0 ? null : System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
			}
			catch (System.Xml.XmlException) {
				return null;
			}
		}

		public void Dispose() => workspace.Dispose();
	}

	// The C# and VB differences, see dnSpy.Roslyn's CSharpLanguageCompiler and VisualBasicLanguageCompiler
	abstract class RoslynLanguageOptions {
		public abstract string LanguageName { get; }
		public abstract string FileExtension { get; }
		public abstract ParseOptions ParseOptions { get; }
		public abstract bool SupportsNetModule { get; }
		public abstract CompilationOptions CreateCompilationOptions(OutputKind outputKind);
		public virtual CompilationOptions CreateCompilationOptionsNoAttributes(CompilationOptions compilationOptions) => compilationOptions;
		// editingCorLib is true if the edited module is the corlib, eg. mscorlib
		public virtual IEnumerable<string> GetRequiredAssemblyReferences(dnlib.DotNet.ModuleDef editedModule, bool editingCorLib) => Array.Empty<string>();
		public abstract string GetHelpUri(Diagnostic diagnostic);

		protected static IEnumerable<string> SplitSymbols(string symbols) {
			foreach (var tmp in symbols.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)) {
				var s = tmp.Trim();
				if (s.Length != 0)
					yield return s;
			}
		}
	}

	sealed class CSharpLanguageOptions : RoslynLanguageOptions {
		readonly CSharpCompilerSettings settings;

		public CSharpLanguageOptions(CSharpCompilerSettings settings) => this.settings = settings;

		public override string LanguageName => LanguageNames.CSharp;
		public override string FileExtension => ".cs";
		public override ParseOptions ParseOptions => new CS.CSharpParseOptions(languageVersion: CS.LanguageVersion.Latest, preprocessorSymbols: SplitSymbols(settings.PreprocessorSymbols));
		public override bool SupportsNetModule => true;

		public override CompilationOptions CreateCompilationOptions(OutputKind outputKind) =>
			new CS.CSharpCompilationOptions(outputKind,
				optimizationLevel: settings.Optimize ? OptimizationLevel.Release : OptimizationLevel.Debug,
				checkOverflow: settings.CheckOverflow,
				allowUnsafe: settings.AllowUnsafe);

		public override CompilationOptions CreateCompilationOptionsNoAttributes(CompilationOptions compilationOptions) =>
			((CS.CSharpCompilationOptions)compilationOptions).WithAllowUnsafe(false);

		public override string GetHelpUri(Diagnostic diagnostic) =>
			"https://learn.microsoft.com/dotnet/csharp/misc/" + diagnostic.Id.ToLowerInvariant();
	}

	sealed class VisualBasicLanguageOptions : RoslynLanguageOptions {
		readonly VisualBasicCompilerSettings settings;
		bool embedVbCoreRuntime;

		public VisualBasicLanguageOptions(VisualBasicCompilerSettings settings) => this.settings = settings;

		public override string LanguageName => LanguageNames.VisualBasic;
		public override string FileExtension => ".vb";
		public override ParseOptions ParseOptions =>
			new Microsoft.CodeAnalysis.VisualBasic.VisualBasicParseOptions(languageVersion: Microsoft.CodeAnalysis.VisualBasic.LanguageVersion.Latest,
				preprocessorSymbols: GetPreprocessorSymbols());
		// The VB compiler doesn't support netmodule + embed runtime
		public override bool SupportsNetModule => !embedVbCoreRuntime;

		IEnumerable<KeyValuePair<string, object>> GetPreprocessorSymbols() {
			foreach (var tmp in SplitSymbols(settings.PreprocessorSymbols)) {
				var s = tmp;
				var index = s.IndexOf('=');
				if (index < 0) {
					yield return new KeyValuePair<string, object>(s, true);
					continue;
				}
				var value = s.Substring(index + 1).Trim();
				s = s.Substring(0, index).Trim();
				if (s.Length == 0)
					continue;
				if (bool.TryParse(value, out bool valueBoolean))
					yield return new KeyValuePair<string, object>(s, valueBoolean);
				else if (int.TryParse(value, out int valueInt32))
					yield return new KeyValuePair<string, object>(s, valueInt32);
				else if (double.TryParse(value, out double valueDouble))
					yield return new KeyValuePair<string, object>(s, valueDouble);
				else {
					if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
						value = value.Substring(1, value.Length - 2);
					yield return new KeyValuePair<string, object>(s, value);
				}
			}
		}

		public override CompilationOptions CreateCompilationOptions(OutputKind outputKind) =>
			new Microsoft.CodeAnalysis.VisualBasic.VisualBasicCompilationOptions(outputKind,
				optionStrict: settings.OptionStrict ? Microsoft.CodeAnalysis.VisualBasic.OptionStrict.On : Microsoft.CodeAnalysis.VisualBasic.OptionStrict.Off,
				optionInfer: settings.OptionInfer,
				optionExplicit: settings.OptionExplicit,
				optionCompareText: !settings.OptionCompareBinary,
				embedVbCoreRuntime: embedVbCoreRuntime,
				optimizationLevel: settings.Optimize ? OptimizationLevel.Release : OptimizationLevel.Debug);

		public override IEnumerable<string> GetRequiredAssemblyReferences(dnlib.DotNet.ModuleDef editedModule, bool editingCorLib) {
			var frameworkKind = dnSpy.Roslyn.Compiler.FrameworkDetector.GetFrameworkKind(editedModule);
			// If we're editing mscorlib, embed the types
			if (settings.EmbedVBRuntime || editingCorLib)
				frameworkKind = dnSpy.Roslyn.Compiler.FrameworkKind.Unknown;
			switch (frameworkKind) {
			case dnSpy.Roslyn.Compiler.FrameworkKind.DotNetFramework2:
				return new[] { "Microsoft.VisualBasic, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a" };
			case dnSpy.Roslyn.Compiler.FrameworkKind.DotNetFramework4:
				return new[] { "Microsoft.VisualBasic, Version=10.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a" };
			default:
				embedVbCoreRuntime = true;
				return Array.Empty<string>();
			}
		}

		public override string GetHelpUri(Diagnostic diagnostic) =>
			"https://learn.microsoft.com/dotnet/visual-basic/misc/" + diagnostic.Id.ToLowerInvariant();
	}
}

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
using System.Globalization;
using System.IO;
using System.Linq;
using dnlib.DotNet;
using dnlib.DotNet.Resources;
using dnlib.PE;
using dnSpy.AsmEditor.Commands;
using dnSpy.Avalonia.Controls;
using dnSpy.Contracts.Decompiler;
using dnSpy.Contracts.Documents;
using dnSpy.Contracts.Documents.TreeView;
using dnSpy.Contracts.Images;
using dnSpy.Contracts.Text;
using dnSpy.Images;

namespace dnSpy.Avalonia.TreeNodes {
	abstract class DocumentNode : TreeNode {
		public IDsDocument Document { get; }
		public override object? Reference => Document;

		protected DocumentNode(TreeNodeContext context, IDsDocument document) : base(context) => Document = document;

		public static DocumentNode Create(TreeNodeContext context, IDsDocument document) {
			if (document.AssemblyDef is not null)
				return new AssemblyDocumentNode(context, document);
			if (document.ModuleDef is not null)
				return new ModuleDocumentNode(context, document);
			if (document.PEImage is not null)
				return new PEDocumentNode(context, document);
			return new UnknownDocumentNode(context, document);
		}
	}

	sealed class AssemblyDocumentNode : DocumentNode {
		public AssemblyDocumentNode(TreeNodeContext context, IDsDocument document) : base(context, document) { }

		protected override string ImageName => DotNetImageNames.GetName(Document.AssemblyDef!);
		protected override bool HasChildren => true;

		protected override IEnumerable<TreeNode> CreateChildren() {
			foreach (var child in Document.Children)
				yield return new ModuleDocumentNode(Context, child);
		}

		protected override void WriteText(ColoredText output, IDecompiler decompiler) =>
			new NodeFormatter().Write(output, decompiler, Document.AssemblyDef!, showToken: false, showAssemblyVersion: true, showAssemblyPublicKeyToken: false);

		public override void Decompile(in DecompileContext ctx) => ctx.Decompiler.Decompile(Document.AssemblyDef!, ctx.Output, ctx.DecompilationContext);
	}

	sealed class ModuleDocumentNode : DocumentNode {
		public ModuleDef Module => Document.ModuleDef!;

		public ModuleDocumentNode(TreeNodeContext context, IDsDocument document) : base(context, document) { }

		protected override string ImageName => DotNetImageNames.GetName(Module);
		protected override bool HasChildren => true;

		protected override IEnumerable<TreeNode> CreateChildren() {
			yield return new ReferencesFolderNode(Context, Module);
			if (Module.Resources.Count > 0)
				yield return new ResourcesFolderNode(Context, Module);
			var namespaces = Module.Types.GroupBy(a => a.Namespace.String ?? string.Empty, StringComparer.Ordinal).OrderBy(a => a.Key, StringComparer.OrdinalIgnoreCase);
			foreach (var ns in namespaces)
				yield return new NamespaceNode(Context, ns.Key, ns.ToArray());
		}

		protected override void WriteText(ColoredText output, IDecompiler decompiler) =>
			new NodeFormatter().Write(output, decompiler, Module, showToken: false);

		public override void Decompile(in DecompileContext ctx) => ctx.Decompiler.Decompile(Module, ctx.Output, ctx.DecompilationContext);

		public NamespaceNode? FindNamespace(string ns) =>
			GetChildren().OfType<NamespaceNode>().FirstOrDefault(a => StringComparer.Ordinal.Equals(a.Name, ns));
	}

	sealed class PEDocumentNode : DocumentNode {
		public PEDocumentNode(TreeNodeContext context, IDsDocument document) : base(context, document) { }

		protected override string ImageName => DotNetImageNames.GetName(Document.PEImage!.ImageNTHeaders.FileHeader.Characteristics);

		protected override void WriteText(ColoredText output, IDecompiler decompiler) => new NodeFormatter().Write(output, decompiler, Document);

		public override void Decompile(in DecompileContext ctx) {
			var decompiler = ctx.Decompiler;
			decompiler.WriteCommentLine(ctx.Output, Document.Filename);
			var peImage = Document.PEImage;
			if (peImage is not null) {
				uint ts = peImage.ImageNTHeaders.FileHeader.TimeDateStamp;
				var date = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(ts).ToLocalTime();
				decompiler.WriteCommentLine(ctx.Output, $"Timestamp: {ts:X8} ({date.ToString(CultureInfo.CurrentUICulture.DateTimeFormat)})");
			}
		}
	}

	sealed class UnknownDocumentNode : DocumentNode {
		public UnknownDocumentNode(TreeNodeContext context, IDsDocument document) : base(context, document) { }

		protected override string ImageName => DsImageStrings.BinaryFile;

		protected override void WriteText(ColoredText output, IDecompiler decompiler) => new NodeFormatter().Write(output, decompiler, Document);

		public override void Decompile(in DecompileContext ctx) => ctx.Decompiler.WriteCommentLine(ctx.Output, Document.Filename);
	}

	sealed class ReferencesFolderNode : TreeNode {
		readonly ModuleDef module;

		public ReferencesFolderNode(TreeNodeContext context, ModuleDef module) : base(context) => this.module = module;

		public override object? Reference => null;
		// There's only one of these folders per module
		protected override object? ReuseKey => module;
		protected override string ImageName => DsImageStrings.FolderClosed;
		protected override string? ExpandedImageName => DsImageStrings.FolderOpened;
		protected override bool HasChildren => true;

		protected override IEnumerable<TreeNode> CreateChildren() {
			foreach (var asmRef in module.GetAssemblyRefs().OrderBy(a => a.FullName, StringComparer.OrdinalIgnoreCase))
				yield return new AssemblyReferenceNode(Context, asmRef);
			foreach (var modRef in module.GetModuleRefs().OrderBy(a => a.FullName, StringComparer.OrdinalIgnoreCase))
				yield return new ModuleReferenceNode(Context, modRef);
		}

		protected override void WriteText(ColoredText output, IDecompiler decompiler) => output.Write(BoxedTextColor.Text, "References");

		public override void Decompile(in DecompileContext ctx) {
			foreach (var asmRef in module.GetAssemblyRefs())
				ctx.Decompiler.WriteCommentLine(ctx.Output, NameUtilities.CleanName(asmRef.ToString()));
			foreach (var modRef in module.GetModuleRefs())
				ctx.Decompiler.WriteCommentLine(ctx.Output, NameUtilities.CleanName(modRef.ToString()));
		}
	}

	sealed class AssemblyReferenceNode : TreeNode {
		public AssemblyRef AssemblyRef { get; }

		public AssemblyReferenceNode(TreeNodeContext context, AssemblyRef asmRef) : base(context) => AssemblyRef = asmRef;

		public override object? Reference => AssemblyRef;
		protected override string ImageName => DsImageStrings.Reference;

		protected override void WriteText(ColoredText output, IDecompiler decompiler) =>
			new NodeFormatter().Write(output, decompiler, AssemblyRef, showToken: false);

		public override void Decompile(in DecompileContext ctx) => ctx.Decompiler.WriteCommentLine(ctx.Output, NameUtilities.CleanName(AssemblyRef.ToString()));
	}

	sealed class ModuleReferenceNode : TreeNode {
		readonly ModuleRef moduleRef;

		public ModuleReferenceNode(TreeNodeContext context, ModuleRef moduleRef) : base(context) => this.moduleRef = moduleRef;

		public override object? Reference => moduleRef;
		protected override string ImageName => DsImageStrings.Reference;

		protected override void WriteText(ColoredText output, IDecompiler decompiler) =>
			new NodeFormatter().Write(output, decompiler, moduleRef, showToken: false);

		public override void Decompile(in DecompileContext ctx) => ctx.Decompiler.WriteCommentLine(ctx.Output, NameUtilities.CleanName(moduleRef.ToString()));
	}

	sealed class ResourcesFolderNode : TreeNode {
		readonly ModuleDef module;

		public ResourcesFolderNode(TreeNodeContext context, ModuleDef module) : base(context) => this.module = module;

		public ModuleDef Module => module;

		public override object? Reference => null;
		// There's only one of these folders per module
		protected override object? ReuseKey => module;
		protected override string ImageName => DsImageStrings.FolderClosed;
		protected override string? ExpandedImageName => DsImageStrings.FolderOpened;
		protected override bool HasChildren => true;

		protected override IEnumerable<TreeNode> CreateChildren() {
			foreach (var resource in module.Resources.OrderBy(a => a.Name.String, StringComparer.OrdinalIgnoreCase))
				yield return new ResourceNode(Context, module, resource);
		}

		protected override void WriteText(ColoredText output, IDecompiler decompiler) => output.Write(BoxedTextColor.Text, "Resources");

		public override void Decompile(in DecompileContext ctx) {
			foreach (var resource in module.Resources)
				ResourceNode.WriteInfo(ctx, resource);
		}
	}

	sealed class ResourceNode : TreeNode {
		Resource resource;
		readonly ModuleDef module;
		// The elements if it's a .resources file
		ResourceElementSet? resourceElementSet;

		public ResourceNode(TreeNodeContext context, ModuleDef module, Resource resource) : base(context) {
			this.module = module;
			this.resource = resource;
		}

		public Resource Resource => resource;
		public ModuleDef Module => module;
		public override object? Reference => resource;
		// Editing a resource element creates a new embedded resource with the same name
		protected override object? ReuseKey => resource.Name.String;
		protected override string ImageName => IsResourceSet ? DsImageStrings.SourceFileGroup : resource.ResourceType == ResourceType.Embedded ? DsImageStrings.BinaryFile : DsImageStrings.Reference;
		public bool IsResourceSet => ResourceChanges.IsResourceSet(resource);
		protected override bool HasChildren => IsResourceSet;

		protected override void UpdateFrom(TreeNode newNode) {
			var other = (ResourceNode)newNode;
			if (other.resource != resource) {
				resource = other.resource;
				resourceElementSet = null;
			}
		}

		ResourceElementSet? GetResourceElementSet() {
			if (resourceElementSet is null && resource is EmbeddedResource er && IsResourceSet) {
				try {
					resourceElementSet = ResourceChanges.ReadResourceSet(module, er);
				}
				catch (Exception ex) when (ex is IOException || ex is ResourceReaderException || ex is InvalidOperationException) {
				}
			}
			return resourceElementSet;
		}

		protected override IEnumerable<TreeNode> CreateChildren() {
			if (GetResourceElementSet() is ResourceElementSet set) {
				foreach (var element in set.ResourceElements)
					yield return new ResourceElementNode(Context, this, element);
			}
		}

		protected override void WriteText(ColoredText output, IDecompiler decompiler) => output.Write(BoxedTextColor.Text, NameUtilities.CleanName(resource.Name));

		public override void Decompile(in DecompileContext ctx) {
			WriteInfo(ctx, resource);
			if (GetResourceElementSet() is ResourceElementSet set) {
				foreach (var element in set.ResourceElements)
					ResourceElementNode.WriteInfo(ctx, element);
			}
		}

		internal static void WriteInfo(in DecompileContext ctx, Resource resource) {
			var name = NameUtilities.CleanName(resource.Name);
			switch (resource) {
			case EmbeddedResource er:
				ctx.Decompiler.WriteCommentLine(ctx.Output, $"{name} ({er.Length} bytes, {er.Attributes & ManifestResourceAttributes.VisibilityMask})");
				break;
			case AssemblyLinkedResource alr:
				ctx.Decompiler.WriteCommentLine(ctx.Output, $"{name} => {NameUtilities.CleanName(alr.Assembly.FullName)}");
				break;
			case LinkedResource lr:
				ctx.Decompiler.WriteCommentLine(ctx.Output, $"{name} => {NameUtilities.CleanName(lr.File.Name)}");
				break;
			default:
				ctx.Decompiler.WriteCommentLine(ctx.Output, name);
				break;
			}
		}
	}

	// An element in a .resources file
	sealed class ResourceElementNode : TreeNode {
		public ResourceNode ResourceNode { get; }
		public ResourceElement ResourceElement { get; }

		public ResourceElementNode(TreeNodeContext context, ResourceNode resourceNode, ResourceElement resourceElement) : base(context) {
			ResourceNode = resourceNode;
			ResourceElement = resourceElement;
		}

		public override object? Reference => ResourceElement;
		protected override object? ReuseKey => ResourceElement.Name;
		protected override string ImageName => ResourceElement.ResourceData.Code switch {
			ResourceTypeCode.String => DsImageStrings.String,
			ResourceTypeCode.ByteArray or ResourceTypeCode.Stream => DsImageStrings.BinaryFile,
			ResourceTypeCode.UserTypes => DsImageStrings.UserDefinedDataType,
			_ => DsImageStrings.FieldPublic,
		};

		protected override void WriteText(ColoredText output, IDecompiler decompiler) => output.Write(BoxedTextColor.Text, NameUtilities.CleanName(ResourceElement.Name));

		public override void Decompile(in DecompileContext ctx) => WriteInfo(ctx, ResourceElement);

		internal static void WriteInfo(in DecompileContext ctx, ResourceElement element) {
			var name = NameUtilities.CleanName(element.Name);
			string value = element.ResourceData switch {
				BuiltInResourceData { Data: string str } => "\"" + str.Replace("\r", "\\r").Replace("\n", "\\n") + "\"",
				BuiltInResourceData { Data: byte[] bytes } => $"byte[{bytes.Length}]",
				BuiltInResourceData builtIn => $"{builtIn.Code}: {builtIn.Data}",
				BinaryResourceData binary => $"{binary.TypeName} ({binary.Data.Length} bytes)",
				var data => data.ToString() ?? string.Empty,
			};
			ctx.Decompiler.WriteCommentLine(ctx.Output, $"{name} = {value}");
		}
	}
}

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

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using dnlib.DotNet;
using dnSpy.Contracts.Decompiler;
using dnSpy.Contracts.Documents;

namespace dnSpy.Avalonia.TreeNodes {
	// The root nodes of the tree view. Kept in sync with IDsDocumentService.
	sealed class DocumentTreeModel {
		readonly TreeNodeContext context;

		public ObservableCollection<DocumentNode> Roots { get; } = new ObservableCollection<DocumentNode>();

		public DocumentTreeModel(TreeNodeContext context) {
			this.context = context;
			context.DocumentService.CollectionChanged += DocumentService_CollectionChanged;
			foreach (var document in context.DocumentService.GetDocuments())
				GetOrAddNode(document);
		}

		void DocumentService_CollectionChanged(object? sender, NotifyDocumentCollectionChangedEventArgs e) {
			switch (e.Type) {
			case NotifyDocumentCollectionType.Add:
				foreach (var document in e.Documents)
					GetOrAddNode(document);
				break;

			case NotifyDocumentCollectionType.Remove:
				var removed = new HashSet<IDsDocument>(e.Documents);
				for (int i = Roots.Count - 1; i >= 0; i--) {
					if (removed.Contains(Roots[i].Document))
						Roots.RemoveAt(i);
				}
				break;

			case NotifyDocumentCollectionType.Clear:
				Roots.Clear();
				break;
			}
		}

		// The document service raises Add events asynchronously (see SetDispatcher()) so a document
		// that was just loaded, eg. by resolving a reference, might not have a node yet.
		public DocumentNode GetOrAddNode(IDsDocument document) {
			foreach (var node in Roots) {
				if (node.Document == document)
					return node;
			}
			var newNode = DocumentNode.Create(context, document);
			Roots.Add(newNode);
			return newNode;
		}

		// The document service raises Remove events asynchronously. This updates the roots now, eg. after an edit
		// that replaced a document.
		public void SyncWithDocumentService() {
			var documents = context.DocumentService.GetDocuments();
			var set = new HashSet<IDsDocument>(documents);
			for (int i = Roots.Count - 1; i >= 0; i--) {
				if (!set.Contains(Roots[i].Document))
					Roots.RemoveAt(i);
			}
			foreach (var document in documents)
				GetOrAddNode(document);
		}

		public void Remove(IEnumerable<DocumentNode> nodes) =>
			context.DocumentService.Remove(nodes.Select(a => a.Document).ToArray());

		public TreeNode? FindNode(object? reference) {
			switch (reference) {
			case IDsDocument document:
				return FindDocumentNode(document);
			case AssemblyDef asm:
				var modNode = FindModuleNode(asm.ManifestModule);
				return modNode?.Parent ?? modNode;
			case ModuleDef module:
				return FindModuleNode(module);
			case AssemblyRef asmRef:
				return ResolveAssembly(asmRef, null);
			case NamespaceReference nsRef:
				return FindNamespaceNode(nsRef);
			case IMemberRef memberRef:
				return FindMemberNode(memberRef);
			default:
				return null;
			}
		}

		TreeNode? FindDocumentNode(IDsDocument document) {
			foreach (var root in Roots) {
				if (root.Document == document)
					return root;
				if (root is AssemblyDocumentNode) {
					foreach (var child in root.GetChildren()) {
						if (child is DocumentNode docNode && docNode.Document == document)
							return docNode;
					}
				}
			}
			return null;
		}

		TreeNode? ResolveAssembly(IAssembly asm, ModuleDef? sourceModule) {
			var document = context.DocumentService.Resolve(asm, sourceModule);
			return document is null ? null : FindDocumentNode(document) ?? GetOrAddNode(document);
		}

		ModuleDocumentNode? FindModuleNode(ModuleDef? module) {
			if (module is null)
				return null;
			if (FindLoadedModuleNode(module) is ModuleDocumentNode node)
				return node;

			// It could've been loaded by the assembly resolver but the Add event hasn't been raised yet
			foreach (var document in context.DocumentService.GetDocuments()) {
				if (document.ModuleDef == module || document.Children.Any(a => a.ModuleDef == module)) {
					GetOrAddNode(document);
					return FindLoadedModuleNode(module);
				}
			}
			return null;
		}

		ModuleDocumentNode? FindLoadedModuleNode(ModuleDef module) {
			foreach (var root in Roots) {
				if (root is ModuleDocumentNode modNode && modNode.Module == module)
					return modNode;
				if (root is AssemblyDocumentNode && root.Document.AssemblyDef == module.Assembly) {
					foreach (var child in root.GetChildren()) {
						if (child is ModuleDocumentNode childModNode && childModNode.Module == module)
							return childModNode;
					}
				}
			}
			return null;
		}

		TreeNode? FindNamespaceNode(NamespaceReference nsRef) {
			var ns = nsRef.Namespace ?? string.Empty;
			if (nsRef.Assembly is AssemblyRef asmRef && ResolveAssembly(asmRef, null) is TreeNode asmNode) {
				foreach (var child in asmNode.GetChildren().OfType<ModuleDocumentNode>()) {
					if (child.FindNamespace(ns) is NamespaceNode nsNode)
						return nsNode;
				}
			}
			foreach (var root in Roots) {
				var modules = root is ModuleDocumentNode modNode ? new[] { modNode } : root.GetChildren().OfType<ModuleDocumentNode>();
				foreach (var module in modules) {
					if (module.FindNamespace(ns) is NamespaceNode nsNode)
						return nsNode;
				}
			}
			return null;
		}

		TreeNode? FindMemberNode(IMemberRef memberRef) {
			var def = Resolve(memberRef);
			if (def is null)
				return null;

			var type = def as TypeDef ?? def.DeclaringType;
			if (type is null)
				return null;
			var typeNode = FindTypeNode(type);
			if (typeNode is null || def is TypeDef)
				return typeNode;

			foreach (var child in typeNode.GetChildren()) {
				if (child.Reference == def)
					return child;
				if ((child is PropertyNode || child is EventNode) && def is MethodDef) {
					foreach (var accessor in child.GetChildren()) {
						if (accessor.Reference == def)
							return accessor;
					}
				}
			}
			return typeNode;
		}

		TypeNode? FindTypeNode(TypeDef type) {
			var declTypes = new List<TypeDef>();
			for (var t = type; t is not null; t = t.DeclaringType)
				declTypes.Add(t);
			declTypes.Reverse();

			var nsNode = FindModuleNode(type.Module)?.FindNamespace(declTypes[0].Namespace.String ?? string.Empty);
			TreeNode? node = nsNode;
			foreach (var t in declTypes) {
				node = node?.GetChildren().OfType<TypeNode>().FirstOrDefault(a => a.TypeDef == t);
				if (node is null)
					return null;
			}
			return node as TypeNode;
		}

		static IMemberDef? Resolve(IMemberRef memberRef) {
			switch (memberRef) {
			case IMemberDef def:
				return def;
			case MethodSpec ms:
				return Resolve(ms.Method);
			case TypeSpec ts:
				return ts.ScopeType.ResolveTypeDef();
			case ITypeDefOrRef tdr:
				return tdr.ResolveTypeDef();
			case MemberRef mr:
				return mr.Resolve() as IMemberDef;
			default:
				return null;
			}
		}
	}
}

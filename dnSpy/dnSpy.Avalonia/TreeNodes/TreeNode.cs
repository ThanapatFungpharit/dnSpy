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
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia.Media.Imaging;
using dnSpy.Avalonia.Controls;
using dnSpy.Avalonia.Images;
using dnSpy.Contracts.Decompiler;
using dnSpy.Contracts.Documents;

namespace dnSpy.Avalonia.TreeNodes {
	sealed class TreeNodeContext {
		public IDsDocumentService DocumentService { get; }
		public Func<IDecompiler> GetDecompiler { get; }

		public TreeNodeContext(IDsDocumentService documentService, Func<IDecompiler> getDecompiler) {
			DocumentService = documentService;
			GetDecompiler = getDecompiler;
		}
	}

	readonly struct DecompileContext {
		public IDecompiler Decompiler { get; }
		public IDecompilerOutput Output { get; }
		public DecompilationContext DecompilationContext { get; }

		public DecompileContext(IDecompiler decompiler, IDecompilerOutput output, DecompilationContext decompilationContext) {
			Decompiler = decompiler;
			Output = output;
			DecompilationContext = decompilationContext;
		}
	}

	// A lazily loaded node in the document tree. Children are created the first time the node
	// is expanded; until then a placeholder child makes the tree view show an expander.
	abstract class TreeNode : INotifyPropertyChanged {
		public event PropertyChangedEventHandler? PropertyChanged;

		public TreeNodeContext Context { get; }
		public TreeNode? Parent { get; private set; }
		public ObservableCollection<TreeNode> Children => children ??= CreateChildrenCollection();

		ObservableCollection<TreeNode>? children;
		bool childrenLoaded;
		bool isExpanded;
		ColoredText? text;

		protected TreeNode(TreeNodeContext context) => Context = context;

		// Not created in the constructor since HasChildren uses the derived classes' fields
		ObservableCollection<TreeNode> CreateChildrenCollection() {
			var list = new ObservableCollection<TreeNode>();
			if (HasChildren)
				list.Add(new PlaceholderNode(Context));
			else
				childrenLoaded = true;
			return list;
		}

		public bool IsExpanded {
			get => isExpanded;
			set {
				if (isExpanded == value)
					return;
				isExpanded = value;
				if (value)
					EnsureChildrenLoaded();
				OnPropertyChanged(nameof(IsExpanded));
				if (ExpandedImageName is not null)
					OnPropertyChanged(nameof(Icon));
			}
		}

		public ColoredText Text {
			get {
				if (text is null) {
					text = new ColoredText();
					WriteText(text, Context.GetDecompiler());
				}
				return text;
			}
		}

		public Bitmap? Icon => ImageCache.Get(isExpanded && ExpandedImageName is not null ? ExpandedImageName : ImageName);

		protected abstract string ImageName { get; }
		protected virtual string? ExpandedImageName => null;
		protected virtual bool HasChildren => false;
		protected virtual IEnumerable<TreeNode> CreateChildren() => Array.Empty<TreeNode>();
		protected abstract void WriteText(ColoredText output, IDecompiler decompiler);
		public abstract void Decompile(in DecompileContext ctx);

		// The object that the node is showing, eg. a TypeDef. Used to find nodes.
		public abstract object? Reference { get; }

		public IReadOnlyList<TreeNode> GetChildren() {
			EnsureChildrenLoaded();
			return Children;
		}

		public void EnsureChildrenLoaded() {
			var list = Children;
			if (childrenLoaded)
				return;
			childrenLoaded = true;
			list.Clear();
			foreach (var child in CreateChildren())
				AddChild(child);
		}

		public void AddChild(TreeNode child) {
			child.Parent = this;
			Children.Add(child);
		}

		// The key used to find the old node when the children are reloaded, see ReloadChildren()
		protected virtual object? ReuseKey => Reference;

		// Called when the children are reloaded and this node is kept instead of newNode. Nodes that
		// cache data from the model update it here.
		protected virtual void UpdateFrom(TreeNode newNode) { }

		// Recreates the children after the model changed (eg. code was edited and members were added or
		// removed). The old child nodes that show the same object are kept so they stay expanded. Children
		// that haven't been loaded yet are created the next time the node is expanded.
		public void ReloadChildren() {
			// The text and icon could've changed, eg. the member was renamed
			text = null;
			OnPropertyChanged(nameof(Text));
			OnPropertyChanged(nameof(Icon));
			if (children is null)
				return;
			if (!childrenLoaded) {
				// HasChildren could've changed
				children.Clear();
				if (HasChildren)
					children.Add(new PlaceholderNode(Context));
				else
					childrenLoaded = true;
				return;
			}

			var oldChildren = children.ToList();
			var newChildren = new List<TreeNode>();
			foreach (var newChild in CreateChildren()) {
				var key = newChild.ReuseKey;
				var oldChild = key is null ? null : oldChildren.FirstOrDefault(a => a.GetType() == newChild.GetType() && Equals(a.ReuseKey, key));
				if (oldChild is not null) {
					oldChildren.Remove(oldChild);
					oldChild.UpdateFrom(newChild);
					newChildren.Add(oldChild);
				}
				else
					newChildren.Add(newChild);
			}
			bool same = newChildren.Count == children.Count && newChildren.Zip(children, (a, b) => a == b).All(a => a);
			if (!same) {
				children.Clear();
				foreach (var child in newChildren)
					AddChild(child);
			}
			foreach (var child in newChildren)
				child.ReloadChildren();
		}

		public void RefreshText() {
			text = null;
			OnPropertyChanged(nameof(Text));
			if (childrenLoaded && children is not null) {
				foreach (var child in children)
					child.RefreshText();
			}
		}

		protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

		public override string ToString() => Text.ToString();
	}

	sealed class PlaceholderNode : TreeNode {
		public PlaceholderNode(TreeNodeContext context) : base(context) { }
		protected override string ImageName => string.Empty;
		public override object? Reference => null;
		protected override void WriteText(ColoredText output, IDecompiler decompiler) { }
		public override void Decompile(in DecompileContext ctx) { }
	}
}

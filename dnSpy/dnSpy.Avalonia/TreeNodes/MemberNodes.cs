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
using System.Linq;
using dnlib.DotNet;
using dnSpy.Avalonia.Controls;
using dnSpy.Contracts.Decompiler;
using dnSpy.Contracts.Documents.TreeView;
using dnSpy.Contracts.Images;
using dnSpy.Contracts.Text;
using dnSpy.Images;

namespace dnSpy.Avalonia.TreeNodes {
	sealed class NamespaceNode : TreeNode {
		public string Name { get; }
		TypeDef[] types;

		public NamespaceNode(TreeNodeContext context, string name, TypeDef[] types) : base(context) {
			Name = name;
			this.types = types;
		}

		public override object? Reference => null;
		protected override object? ReuseKey => Name;
		protected override string ImageName => DsImageStrings.Namespace;

		protected override void UpdateFrom(TreeNode newNode) => types = ((NamespaceNode)newNode).types;
		protected override bool HasChildren => types.Length > 0;

		protected override IEnumerable<TreeNode> CreateChildren() {
			foreach (var type in types.OrderBy(a => a, TypeDefComparer.Instance))
				yield return new TypeNode(Context, type);
		}

		protected override void WriteText(ColoredText output, IDecompiler decompiler) {
			// Same as the WPF tree view, the global namespace is shown as '-'
			if (Name.Length == 0)
				output.Write(BoxedTextColor.Text, "-");
			else
				new NodeFormatter().WriteNamespace(output, decompiler, Name);
		}

		public override void Decompile(in DecompileContext ctx) => ctx.Decompiler.DecompileNamespace(Name, types, ctx.Output, ctx.DecompilationContext);
	}

	sealed class TypeNode : TreeNode {
		public TypeDef TypeDef { get; }

		public TypeNode(TreeNodeContext context, TypeDef type) : base(context) => TypeDef = type;

		public override object? Reference => TypeDef;
		protected override string ImageName => DotNetImageNames.GetName(TypeDef);
		protected override bool HasChildren =>
			TypeDef.Methods.Count > 0 || TypeDef.Fields.Count > 0 || TypeDef.Properties.Count > 0 ||
			TypeDef.Events.Count > 0 || TypeDef.NestedTypes.Count > 0;

		// Same order as the WPF tree view: methods, properties, events, fields, nested types
		protected override IEnumerable<TreeNode> CreateChildren() {
			var accessors = TypeDef.GetPropertyAndEventMethods();
			foreach (var m in TypeDef.Methods.Where(a => !accessors.Contains(a)).OrderBy(a => a, MethodDefComparer.Instance))
				yield return new MethodNode(Context, m);
			foreach (var p in TypeDef.Properties.OrderBy(a => a, PropertyDefComparer.Instance))
				yield return new PropertyNode(Context, p);
			foreach (var e in TypeDef.Events.OrderBy(a => a, EventDefComparer.Instance))
				yield return new EventNode(Context, e);
			foreach (var f in TypeDef.Fields.OrderBy(a => a, FieldDefComparer.Instance))
				yield return new FieldNode(Context, f);
			foreach (var t in TypeDef.NestedTypes.OrderBy(a => a, TypeDefComparer.Instance))
				yield return new TypeNode(Context, t);
		}

		protected override void WriteText(ColoredText output, IDecompiler decompiler) => new NodeFormatter().Write(output, decompiler, TypeDef, showToken: false);
		public override void Decompile(in DecompileContext ctx) => ctx.Decompiler.Decompile(TypeDef, ctx.Output, ctx.DecompilationContext);
	}

	sealed class MethodNode : TreeNode {
		public MethodDef MethodDef { get; }

		public MethodNode(TreeNodeContext context, MethodDef method) : base(context) => MethodDef = method;

		public override object? Reference => MethodDef;
		protected override string ImageName => DotNetImageNames.GetName(MethodDef);
		protected override void WriteText(ColoredText output, IDecompiler decompiler) => new NodeFormatter().Write(output, decompiler, MethodDef, showToken: false);
		public override void Decompile(in DecompileContext ctx) => ctx.Decompiler.Decompile(MethodDef, ctx.Output, ctx.DecompilationContext);
	}

	sealed class FieldNode : TreeNode {
		public FieldDef FieldDef { get; }

		public FieldNode(TreeNodeContext context, FieldDef field) : base(context) => FieldDef = field;

		public override object? Reference => FieldDef;
		protected override string ImageName => DotNetImageNames.GetName(FieldDef);
		protected override void WriteText(ColoredText output, IDecompiler decompiler) => new NodeFormatter().Write(output, decompiler, FieldDef, showToken: false);
		public override void Decompile(in DecompileContext ctx) => ctx.Decompiler.Decompile(FieldDef, ctx.Output, ctx.DecompilationContext);
	}

	sealed class PropertyNode : TreeNode {
		public PropertyDef PropertyDef { get; }

		public PropertyNode(TreeNodeContext context, PropertyDef property) : base(context) => PropertyDef = property;

		public override object? Reference => PropertyDef;
		protected override string ImageName => DotNetImageNames.GetName(PropertyDef);
		protected override bool HasChildren => PropertyDef.GetMethods.Count > 0 || PropertyDef.SetMethods.Count > 0 || PropertyDef.OtherMethods.Count > 0;

		protected override IEnumerable<TreeNode> CreateChildren() {
			foreach (var m in PropertyDef.GetMethods)
				yield return new MethodNode(Context, m);
			foreach (var m in PropertyDef.SetMethods)
				yield return new MethodNode(Context, m);
			foreach (var m in PropertyDef.OtherMethods)
				yield return new MethodNode(Context, m);
		}

		protected override void WriteText(ColoredText output, IDecompiler decompiler) => new NodeFormatter().Write(output, decompiler, PropertyDef, showToken: false, isIndexer: null);
		public override void Decompile(in DecompileContext ctx) => ctx.Decompiler.Decompile(PropertyDef, ctx.Output, ctx.DecompilationContext);
	}

	sealed class EventNode : TreeNode {
		public EventDef EventDef { get; }

		public EventNode(TreeNodeContext context, EventDef @event) : base(context) => EventDef = @event;

		public override object? Reference => EventDef;
		protected override string ImageName => DotNetImageNames.GetName(EventDef);
		protected override bool HasChildren => EventDef.AddMethod is not null || EventDef.RemoveMethod is not null || EventDef.InvokeMethod is not null || EventDef.OtherMethods.Count > 0;

		protected override IEnumerable<TreeNode> CreateChildren() {
			if (EventDef.AddMethod is MethodDef add)
				yield return new MethodNode(Context, add);
			if (EventDef.RemoveMethod is MethodDef remove)
				yield return new MethodNode(Context, remove);
			if (EventDef.InvokeMethod is MethodDef invoke)
				yield return new MethodNode(Context, invoke);
			foreach (var m in EventDef.OtherMethods)
				yield return new MethodNode(Context, m);
		}

		protected override void WriteText(ColoredText output, IDecompiler decompiler) => new NodeFormatter().Write(output, decompiler, EventDef, showToken: false);
		public override void Decompile(in DecompileContext ctx) => ctx.Decompiler.Decompile(EventDef, ctx.Output, ctx.DecompilationContext);
	}
}

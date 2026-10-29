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
using dnSpy.AsmEditor.Assembly;
using dnSpy.AsmEditor.Event;
using dnSpy.AsmEditor.Field;
using dnSpy.AsmEditor.Method;
using dnSpy.AsmEditor.MethodBody;
using dnSpy.AsmEditor.Module;
using dnSpy.AsmEditor.Property;
using dnSpy.AsmEditor.Types;
using dnSpy.Contracts.Documents;

namespace dnSpy.AsmEditor.Commands {
	/// <summary>
	/// The metadata changes made by an 'edit settings' command, without any UI updates
	/// </summary>
	sealed class SettingsChange {
		readonly Action apply;
		readonly Action undo;

		public SettingsChange(Action apply, Action undo) {
			this.apply = apply ?? throw new ArgumentNullException(nameof(apply));
			this.undo = undo ?? throw new ArgumentNullException(nameof(undo));
		}

		public void Apply() => apply();
		public void Undo() => undo();
	}

	/// <summary>
	/// Creates the changes of the 'edit settings' commands. The original values are saved when they're created.
	/// References to the edited definition from the same module are renamed too.
	/// </summary>
	static class SettingsChanges {
		public static SettingsChange Create(AssemblyDef asm, AssemblyOptions newOptions) {
			var origOptions = new AssemblyOptions(asm);
			var asmRefs = Array.Empty<(AssemblyRef asmRef, UTF8String name, PublicKeyBase publicKeyOrToken)>();
			if (newOptions.Name != origOptions.Name && asm.ManifestModule is ModuleDef module)
				asmRefs = RefFinder.FindAssemblyRefsToThisModule(module).Where(a => AssemblyNameComparer.NameAndPublicKeyTokenOnly.Equals(a, asm)).Select(a => (a, a.Name, a.PublicKeyOrToken)).ToArray();
			return new SettingsChange(() => {
				newOptions.CopyTo(asm);
				var pkt = newOptions.PublicKey.Token;
				foreach (var info in asmRefs) {
					info.asmRef.Name = newOptions.Name;
					if (info.asmRef.PublicKeyOrToken is PublicKeyToken)
						info.asmRef.PublicKeyOrToken = pkt;
					else
						info.asmRef.PublicKeyOrToken = newOptions.PublicKey;
				}
			}, () => {
				origOptions.CopyTo(asm);
				foreach (var info in asmRefs) {
					info.asmRef.Name = info.name;
					info.asmRef.PublicKeyOrToken = info.publicKeyOrToken;
				}
			});
		}

		public static SettingsChange Create(ModuleDef module, ModuleOptions newOptions) {
			var origOptions = new ModuleOptions(module);
			return new SettingsChange(() => newOptions.CopyTo(module), () => origOptions.CopyTo(module));
		}

		public static SettingsChange Create(TypeDef type, ModuleDef module, TypeDefOptions newOptions) {
			var origOptions = new TypeDefOptions(type);
			var typeRefs = Array.Empty<(TypeRef typeRef, UTF8String ns, UTF8String name)>();
			if (origOptions.Name != newOptions.Name || origOptions.Namespace != newOptions.Namespace)
				typeRefs = RefFinder.FindTypeRefsToThisModule(module).Where(a => RefFinder.TypeEqualityComparerInstance.Equals(a, type)).Select(a => (a, a.Namespace, a.Name)).ToArray();
			return new SettingsChange(() => {
				newOptions.CopyTo(type, module);
				foreach (var info in typeRefs) {
					info.typeRef.Namespace = type.Namespace;
					info.typeRef.Name = type.Name;
				}
			}, () => {
				origOptions.CopyTo(type, module);
				foreach (var info in typeRefs) {
					info.typeRef.Namespace = info.ns;
					info.typeRef.Name = info.name;
				}
			});
		}

		public static SettingsChange Create(MethodDef method, MethodDefOptions newOptions) {
			var origOptions = new MethodDefOptions(method);
			var memberRefs = Array.Empty<(MemberRef memberRef, UTF8String name)>();
			if (origOptions.Name != newOptions.Name && method.Module is ModuleDef module)
				memberRefs = RefFinder.FindMemberRefsToThisModule(module).Where(a => RefFinder.Equals(a, method)).Select(a => (a, a.Name)).ToArray();
			return new SettingsChange(() => {
				newOptions.CopyTo(method);
				foreach (var info in memberRefs)
					info.memberRef.Name = method.Name;
			}, () => {
				origOptions.CopyTo(method);
				foreach (var info in memberRefs)
					info.memberRef.Name = info.name;
			});
		}

		/// <summary>
		/// Replaces the method body, eg. with the IL edited in the method body editor or a stub
		/// </summary>
		public static SettingsChange Create(MethodDef method, IMethodAnnotations methodAnnotations, MethodBodyOptions newOptions) {
			var origMethodBody = method.MethodBody;
			bool isBodyModified = false;
			return new SettingsChange(() => {
				isBodyModified = methodAnnotations.IsBodyModified(method);
				methodAnnotations.SetBodyModified(method, true);
				newOptions.CopyTo(method);
			}, () => {
				method.MethodBody = origMethodBody;
				methodAnnotations.SetBodyModified(method, isBodyModified);
			});
		}

		public static SettingsChange Create(FieldDef field, FieldDefOptions newOptions) {
			var origOptions = new FieldDefOptions(field);
			var memberRefs = Array.Empty<(MemberRef memberRef, UTF8String name)>();
			if (origOptions.Name != newOptions.Name && field.Module is ModuleDef module)
				memberRefs = RefFinder.FindMemberRefsToThisModule(module).Where(a => RefFinder.Equals(a, field)).Select(a => (a, a.Name)).ToArray();
			return new SettingsChange(() => {
				newOptions.CopyTo(field);
				foreach (var info in memberRefs)
					info.memberRef.Name = field.Name;
			}, () => {
				origOptions.CopyTo(field);
				foreach (var info in memberRefs)
					info.memberRef.Name = info.name;
			});
		}

		public static SettingsChange Create(PropertyDef property, PropertyDefOptions newOptions) {
			var origOptions = new PropertyDefOptions(property);
			return new SettingsChange(() => newOptions.CopyTo(property), () => origOptions.CopyTo(property));
		}

		public static SettingsChange Create(EventDef @event, EventDefOptions newOptions) {
			var origOptions = new EventDefOptions(@event);
			return new SettingsChange(() => newOptions.CopyTo(@event), () => origOptions.CopyTo(@event));
		}

		/// <summary>
		/// Renames a namespace: all types in <paramref name="module"/> in namespace <paramref name="oldName"/> are moved to
		/// <paramref name="newName"/>
		/// </summary>
		public static SettingsChange RenameNamespace(ModuleDef module, string oldName, string newName) {
			var types = module.Types.Where(a => a.Namespace == oldName).ToArray();
			var origNamespaces = types.Select(a => a.Namespace).ToArray();
			UTF8String newNamespace = newName;
			return new SettingsChange(() => {
				foreach (var type in types)
					type.Namespace = newNamespace;
			}, () => {
				for (int i = 0; i < types.Length; i++)
					types[i].Namespace = origNamespaces[i];
			});
		}
	}
}

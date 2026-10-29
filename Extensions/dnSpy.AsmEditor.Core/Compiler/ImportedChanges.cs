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
using dnlib.DotNet;
using dnSpy.AsmEditor.Event;
using dnSpy.AsmEditor.Field;
using dnSpy.AsmEditor.Method;
using dnSpy.AsmEditor.Property;
using dnSpy.AsmEditor.Types;
using dnSpy.Contracts.Documents;
using Emit = dnlib.DotNet.Emit;

namespace dnSpy.AsmEditor.Compiler {
	/// <summary>
	/// Applies the result of a <see cref="ModuleImporter"/> to the edited module and can undo it.
	/// It does the same changes to the dnlib model as the WPF tree node updaters (see AddUpdatedNodesHelper)
	/// but doesn't know about any tree view, the host refreshes its UI after calling <see cref="Apply"/>
	/// or <see cref="Undo"/>.
	/// </summary>
	sealed class ImportedChanges {
		readonly ModuleDef module;
		readonly AssemblyDef? assembly;
		readonly TypeDef[] newTypes;
		readonly ExistingTypeUpdater[] existingTypeUpdaters;
		readonly Resource[] newResources;
		readonly DeclSecurity[]? newAssemblyDeclSecurities;
		readonly DeclSecurity[]? origAssemblyDeclSecurities;
		readonly CustomAttribute[]? newAssemblyCustomAttributes;
		readonly CustomAttribute[]? origAssemblyCustomAttributes;
		readonly CustomAttribute[]? newModuleCustomAttributes;
		readonly CustomAttribute[]? origModuleCustomAttributes;
		readonly ExportedType[]? newExportedTypes;
		readonly ExportedType[]? origExportedTypes;
		readonly Version? newAssemblyVersion;
		readonly Version? origAssemblyVersion;
		bool isApplied;

		public ModuleDef Module => module;

		/// <summary>
		/// The existing types that were changed (non-nested), and the new non-nested types
		/// </summary>
		public IEnumerable<TypeDef> ModifiedTypes => existingTypeUpdaters.Select(a => a.TargetType).Concat(newTypes);

		/// <summary>
		/// true if the assembly or module was changed, eg. its attributes or version
		/// </summary>
		public bool ModifiedAssemblyOrModule =>
			newAssemblyDeclSecurities is not null || newAssemblyCustomAttributes is not null || newModuleCustomAttributes is not null ||
			newExportedTypes is not null || newAssemblyVersion is not null || newResources.Length != 0;

		public ImportedChanges(ModuleImporter importer, ModuleDef module, IMethodAnnotations methodAnnotations) {
			this.module = module ?? throw new ArgumentNullException(nameof(module));
			if (methodAnnotations is null)
				throw new ArgumentNullException(nameof(methodAnnotations));
			assembly = module.Assembly;
			newTypes = importer.NewNonNestedTypes.Select(a => a.TargetType!).ToArray();
			foreach (var t in newTypes) {
				// Can't be a nested type, and can't be part of a module yet
				if (t.DeclaringType is not null || t.Module is not null)
					throw new InvalidOperationException();
			}
			var mergedTypes = importer.MergedNonNestedTypes;
			if (!mergedTypes.All(a => a.TargetType!.Module == module))
				throw new InvalidOperationException();
			existingTypeUpdaters = mergedTypes.Select(a => new ExistingTypeUpdater(methodAnnotations, a)).ToArray();
			newResources = importer.NewResources ?? Array.Empty<Resource>();

			newAssemblyDeclSecurities = importer.NewAssemblyDeclSecurities;
			newAssemblyCustomAttributes = importer.NewAssemblyCustomAttributes;
			newModuleCustomAttributes = importer.NewModuleCustomAttributes;
			newExportedTypes = importer.NewExportedTypes;
			newAssemblyVersion = importer.NewAssemblyVersion;
			if (newAssemblyDeclSecurities is not null)
				origAssemblyDeclSecurities = assembly?.DeclSecurities.ToArray();
			if (newAssemblyCustomAttributes is not null)
				origAssemblyCustomAttributes = assembly?.CustomAttributes.ToArray();
			if (newModuleCustomAttributes is not null)
				origModuleCustomAttributes = module.CustomAttributes.ToArray();
			if (newExportedTypes is not null)
				origExportedTypes = module.ExportedTypes.ToArray();
			if (newAssemblyVersion is not null)
				origAssemblyVersion = assembly?.Version;
		}

		public void Apply() {
			if (isApplied)
				throw new InvalidOperationException();
			isApplied = true;

			foreach (var type in newTypes)
				module.Types.Add(type);
			foreach (var updater in existingTypeUpdaters)
				updater.Add();
			if (origAssemblyDeclSecurities is not null && newAssemblyDeclSecurities is not null)
				Replace(assembly!.DeclSecurities, newAssemblyDeclSecurities);
			if (origAssemblyCustomAttributes is not null && newAssemblyCustomAttributes is not null)
				Replace(assembly!.CustomAttributes, newAssemblyCustomAttributes);
			if (origModuleCustomAttributes is not null && newModuleCustomAttributes is not null)
				Replace(module.CustomAttributes, newModuleCustomAttributes);
			if (origExportedTypes is not null && newExportedTypes is not null)
				Replace(module.ExportedTypes, newExportedTypes);
			if (newAssemblyVersion is not null && origAssemblyVersion is not null)
				assembly!.Version = newAssemblyVersion;
			foreach (var resource in newResources)
				module.Resources.Add(resource);
		}

		public void Undo() {
			if (!isApplied)
				throw new InvalidOperationException();
			isApplied = false;

			for (int i = newResources.Length - 1; i >= 0; i--)
				RemoveItem(module.Resources, newResources[i]);
			if (newAssemblyVersion is not null && origAssemblyVersion is not null)
				assembly!.Version = origAssemblyVersion;
			if (origExportedTypes is not null && newExportedTypes is not null)
				Replace(module.ExportedTypes, origExportedTypes);
			if (origModuleCustomAttributes is not null && newModuleCustomAttributes is not null)
				Replace(module.CustomAttributes, origModuleCustomAttributes);
			if (origAssemblyCustomAttributes is not null && newAssemblyCustomAttributes is not null)
				Replace(assembly!.CustomAttributes, origAssemblyCustomAttributes);
			if (origAssemblyDeclSecurities is not null && newAssemblyDeclSecurities is not null)
				Replace(assembly!.DeclSecurities, origAssemblyDeclSecurities);
			for (int i = existingTypeUpdaters.Length - 1; i >= 0; i--)
				existingTypeUpdaters[i].Remove();
			for (int i = newTypes.Length - 1; i >= 0; i--)
				RemoveItem(module.Types, newTypes[i]);
		}

		static void Replace<T>(IList<T> list, T[] items) {
			list.Clear();
			foreach (var item in items)
				list.Add(item);
		}

		static void RemoveItem<T>(IList<T> list, T item) {
			if (!list.Remove(item))
				throw new InvalidOperationException();
		}

		// Removes a member and remembers where it was so it can be inserted at the same index again
		sealed class DeletedMember<T> where T : class {
			readonly IList<T> list;
			readonly T member;
			int index;

			public DeletedMember(IList<T> list, T member) {
				this.list = list;
				this.member = member;
			}

			public void Add() {
				index = list.IndexOf(member);
				if (index < 0)
					throw new InvalidOperationException();
				list.RemoveAt(index);
			}

			public void Remove() => list.Insert(index, member);
		}

		sealed class ExistingTypeUpdater {
			public TypeDef TargetType { get; }

			readonly IMethodAnnotations methodAnnotations;
			readonly ModuleDef ownerModule;
			readonly TypeDefOptions origTypeDefOptions;
			readonly TypeDefOptions newTypeDefOptions;
			readonly ExistingTypeUpdater[] nestedTypeUpdaters;
			readonly TypeDef[] newNestedTypes;
			readonly FieldDef[] newFields;
			readonly MethodDef[] newMethods;
			readonly EventDef[] newEvents;
			readonly PropertyDef[] newProperties;
			readonly (FieldDef field, FieldDefOptions orig, FieldDefOptions edited)[] editedFields;
			readonly (MethodDef method, MethodState orig, MethodState edited)[] editedMethods;
			readonly (PropertyDef property, PropertyDefOptions orig, PropertyDefOptions edited)[] editedProperties;
			readonly (EventDef evt, EventDefOptions orig, EventDefOptions edited)[] editedEvents;
			readonly DeletedMember<TypeDef>[] deletedTypes;
			readonly DeletedMember<FieldDef>[] deletedFields;
			readonly DeletedMember<MethodDef>[] deletedMethods;
			readonly DeletedMember<PropertyDef>[] deletedProperties;
			readonly DeletedMember<EventDef>[] deletedEvents;

			readonly struct MethodState {
				readonly Emit.MethodBody body;
				readonly MethodDefOptions methodDefOptions;
				readonly bool isBodyModified;

				public MethodState(Emit.MethodBody body, MethodDefOptions methodDefOptions, bool isBodyModified) {
					this.body = body;
					this.methodDefOptions = methodDefOptions;
					this.isBodyModified = isBodyModified;
				}

				public void CopyTo(MethodDef method, IMethodAnnotations methodAnnotations) {
					method.MethodBody = body;
					methodDefOptions.CopyTo(method);
					methodAnnotations.SetBodyModified(method, isBodyModified);
				}
			}

			public ExistingTypeUpdater(IMethodAnnotations methodAnnotations, MergedImportedType type) {
				this.methodAnnotations = methodAnnotations;
				TargetType = type.TargetType!;
				ownerModule = TargetType.Module;
				origTypeDefOptions = new TypeDefOptions(TargetType);
				newTypeDefOptions = type.NewTypeDefOptions;
				nestedTypeUpdaters = type.NewOrExistingNestedTypes.OfType<MergedImportedType>().Select(a => new ExistingTypeUpdater(methodAnnotations, a)).ToArray();
				newNestedTypes = type.NewOrExistingNestedTypes.OfType<NewImportedType>().Select(a => a.TargetType!).ToArray();
				if (nestedTypeUpdaters.Length + newNestedTypes.Length != type.NewOrExistingNestedTypes.Count)
					throw new InvalidOperationException();
				foreach (var t in newNestedTypes) {
					if (t.Module is not null)
						throw new InvalidOperationException();
				}
				newFields = type.NewFields.ToArray();
				// The property and event accessors are added together with the properties and events
				var specialMethods = GetSpecialMethods(type);
				newMethods = type.NewMethods.Where(a => !specialMethods.Contains(a)).ToArray();
				newEvents = type.NewEvents.ToArray();
				newProperties = type.NewProperties.ToArray();
				editedFields = type.EditedFields.Select(a => (a.OriginalField, new FieldDefOptions(a.OriginalField), a.FieldDefOptions)).ToArray();
				editedMethods = type.EditedMethods.Select(a => (a.OriginalMethod,
					new MethodState(a.OriginalMethod.MethodBody, new MethodDefOptions(a.OriginalMethod), methodAnnotations.IsBodyModified(a.OriginalMethod)),
					new MethodState(a.NewBody, a.MethodDefOptions, true))).ToArray();
				editedProperties = type.EditedProperties.Select(a => (a.OriginalProperty, new PropertyDefOptions(a.OriginalProperty), a.PropertyDefOptions)).ToArray();
				editedEvents = type.EditedEvents.Select(a => (a.OriginalEvent, new EventDefOptions(a.OriginalEvent), a.EventDefOptions)).ToArray();
				deletedTypes = type.DeletedNestedTypes.Select(a => new DeletedMember<TypeDef>(a.DeclaringType.NestedTypes, a)).ToArray();
				deletedFields = type.DeletedFields.Select(a => new DeletedMember<FieldDef>(a.DeclaringType.Fields, a)).ToArray();
				deletedMethods = type.DeletedMethods.Select(a => new DeletedMember<MethodDef>(a.DeclaringType.Methods, a)).ToArray();
				deletedProperties = type.DeletedProperties.Select(a => new DeletedMember<PropertyDef>(a.DeclaringType.Properties, a)).ToArray();
				deletedEvents = type.DeletedEvents.Select(a => new DeletedMember<EventDef>(a.DeclaringType.Events, a)).ToArray();
			}

			static HashSet<MethodDef> GetSpecialMethods(MergedImportedType type) {
				var specialMethods = new HashSet<MethodDef>();
				foreach (var p in type.NewProperties)
					specialMethods.UnionWith(GetMethods(p));
				foreach (var e in type.NewEvents)
					specialMethods.UnionWith(GetMethods(e));
				return specialMethods;
			}

			static IEnumerable<MethodDef> GetMethods(PropertyDef property) =>
				property.GetMethods.Concat(property.SetMethods).Concat(property.OtherMethods);

			static IEnumerable<MethodDef> GetMethods(EventDef @event) {
				if (@event.AddMethod is not null)
					yield return @event.AddMethod;
				if (@event.RemoveMethod is not null)
					yield return @event.RemoveMethod;
				if (@event.InvokeMethod is not null)
					yield return @event.InvokeMethod;
				foreach (var m in @event.OtherMethods)
					yield return m;
			}

			public void Add() {
				newTypeDefOptions.CopyTo(TargetType, ownerModule);
				foreach (var d in deletedTypes)
					d.Add();
				foreach (var d in deletedFields)
					d.Add();
				foreach (var d in deletedMethods)
					d.Add();
				foreach (var d in deletedProperties)
					d.Add();
				foreach (var d in deletedEvents)
					d.Add();
				foreach (var u in nestedTypeUpdaters)
					u.Add();
				foreach (var t in newNestedTypes)
					TargetType.NestedTypes.Add(t);
				foreach (var f in newFields)
					TargetType.Fields.Add(f);
				foreach (var m in newMethods)
					TargetType.Methods.Add(m);
				foreach (var e in newEvents) {
					TargetType.Events.Add(e);
					TargetType.Methods.AddRange(GetMethods(e));
				}
				foreach (var p in newProperties) {
					TargetType.Properties.Add(p);
					TargetType.Methods.AddRange(GetMethods(p));
				}
				foreach (var (field, _, edited) in editedFields)
					edited.CopyTo(field);
				foreach (var (method, _, edited) in editedMethods)
					edited.CopyTo(method, methodAnnotations);
				foreach (var (property, _, edited) in editedProperties)
					edited.CopyTo(property);
				foreach (var (evt, _, edited) in editedEvents)
					edited.CopyTo(evt);
			}

			public void Remove() {
				for (int i = editedEvents.Length - 1; i >= 0; i--)
					editedEvents[i].orig.CopyTo(editedEvents[i].evt);
				for (int i = editedProperties.Length - 1; i >= 0; i--)
					editedProperties[i].orig.CopyTo(editedProperties[i].property);
				for (int i = editedMethods.Length - 1; i >= 0; i--)
					editedMethods[i].orig.CopyTo(editedMethods[i].method, methodAnnotations);
				for (int i = editedFields.Length - 1; i >= 0; i--)
					editedFields[i].orig.CopyTo(editedFields[i].field);
				for (int i = newProperties.Length - 1; i >= 0; i--) {
					foreach (var m in GetMethods(newProperties[i]))
						RemoveItem(TargetType.Methods, m);
					RemoveItem(TargetType.Properties, newProperties[i]);
				}
				for (int i = newEvents.Length - 1; i >= 0; i--) {
					foreach (var m in GetMethods(newEvents[i]))
						RemoveItem(TargetType.Methods, m);
					RemoveItem(TargetType.Events, newEvents[i]);
				}
				for (int i = newMethods.Length - 1; i >= 0; i--)
					RemoveItem(TargetType.Methods, newMethods[i]);
				for (int i = newFields.Length - 1; i >= 0; i--)
					RemoveItem(TargetType.Fields, newFields[i]);
				for (int i = newNestedTypes.Length - 1; i >= 0; i--)
					RemoveItem(TargetType.NestedTypes, newNestedTypes[i]);
				for (int i = nestedTypeUpdaters.Length - 1; i >= 0; i--)
					nestedTypeUpdaters[i].Remove();
				for (int i = deletedEvents.Length - 1; i >= 0; i--)
					deletedEvents[i].Remove();
				for (int i = deletedProperties.Length - 1; i >= 0; i--)
					deletedProperties[i].Remove();
				for (int i = deletedMethods.Length - 1; i >= 0; i--)
					deletedMethods[i].Remove();
				for (int i = deletedFields.Length - 1; i >= 0; i--)
					deletedFields[i].Remove();
				for (int i = deletedTypes.Length - 1; i >= 0; i--)
					deletedTypes[i].Remove();
				origTypeDefOptions.CopyTo(TargetType, ownerModule);
			}
		}
	}
}

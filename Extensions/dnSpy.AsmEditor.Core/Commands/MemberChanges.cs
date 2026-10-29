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
using System.Collections.Generic;
using System.Linq;
using dnlib.DotNet;
using dnSpy.AsmEditor.DnlibDialogs;
using dnSpy.AsmEditor.Event;
using dnSpy.AsmEditor.Field;
using dnSpy.AsmEditor.Method;
using dnSpy.AsmEditor.Property;
using dnSpy.AsmEditor.Types;

namespace dnSpy.AsmEditor.Commands {
	/// <summary>
	/// The metadata changes of the create and delete commands, without any UI updates, and the default options of
	/// new types and members (same as the WPF commands)
	/// </summary>
	static class MemberChanges {
		public const string DEFAULT_TYPE_NAME = "MyType";

		public static TypeDefOptions CreateTypeOptions(ModuleDef module, string ns, bool isNestedType) =>
			TypeDefOptions.Create(isNestedType ? UTF8String.Empty : ns, DEFAULT_TYPE_NAME, module.CorLibTypes.Object.TypeDefOrRef, isNestedType);

		public static MethodDefOptions CreateMethodOptions(TypeDef ownerType) {
			var module = ownerType.Module;
			bool isInstance = !(ownerType.IsAbstract && ownerType.IsSealed);
			var sig = isInstance ? MethodSig.CreateInstance(module.CorLibTypes.Void) : MethodSig.CreateStatic(module.CorLibTypes.Void);
			var options = MethodDefOptions.Create("MyMethod", sig);
			if (ownerType.IsInterface)
				options.Attributes |= MethodAttributes.Abstract | MethodAttributes.Virtual | MethodAttributes.NewSlot;
			return options;
		}

		public static FieldDefOptions CreateFieldOptions(TypeDef ownerType) {
			var module = ownerType.Module;
			FieldDefOptions options;
			if (ownerType.IsEnum) {
				var ts = ownerType.GetEnumUnderlyingType();
				if (ts is not null) {
					options = FieldDefOptions.Create("MyField", new FieldSig(new ValueTypeSig(ownerType)));
					options.Constant = module.UpdateRowId(new ConstantUser(ModelUtils.GetDefaultValue(ts), ts.RemovePinnedAndModifiers().GetElementType()));
					options.Attributes |= FieldAttributes.Literal | FieldAttributes.Static | FieldAttributes.HasDefault;
				}
				else {
					options = FieldDefOptions.Create("value__", new FieldSig(module.CorLibTypes.Int32));
					options.Attributes |= FieldAttributes.SpecialName | FieldAttributes.RTSpecialName;
				}
			}
			else if (ownerType.IsAbstract && ownerType.IsSealed) {
				options = FieldDefOptions.Create("MyField", new FieldSig(module.CorLibTypes.Int32));
				options.Attributes |= FieldAttributes.Static;
			}
			else
				options = FieldDefOptions.Create("MyField", new FieldSig(module.CorLibTypes.Int32));
			return options;
		}

		public static PropertyDefOptions CreatePropertyOptions(TypeDef ownerType) =>
			PropertyDefOptions.Create(ownerType.Module, "MyProperty", !(ownerType.IsAbstract && ownerType.IsSealed));

		public static EventDefOptions CreateEventOptions(TypeDef ownerType) =>
			EventDefOptions.Create("MyEvent", ownerType.Module.CorLibTypes.GetTypeRef("System", "EventHandler"));

		static SettingsChange Add<T>(IList<T> list, T item) =>
			new SettingsChange(() => list.Add(item), () => {
				if (!list.Remove(item))
					throw new InvalidOperationException();
			});

		/// <summary>
		/// Adds a new type to the module or, if <paramref name="declaringType"/> isn't null, to that type
		/// </summary>
		public static SettingsChange AddType(ModuleDef module, TypeDef? declaringType, TypeDef type) =>
			declaringType is null ? Add(module.Types, type) : Add(declaringType.NestedTypes, type);

		public static SettingsChange AddMethod(TypeDef ownerType, MethodDef method) => Add(ownerType.Methods, method);
		public static SettingsChange AddField(TypeDef ownerType, FieldDef field) => Add(ownerType.Fields, field);
		public static SettingsChange AddProperty(TypeDef ownerType, PropertyDef property) => Add(ownerType.Properties, property);
		public static SettingsChange AddEvent(TypeDef ownerType, EventDef @event) => Add(ownerType.Events, @event);

		/// <summary>
		/// Deletes types and members. Deleted methods are removed from their properties and events. Deleted properties
		/// and events also delete their accessor methods.
		/// </summary>
		/// <param name="defs">Types, methods, fields, properties and events</param>
		public static SettingsChange Delete(IMemberDef[] defs) {
			var undoActions = new List<Action>();
			return new SettingsChange(() => {
				undoActions.Clear();
				foreach (var def in defs)
					Delete(def, undoActions);
			}, () => {
				for (int i = undoActions.Count - 1; i >= 0; i--)
					undoActions[i]();
				undoActions.Clear();
			});
		}

		static void RemoveAt<T>(IList<T> list, T item, List<Action> undoActions) {
			int index = list.IndexOf(item);
			if (index < 0)
				return;
			list.RemoveAt(index);
			undoActions.Add(() => list.Insert(index, item));
		}

		static void Delete(IMemberDef def, List<Action> undoActions) {
			switch (def) {
			case TypeDef type:
				RemoveAt(type.DeclaringType is null ? type.Module.Types : type.DeclaringType.NestedTypes, type, undoActions);
				break;

			case MethodDef method:
				var ownerType = method.DeclaringType;
				if (ownerType is null)
					break;
				foreach (var prop in ownerType.Properties) {
					RemoveAll(prop.GetMethods, method, undoActions);
					RemoveAll(prop.SetMethods, method, undoActions);
					RemoveAll(prop.OtherMethods, method, undoActions);
				}
				foreach (var evt in ownerType.Events) {
					var e = evt;
					if (e.AddMethod == method) {
						e.AddMethod = null;
						undoActions.Add(() => e.AddMethod = method);
					}
					if (e.InvokeMethod == method) {
						e.InvokeMethod = null;
						undoActions.Add(() => e.InvokeMethod = method);
					}
					if (e.RemoveMethod == method) {
						e.RemoveMethod = null;
						undoActions.Add(() => e.RemoveMethod = method);
					}
					RemoveAll(e.OtherMethods, method, undoActions);
				}
				RemoveAt(ownerType.Methods, method, undoActions);
				break;

			case FieldDef field:
				if (field.DeclaringType is TypeDef fieldOwner)
					RemoveAt(fieldOwner.Fields, field, undoActions);
				break;

			case PropertyDef property:
				if (property.DeclaringType is TypeDef propOwner) {
					var accessors = property.GetMethods.Concat(property.SetMethods).Concat(property.OtherMethods).Distinct().ToArray();
					RemoveAt(propOwner.Properties, property, undoActions);
					foreach (var accessor in accessors)
						RemoveAt(propOwner.Methods, accessor, undoActions);
				}
				break;

			case EventDef @event:
				if (@event.DeclaringType is TypeDef eventOwner) {
					var accessors = new[] { @event.AddMethod, @event.InvokeMethod, @event.RemoveMethod }.Concat(@event.OtherMethods).OfType<MethodDef>().Distinct().ToArray();
					RemoveAt(eventOwner.Events, @event, undoActions);
					foreach (var accessor in accessors)
						RemoveAt(eventOwner.Methods, accessor, undoActions);
				}
				break;

			default:
				throw new ArgumentOutOfRangeException(nameof(def));
			}
		}

		static void RemoveAll(IList<MethodDef> list, MethodDef method, List<Action> undoActions) {
			while (true) {
				int index = list.IndexOf(method);
				if (index < 0)
					break;
				list.RemoveAt(index);
				undoActions.Add(() => list.Insert(index, method));
			}
		}

		/// <summary>
		/// Deletes all non-nested types in a namespace
		/// </summary>
		public static SettingsChange DeleteNamespace(ModuleDef module, string ns) =>
			Delete(module.Types.Where(a => a.Namespace == ns).ToArray());
	}
}

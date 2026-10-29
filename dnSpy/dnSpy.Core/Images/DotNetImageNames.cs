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
using System.Diagnostics;
using dnlib.DotNet;
using dnlib.PE;
using dnSpy.Contracts.Decompiler;
using dnSpy.Contracts.Images;

namespace dnSpy.Images {
	// Picks the image (a DsImageStrings name) of a .NET node. It's UI independent so the WPF
	// DotNetImageService and other frontends show the same images.
	static class DotNetImageNames {
		public static string GetName(Characteristics ch) {
			bool isExe = (ch & Characteristics.Dll) == 0;
			return isExe ? DsImageStrings.AssemblyExe : DsImageStrings.Assembly;
		}
		public static string GetName(AssemblyDef assembly) => GetName(assembly.ManifestModule?.Characteristics ?? Characteristics.Dll);
		public static string GetName(ModuleDef module) => DsImageStrings.ModulePublic;
		public static string GetName(TypeDef type) {
			if (type.IsValueType) {
				if (type.IsEnum) {
					switch (type.Visibility) {
					case TypeAttributes.Public:
					case TypeAttributes.NestedPublic:
						return DsImageStrings.EnumerationPublic;
					case TypeAttributes.NestedPrivate:
						return DsImageStrings.EnumerationPrivate;
					case TypeAttributes.NestedFamily:
						return DsImageStrings.EnumerationProtected;
					case TypeAttributes.NotPublic:
					case TypeAttributes.NestedAssembly:
					case TypeAttributes.NestedFamANDAssem:
						return DsImageStrings.EnumerationInternal;
					case TypeAttributes.NestedFamORAssem:
						return DsImageStrings.EnumerationShortcut;
					}
				}
				else {
					switch (type.Visibility) {
					case TypeAttributes.Public:
					case TypeAttributes.NestedPublic:
						return DsImageStrings.StructurePublic;
					case TypeAttributes.NestedPrivate:
						return DsImageStrings.StructurePrivate;
					case TypeAttributes.NestedFamily:
						return DsImageStrings.StructureProtected;
					case TypeAttributes.NotPublic:
					case TypeAttributes.NestedAssembly:
					case TypeAttributes.NestedFamANDAssem:
						return DsImageStrings.StructureInternal;
					case TypeAttributes.NestedFamORAssem:
						return DsImageStrings.StructureShortcut;
					}
				}
			}
			else {
				if (type.IsInterface) {
					switch (type.Visibility) {
					case TypeAttributes.Public:
					case TypeAttributes.NestedPublic:
						return DsImageStrings.InterfacePublic;
					case TypeAttributes.NestedPrivate:
						return DsImageStrings.InterfacePrivate;
					case TypeAttributes.NestedFamily:
						return DsImageStrings.InterfaceProtected;
					case TypeAttributes.NotPublic:
					case TypeAttributes.NestedAssembly:
					case TypeAttributes.NestedFamANDAssem:
						return DsImageStrings.InterfaceInternal;
					case TypeAttributes.NestedFamORAssem:
						return DsImageStrings.InterfaceShortcut;
					}
				}
				else if (IsDelegate(type)) {
					switch (type.Visibility) {
					case TypeAttributes.Public:
					case TypeAttributes.NestedPublic:
						return DsImageStrings.DelegatePublic;
					case TypeAttributes.NestedPrivate:
						return DsImageStrings.DelegatePrivate;
					case TypeAttributes.NestedFamily:
						return DsImageStrings.DelegateProtected;
					case TypeAttributes.NotPublic:
					case TypeAttributes.NestedAssembly:
					case TypeAttributes.NestedFamANDAssem:
						return DsImageStrings.DelegateInternal;
					case TypeAttributes.NestedFamORAssem:
						return DsImageStrings.DelegateShortcut;
					}
				}
				else if (IsException(type)) {
					switch (type.Visibility) {
					case TypeAttributes.Public:
					case TypeAttributes.NestedPublic:
						return DsImageStrings.ExceptionPublic;
					case TypeAttributes.NestedPrivate:
						return DsImageStrings.ExceptionPrivate;
					case TypeAttributes.NestedFamily:
						return DsImageStrings.ExceptionProtected;
					case TypeAttributes.NotPublic:
					case TypeAttributes.NestedAssembly:
					case TypeAttributes.NestedFamANDAssem:
						return DsImageStrings.ExceptionInternal;
					case TypeAttributes.NestedFamORAssem:
						return DsImageStrings.ExceptionShortcut;
					}
				}
				else if (IsModule(type)) {
					switch (type.Visibility) {
					case TypeAttributes.Public:
					case TypeAttributes.NestedPublic:
						return DsImageStrings.ModulePublic;
					case TypeAttributes.NestedPrivate:
						return DsImageStrings.ModulePrivate;
					case TypeAttributes.NestedFamily:
						return DsImageStrings.ModuleProtected;
					case TypeAttributes.NotPublic:
					case TypeAttributes.NestedAssembly:
					case TypeAttributes.NestedFamANDAssem:
					case TypeAttributes.NestedFamORAssem:
						return DsImageStrings.ModuleInternal;
					}
				}
				else if (type.GenericParameters.Count > 0) {
					switch (type.Visibility) {
					case TypeAttributes.Public:
					case TypeAttributes.NestedPublic:
						return DsImageStrings.Template;
					case TypeAttributes.NestedPrivate:
						return DsImageStrings.TemplatePrivate;
					case TypeAttributes.NestedFamily:
						return DsImageStrings.TemplateProtected;
					case TypeAttributes.NotPublic:
					case TypeAttributes.NestedAssembly:
					case TypeAttributes.NestedFamANDAssem:
						return DsImageStrings.TemplateInternal;
					case TypeAttributes.NestedFamORAssem:
						return DsImageStrings.TemplateShortcut;
					}
				}
				else {
					switch (type.Visibility) {
					case TypeAttributes.Public:
					case TypeAttributes.NestedPublic:
						return DsImageStrings.ClassPublic;
					case TypeAttributes.NestedPrivate:
						return DsImageStrings.ClassPrivate;
					case TypeAttributes.NestedFamily:
						return DsImageStrings.ClassProtected;
					case TypeAttributes.NotPublic:
					case TypeAttributes.NestedAssembly:
					case TypeAttributes.NestedFamANDAssem:
						return DsImageStrings.ClassInternal;
					case TypeAttributes.NestedFamORAssem:
						return DsImageStrings.ClassShortcut;
					}
				}
			}
			Debug.Fail("Impossible to get here");
			return DsImageStrings.ClassPublic;
		}

		static bool IsModule(TypeDef type) =>
			type is not null && type.DeclaringType is null && type.IsSealed && type.IsDefined(stringMicrosoftVisualBasicCompilerServices, stringStandardModuleAttribute);
		static readonly UTF8String stringMicrosoftVisualBasicCompilerServices = new UTF8String("Microsoft.VisualBasic.CompilerServices");
		static readonly UTF8String stringStandardModuleAttribute = new UTF8String("StandardModuleAttribute");

		static bool IsDelegate(TypeDef type) =>
			type.BaseType is not null && type.BaseType.FullName == "System.MulticastDelegate" && type.BaseType.DefinitionAssembly.IsCorLib();

		static bool IsException(TypeDef type) {
			TypeDef? td = type;
			if (IsSystemException(td))
				return true;
			for (int i = 0; i < 1000 && td is not null; i++) {
				if (IsSystemException(td.BaseType))
					return true;
				var bt = td.BaseType;
				td = bt?.ScopeType.ResolveTypeDef();
			}
			return false;
		}

		static bool IsSystemException(ITypeDefOrRef? type) =>
			type is not null &&
			type.DeclaringType is null &&
			type.Namespace == "System" &&
			type.Name == "Exception" &&
			type.DefinitionAssembly.IsCorLib();
		public static string GetName(FieldDef field) {
			if (field.DeclaringType is not null && field.DeclaringType.IsEnum && !field.IsSpecialName) {
				switch (field.Access) {
				default:
				case FieldAttributes.Public:
					return DsImageStrings.EnumerationItemPublic;
				case FieldAttributes.Private:
					return DsImageStrings.EnumerationItemPrivate;
				case FieldAttributes.Family:
					return DsImageStrings.EnumerationItemProtected;
				case FieldAttributes.Assembly:
				case FieldAttributes.FamANDAssem:
					return DsImageStrings.EnumerationItemInternal;
				case FieldAttributes.CompilerControlled:
					return DsImageStrings.EnumerationItemSealed;
				case FieldAttributes.FamORAssem:
					return DsImageStrings.EnumerationItemShortcut;
				}
			}

			if (field.IsLiteral || (field.IsInitOnly && IsDecimalConstant(field))) {
				switch (field.Access) {
				default:
				case FieldAttributes.Public:
					return DsImageStrings.ConstantPublic;
				case FieldAttributes.Private:
					return DsImageStrings.ConstantPrivate;
				case FieldAttributes.Family:
					return DsImageStrings.ConstantProtected;
				case FieldAttributes.Assembly:
				case FieldAttributes.FamANDAssem:
					return DsImageStrings.ConstantInternal;
				case FieldAttributes.CompilerControlled:
					return DsImageStrings.ConstantSealed;
				case FieldAttributes.FamORAssem:
					return DsImageStrings.ConstantShortcut;
				}
			}
			else {
				switch (field.Access) {
				default:
				case FieldAttributes.Public:
					return DsImageStrings.FieldPublic;
				case FieldAttributes.Private:
					return DsImageStrings.FieldPrivate;
				case FieldAttributes.Family:
					return DsImageStrings.FieldProtected;
				case FieldAttributes.Assembly:
				case FieldAttributes.FamANDAssem:
					return DsImageStrings.FieldInternal;
				case FieldAttributes.CompilerControlled:
					return DsImageStrings.FieldSealed;
				case FieldAttributes.FamORAssem:
					return DsImageStrings.FieldShortcut;
				}
			}
		}

		static bool IsSystemDecimal(TypeSig ts) => ts is not null && ts.DefinitionAssembly.IsCorLib() && ts.FullName == "System.Decimal";
		static bool IsDecimalConstant(FieldDef field) => IsSystemDecimal(field.FieldType) && field.CustomAttributes.IsDefined("System.Runtime.CompilerServices.DecimalConstantAttribute");

		public static string GetName(MethodDef method) {
			if (method.IsSpecialName && method.Name.StartsWith("op_", StringComparison.Ordinal)) {
				switch (method.Access) {
				default:
				case MethodAttributes.Public:
					return DsImageStrings.OperatorPublic;
				case MethodAttributes.Private:
					return DsImageStrings.OperatorPrivate;
				case MethodAttributes.Family:
					return DsImageStrings.OperatorProtected;
				case MethodAttributes.Assembly:
				case MethodAttributes.FamANDAssem:
					return DsImageStrings.OperatorInternal;
				case MethodAttributes.CompilerControlled:
					return DsImageStrings.OperatorSealed;
				case MethodAttributes.FamORAssem:
					return DsImageStrings.OperatorShortcut;
				}
			}

			if (method.IsStatic && method.CustomAttributes.IsDefined("System.Runtime.CompilerServices.ExtensionAttribute"))
				return DsImageStrings.ExtensionMethod;

			switch (method.Access) {
			default:
			case MethodAttributes.Public:
				return DsImageStrings.MethodPublic;
			case MethodAttributes.Private:
				return DsImageStrings.MethodPrivate;
			case MethodAttributes.Family:
				return DsImageStrings.MethodProtected;
			case MethodAttributes.Assembly:
			case MethodAttributes.FamANDAssem:
				return DsImageStrings.MethodInternal;
			case MethodAttributes.CompilerControlled:
				return DsImageStrings.MethodSealed;
			case MethodAttributes.FamORAssem:
				return DsImageStrings.MethodShortcut;
			}
		}

		public static string GetName(EventDef @event) {
			var method = @event.AddMethod ?? @event.RemoveMethod;
			if (method is null)
				return DsImageStrings.EventPublic;

			switch (method.Access) {
			default:
			case MethodAttributes.Public:
				return DsImageStrings.EventPublic;
			case MethodAttributes.Private:
				return DsImageStrings.EventPrivate;
			case MethodAttributes.Family:
				return DsImageStrings.EventProtected;
			case MethodAttributes.Assembly:
			case MethodAttributes.FamANDAssem:
				return DsImageStrings.EventInternal;
			case MethodAttributes.CompilerControlled:
				return DsImageStrings.EventSealed;
			case MethodAttributes.FamORAssem:
				return DsImageStrings.EventShortcut;
			}
		}

		public static string GetName(PropertyDef property) {
			var method = property.GetMethod ?? property.SetMethod;
			if (method is null)
				return DsImageStrings.Property;

			switch (method.Access) {
			default:
			case MethodAttributes.Public:
				return DsImageStrings.Property;
			case MethodAttributes.Private:
				return DsImageStrings.PropertyPrivate;
			case MethodAttributes.Family:
				return DsImageStrings.PropertyProtected;
			case MethodAttributes.Assembly:
			case MethodAttributes.FamANDAssem:
				return DsImageStrings.PropertyInternal;
			case MethodAttributes.CompilerControlled:
				return DsImageStrings.PropertySealed;
			case MethodAttributes.FamORAssem:
				return DsImageStrings.PropertyShortcut;
			}
		}

	}
}

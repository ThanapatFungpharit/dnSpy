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
using Avalonia.Controls;
using dnlib.DotNet;
using dnSpy.AsmEditor;
using dnSpy.AsmEditor.DnlibDialogs;
using dnSpy.AsmEditor.Properties;
using dnSpy.AsmEditor.ViewHelpers;
using dnSpy.Contracts.MVVM;
using dnSpy.Contracts.Search;

namespace dnSpy.Avalonia.AsmEditor {
	// Avalonia implementations of the helpers the view models use to show sub dialogs. The owner window is found
	// when the dialog is shown since the control isn't always in a window when the helper is created.
	abstract class ViewHelperBase {
		readonly Control? owner;
		protected ViewHelperBase(Control? owner) => this.owner = owner;
		protected Control? Owner => owner;
		protected Window? OwnerWindow => DialogUtils.GetOwnerWindow(owner);

		protected bool ShowDialog(DialogWindow dialog, object dataContext, string? title) {
			if (title is not null)
				dialog.Title = title;
			dialog.DataContext = dataContext;
			return dialog.ShowModal(OwnerWindow);
		}
	}

	sealed class EditDialog<TVM, TDialog> : ViewHelperBase, IEdit<TVM> where TVM : class where TDialog : DialogWindow, new() {
		public EditDialog(Control? owner) : base(owner) { }
		public TVM? Edit(string? title, TVM obj) => ShowDialog(new TDialog(), obj, title) ? obj : null;
	}

	sealed class EditMethodDef : ViewHelperBase, IEdit<MethodDefVM> {
		readonly ModuleDef ownerModule;

		public EditMethodDef(ModuleDef ownerModule, Control? owner)
			: base(owner) => this.ownerModule = ownerModule;

		public MethodDefVM? Edit(string? title, MethodDefVM vm) {
			var method = new DnlibTypePicker(Owner).GetDnlibType(dnSpy_AsmEditor_Core_Resources.Pick_Method, DnlibTypePickerFilter.SameModuleOnly(ownerModule, VisibleMembersFlags.MethodDef), vm.Method, ownerModule);
			if (method is null)
				return null;
			vm.Method = method;
			return vm;
		}
	}

	sealed class EditMethodOverride : ViewHelperBase, IEdit<MethodOverrideVM> {
		public EditMethodOverride(Control? owner) : base(owner) { }

		public MethodOverrideVM? Edit(string? title, MethodOverrideVM mo) {
			var method = new DnlibTypePicker(Owner).GetDnlibType<IMethodDefOrRef>(dnSpy_AsmEditor_Core_Resources.Pick_Method, new DnlibTypePickerFilter(VisibleMembersFlags.MethodDef), mo.MethodDeclaration, mo.OwnerModule);
			if (method is null)
				return null;
			mo.MethodDeclaration = method;
			return mo;
		}
	}

	sealed class TypeSigCreator : ViewHelperBase, ITypeSigCreator {
		public TypeSigCreator(Control? owner) : base(owner) { }

		public TypeSig? Create(TypeSigCreatorOptions options, TypeSig? typeSig, out bool canceled) {
			var data = new TypeSigCreatorVM(options, typeSig);
			data.TypeSig = typeSig;
			if (!ShowDialog(new TypeSigCreatorDlg(), data, null)) {
				canceled = true;
				return null;
			}
			canceled = false;
			return data.TypeSig;
		}
	}

	sealed class CreateTypeSigArray : ViewHelperBase, ICreateTypeSigArray {
		public CreateTypeSigArray(Control? owner) : base(owner) { }

		public TypeSig[]? Create(TypeSigCreatorOptions options, int? count, TypeSig[]? typeSigs) {
			var data = new CreateTypeSigArrayVM(options, count);
			if (typeSigs is not null)
				data.TypeSigCollection.AddRange(typeSigs);
			if (!ShowDialog(new CreateTypeSigArrayDlg(), data, null))
				return null;
			return data.TypeSigArray;
		}
	}

	sealed class CreateMethodPropertySig : ViewHelperBase, ICreateMethodPropertySig {
		public CreateMethodPropertySig(Control? owner) : base(owner) { }

		public MethodBaseSig? Create(MethodSigCreatorOptions options, MethodBaseSig? origSig) {
			var data = new MethodSigCreatorVM(options);
			if (origSig is MethodSig methodSig)
				data.MethodSig = methodSig;
			else if (origSig is PropertySig propertySig)
				data.PropertySig = propertySig;
			if (!ShowDialog(new MethodSigCreatorDlg(), data, null))
				return null;
			return data.MethodBaseSig;
		}
	}

	sealed class CreateConstantType : ViewHelperBase, ICreateConstantType {
		static readonly ConstantType[] DefaultConstants = new ConstantType[] {
			ConstantType.Boolean,
			ConstantType.Char,
			ConstantType.SByte,
			ConstantType.Byte,
			ConstantType.Int16,
			ConstantType.UInt16,
			ConstantType.Int32,
			ConstantType.UInt32,
			ConstantType.Int64,
			ConstantType.UInt64,
			ConstantType.Single,
			ConstantType.Double,
			ConstantType.String,
			ConstantType.Enum,
			ConstantType.Type,
			ConstantType.ObjectArray,
			ConstantType.BooleanArray,
			ConstantType.CharArray,
			ConstantType.SByteArray,
			ConstantType.ByteArray,
			ConstantType.Int16Array,
			ConstantType.UInt16Array,
			ConstantType.Int32Array,
			ConstantType.UInt32Array,
			ConstantType.Int64Array,
			ConstantType.UInt64Array,
			ConstantType.SingleArray,
			ConstantType.DoubleArray,
			ConstantType.StringArray,
			ConstantType.EnumArray,
			ConstantType.TypeArray,
		};

		public CreateConstantType(Control? owner) : base(owner) { }

		public object? Create(ModuleDef ownerModule, object? value, ConstantType[] validConstants, bool allowNullString, bool arraysCanBeNull, TypeSigCreatorOptions? options, out object? resultNoSpecialNull, out bool canceled) {
			var data = new ConstantTypeVM(ownerModule, value, validConstants ?? DefaultConstants, true, true, options);
			if (!ShowDialog(new ConstantTypeDlg(), data, null)) {
				canceled = true;
				resultNoSpecialNull = null;
				return null;
			}
			canceled = false;
			resultNoSpecialNull = data.ValueNoSpecialNull;
			return data.Value;
		}
	}

	sealed class ShowWarningMessage : ViewHelperBase, IShowWarningMessage {
		public ShowWarningMessage(Control? owner) : base(owner) { }

		// The guid is used by WPF to let the user hide the message, it's always shown here
		public void Show(Guid? guid, string msg) => DialogUtils.ShowMessage(Owner, msg);
	}

	sealed class OpenPublicKeyFile : ViewHelperBase, IOpenPublicKeyFile {
		public OpenPublicKeyFile(Control? owner) : base(owner) { }

		public PublicKey? Open() {
			var filename = DialogUtils.PickOpenFilename(Owner, PickFilenameConstants.StrongNameKeyFilter);
			if (string.IsNullOrEmpty(filename))
				return null;
			try {
				var snk = new StrongNameKey(filename);
				return new PublicKey(snk.PublicKey);
			}
			catch {
			}
			try {
				var snk = new StrongNamePublicKey(filename);
				return new PublicKey(snk.CreatePublicKey());
			}
			catch {
			}
			DialogUtils.ShowMessage(Owner, string.Format(dnSpy_AsmEditor_Core_Resources.Error_NotSNKFile, filename));
			return null;
		}
	}

	sealed class OpenFile : ViewHelperBase, IOpenFile {
		public OpenFile(Control? owner) : base(owner) { }

		public byte[]? Open(string? filter) {
			var filename = DialogUtils.PickOpenFilename(Owner, filter);
			if (string.IsNullOrEmpty(filename))
				return null;
			try {
				return System.IO.File.ReadAllBytes(filename);
			}
			catch (Exception ex) {
				DialogUtils.ShowMessage(Owner, string.Format(dnSpy_AsmEditor_Core_Resources.Error_OpenFile, ex.Message));
				return null;
			}
		}
	}

	sealed class PickSaveFilename : ViewHelperBase, IPickSaveFilename {
		public PickSaveFilename(Control? owner) : base(owner) { }

		public string? GetFilename(string? currentFileName, string? defaultExtension, string? filter = null) =>
			DialogUtils.PickSaveFilename(Owner, currentFileName, defaultExtension, filter);
	}
}

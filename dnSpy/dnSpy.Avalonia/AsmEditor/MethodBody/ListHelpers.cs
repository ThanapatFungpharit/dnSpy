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
using System.Collections.Specialized;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using dnlib.DotNet;
using dnSpy.AsmEditor.DnlibDialogs;
using dnSpy.AsmEditor.MethodBody;
using dnSpy.AsmEditor.Properties;
using dnSpy.AsmEditor.ViewHelpers;
using dnSpy.Contracts.MVVM;
using dnSpy.Contracts.Search;

namespace dnSpy.Avalonia.AsmEditor {
	sealed class InstructionsListHelper : ListBoxHelper<InstructionVM>, IEditOperand, ISelectItems<InstructionVM> {
		CilBodyVM cilBodyVM = null!;

		protected override string AddNewBeforeSelectionMessage => dnSpy_AsmEditor_Core_Resources.Instr_Command1;
		protected override string AddNewAfterSelectionMessage => dnSpy_AsmEditor_Core_Resources.Instr_Command2;
		protected override string AppendNewMessage => dnSpy_AsmEditor_Core_Resources.Instr_Command3;
		protected override string RemoveSingularMessage => dnSpy_AsmEditor_Core_Resources.Instr_Command4;
		protected override string RemovePluralMessage => dnSpy_AsmEditor_Core_Resources.Instr_Command5;
		protected override string RemoveAllMessage => dnSpy_AsmEditor_Core_Resources.Instr_Command6;

		public InstructionsListHelper(ListBox listBox) : base(listBox) { }

		protected override void OnDataContextChangedInternal(object dataContext) {
			cilBodyVM = ((MethodBodyVM)dataContext).CilBodyVM;
			cilBodyVM.SelectItems = this;
			coll = cilBodyVM.InstructionsListVM;
			coll.CollectionChanged += coll_CollectionChanged;
			InitializeInstructions(coll);

			Add(new ListCommand(dnSpy_AsmEditor_Core_Resources.NopInstrCommand, dnSpy_AsmEditor_Core_Resources.NopInstrsCommand, cilBodyVM.ReplaceInstructionWithNopCommand, Key.N));
			Add(new ListCommand(dnSpy_AsmEditor_Core_Resources.ReplaceNopsInstrCommand, dnSpy_AsmEditor_Core_Resources.ReplaceNopsInstrsCommand, cilBodyVM.ReplaceInstructionWithMultipleNopsCommand, Key.R));
			Add(new ListCommand(dnSpy_AsmEditor_Core_Resources.InvertBranchCommand, dnSpy_AsmEditor_Core_Resources.InvertBranchesCommand, cilBodyVM.InvertBranchCommand, Key.I));
			Add(new ListCommand(dnSpy_AsmEditor_Core_Resources.ConvertUncondBranchCommand, dnSpy_AsmEditor_Core_Resources.ConvertUncondBranchesCommand, cilBodyVM.ConvertBranchToUnconditionalBranchCommand, Key.B));
			Add(new ListCommand(dnSpy_AsmEditor_Core_Resources.RemoveAndAddPopsCommand, null, cilBodyVM.RemoveInstructionAndAddPopsCommand, Key.P));
			AddSeparator();
			Add(new ListCommand(dnSpy_AsmEditor_Core_Resources.SimplifyAllInstructionsCommand, null, cilBodyVM.SimplifyAllInstructionsCommand, Key.S));
			Add(new ListCommand(dnSpy_AsmEditor_Core_Resources.OptimizeAllInstructionsCommand, null, cilBodyVM.OptimizeAllInstructionsCommand, Key.O));
			AddSeparator();
			AddStandardMenuHandlers();
			Add(new ListCommand(dnSpy_AsmEditor_Core_Resources.CopyMetaDataToken, dnSpy_AsmEditor_Core_Resources.CopyMetaDataTokens,
				new RelayCommand(a => SetClipboardText(MethodBodyListUtils.OperandMDTokensToText((InstructionVM[])a!)), a => ((InstructionVM[])a!).Any(b => MethodBodyListUtils.GetOperandMDToken(b.InstructionOperandVM) is not null)),
				Key.M, KeyModifiers.Control));
			Add(new ListCommand(dnSpy_AsmEditor_Core_Resources.CopyRVACommand, dnSpy_AsmEditor_Core_Resources.CopyRVAsCommand,
				new RelayCommand(a => SetClipboardText(MethodBodyListUtils.InstructionOffsetsToText(cilBodyVM.RVA.Value, (InstructionVM[])a!)), a => !cilBodyVM.RVA.HasError && ((InstructionVM[])a!).Length > 0),
				Key.R, KeyModifiers.Control));
			Add(new ListCommand(dnSpy_AsmEditor_Core_Resources.CopyFileOffsetCommand, dnSpy_AsmEditor_Core_Resources.CopyFileOffsetsCommand,
				new RelayCommand(a => SetClipboardText(MethodBodyListUtils.InstructionOffsetsToText(cilBodyVM.FileOffset.Value, (InstructionVM[])a!)), a => !cilBodyVM.FileOffset.HasError && ((InstructionVM[])a!).Length > 0),
				Key.F, KeyModifiers.Control));
		}

		protected override string GetItemsAsText(InstructionVM[] items) => MethodBodyListUtils.InstructionsToText(items);

		void coll_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) {
			if (e.NewItems is not null)
				InitializeInstructions(e.NewItems);
		}

		void InitializeInstructions(System.Collections.IList list) {
			foreach (InstructionVM? instr in list)
				instr!.InstructionOperandVM.EditOperand = this;
		}

		[Flags]
		enum MenuCommandFlags {
			FieldDef		= 0x00000001,
			FieldMemberRef	= 0x00000002,
			MethodDef		= 0x00000004,
			MethodMemberRef	= 0x00000008,
			MethodSpec		= 0x00000010,
			TypeDef			= 0x00000020,
			TypeRef			= 0x00000040,
			TypeSpec		= 0x00000080,
		}

		void ShowMenu(object? parameter, InstructionOperandVM opvm, MenuCommandFlags flags) {
			var items = new List<MenuItem>();
			void AddItem(string header, Action action) {
				var menuItem = new MenuItem { Header = header };
				menuItem.Click += (s, e) => action();
				items.Add(menuItem);
			}
			if ((flags & (MenuCommandFlags.TypeDef | MenuCommandFlags.TypeRef)) != 0)
				AddItem(dnSpy_AsmEditor_Core_Resources.EditOperand_Type, () => AddType(opvm));
			if ((flags & MenuCommandFlags.TypeSpec) != 0)
				AddItem(dnSpy_AsmEditor_Core_Resources.EditOperand_TypeSpec, () => AddTypeSpec(opvm));
			if ((flags & MenuCommandFlags.MethodDef) != 0)
				AddItem(dnSpy_AsmEditor_Core_Resources.EditOperand_Method, () => AddMethodDef(opvm));
			if ((flags & MenuCommandFlags.MethodMemberRef) != 0)
				AddItem(dnSpy_AsmEditor_Core_Resources.EditOperand_Method_MemberRef, () => AddMethodMemberRef(opvm));
			if ((flags & MenuCommandFlags.MethodSpec) != 0)
				AddItem(dnSpy_AsmEditor_Core_Resources.EditOperand_MethodSpec, () => AddMethodSpec(opvm));
			if ((flags & MenuCommandFlags.FieldDef) != 0)
				AddItem(dnSpy_AsmEditor_Core_Resources.EditOperand_Field, () => AddFieldDef(opvm));
			if ((flags & MenuCommandFlags.FieldMemberRef) != 0)
				AddItem(dnSpy_AsmEditor_Core_Resources.EditOperand_Field_MemberRef, () => AddFieldMemberRef(opvm));

			var contextMenu = new ContextMenu { ItemsSource = items, Placement = PlacementMode.Bottom };
			LastOperandMenu = contextMenu;
			contextMenu.Open(parameter as Control ?? listBox);
		}

		// Used by the smoke test
		internal static ContextMenu? LastOperandMenu { get; private set; }

		void AddFieldDef(InstructionOperandVM opvm) {
			var op = opvm.Other as IField ?? (object?)cilBodyVM.TypeSigCreatorOptions.OwnerType;
			if (new DnlibTypePicker(listBox).GetDnlibType(dnSpy_AsmEditor_Core_Resources.Pick_Field, new DnlibTypePickerFilter(VisibleMembersFlags.FieldDef), op, cilBodyVM.OwnerModule) is IField field)
				opvm.Other = field;
		}

		void AddFieldMemberRef(InstructionOperandVM opvm) {
			MemberRef? mr = opvm.Other as MemberRef;
			if (opvm.Other is FieldDef fd)
				mr = cilBodyVM.OwnerModule.Import(fd);
			if (mr is not null && mr.FieldSig is null)
				mr = null;
			AddMemberRef(opvm, mr, true);
		}

		void AddMethodDef(InstructionOperandVM opvm) {
			var op = opvm.Other as IMethod ?? (object?)cilBodyVM.TypeSigCreatorOptions.OwnerType;
			if (new DnlibTypePicker(listBox).GetDnlibType(dnSpy_AsmEditor_Core_Resources.Pick_Method, new DnlibTypePickerFilter(VisibleMembersFlags.MethodDef), op, cilBodyVM.OwnerModule) is IMethod method)
				opvm.Other = method;
		}

		void AddMethodMemberRef(InstructionOperandVM opvm) {
			MemberRef? mr = opvm.Other as MemberRef;
			var md = opvm.Other as MethodDef;
			if (opvm.Other is MethodSpec ms) {
				mr = ms.Method as MemberRef;
				md = ms.Method as MethodDef;
			}
			if (md is not null)
				mr = cilBodyVM.OwnerModule.Import(md);
			if (mr is not null && mr.MethodSig is null)
				mr = null;
			AddMemberRef(opvm, mr, false);
		}

		void AddMemberRef(InstructionOperandVM opvm, MemberRef? mr, bool isField) {
			var opts = mr is null ? new MemberRefOptions() : new MemberRefOptions(mr);
			MemberRefVM? vm = new MemberRefVM(opts, cilBodyVM.TypeSigCreatorOptions, isField);
			var title = isField ? dnSpy_AsmEditor_Core_Resources.EditFieldMemberRef : dnSpy_AsmEditor_Core_Resources.EditMethodMemberRef;
			vm = new EditDialog<MemberRefVM, MemberRefDlg>(listBox).Edit(title, vm);
			if (vm is null)
				return;
			opvm.Other = vm.CreateMemberRefOptions().Create(cilBodyVM.OwnerModule);
		}

		void AddMethodSpec(InstructionOperandVM opvm) {
			var ms = opvm.Other as MethodSpec;
			var opts = ms is null ? new MethodSpecOptions() : new MethodSpecOptions(ms);
			MethodSpecVM? vm = new MethodSpecVM(opts, cilBodyVM.TypeSigCreatorOptions);
			vm = new EditDialog<MethodSpecVM, MethodSpecDlg>(listBox).Edit(dnSpy_AsmEditor_Core_Resources.EditMethodSpec, vm);
			if (vm is null)
				return;
			opvm.Other = vm.CreateMethodSpecOptions().Create(cilBodyVM.OwnerModule);
		}

		void AddType(InstructionOperandVM opvm) {
			var op = opvm.Other as ITypeDefOrRef ?? (object?)cilBodyVM.TypeSigCreatorOptions.OwnerType;
			if (new DnlibTypePicker(listBox).GetDnlibType(dnSpy_AsmEditor_Core_Resources.Pick_Type, new DnlibTypePickerFilter(VisibleMembersFlags.TypeDef), op, cilBodyVM.OwnerModule) is ITypeDefOrRef type)
				opvm.Other = type;
		}

		void AddTypeSpec(InstructionOperandVM opvm) {
			var opts = cilBodyVM.TypeSigCreatorOptions.Clone(dnSpy_AsmEditor_Core_Resources.CreateTypeSpec);
			var newSig = new TypeSigCreator(listBox).Create(opts, (opvm.Other as ITypeDefOrRef).ToTypeSig(), out bool canceled);
			if (canceled)
				return;
			opvm.Other = newSig.ToTypeDefOrRef();
		}

		void EditMethodSig(InstructionOperandVM opvm) {
			var opts = new MethodSigCreatorOptions(cilBodyVM.TypeSigCreatorOptions.Clone(dnSpy_AsmEditor_Core_Resources.CreateMethodSig));
			opts.CanHaveSentinel = true;
			var sig = (MethodSig?)new CreateMethodPropertySig(listBox).Create(opts, opvm.Other as MethodSig);
			if (sig is not null)
				opvm.Other = sig;
		}

		void EditSwitchOperand(InstructionOperandVM opvm) {
			var data = new SwitchOperandVM(cilBodyVM.InstructionsListVM, opvm.Other as InstructionVM[]);
			var dialog = new SwitchOperandDlg { DataContext = data };
			if (!dialog.ShowModal(DialogUtils.GetOwnerWindow(listBox)))
				return;
			opvm.Other = data.GetSwitchList();
		}

		void IEditOperand.Edit(object? parameter, InstructionOperandVM opvm) {
			switch (opvm.InstructionOperandType) {
			case InstructionOperandType.Field:
				ShowMenu(parameter, opvm, MenuCommandFlags.FieldDef | MenuCommandFlags.FieldMemberRef);
				break;

			case InstructionOperandType.Method:
				ShowMenu(parameter, opvm, MenuCommandFlags.MethodDef | MenuCommandFlags.MethodMemberRef | MenuCommandFlags.MethodSpec);
				break;

			case InstructionOperandType.Token:
				ShowMenu(parameter, opvm, MenuCommandFlags.FieldDef | MenuCommandFlags.FieldMemberRef |
						MenuCommandFlags.MethodDef | MenuCommandFlags.MethodMemberRef | MenuCommandFlags.MethodSpec |
						MenuCommandFlags.TypeDef | MenuCommandFlags.TypeRef | MenuCommandFlags.TypeSpec);
				break;

			case InstructionOperandType.Type:
				ShowMenu(parameter, opvm, MenuCommandFlags.TypeDef | MenuCommandFlags.TypeRef | MenuCommandFlags.TypeSpec);
				break;

			case InstructionOperandType.MethodSig:
				EditMethodSig(opvm);
				break;

			case InstructionOperandType.SwitchTargets:
				EditSwitchOperand(opvm);
				break;

			default:
				throw new InvalidOperationException();
			}
		}

		public void Select(IEnumerable<InstructionVM> items) {
			var instrs = items.ToArray();
			if (instrs.Length == 0 || listBox.SelectedItems is null)
				return;
			listBox.SelectedItems.Clear();
			foreach (var instr in instrs)
				listBox.SelectedItems.Add(instr);
			// Select the last one because the selected item is usually the last visible item in the view
			listBox.ScrollIntoView(instrs[instrs.Length - 1]);
		}

		protected override bool CanUseClipboardData(InstructionVM[] data, bool fromThisInstance) => true;

		protected override InstructionVM[] BeforeCopyingData(InstructionVM[] data, bool fromThisInstance) =>
			MethodBodyListUtils.ImportInstructions(cilBodyVM, data, fromThisInstance);

		protected override void AfterCopyingData(InstructionVM[] data, InstructionVM[] origData, bool fromThisInstance) =>
			MethodBodyListUtils.FixPastedInstructions(cilBodyVM, data, origData, fromThisInstance);
	}

	sealed class LocalsListHelper : ListBoxHelper<LocalVM> {
		CilBodyVM cilBodyVM = null!;
		readonly TypeSigCreator typeSigCreator;

		protected override string AddNewBeforeSelectionMessage => dnSpy_AsmEditor_Core_Resources.Local_Command1;
		protected override string AddNewAfterSelectionMessage => dnSpy_AsmEditor_Core_Resources.Local_Command2;
		protected override string AppendNewMessage => dnSpy_AsmEditor_Core_Resources.Local_Command3;
		protected override string RemoveSingularMessage => dnSpy_AsmEditor_Core_Resources.Local_Command4;
		protected override string RemovePluralMessage => dnSpy_AsmEditor_Core_Resources.Local_Command5;
		protected override string RemoveAllMessage => dnSpy_AsmEditor_Core_Resources.Local_Command6;

		public LocalsListHelper(ListBox listBox) : base(listBox) => typeSigCreator = new TypeSigCreator(listBox);

		protected override void OnDataContextChangedInternal(object dataContext) {
			cilBodyVM = ((MethodBodyVM)dataContext).CilBodyVM;
			coll = cilBodyVM.LocalsListVM;
			coll.CollectionChanged += (s, e) => {
				if (e.NewItems is not null)
					InitializeLocals(e.NewItems);
			};
			InitializeLocals(coll);
			AddStandardMenuHandlers();
		}

		void InitializeLocals(System.Collections.IList list) {
			foreach (LocalVM? local in list)
				local!.TypeSigCreator = typeSigCreator;
		}

		protected override string GetItemsAsText(LocalVM[] items) => MethodBodyListUtils.LocalsToText(items);
		protected override bool CanUseClipboardData(LocalVM[] data, bool fromThisInstance) => true;
		protected override LocalVM[] BeforeCopyingData(LocalVM[] data, bool fromThisInstance) => MethodBodyListUtils.ImportLocals(cilBodyVM, data, fromThisInstance);
	}

	sealed class ExceptionHandlersListHelper : ListBoxHelper<ExceptionHandlerVM> {
		readonly TypeSigCreator typeSigCreator;

		protected override string AddNewBeforeSelectionMessage => dnSpy_AsmEditor_Core_Resources.EH_Command1;
		protected override string AddNewAfterSelectionMessage => dnSpy_AsmEditor_Core_Resources.EH_Command2;
		protected override string AppendNewMessage => dnSpy_AsmEditor_Core_Resources.EH_Command3;
		protected override string RemoveSingularMessage => dnSpy_AsmEditor_Core_Resources.EH_Command4;
		protected override string RemovePluralMessage => dnSpy_AsmEditor_Core_Resources.EH_Command5;
		protected override string RemoveAllMessage => dnSpy_AsmEditor_Core_Resources.EH_Command6;

		public ExceptionHandlersListHelper(ListBox listBox) : base(listBox) => typeSigCreator = new TypeSigCreator(listBox);

		protected override void OnDataContextChangedInternal(object dataContext) {
			coll = ((MethodBodyVM)dataContext).CilBodyVM.ExceptionHandlersListVM;
			coll.CollectionChanged += (s, e) => {
				if (e.NewItems is not null)
					InitializeExceptionHandlers(e.NewItems);
			};
			InitializeExceptionHandlers(coll);
			AddStandardMenuHandlers();
			Add(new ListCommand(dnSpy_AsmEditor_Core_Resources.CopyMetaDataToken, dnSpy_AsmEditor_Core_Resources.CopyMetaDataTokens,
				new RelayCommand(a => SetClipboardText(MethodBodyListUtils.CatchTypeMDTokensToText((ExceptionHandlerVM[])a!)), a => ((ExceptionHandlerVM[])a!).Any(b => MethodBodyListUtils.GetCatchTypeToken(b.CatchType) is not null)),
				Key.M, KeyModifiers.Control));
		}

		void InitializeExceptionHandlers(System.Collections.IList list) {
			foreach (ExceptionHandlerVM? eh in list)
				eh!.TypeSigCreator = typeSigCreator;
		}

		protected override string GetItemsAsText(ExceptionHandlerVM[] items) => MethodBodyListUtils.ExceptionHandlersToText(items);
	}

	sealed class SwitchOperandsListHelper : ListBoxHelper<SwitchInstructionVM> {
		protected override string AddNewBeforeSelectionMessage => dnSpy_AsmEditor_Core_Resources.Instr_Command1;
		protected override string AddNewAfterSelectionMessage => dnSpy_AsmEditor_Core_Resources.Instr_Command2;
		protected override string AppendNewMessage => dnSpy_AsmEditor_Core_Resources.Instr_Command3;
		protected override string RemoveSingularMessage => dnSpy_AsmEditor_Core_Resources.Instr_Command4;
		protected override string RemovePluralMessage => dnSpy_AsmEditor_Core_Resources.Instr_Command5;
		protected override string RemoveAllMessage => dnSpy_AsmEditor_Core_Resources.Instr_Command6;

		public SwitchOperandsListHelper(ListBox listBox) : base(listBox) { }

		protected override void OnDataContextChangedInternal(object dataContext) {
			coll = ((SwitchOperandVM)dataContext).InstructionsListVM;
			AddStandardMenuHandlers();
		}

		protected override string GetItemsAsText(SwitchInstructionVM[] items) => MethodBodyListUtils.InstructionsToText(items.Select(a => a.InstructionVM).ToArray());
	}
}

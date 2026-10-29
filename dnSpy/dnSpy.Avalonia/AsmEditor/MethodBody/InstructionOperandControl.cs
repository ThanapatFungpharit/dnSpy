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
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using dnSpy.AsmEditor.MethodBody;
using dnSpy.Avalonia.Controls;

namespace dnSpy.Avalonia.AsmEditor {
	// Edits an instruction's operand: a text box (numbers, strings), a button that shows a picker or a menu
	// (fields, methods, types, signatures, switch targets) or a combo box (branch targets, locals, parameters)
	sealed class InstructionOperandControl : ContentControl {
		public static readonly StyledProperty<InstructionOperandVM?> InstructionOperandVMProperty =
			AvaloniaProperty.Register<InstructionOperandControl, InstructionOperandVM?>(nameof(InstructionOperandVM));

		public InstructionOperandVM? InstructionOperandVM {
			get => GetValue(InstructionOperandVMProperty);
			set => SetValue(InstructionOperandVMProperty, value);
		}

		public static readonly IDataTemplate CilObjectTemplate = new FuncDataTemplate<object?>((value, scope) => {
			var textBlock = new ColoredTextBlock { TextTrimming = global::Avalonia.Media.TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
			textBlock.Bind(ColoredTextBlock.ColoredTextProperty, new Binding { Converter = CilObjectConverter.Instance });
			return textBlock;
		});

		protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
			base.OnPropertyChanged(change);
			if (change.Property == InstructionOperandVMProperty) {
				if (change.OldValue is InstructionOperandVM oldValue)
					oldValue.PropertyChanged -= InstructionOperandVM_PropertyChanged;
				Content = null;
				if (change.NewValue is InstructionOperandVM newValue) {
					newValue.PropertyChanged += InstructionOperandVM_PropertyChanged;
					InitializeOperandType(newValue);
				}
			}
		}

		void InstructionOperandVM_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
			if (e.PropertyName == nameof(dnSpy.AsmEditor.MethodBody.InstructionOperandVM.InstructionOperandType))
				InitializeOperandType((InstructionOperandVM)sender!);
		}

		void InitializeOperandType(InstructionOperandVM vm) {
			switch (vm.InstructionOperandType) {
			case InstructionOperandType.None:
				Content = null;
				break;

			case InstructionOperandType.SByte:
			case InstructionOperandType.Byte:
			case InstructionOperandType.Int32:
			case InstructionOperandType.Int64:
			case InstructionOperandType.Single:
			case InstructionOperandType.Double:
			case InstructionOperandType.String:
				if (Content is not TextBox textBox) {
					textBox = new TextBox { Classes = { "cilOperand" } };
					textBox.Bind(TextBox.TextProperty, new Binding("Text.StringValue") { Source = vm, Mode = BindingMode.TwoWay });
					textBox.Bind(DataErrorInfoHelper.SourceProperty, new Binding("Text") { Source = vm });
					DataErrorInfoHelper.SetProperty(textBox, "StringValue");
					Content = textBox;
				}
				textBox.Classes.Set("cilString", vm.StringIsSelected);
				break;

			case InstructionOperandType.Field:
			case InstructionOperandType.Method:
			case InstructionOperandType.Token:
			case InstructionOperandType.Type:
			case InstructionOperandType.MethodSig:
			case InstructionOperandType.SwitchTargets:
				if (Content is not Button) {
					var button = new Button {
						Classes = { "cilOperand" },
						HorizontalAlignment = HorizontalAlignment.Stretch,
						HorizontalContentAlignment = HorizontalAlignment.Left,
						Command = vm.EditOtherCommand,
						// Like WPF's FastClickButton: the list item would otherwise get the first click
						ClickMode = ClickMode.Press,
					};
					button.CommandParameter = button;
					var textBlock = new ColoredTextBlock { TextTrimming = global::Avalonia.Media.TextTrimming.CharacterEllipsis };
					textBlock.Bind(ColoredTextBlock.ColoredTextProperty, new Binding(nameof(vm.Other)) { Source = vm, Converter = CilObjectConverter.Instance });
					button.Content = textBlock;
					button.Bind(DataErrorInfoHelper.SourceProperty, new Binding { Source = vm });
					DataErrorInfoHelper.SetProperty(button, nameof(vm.Other));
					Content = button;
				}
				break;

			case InstructionOperandType.BranchTarget:
			case InstructionOperandType.Local:
			case InstructionOperandType.Parameter:
				if (Content is not ComboBox) {
					var comboBox = new ComboBox {
						Classes = { "cilOperand" },
						HorizontalAlignment = HorizontalAlignment.Stretch,
						ItemTemplate = CilObjectTemplate,
					};
					comboBox.Bind(ItemsControl.ItemsSourceProperty, new Binding("OperandListVM.Items") { Source = vm });
					comboBox.Bind(global::Avalonia.Controls.Primitives.SelectingItemsControl.SelectedIndexProperty, new Binding("OperandListVM.SelectedIndex") { Source = vm, Mode = BindingMode.TwoWay });
					comboBox.Bind(DataErrorInfoHelper.SourceProperty, new Binding("OperandListVM") { Source = vm });
					DataErrorInfoHelper.SetProperty(comboBox, "SelectedIndex");
					Content = comboBox;
				}
				break;

			default:
				throw new InvalidOperationException();
			}
		}
	}
}

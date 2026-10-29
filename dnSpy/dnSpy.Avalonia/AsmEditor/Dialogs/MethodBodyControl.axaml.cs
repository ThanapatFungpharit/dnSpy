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

using Avalonia.Controls;
using Avalonia.Input;
using dnSpy.AsmEditor.MethodBody;

namespace dnSpy.Avalonia.AsmEditor {
	sealed partial class MethodBodyControl : UserControl {
		InstructionsListHelper? instructionsListHelper;
		LocalsListHelper? localsListHelper;
		ExceptionHandlersListHelper? exceptionHandlersListHelper;

		public MethodBodyControl() {
			InitializeComponent();
			DataContextChanged += MethodBodyControl_DataContextChanged;
			Loaded += (s, e) => {
				if (DataContext is MethodBodyVM data) {
					if (data.IsCilBody)
						instructionsListBox.Focus(NavigationMethod.Tab);
					else
						rvaTextBox.Focus(NavigationMethod.Tab);
				}
			};
		}

		// Used by the smoke test
		internal ListBox InstructionsListBox => instructionsListBox;
		internal ListBox LocalsListBox => localsListBox;
		internal ListBox ExceptionHandlersListBox => ehListBox;
		internal TabControl TabControl => tabControl;

		void MethodBodyControl_DataContextChanged(object? sender, System.EventArgs e) {
			if (DataContext is not MethodBodyVM data || instructionsListHelper is not null)
				return;
			instructionsListHelper = new InstructionsListHelper(instructionsListBox);
			localsListHelper = new LocalsListHelper(localsListBox);
			exceptionHandlersListHelper = new ExceptionHandlersListHelper(ehListBox);
			instructionsListHelper.OnDataContextChanged(data);
			localsListHelper.OnDataContextChanged(data);
			exceptionHandlersListHelper.OnDataContextChanged(data);
		}
	}
}

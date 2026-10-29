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
using dnSpy.AsmEditor.Resources;

namespace dnSpy.Avalonia.AsmEditor {
	sealed partial class ResourceElementControl : UserControl {
		public ResourceElementControl() {
			InitializeComponent();
			DataContextChanged += (s, e) => {
				if (DataContext is ResourceElementVM data) {
					data.OpenFile = new OpenFile(this);
					data.DnlibTypePicker = new DnlibTypePicker(this);
				}
			};
			Loaded += (s, e) => {
				var vm = DataContext as ResourceElementVM;
				if (vm is not null && !string.IsNullOrEmpty(vm.Name) && vm.IsSingleLineValue)
					valueTextBox.Focus(NavigationMethod.Tab);
				else if (vm is not null && !string.IsNullOrEmpty(vm.Name) && vm.IsMultiLineValue)
					multiLineTextBox.Focus(NavigationMethod.Tab);
				else
					nameTextBox.Focus(NavigationMethod.Tab);
			};
		}
	}
}

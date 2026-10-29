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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using dnSpy.AsmEditor.DnlibDialogs;

namespace dnSpy.Avalonia.AsmEditor {
	// Edits a ListVM<TVM, TModel>. The items are shown with ItemTemplate, by default their FullName.
	sealed partial class ListVMControl : UserControl {
		public static readonly StyledProperty<IDataTemplate?> ItemTemplateProperty =
			AvaloniaProperty.Register<ListVMControl, IDataTemplate?>(nameof(ItemTemplate));

		public IDataTemplate? ItemTemplate {
			get => GetValue(ItemTemplateProperty);
			set => SetValue(ItemTemplateProperty, value);
		}

		static readonly IDataTemplate defaultItemTemplate = new FuncDataTemplate<object>((item, scope) => {
			var textBlock = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
			textBlock.Bind(TextBlock.TextProperty, new global::Avalonia.Data.Binding("FullName"));
			textBlock.Bind(ToolTip.TipProperty, new global::Avalonia.Data.Binding("FullName"));
			return textBlock;
		});

		public ListVMControl() {
			InitializeComponent();
			listBox.ItemTemplate = defaultItemTemplate;
		}

		protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
			base.OnPropertyChanged(change);
			if (change.Property == ItemTemplateProperty)
				listBox.ItemTemplate = ItemTemplate ?? defaultItemTemplate;
		}

		void listBox_DoubleTapped(object? sender, TappedEventArgs e) {
			if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is null)
				return;
			if (DataContext is ListVM data)
				data.EditItem();
		}
	}
}

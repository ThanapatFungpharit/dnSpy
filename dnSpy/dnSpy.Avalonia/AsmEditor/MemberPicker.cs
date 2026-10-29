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
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using dnlib.DotNet;
using dnSpy.AsmEditor.Properties;
using dnSpy.AsmEditor.ViewHelpers;
using dnSpy.Avalonia.Images;
using dnSpy.Contracts.Documents;
using dnSpy.Contracts.Search;
using dnSpy.Images;

namespace dnSpy.Avalonia.AsmEditor {
	sealed class DnlibTypePicker : ViewHelperBase, IDnlibTypePicker {
		// Set by the main window
		internal static IDsDocumentService? DocumentService { get; set; }

		public DnlibTypePicker(Control? owner) : base(owner) { }

		public T? GetDnlibType<T>(string title, DnlibTypePickerFilter filter, T? selectedObject, ModuleDef ownerModule) where T : class {
			if (DocumentService is null)
				return null;
			var dialog = new MemberPickerDlg(DocumentService, filter, selectedObject) { Title = title };
			if (!dialog.ShowModal(OwnerWindow))
				return null;
			return DnlibTypePickerUtils.ImportObject(ownerModule, dialog.SelectedObject) as T;
		}
	}

	// Lets the user pick a type, member, module etc. Everything that can be picked is listed and filtered by
	// the search text.
	sealed class MemberPickerDlg : DialogWindow {
		const int MAX_ITEMS = 2000;

		sealed class Item {
			public object Object { get; }
			public string Text { get; }
			public string Location { get; }
			public Bitmap? Icon { get; }

			public Item(object obj, string text, string location, string iconName) {
				Object = obj;
				Text = text;
				Location = location;
				Icon = ImageCache.Get(iconName);
			}
		}

		readonly List<Item> allItems;
		readonly TextBox searchTextBox;
		readonly ListBox listBox;
		readonly TextBlock statusTextBlock;
		readonly Button okButton;

		public object? SelectedObject => (listBox.SelectedItem as Item)?.Object;

		public MemberPickerDlg(IDsDocumentService documentService, DnlibTypePickerFilter filter, object? selectedObject) {
			Width = 700;
			Height = 500;
			MinWidth = 400;
			MinHeight = 250;
			allItems = GetItems(documentService, filter).ToList();

			searchTextBox = new TextBox { Watermark = dnSpy_AsmEditor_Core_Resources.PickMember_Search, Margin = new Thickness(5) };
			searchTextBox.TextChanged += (s, e) => UpdateItems();
			listBox = new ListBox {
				Margin = new Thickness(5, 0, 5, 0),
				SelectionMode = SelectionMode.Single,
				ItemTemplate = new FuncDataTemplate<Item>((item, scope) => {
					var panel = new DockPanel();
					var image = new Image { Width = 16, Height = 16, Margin = new Thickness(0, 0, 5, 0), Source = item?.Icon };
					DockPanel.SetDock(image, Dock.Left);
					var location = new TextBlock { Text = item?.Location, Opacity = 0.6, Margin = new Thickness(10, 0, 0, 0) };
					DockPanel.SetDock(location, Dock.Right);
					panel.Children.Add(image);
					panel.Children.Add(location);
					panel.Children.Add(new TextBlock { Text = item?.Text, TextTrimming = global::Avalonia.Media.TextTrimming.CharacterEllipsis });
					return panel;
				}),
			};
			listBox.SelectionChanged += (s, e) => okButton!.IsEnabled = listBox.SelectedItem is not null;
			listBox.DoubleTapped += (s, e) => {
				if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is not null && listBox.SelectedItem is not null)
					CloseDialog(true);
			};
			statusTextBlock = new TextBlock { Margin = new Thickness(5), VerticalAlignment = VerticalAlignment.Center };
			okButton = new Button { Content = dnSpy_AsmEditor_Core_Resources.Button_OK, IsDefault = true, IsEnabled = false, Margin = new Thickness(0, 0, 5, 0) };
			okButton.Classes.Add("dialog");
			okButton.Click += (s, e) => CloseDialog(true);
			var cancelButton = new Button { Content = dnSpy_AsmEditor_Core_Resources.Button_Cancel, IsCancel = true };
			cancelButton.Classes.Add("dialog");
			cancelButton.Click += (s, e) => CloseDialog(false);
			var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(5), Children = { okButton, cancelButton } };

			var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), ColumnDefinitions = new ColumnDefinitions("*,Auto") };
			Grid.SetColumnSpan(searchTextBox, 2);
			Grid.SetRow(listBox, 1);
			Grid.SetColumnSpan(listBox, 2);
			Grid.SetRow(statusTextBlock, 2);
			Grid.SetRow(buttons, 2);
			Grid.SetColumn(buttons, 1);
			grid.Children.Add(searchTextBox);
			grid.Children.Add(listBox);
			grid.Children.Add(statusTextBlock);
			grid.Children.Add(buttons);
			Content = grid;

			if (selectedObject is IMemberDef member && allItems.FirstOrDefault(a => a.Object == member) is Item selectedItem) {
				searchTextBox.Text = member.Name;
				UpdateItems();
				listBox.SelectedItem = selectedItem;
			}
			else
				UpdateItems();
			FocusHelper.SetFocusedElement(grid, searchTextBox);
		}

		protected override void OnKeyDown(KeyEventArgs e) {
			// Down arrow in the search box moves to the list
			if (e.Key == Key.Down && e.KeyModifiers == KeyModifiers.None && searchTextBox.IsFocused && listBox.ItemCount > 0) {
				if (listBox.SelectedIndex < 0)
					listBox.SelectedIndex = 0;
				listBox.ContainerFromIndex(listBox.SelectedIndex)?.Focus(NavigationMethod.Directional);
				e.Handled = true;
				return;
			}
			base.OnKeyDown(e);
		}

		void UpdateItems() {
			var search = searchTextBox.Text?.Trim() ?? string.Empty;
			var selected = listBox.SelectedItem;
			IEnumerable<Item> items = allItems;
			if (search.Length != 0)
				items = items.Where(a => a.Text.Contains(search, StringComparison.OrdinalIgnoreCase));
			var list = items.Take(MAX_ITEMS + 1).ToList();
			bool tooMany = list.Count > MAX_ITEMS;
			if (tooMany)
				list.RemoveAt(list.Count - 1);
			listBox.ItemsSource = list;
			if (selected is Item item && list.Contains(item))
				listBox.SelectedItem = item;
			else if (list.Count == 1)
				listBox.SelectedIndex = 0;
			statusTextBlock.Text = tooMany ? $"Showing the first {MAX_ITEMS} matches" : string.Empty;
		}

		static IEnumerable<Item> GetItems(IDsDocumentService documentService, DnlibTypePickerFilter filter) {
			var flags = filter.Flags;
			var seen = new HashSet<ModuleDef>();
			foreach (var document in documentService.GetDocuments().SelectMany(a => a.GetAllChildrenAndSelf())) {
				var module = document.ModuleDef;
				if (module is null || !seen.Add(module) || !filter.IsVisible(module))
					continue;
				var location = module.Assembly?.Name.String ?? module.Name.String;
				if ((flags & VisibleMembersFlags.AssemblyDef) != 0 && document.AssemblyDef is AssemblyDef asm && asm.ManifestModule == module)
					yield return new Item(document, asm.FullName, Path.GetFileName(document.Filename), DotNetImageNames.GetName(asm));
				if ((flags & VisibleMembersFlags.ModuleDef) != 0)
					yield return new Item(document, module.Name, Path.GetFileName(document.Filename), DotNetImageNames.GetName(module));
				foreach (var type in module.GetTypes()) {
					if (IsVisible(type, flags))
						yield return new Item(type, type.FullName, location, DotNetImageNames.GetName(type));
					if ((flags & VisibleMembersFlags.FieldDef) != 0) {
						foreach (var field in type.Fields)
							yield return new Item(field, field.FullName, location, DotNetImageNames.GetName(field));
					}
					if ((flags & (VisibleMembersFlags.MethodDef | VisibleMembersFlags.InstanceConstructor)) != 0) {
						foreach (var method in type.Methods) {
							if ((flags & VisibleMembersFlags.MethodDef) != 0 || method.IsInstanceConstructor)
								yield return new Item(method, method.FullName, location, DotNetImageNames.GetName(method));
						}
					}
					if ((flags & VisibleMembersFlags.PropertyDef) != 0) {
						foreach (var property in type.Properties)
							yield return new Item(property, property.FullName, location, DotNetImageNames.GetName(property));
					}
					if ((flags & VisibleMembersFlags.EventDef) != 0) {
						foreach (var @event in type.Events)
							yield return new Item(@event, @event.FullName, location, DotNetImageNames.GetName(@event));
					}
				}
			}
		}

		static bool IsVisible(TypeDef type, VisibleMembersFlags flags) {
			if ((flags & VisibleMembersFlags.TypeDef) != 0)
				return true;
			if ((flags & VisibleMembersFlags.GenericTypeDef) != 0 && type.HasGenericParameters)
				return true;
			if ((flags & VisibleMembersFlags.NonGenericTypeDef) != 0 && !type.HasGenericParameters)
				return true;
			if ((flags & VisibleMembersFlags.EnumTypeDef) != 0 && type.IsEnum)
				return true;
			if ((flags & VisibleMembersFlags.InterfaceTypeDef) != 0 && type.IsInterface)
				return true;
			if ((flags & VisibleMembersFlags.ClassTypeDef) != 0 && !type.IsValueType && !type.IsInterface && !type.IsDelegate)
				return true;
			if ((flags & VisibleMembersFlags.StructTypeDef) != 0 && type.IsValueType && !type.IsEnum)
				return true;
			if ((flags & VisibleMembersFlags.DelegateTypeDef) != 0 && type.IsDelegate)
				return true;
			return false;
		}
	}
}

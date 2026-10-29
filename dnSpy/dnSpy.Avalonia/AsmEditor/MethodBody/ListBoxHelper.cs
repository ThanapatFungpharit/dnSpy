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
using System.Diagnostics;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using dnSpy.AsmEditor.Commands;
using dnSpy.AsmEditor.Properties;
using dnSpy.Contracts.MVVM;

namespace dnSpy.Avalonia.AsmEditor {
	// A context menu command of a list. The command parameter is the selected items (T[]).
	sealed class ListCommand {
		public string Header { get; }
		public string? HeaderPlural { get; }
		public ICommand Command { get; }
		public Key Key { get; }
		public KeyModifiers Modifiers { get; }

		public ListCommand(string header, string? headerPlural, ICommand command, Key key, KeyModifiers modifiers = KeyModifiers.None) {
			Header = header;
			HeaderPlural = headerPlural;
			Command = command;
			Key = key;
			Modifiers = modifiers;
		}
	}

	// The context menu, keyboard shortcuts and copy/paste of the method body editor's lists. Same commands as WPF's
	// ListBoxHelperBase.
	abstract class ListBoxHelper<T> where T : class, IIndexedItem {
		protected readonly ListBox listBox;
		protected IndexObservableCollection<T> coll = null!;
		readonly List<ListCommand?> commands = new List<ListCommand?>();
		readonly int copiedDataId;
		static int classCopiedDataId;

		// The copied items. They can't be serialized so they're only stored in this process.
		static T[]? clipboardItems;
		static int clipboardItemsId;

		protected abstract string AddNewBeforeSelectionMessage { get; }
		protected abstract string AddNewAfterSelectionMessage { get; }
		protected abstract string AppendNewMessage { get; }
		protected abstract string RemoveSingularMessage { get; }
		protected abstract string RemovePluralMessage { get; }
		protected abstract string RemoveAllMessage { get; }

		protected abstract void OnDataContextChangedInternal(object dataContext);
		protected abstract string GetItemsAsText(T[] items);
		protected virtual bool CopyItemsAsTextCanExecute(T[] items) => items.Length > 0;
		protected virtual bool CanUseClipboardData(T[] data, bool fromThisInstance) => fromThisInstance;
		protected virtual T[] BeforeCopyingData(T[] data, bool fromThisInstance) => data;
		protected virtual void AfterCopyingData(T[] data, T[] origData, bool fromThisInstance) { }

		protected ListBoxHelper(ListBox listBox) {
			this.listBox = listBox;
			copiedDataId = System.Threading.Interlocked.Increment(ref classCopiedDataId);
		}

		public void OnDataContextChanged(object dataContext) {
			if (coll is not null)
				throw new InvalidOperationException("DataContext changed more than once");
			OnDataContextChangedInternal(dataContext);
			var contextMenu = new ContextMenu();
			contextMenu.Opening += (s, e) => UpdateContextMenu(contextMenu);
			listBox.ContextMenu = contextMenu;
			listBox.AddHandler(InputElement.KeyDownEvent, ListBox_KeyDown, RoutingStrategies.Tunnel);
		}

		protected T[] GetSelectedItems() => listBox.SelectedItems?.OfType<T>().ToArray() ?? Array.Empty<T>();

		protected void AddSeparator() => commands.Add(null);
		protected void Add(ListCommand command) => commands.Add(command);

		protected void AddStandardMenuHandlers() {
			AddAddNewItemHandlers();
			AddSeparator();
			AddMoveItemHandlers();
			AddSeparator();
			AddRemoveItemHandlers();
			AddSeparator();
			AddCopyHandlers();
		}

		void AddAddNewItemHandlers() {
			if (!coll.CanCreateNewItems)
				return;
			Add(new ListCommand(AddNewBeforeSelectionMessage, null, coll.AddItemBeforeCommand, Key.F));
			Add(new ListCommand(AddNewAfterSelectionMessage, null, coll.AddItemAfterCommand, Key.C));
			Add(new ListCommand(AppendNewMessage, null, coll.AppendItemCommand, Key.A));
		}

		void AddMoveItemHandlers() {
			if (!coll.CanMoveItems)
				return;
			Add(new ListCommand(dnSpy_AsmEditor_Core_Resources.MoveUpCommand, null, coll.ItemMoveUpCommand, Key.U));
			Add(new ListCommand(dnSpy_AsmEditor_Core_Resources.MoveDownCommand, null, coll.ItemMoveDownCommand, Key.D));
		}

		void AddRemoveItemHandlers() {
			if (!coll.CanRemoveItems)
				return;
			Add(new ListCommand(RemoveSingularMessage, RemovePluralMessage, coll.RemoveItemCommand, Key.Delete));
			Add(new ListCommand(RemoveAllMessage, null, coll.RemoveAllItemsCommand, Key.Delete, KeyModifiers.Control));
		}

		void AddCopyHandlers() {
			Add(new ListCommand(dnSpy_AsmEditor_Core_Resources.CopyAsTextCommand, null, new RelayCommand(a => CopyItemsAsText((T[])a!), a => CopyItemsAsTextCanExecute((T[])a!)), Key.T, KeyModifiers.Control));
			Add(new ListCommand(dnSpy_AsmEditor_Core_Resources.CutCommand, null, new RelayCommand(a => CutItems((T[])a!), a => ((T[])a!).Length > 0), Key.X, KeyModifiers.Control));
			Add(new ListCommand(dnSpy_AsmEditor_Core_Resources.CopyCommand, null, new RelayCommand(a => CopyItems((T[])a!), a => ((T[])a!).Length > 0), Key.C, KeyModifiers.Control));
			Add(new ListCommand(dnSpy_AsmEditor_Core_Resources.PasteCommand, null, new RelayCommand(a => PasteItems(0), a => GetClipboardData() is not null), Key.V, KeyModifiers.Control));
			Add(new ListCommand(dnSpy_AsmEditor_Core_Resources.PasteAfterSelectionCommand, null, new RelayCommand(a => PasteItems(1), a => listBox.SelectedIndex >= 0 && GetClipboardData() is not null), Key.V, KeyModifiers.Control | KeyModifiers.Alt));
		}

		void UpdateContextMenu(ContextMenu contextMenu) {
			var items = GetSelectedItems();
			var menuItems = new List<Control>();
			bool addSeparator = false;
			foreach (var command in commands) {
				if (command is null) {
					addSeparator = true;
					continue;
				}
				if (addSeparator && menuItems.Count > 0)
					menuItems.Add(new Separator());
				addSeparator = false;
				var menuItem = new MenuItem {
					Header = items.Length > 1 ? command.HeaderPlural ?? command.Header : command.Header,
					IsEnabled = command.Command.CanExecute(items),
					InputGesture = new KeyGesture(command.Key, command.Modifiers),
				};
				var tmp = command;
				menuItem.Click += (s, e) => tmp.Command.Execute(GetSelectedItems());
				menuItems.Add(menuItem);
			}
			contextMenu.ItemsSource = menuItems;
		}

		// Letters without modifiers are also shortcuts so the text boxes and combo boxes in the list must handle
		// their keys first
		void ListBox_KeyDown(object? sender, KeyEventArgs e) {
			if (e.Source is Visual visual && (visual.FindAncestorOfType<TextBox>(includeSelf: true) is not null || visual.FindAncestorOfType<ComboBox>(includeSelf: true) is not null))
				return;
			// Shift+arrow keys etc select items
			if ((e.KeyModifiers & KeyModifiers.Shift) != 0)
				return;
			var modifiers = e.KeyModifiers;
			foreach (var command in commands) {
				if (command is null || command.Key != e.Key || command.Modifiers != modifiers)
					continue;
				var items = GetSelectedItems();
				if (command.Command.CanExecute(items))
					command.Command.Execute(items);
				// Make sure that no default keyboard handler gets called
				e.Handled = true;
				return;
			}
		}

		protected void SetClipboardText(string text) {
			if (text.Length == 0)
				return;
			if (TopLevel.GetTopLevel(listBox)?.Clipboard is IClipboard clipboard)
				_ = clipboard.SetTextAsync(text);
		}

		void CopyItemsAsText(T[] items) {
			Array.Sort(items, (a, b) => a.Index.CompareTo(b.Index));
			SetClipboardText(GetItemsAsText(items));
		}

		static T[] SortClipboardItems(T[] items) {
			Array.Sort(items, (a, b) => a.Index.CompareTo(b.Index));
			return items;
		}

		void AddToClipboard(T[] items) {
			clipboardItems = items;
			clipboardItemsId = copiedDataId;
		}

		static T[] CloneData(T[] items) => items.Select(a => (T)a.Clone()).ToArray();

		void CutItems(T[] items) {
			var list = SortClipboardItems(items).ToList();
			for (int i = list.Count - 1; i >= 0; i--) {
				var item = list[i];
				Debug.Assert(item.Index >= 0 && item.Index < coll.Count && item == coll[item.Index]);
				coll.RemoveAt(item.Index);
			}
			AddToClipboard(items);
		}

		void CopyItems(T[] items) => AddToClipboard(CloneData(SortClipboardItems(items)));

		T[]? GetClipboardData() {
			var data = clipboardItems;
			if (data is null || !CanUseClipboardData(data, clipboardItemsId == copiedDataId))
				return null;
			return data;
		}

		int GetPasteIndex(int relIndex) {
			int index = listBox.SelectedIndex;
			if (index < 0 || index > coll.Count)
				index = coll.Count;
			index += relIndex;
			if (index < 0 || index > coll.Count)
				index = coll.Count;
			return index;
		}

		void PasteItems(int relIndex) {
			var copiedData = GetClipboardData();
			if (copiedData is null)
				return;
			bool fromThisInstance = clipboardItemsId == copiedDataId;
			int index = GetPasteIndex(relIndex);
			var origClonedData = CloneData(copiedData);
			copiedData = BeforeCopyingData(origClonedData, fromThisInstance);
			for (int i = 0; i < copiedData.Length; i++)
				coll.Insert(index + i, copiedData[i]);
			AfterCopyingData(copiedData, origClonedData, fromThisInstance);
		}
	}
}

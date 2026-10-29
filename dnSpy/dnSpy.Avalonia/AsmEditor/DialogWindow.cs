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

using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace dnSpy.Avalonia.AsmEditor {
	// Base class of the AsmEditor dialogs. The view models call the sub dialog helpers (IEdit<T>, ITypeSigCreator, etc)
	// synchronously like WPF's ShowDialog() so the dialogs are shown modally in a nested dispatcher frame.
	class DialogWindow : Window {
		bool dialogResult;

		// The dialogs that are shown, the last one is the top most one
		internal static IReadOnlyList<DialogWindow> OpenDialogs => openDialogs;
		static readonly List<DialogWindow> openDialogs = new List<DialogWindow>();

		public DialogWindow() {
			WindowStartupLocation = WindowStartupLocation.CenterOwner;
			ShowInTaskbar = false;
			Classes.Add("dnSpyDialog");
		}

		// Shows the dialog and returns true if the user pressed OK
		public bool ShowModal(Window? owner) {
			owner ??= DialogUtils.GetActiveWindow();
			dialogResult = false;
			var frame = new DispatcherFrame();
			openDialogs.Add(this);
			Closed += (s, e) => {
				openDialogs.Remove(this);
				frame.Continue = false;
			};
			if (owner is null)
				Show();
			else
				_ = ShowDialog<object?>(owner);
			Dispatcher.UIThread.PushFrame(frame);
			return dialogResult;
		}

		public void CloseDialog(bool result) {
			dialogResult = result;
			Close();
		}

		protected void okButton_Click(object? sender, RoutedEventArgs e) => CloseDialog(true);
		protected void cancelButton_Click(object? sender, RoutedEventArgs e) => CloseDialog(false);

		protected override void OnKeyDown(KeyEventArgs e) {
			base.OnKeyDown(e);
			if (!e.Handled && e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None) {
				e.Handled = true;
				CloseDialog(false);
			}
		}
	}
}

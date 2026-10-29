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
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using dnSpy.Avalonia.Dialogs;

namespace dnSpy.Avalonia.AsmEditor {
	static class DialogUtils {
		// The window that owns new dialogs: the active one, or the main window
		public static Window? GetActiveWindow() {
			if (global::Avalonia.Application.Current?.ApplicationLifetime is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
				return desktop.Windows.FirstOrDefault(a => a.IsActive) ?? desktop.MainWindow;
			return MainWindow;
		}

		// Used when there's no desktop lifetime, eg. in the smoke test
		internal static Window? MainWindow { get; set; }

		public static Window? GetOwnerWindow(Control? control) =>
			(control is null ? null : TopLevel.GetTopLevel(control) as Window) ?? GetActiveWindow();

		// The view models expect synchronous dialogs so this runs a nested dispatcher frame until the task completes
		public static T Wait<T>(Task<T> task) {
			if (!task.IsCompleted) {
				var frame = new DispatcherFrame();
				task.ContinueWith(t => Dispatcher.UIThread.Post(() => frame.Continue = false), TaskScheduler.Default);
				Dispatcher.UIThread.PushFrame(frame);
			}
			return task.GetAwaiter().GetResult();
		}

		public static void ShowMessage(Control? owner, string message) {
			var window = GetOwnerWindow(owner);
			if (window is null)
				return;
			Wait(MessageBox.ShowAsync(window, message, "dnSpy"));
		}

		// Converts a WPF/WinForms filter, eg. "Text files (*.txt)|*.txt|All files (*.*)|*.*"
		public static List<FilePickerFileType>? ToFileTypes(string? filter) {
			if (string.IsNullOrEmpty(filter))
				return null;
			var parts = filter.Split('|');
			var list = new List<FilePickerFileType>();
			for (int i = 0; i + 1 < parts.Length; i += 2)
				list.Add(new FilePickerFileType(parts[i]) { Patterns = parts[i + 1].Split(';', StringSplitOptions.RemoveEmptyEntries) });
			return list;
		}

		public static string? PickOpenFilename(Control? owner, string? filter) {
			var window = GetOwnerWindow(owner);
			if (window is null || !window.StorageProvider.CanOpen)
				return null;
			var files = Wait(window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
				AllowMultiple = false,
				FileTypeFilter = ToFileTypes(filter),
			}));
			return files.Count == 0 ? null : files[0].TryGetLocalPath();
		}

		public static string[] PickOpenFilenames(Control? owner, string? filter) {
			var window = GetOwnerWindow(owner);
			if (window is null || !window.StorageProvider.CanOpen)
				return Array.Empty<string>();
			var files = Wait(window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
				AllowMultiple = true,
				FileTypeFilter = ToFileTypes(filter),
			}));
			return files.Select(a => a.TryGetLocalPath()).OfType<string>().ToArray();
		}

		public static string? PickSaveFilename(Control? owner, string? currentFilename, string? defaultExtension, string? filter) {
			var window = GetOwnerWindow(owner);
			if (window is null || !window.StorageProvider.CanSave)
				return null;
			var file = Wait(window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions {
				SuggestedFileName = System.IO.Path.GetFileName(currentFilename),
				DefaultExtension = defaultExtension,
				FileTypeChoices = ToFileTypes(filter),
			}));
			return file?.TryGetLocalPath();
		}
	}
}

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
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace dnSpy.Avalonia.AsmEditor {
	// Avalonia doesn't have a GroupBox, the template is in App.axaml
	sealed class GroupBox : HeaderedContentControl {
	}

	// Like WPF's FocusManager.FocusedElement: focuses the element when the control is loaded
	sealed class FocusHelper : AvaloniaObject {
		public static readonly AttachedProperty<IInputElement?> FocusedElementProperty =
			AvaloniaProperty.RegisterAttached<FocusHelper, Control, IInputElement?>("FocusedElement");

		static FocusHelper() => FocusedElementProperty.Changed.AddClassHandler<Control>(OnFocusedElementChanged);

		public static IInputElement? GetFocusedElement(Control control) => control.GetValue(FocusedElementProperty);
		public static void SetFocusedElement(Control control, IInputElement? value) => control.SetValue(FocusedElementProperty, value);

		static void OnFocusedElementChanged(Control control, AvaloniaPropertyChangedEventArgs e) {
			if (control.IsLoaded)
				Focus(control);
			else
				control.Loaded += Control_Loaded;
		}

		static void Control_Loaded(object? sender, RoutedEventArgs e) {
			var control = (Control)sender!;
			control.Loaded -= Control_Loaded;
			Focus(control);
		}

		static void Focus(Control control) {
			if (GetFocusedElement(control) is not IInputElement element)
				return;
			element.Focus(NavigationMethod.Tab);
			if (element is TextBox textBox)
				textBox.SelectAll();
		}
	}
}

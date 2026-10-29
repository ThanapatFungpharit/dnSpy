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

using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;

namespace dnSpy.Avalonia.AsmEditor {
	// The view models validate with IDataErrorInfo like WPF's ValidatesOnDataErrors=True but Avalonia only supports
	// INotifyDataErrorInfo. This shows the error of property Property of object Source (eg. the binding's source) on
	// the control and updates it whenever Source raises PropertyChanged.
	sealed class DataErrorInfoHelper : AvaloniaObject {
		public static readonly AttachedProperty<object?> SourceProperty =
			AvaloniaProperty.RegisterAttached<DataErrorInfoHelper, Control, object?>("Source");
		public static readonly AttachedProperty<string?> PropertyProperty =
			AvaloniaProperty.RegisterAttached<DataErrorInfoHelper, Control, string?>("Property");
		static readonly AttachedProperty<Subscription?> SubscriptionProperty =
			AvaloniaProperty.RegisterAttached<DataErrorInfoHelper, Control, Subscription?>("Subscription");

		static DataErrorInfoHelper() {
			SourceProperty.Changed.AddClassHandler<Control>((c, e) => Update(c));
			PropertyProperty.Changed.AddClassHandler<Control>((c, e) => Update(c));
		}

		public static object? GetSource(Control control) => control.GetValue(SourceProperty);
		public static void SetSource(Control control, object? value) => control.SetValue(SourceProperty, value);
		public static string? GetProperty(Control control) => control.GetValue(PropertyProperty);
		public static void SetProperty(Control control, string? value) => control.SetValue(PropertyProperty, value);

		static void Update(Control control) {
			control.GetValue(SubscriptionProperty)?.Dispose();
			var source = GetSource(control);
			var property = GetProperty(control);
			if (source is null || property is null) {
				control.SetValue(SubscriptionProperty, null);
				ShowError(control, null);
				return;
			}
			var subscription = new Subscription(control, source, property);
			control.SetValue(SubscriptionProperty, subscription);
			subscription.UpdateError();
		}

		static void ShowError(Control control, string? error) {
			if (string.IsNullOrEmpty(error)) {
				DataValidationErrors.ClearErrors(control);
				if (control.GetValue(ToolTip.TipProperty) is ErrorToolTip)
					control.ClearValue(ToolTip.TipProperty);
			}
			else {
				DataValidationErrors.SetErrors(control, new object[] { error });
				if (control.GetValue(ToolTip.TipProperty) is null or ErrorToolTip)
					ToolTip.SetTip(control, new ErrorToolTip(error));
			}
		}

		// A tool tip that was set by this class and can be replaced or cleared
		sealed class ErrorToolTip {
			readonly string error;
			public ErrorToolTip(string error) => this.error = error;
			public override string ToString() => error;
		}

		sealed class Subscription {
			readonly Control control;
			readonly object source;
			readonly string property;

			public Subscription(Control control, object source, string property) {
				this.control = control;
				this.source = source;
				this.property = property;
				if (source is INotifyPropertyChanged inpc)
					inpc.PropertyChanged += Source_PropertyChanged;
			}

			void Source_PropertyChanged(object? sender, PropertyChangedEventArgs e) => UpdateError();

			public void UpdateError() {
				string? error = null;
				if (source is IDataErrorInfo dataErrorInfo) {
					try {
						error = dataErrorInfo[property];
					}
					catch (System.NotImplementedException) {
					}
				}
				ShowError(control, error);
			}

			public void Dispose() {
				if (source is INotifyPropertyChanged inpc)
					inpc.PropertyChanged -= Source_PropertyChanged;
			}
		}
	}
}

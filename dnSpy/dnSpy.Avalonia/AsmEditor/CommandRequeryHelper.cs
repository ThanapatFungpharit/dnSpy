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
using Avalonia.Interactivity;
using Avalonia.Threading;
using dnSpy.Contracts.MVVM;

namespace dnSpy.Avalonia.AsmEditor {
	// RelayCommand.CanExecute is re-checked after user input, like WPF's CommandManager does
	static class CommandRequeryHelper {
		static bool initialized;
		static bool pending;

		public static void Initialize() {
			if (initialized)
				return;
			initialized = true;
			InputElement.KeyUpEvent.AddClassHandler<TopLevel>((s, e) => Invalidate(), RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
			InputElement.PointerReleasedEvent.AddClassHandler<TopLevel>((s, e) => Invalidate(), RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
			InputElement.TextInputEvent.AddClassHandler<TopLevel>((s, e) => Invalidate(), RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
			InputElement.GotFocusEvent.AddClassHandler<TopLevel>((s, e) => Invalidate(), RoutingStrategies.Bubble, handledEventsToo: true);
		}

		public static void Invalidate() {
			if (pending)
				return;
			pending = true;
			Dispatcher.UIThread.Post(() => {
				pending = false;
				CommandRequery.InvalidateRequerySuggested();
			}, DispatcherPriority.Background);
		}
	}
}

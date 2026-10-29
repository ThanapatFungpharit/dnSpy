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

namespace dnSpy.Contracts.MVVM {
	/// <summary>
	/// Tells <see cref="RelayCommand"/>s that they should check whether they can execute. The WPF GUI forwards
	/// it to <c>CommandManager.RequerySuggested</c>, other UIs call <see cref="InvalidateRequerySuggested"/>,
	/// eg. after user input.
	/// </summary>
	public static class CommandRequery {
		static Action<EventHandler>? addHandler;
		static Action<EventHandler>? removeHandler;
		static readonly List<EventHandler> handlers = new List<EventHandler>();

		/// <summary>
		/// Forwards all handlers to another event, eg. WPF's <c>CommandManager.RequerySuggested</c>. It must be
		/// called before any command is used.
		/// </summary>
		/// <param name="addHandler">Adds a handler</param>
		/// <param name="removeHandler">Removes a handler</param>
		public static void Initialize(Action<EventHandler> addHandler, Action<EventHandler> removeHandler) {
			if (CommandRequery.addHandler is not null)
				throw new InvalidOperationException();
			CommandRequery.addHandler = addHandler ?? throw new ArgumentNullException(nameof(addHandler));
			CommandRequery.removeHandler = removeHandler ?? throw new ArgumentNullException(nameof(removeHandler));
			EventHandler[] oldHandlers;
			lock (handlers) {
				oldHandlers = handlers.ToArray();
				handlers.Clear();
			}
			foreach (var handler in oldHandlers)
				addHandler(handler);
		}

		/// <summary>
		/// Raised when commands should check whether they can execute
		/// </summary>
		public static event EventHandler? RequerySuggested {
			add {
				if (value is null)
					return;
				if (addHandler is not null)
					addHandler(value);
				else {
					lock (handlers)
						handlers.Add(value);
				}
			}
			remove {
				if (value is null)
					return;
				if (removeHandler is not null)
					removeHandler(value);
				else {
					lock (handlers)
						handlers.Remove(value);
				}
			}
		}

		/// <summary>
		/// Raises <see cref="RequerySuggested"/>. Only used if <see cref="Initialize(Action{EventHandler}, Action{EventHandler})"/>
		/// wasn't called. Must be called on the UI thread.
		/// </summary>
		public static void InvalidateRequerySuggested() {
			EventHandler[] currentHandlers;
			lock (handlers)
				currentHandlers = handlers.ToArray();
			foreach (var handler in currentHandlers)
				handler(null, EventArgs.Empty);
		}
	}
}

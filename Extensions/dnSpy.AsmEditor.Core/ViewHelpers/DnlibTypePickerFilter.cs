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
using dnlib.DotNet;
using dnSpy.Contracts.Search;

namespace dnSpy.AsmEditor.ViewHelpers {
	/// <summary>
	/// What an <see cref="IDnlibTypePicker"/> shows
	/// </summary>
	sealed class DnlibTypePickerFilter {
		/// <summary>
		/// Members that can be picked
		/// </summary>
		public VisibleMembersFlags Flags { get; }

		/// <summary>
		/// If non-null, only this module is shown
		/// </summary>
		public ModuleDef? SameModule { get; private set; }

		/// <summary>
		/// If non-null, only the assembly of this module (or the module if it has no assembly) is shown
		/// </summary>
		public ModuleDef? SameAssembly { get; private set; }

		/// <summary>
		/// true if it shows the methods that can be the entry point of <see cref="SameModule"/>
		/// </summary>
		public bool IsEntryPoint { get; private set; }

		public DnlibTypePickerFilter(VisibleMembersFlags flags) => Flags = flags;

		public static DnlibTypePickerFilter EntryPointOf(ModuleDef module) =>
			new DnlibTypePickerFilter(VisibleMembersFlags.MethodDef) { SameModule = module ?? throw new ArgumentNullException(nameof(module)), IsEntryPoint = true };

		public static DnlibTypePickerFilter SameModuleOnly(ModuleDef module, VisibleMembersFlags flags) =>
			new DnlibTypePickerFilter(flags) { SameModule = module ?? throw new ArgumentNullException(nameof(module)) };

		public static DnlibTypePickerFilter SameAssemblyOnly(ModuleDef module, VisibleMembersFlags flags) =>
			new DnlibTypePickerFilter(flags) { SameAssembly = module ?? throw new ArgumentNullException(nameof(module)) };

		/// <summary>
		/// Checks whether the module and its members can be shown
		/// </summary>
		/// <param name="module">Module</param>
		public bool IsVisible(ModuleDef module) {
			if (SameModule is not null)
				return module == SameModule;
			if (SameAssembly is not null) {
				if (SameAssembly.Assembly is null)
					return module == SameAssembly;
				return module.Assembly == SameAssembly.Assembly;
			}
			return true;
		}
	}
}

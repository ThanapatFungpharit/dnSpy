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

namespace dnSpy.Contracts.Extension {
	/// <summary>
	/// Adds another assembly's MEF parts to the catalog when this extension is loaded. Use it when
	/// an extension keeps its UI independent parts in a separate assembly that is also used by hosts
	/// without a UI. Apply it to the extension assembly (the *.x.dll file).
	/// </summary>
	[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
	public sealed class ExtensionPartsAssemblyAttribute : Attribute {
		/// <summary>
		/// Any type in the assembly with the MEF parts
		/// </summary>
		public Type Type { get; }

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="type">Any type in the assembly with the MEF parts</param>
		public ExtensionPartsAssemblyAttribute(Type type) => Type = type ?? throw new ArgumentNullException(nameof(type));
	}
}

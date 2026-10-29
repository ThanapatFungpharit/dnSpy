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

using dnlib.DotNet;

namespace dnSpy.AsmEditor.ViewHelpers {
	static class DnlibTypePickerUtils {
		/// <summary>
		/// Imports the picked object into <paramref name="ownerModule"/>
		/// </summary>
		/// <param name="ownerModule">Module owning the returned reference</param>
		/// <param name="obj">Picked object</param>
		public static object? ImportObject(ModuleDef ownerModule, object? obj) {
			var importer = new Importer(ownerModule, ImporterOptions.TryToUseDefs);

			if (obj is IType type)
				return importer.Import(type);

			if (obj is IField field && field.IsField)
				return importer.Import(field);

			if (obj is IMethod method && method.IsMethod)
				return importer.Import(method);

			// DsDocument, namespace, PropertyDef, EventDef, AssemblyRef, ModuleRef
			return obj;
		}
	}
}

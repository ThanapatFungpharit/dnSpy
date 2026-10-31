/*
    Copyright (C) 2014-2019 de4dot@gmail.com

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
using dnSpy.Contracts.Documents.Tabs.DocViewer;

namespace dnSpy.Documents.Tabs.DocViewer {
	class ContentCustomDataContext : IContentCustomDataContext, IDisposable {
		public string Text => text!;
		string? text;
		Dictionary<string, object>? customDataDict;
		Dictionary<string, object>? resultDict;

		public ContentCustomDataContext(string text, Dictionary<string, object> customDataDict) {
			this.text = text;
			this.customDataDict = customDataDict;
			resultDict = new Dictionary<string, object>(StringComparer.Ordinal);
		}

		internal Dictionary<string, object>? GetResultDictionary() => resultDict;

		public void AddCustomData(string id, object data) {
			if (customDataDict is null)
				throw new ObjectDisposedException(nameof(IContentCustomDataContext));
			Debug2.Assert(resultDict is not null);
			if (id is null)
				throw new ArgumentNullException(nameof(id));
			if (resultDict.ContainsKey(id))
				throw new InvalidOperationException(nameof(AddCustomData) + "() can only be called once with the same " + nameof(id));
			resultDict.Add(id, data);
		}

		public TData[] GetData<TData>(string id) {
			if (customDataDict is null)
				throw new ObjectDisposedException(nameof(IContentCustomDataContext));
			if (id is null)
				throw new ArgumentNullException(nameof(id));

			if (!customDataDict.TryGetValue(id, out var listObj))
				return Array.Empty<TData>();
			var list = (List<TData>)listObj;
			return list.ToArray();
		}

		public virtual void Dispose() {
			text = null;
			customDataDict = null;
			resultDict = null;
		}
	}
}

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

using System.Diagnostics;
using dnSpy.Contracts.Decompiler;
using dnSpy.Contracts.Documents.Tabs.DocViewer;
using dnSpy.Contracts.Text;

namespace dnSpy.Documents.Tabs.DocViewer {
	[ExportContentCustomDataProvider]
	sealed class SpanReferenceContentCustomDataProvider : IContentCustomDataProvider {
		public void OnCustomData(IContentCustomDataContext context) {
			SpanDataCollection<ReferenceAndId> result;
			var data = context.GetData<SpanReference>(PredefinedCustomDataIds.SpanReference);
			if (data.Length == 0)
				result = SpanDataCollection<ReferenceAndId>.Empty;
			else {
				var builder = SpanDataCollectionBuilder<ReferenceAndId>.CreateBuilder(data.Length);
				int prevEnd = 0;
				foreach (var d in data) {
					// The data should already be sorted. We don't support overlaps at the moment.
					Debug.Assert(prevEnd <= d.Span.Start);
					if (prevEnd <= d.Span.Start) {
						builder.Add(d.Span, new ReferenceAndId(d.Reference, d.Id));
						prevEnd = d.Span.End;
					}
				}
				result = builder.Create();
			}
			context.AddCustomData(DocumentViewerContentDataIds.SpanReference, result);
		}
	}
}

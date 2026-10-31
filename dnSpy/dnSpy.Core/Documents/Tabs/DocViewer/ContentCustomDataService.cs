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
using System.ComponentModel.Composition;
using System.Linq;
using dnSpy.Contracts.Documents.Tabs.DocViewer;

namespace dnSpy.Documents.Tabs.DocViewer {
	[Export(typeof(ContentCustomDataService))]
	sealed class ContentCustomDataService {
		internal Lazy<IContentCustomDataProvider, IContentCustomDataProviderMetadata>[] Providers { get; }

		[ImportingConstructor]
		ContentCustomDataService([ImportMany] IEnumerable<Lazy<IContentCustomDataProvider, IContentCustomDataProviderMetadata>> providers) =>
			Providers = providers.OrderBy(a => a.Metadata.Order).ToArray();

		// Creates the content and runs the custom data providers that don't need a document viewer (debug info,
		// brace pairs, block structure, ...). Hosts without a UI call this instead of DocumentViewerOutputBase.CreateContent().
		public DocumentViewerContent CreateContent(DocumentViewerOutputBase output) {
			if (output is null)
				throw new ArgumentNullException(nameof(output));
			output.SetStatePostProcessing();
			output.SetStateCustomDataProviders();
			using (var context = new ContentCustomDataContext(output.GetCachedText(), output.GetCustomDataDictionary())) {
				foreach (var lz in Providers)
					lz.Value.OnCustomData(context);
				return output.CreateContent(context.GetResultDictionary()!);
			}
		}
	}
}

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
using System.Diagnostics;
using System.Linq;
using dnSpy.Contracts.Documents.Tabs.DocViewer;
using Microsoft.VisualStudio.Utilities;

namespace dnSpy.Documents.Tabs.DocViewer {
	interface IDocumentViewerContentFactoryProvider {
		IDocumentViewerContentFactory Create();
	}

	/// <summary>
	/// Provides a <see cref="IDocumentViewerOutput"/> which is used to create a <see cref="DocumentViewerContent"/>
	/// </summary>
	interface IDocumentViewerContentFactory {
		/// <summary>
		/// Gets the output
		/// </summary>
		IDocumentViewerOutput Output { get; }

		/// <summary>
		/// Creates the content. This method can only be called once.
		/// </summary>
		/// <param name="documentViewer">Document viewer</param>
		/// <param name="contentType">Content type</param>
		/// <returns></returns>
		DocumentViewerContent CreateContent(IDocumentViewer documentViewer, IContentType contentType);
	}

	[Export(typeof(IDocumentViewerContentFactoryProvider))]
	sealed class DocumentViewerContentFactoryProvider : IDocumentViewerContentFactoryProvider {
		readonly Lazy<IDocumentViewerPostProcessor, IDocumentViewerPostProcessorMetadata>[] documentViewerPostProcessors;
		readonly Action<DocumentViewerContentFactory.DocumentViewerCustomDataContext>[] documentViewerCustomDataProviders;

		[ImportingConstructor]
		DocumentViewerContentFactoryProvider([ImportMany] IEnumerable<Lazy<IDocumentViewerPostProcessor, IDocumentViewerPostProcessorMetadata>> documentViewerPostProcessors, [ImportMany] IEnumerable<Lazy<IDocumentViewerCustomDataProvider, IDocumentViewerCustomDataProviderMetadata>> documentViewerCustomDataProviders, ContentCustomDataService contentCustomDataService) {
			this.documentViewerPostProcessors = documentViewerPostProcessors.OrderBy(a => a.Metadata.Order).ToArray();
			// The UI independent providers live in dnSpy.Core, call them in the same order as before they were moved there
			var providers = new List<(double order, Action<DocumentViewerContentFactory.DocumentViewerCustomDataContext> onCustomData)>();
			foreach (var lz in contentCustomDataService.Providers)
				providers.Add((lz.Metadata.Order, context => lz.Value.OnCustomData(context)));
			foreach (var lz in documentViewerCustomDataProviders)
				providers.Add((lz.Metadata.Order, context => lz.Value.OnCustomData(context)));
			this.documentViewerCustomDataProviders = providers.OrderBy(a => a.order).Select(a => a.onCustomData).ToArray();
		}

		public IDocumentViewerContentFactory Create() => new DocumentViewerContentFactory(documentViewerPostProcessors, documentViewerCustomDataProviders);
	}

	sealed class DocumentViewerContentFactory : IDocumentViewerContentFactory {
		public IDocumentViewerOutput Output {
			get {
				if (documentViewerOutput is null)
					throw new InvalidOperationException();
				return documentViewerOutput;
			}
		}

		readonly Lazy<IDocumentViewerPostProcessor, IDocumentViewerPostProcessorMetadata>[] documentViewerPostProcessors;
		readonly Action<DocumentViewerCustomDataContext>[] documentViewerCustomDataProviders;
		DocumentViewerOutput? documentViewerOutput;

		public DocumentViewerContentFactory(Lazy<IDocumentViewerPostProcessor, IDocumentViewerPostProcessorMetadata>[] documentViewerPostProcessors, Action<DocumentViewerCustomDataContext>[] documentViewerCustomDataProviders) {
			this.documentViewerPostProcessors = documentViewerPostProcessors ?? throw new ArgumentNullException(nameof(documentViewerPostProcessors));
			this.documentViewerCustomDataProviders = documentViewerCustomDataProviders ?? throw new ArgumentNullException(nameof(documentViewerCustomDataProviders));
			documentViewerOutput = DocumentViewerOutput.Create();
		}

		internal sealed class DocumentViewerCustomDataContext : ContentCustomDataContext, IDocumentViewerCustomDataContext {
			public IDocumentViewer DocumentViewer => documentViewer!;
			public IContentType ContentType { get; }
			IDocumentViewer? documentViewer;

			public DocumentViewerCustomDataContext(IDocumentViewer documentViewer, string text, IContentType contentType, Dictionary<string, object> customDataDict)
				: base(text, customDataDict) {
				this.documentViewer = documentViewer;
				ContentType = contentType;
			}

			public override void Dispose() {
				base.Dispose();
				documentViewer = null;
			}
		}

		sealed class DocumentViewerPostProcessorContext : IDocumentViewerPostProcessorContext, IDisposable {
			public string Text => text!;
			public IDocumentViewerOutput DocumentViewerOutput => documentViewerOutput!;
			public IDocumentViewer DocumentViewer => documentViewer!;
			public IContentType ContentType { get; }
			string? text;
			IDocumentViewerOutput? documentViewerOutput;
			IDocumentViewer? documentViewer;

			public DocumentViewerPostProcessorContext(IDocumentViewerOutput documentViewerOutput, IDocumentViewer documentViewer, string text, IContentType contentType) {
				this.documentViewerOutput = documentViewerOutput;
				this.documentViewer = documentViewer;
				this.text = text;
				ContentType = contentType;
			}

			public void Dispose() {
				text = null;
				documentViewer = null;
				documentViewerOutput = null;
			}
		}

		public DocumentViewerContent CreateContent(IDocumentViewer documentViewer, IContentType contentType) {
			if (documentViewerOutput is null)
				throw new InvalidOperationException();
			if (documentViewer is null)
				throw new ArgumentNullException(nameof(documentViewer));
			if (contentType is null)
				throw new ArgumentNullException(nameof(contentType));

			documentViewerOutput.SetStatePostProcessing();
			using (var context = new DocumentViewerPostProcessorContext(documentViewerOutput, documentViewer, documentViewerOutput.GetCachedText(), contentType)) {
				foreach (var lz in documentViewerPostProcessors)
					lz.Value.PostProcess(context);
			}

			documentViewerOutput.SetStateCustomDataProviders();
			using (var context = new DocumentViewerCustomDataContext(documentViewer, documentViewerOutput.GetCachedText(), contentType, documentViewerOutput.GetCustomDataDictionary())) {
				foreach (var onCustomData in documentViewerCustomDataProviders)
					onCustomData(context);
				var output = documentViewerOutput;
				documentViewerOutput = null;
				return output.CreateContent(context.GetResultDictionary()!);
			}
		}
	}
}

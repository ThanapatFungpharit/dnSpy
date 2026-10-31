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
using System.ComponentModel.Composition;

namespace dnSpy.Contracts.Documents.Tabs.DocViewer {
	/// <summary>
	/// Uses custom data created by the decompiler and transforms it to some other data. Use
	/// <see cref="ExportContentCustomDataProviderAttribute"/> to export an instance.
	/// It's called by the document viewer and by hosts without a UI. Use IDocumentViewerCustomDataProvider
	/// if you need the document viewer.
	/// </summary>
	public interface IContentCustomDataProvider {
		/// <summary>
		/// Gets called to create data that gets stored in an <see cref="DocumentViewerContent"/> instance
		/// </summary>
		/// <param name="context">Context</param>
		void OnCustomData(IContentCustomDataContext context);
	}

	/// <summary>Metadata</summary>
	public interface IContentCustomDataProviderMetadata {
		/// <summary>See <see cref="ExportContentCustomDataProviderAttribute.Order"/></summary>
		double Order { get; }
	}

	/// <summary>
	/// Exports a <see cref="IContentCustomDataProvider"/> instance
	/// </summary>
	[MetadataAttribute, AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
	public sealed class ExportContentCustomDataProviderAttribute : ExportAttribute, IContentCustomDataProviderMetadata {
		/// <summary>Constructor</summary>
		public ExportContentCustomDataProviderAttribute()
			: this(double.MaxValue) {
		}

		/// <summary>Constructor</summary>
		/// <param name="order">Order of this instance. It's compared with ExportDocumentViewerCustomDataProviderAttribute.Order
		/// when the document viewer calls both kinds of providers.</param>
		public ExportContentCustomDataProviderAttribute(double order)
			: base(typeof(IContentCustomDataProvider)) => Order = order;

		/// <summary>
		/// Order of this instance
		/// </summary>
		public double Order { get; set; }
	}
}

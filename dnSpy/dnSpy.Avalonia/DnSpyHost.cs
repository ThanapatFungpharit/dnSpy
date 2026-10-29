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
using System.IO;
using System.Linq;
using Avalonia.Threading;
using dnSpy.AsmEditor.Compiler;
using dnSpy.Composition;
using dnSpy.Contracts.Decompiler;
using dnSpy.Contracts.Documents;
using dnSpy.Contracts.Settings;
using Microsoft.VisualStudio.Composition;

namespace dnSpy.Avalonia {
	// Owns the headless MEF host and the services the UI uses
	sealed class DnSpyHost {
		static readonly Guid SETTINGS_GUID = new Guid("6B1D3B8C-6E58-4C3C-9D3E-9D7C2B0F5E41");
		const string DOCUMENT_SECTION = "Document";
		const string DOCUMENT_NAME_ATTR = "name";
		const string DOCUMENT_TYPE_ATTR = "type";

		readonly ExportProvider exportProvider;

		public IDsDocumentService DocumentService { get; }
		public IDecompilerService DecompilerService { get; }
		public ISettingsService SettingsService { get; }
		public IMethodAnnotations MethodAnnotations { get; }
		public RawModuleBytesProvider RawModuleBytesProvider { get; }

		// useUserFiles is false in the smoke test: it doesn't read the settings file or read/write the MEF cache.
		// Settings are only written by SaveSettings().
		public DnSpyHost(bool useUserFiles = true) {
			var options = new MefHostOptions {
				// The WPF extensions can't be loaded and there are no Avalonia extensions yet
				LoadExtensions = false,
				CacheFilename = Path.Combine(Path.GetDirectoryName(MefHost.DefaultCacheFilename)!, "dnSpy.Avalonia-mef-info.bin"),
				UseCache = useUserFiles,
				ReadSettings = useUserFiles,
			};
			options.Assemblies.Add(typeof(IDsDocumentService).Assembly);
			// The ILSpy decompiler parts are internal, its resources class is the only public type
			options.Assemblies.Add(typeof(Decompiler.ILSpy.Core.Properties.dnSpy_Decompiler_ILSpy_Core_Resources).Assembly);
			options.Assemblies.Add(typeof(BamlDecompiler.BamlSettings).Assembly);
			// The code editor's module importer
			options.Assemblies.Add(typeof(RawModuleBytesProvider).Assembly);
			exportProvider = new MefHost(options).CreateExportProvider();

			DocumentService = exportProvider.GetExportedValue<IDsDocumentService>();
			DecompilerService = exportProvider.GetExportedValue<IDecompilerService>();
			SettingsService = exportProvider.GetExportedValue<ISettingsService>();
			MethodAnnotations = exportProvider.GetExportedValue<IMethodAnnotations>();
			RawModuleBytesProvider = exportProvider.GetExportedValue<RawModuleBytesProvider>();
			// The method body editor writes IL operands with it. WPF initializes it with an IAutoLoaded part.
			global::dnSpy.AsmEditor.MethodBody.BodyUtils.Initialize(exportProvider.GetExportedValues<ISimpleILPrinter>());
			DocumentService.SetDispatcher(a => Dispatcher.UIThread.Post(a, DispatcherPriority.Background));
		}

		public IEnumerable<DsDocumentInfo> GetSavedDocuments() {
			var section = SettingsService.GetOrCreateSection(SETTINGS_GUID);
			foreach (var docSection in section.SectionsWithName(DOCUMENT_SECTION)) {
				var name = docSection.Attribute<string>(DOCUMENT_NAME_ATTR);
				var type = docSection.Attribute<Guid?>(DOCUMENT_TYPE_ATTR) ?? DocumentConstants.DOCUMENTTYPE_FILE;
				if (!string.IsNullOrEmpty(name))
					yield return new DsDocumentInfo(name, type);
			}
		}

		public void SaveSettings() {
			var section = SettingsService.RecreateSection(SETTINGS_GUID);
			foreach (var info in DocumentService.GetDocuments().Where(a => !a.IsAutoLoaded).Select(a => a.SerializedDocument)) {
				if (info is null)
					continue;
				var docSection = section.CreateSection(DOCUMENT_SECTION);
				docSection.Attribute(DOCUMENT_NAME_ATTR, info.Value.Name);
				docSection.Attribute(DOCUMENT_TYPE_ATTR, info.Value.Type);
			}
			MefHost.SaveSettings(exportProvider);
		}
	}
}

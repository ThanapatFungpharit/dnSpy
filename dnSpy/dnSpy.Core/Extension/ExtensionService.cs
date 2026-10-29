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
using dnSpy.Contracts.Extension;

namespace dnSpy.Extension {
	interface IExtensionService {
		IEnumerable<IExtension> Extensions { get; }
		IEnumerable<LoadedExtension> LoadedExtensions { get; }
	}

	[Export, Export(typeof(IExtensionService))]
	sealed class ExtensionService : IExtensionService {
		readonly Lazy<IAutoLoaded, IAutoLoadedMetadata>[] mefAutoLoaded;
		readonly Lazy<IExtension, IExtensionMetadata>[] extensions;

		public IEnumerable<IExtension> Extensions => extensions.Select(a => a.Value);

		public IEnumerable<LoadedExtension> LoadedExtensions {
			get {
				Debug2.Assert(loadedExtensions is not null, "Called too early");
				return (loadedExtensions ?? Array.Empty<LoadedExtension>());
			}
			internal set {
				Debug2.Assert(loadedExtensions is null);
				if (loadedExtensions is not null)
					throw new InvalidOperationException();
				loadedExtensions = value.ToArray();
			}
		}
		LoadedExtension[]? loadedExtensions = null;

		[ImportingConstructor]
		ExtensionService([ImportMany] IEnumerable<Lazy<IAutoLoaded, IAutoLoadedMetadata>> mefAutoLoaded, [ImportMany] IEnumerable<Lazy<IExtension, IExtensionMetadata>> extensions) {
			this.mefAutoLoaded = mefAutoLoaded.OrderBy(a => a.Metadata.Order).ToArray();
			this.extensions = extensions.OrderBy(a => a.Metadata.Order).ToArray();
		}

		// A UI host loads the extensions' MergedResourceDictionaries in loadResources
		public void LoadExtensions(Action? loadResources = null) {
			LoadAutoLoaded(AutoLoadedLoadType.BeforeExtensions);
			loadResources?.Invoke();
			LoadAutoLoaded(AutoLoadedLoadType.AfterExtensions);
			NotifyExtensions(ExtensionEvent.Loaded, null);
			LoadAutoLoaded(AutoLoadedLoadType.AfterExtensionsLoaded);
		}

		void LoadAutoLoaded(AutoLoadedLoadType loadType) {
			foreach (var m in mefAutoLoaded.Where(a => a.Metadata.LoadType == loadType)) {
				var o = m.Value;
			}
		}

		void NotifyExtensions(ExtensionEvent @event, object? obj) {
			foreach (var m in extensions)
				m.Value.OnEvent(@event, obj);
		}

		public void OnAppLoaded() {
			NotifyExtensions(ExtensionEvent.AppLoaded, null);
			LoadAutoLoaded(AutoLoadedLoadType.AppLoaded);
		}

		public void OnAppExit() => NotifyExtensions(ExtensionEvent.AppExit, null);
	}
}

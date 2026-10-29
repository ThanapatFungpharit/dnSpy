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
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using dnSpy.Contracts.App;
using dnSpy.Contracts.Extension;
using dnSpy.Contracts.Resources;
using dnSpy.Contracts.Settings;
using dnSpy.Extension;
using dnSpy.Settings;
using Microsoft.VisualStudio.Composition;

namespace dnSpy.Composition {
	sealed class MefHostOptions {
		// The host's own part assemblies. dnSpy.Core is always added.
		public List<Assembly> Assemblies { get; } = new List<Assembly>();
		public bool LoadExtensions { get; set; } = true;
		public string? ExtraExtensionDirectory { get; set; }
		// Version that's compared against the extensions' *.x.dll.xml config files
		public Version AppVersion { get; set; } = typeof(MefHost).Assembly.GetName().Version!;
		public bool UseCache { get; set; } = true;
		public string CacheFilename { get; set; } = MefHost.DefaultCacheFilename;
		public bool ReadSettings { get; set; } = true;
	}

	// Creates the MEF export provider without depending on any UI framework. There can only be
	// one instance since it owns the global ResourceHelper token cache.
	sealed class MefHost {
		public static string DefaultCacheFilename => Path.Combine(BGJitUtils.GetFolder(), "dnSpy-mef-info.bin");

		readonly MefHostOptions options;
		readonly ResourceManagerTokenCacheImpl resourceManagerTokenCacheImpl;
		readonly List<LoadedExtension> loadedExtensions = new List<LoadedExtension>();
		readonly object lockObj = new object();
		long resourceManagerTokensOffset = -1;
		volatile Assembly[]? mefAssemblies;
		bool writingCachedMefFile;
		bool tokensUpdatedWhileWriting;
#if NET
		readonly DotNetAssemblyLoader dotNetAssemblyLoader = new DotNetAssemblyLoader(System.Runtime.Loader.AssemblyLoadContext.Default);
#endif

		public IReadOnlyList<LoadedExtension> LoadedExtensions => loadedExtensions;

		public Assembly[] Assemblies => mefAssemblies ?? throw new InvalidOperationException("Called too early");

		public MefHost(MefHostOptions options) {
			this.options = options ?? throw new ArgumentNullException(nameof(options));
			resourceManagerTokenCacheImpl = new ResourceManagerTokenCacheImpl();
			resourceManagerTokenCacheImpl.TokensUpdated += ResourceManagerTokenCacheImpl_TokensUpdated;
			ResourceHelper.SetResourceManagerTokenCache(resourceManagerTokenCacheImpl);
		}

		public ExportProvider CreateExportProvider() {
			mefAssemblies = GetAssemblies();
			var resolver = Resolver.DefaultInstance;

			var factory = TryCreateExportProviderFactoryCached(resolver) ?? CreateExportProviderFactorySlow(resolver);
			var exportProvider = factory.CreateExportProvider();

			if (options.ReadSettings) {
				var settingsService = exportProvider.GetExportedValue<ISettingsService>();
				try {
					new XmlSettingsReader(settingsService).Read();
				}
				catch {
				}
			}

			return exportProvider;
		}

		public static void SaveSettings(ExportProvider exportProvider) {
			try {
				new XmlSettingsWriter(exportProvider.GetExportedValue<SettingsService>()).Write();
			}
			catch {
			}
		}

		IExportProviderFactory? TryCreateExportProviderFactoryCached(Resolver resolver) {
			if (!options.UseCache)
				return null;
			try {
				return TryCreateExportProviderFactoryCachedCore(resolver);
			}
			catch (Exception ex) {
				Debug.Fail(ex.ToString());
				return null;
			}
		}

		IExportProviderFactory? TryCreateExportProviderFactoryCachedCore(Resolver resolver) {
			Debug2.Assert(mefAssemblies is not null);
			var filename = options.CacheFilename;
			if (!File.Exists(filename))
				return null;

			Stream? cachedStream = null;
			try {
				try {
					cachedStream = File.OpenRead(filename);
					if (!new CachedMefInfo(mefAssemblies, cachedStream).CheckFile(resourceManagerTokenCacheImpl, out var tokensOffset))
						return null;
					resourceManagerTokensOffset = tokensOffset;
				}
				catch (Exception ex) when (IsFileIOException(ex)) {
					return null;
				}
				try {
					return new CachedComposition().LoadExportProviderFactoryAsync(cachedStream, resolver).Result;
				}
				catch (AggregateException ex) when (ex.InnerExceptions.Count == 1 && IsFileIOException(ex.InnerExceptions[0])) {
					return null;
				}
			}
			finally {
				cachedStream?.Dispose();
			}

			static bool IsFileIOException(Exception ex) => ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException;
		}

		IExportProviderFactory CreateExportProviderFactorySlow(Resolver resolver) {
			Debug2.Assert(mefAssemblies is not null);
			var discovery = new AttributedPartDiscoveryV1(resolver);
			var parts = discovery.CreatePartsAsync(mefAssemblies).Result;
			Debug.Assert(parts.ThrowOnErrors() == parts);

			var catalog = ComposableCatalog.Create(resolver).AddParts(parts);
			var config = CompositionConfiguration.Create(catalog);
			// If this fails/throws, one of the following is probably true:
			//	- you didn't build all projects or all files aren't in the same output dir
			//	- netcoreapp: dnSpy isn't the startup project (eg. dnSpy-x86 is)
			Debug.Assert(config.ThrowOnErrors() == config);

			if (options.UseCache) {
				lock (lockObj)
					writingCachedMefFile = true;
				Task.Run(() => SaveMefStateAsync(config)).ContinueWith(t => {
					var ex = t.Exception;
					Debug2.Assert(ex is null);
					bool updateTokens;
					lock (lockObj) {
						writingCachedMefFile = false;
						updateTokens = tokensUpdatedWhileWriting;
						tokensUpdatedWhileWriting = false;
					}
					if (updateTokens)
						UpdateResourceManagerTokens();
				}, CancellationToken.None);
			}

			return config.CreateExportProviderFactory();
		}

		async Task SaveMefStateAsync(CompositionConfiguration config) {
			Debug2.Assert(mefAssemblies is not null);
			string filename = options.CacheFilename;
			bool fileCreated = false;
			bool deleteFile = true;
			try {
				Directory.CreateDirectory(Path.GetDirectoryName(filename)!);
				using (var cachedStream = File.Create(filename)) {
					fileCreated = true;
					long resourceManagerTokensOffsetTmp;
					new CachedMefInfo(mefAssemblies, cachedStream).WriteFile(resourceManagerTokenCacheImpl.GetTokens(mefAssemblies), out resourceManagerTokensOffsetTmp);
					await new CachedComposition().SaveAsync(config, cachedStream);
					Interlocked.Exchange(ref resourceManagerTokensOffset, resourceManagerTokensOffsetTmp);
					deleteFile = false;
				}
			}
			catch (IOException) {
			}
			catch (UnauthorizedAccessException) {
			}
			catch (SecurityException) {
			}
			finally {
				if (fileCreated && deleteFile) {
					try {
						File.Delete(filename);
					}
					catch { }
				}
			}
		}

		void ResourceManagerTokenCacheImpl_TokensUpdated(object? sender, EventArgs e) {
			lock (lockObj) {
				if (writingCachedMefFile) {
					tokensUpdatedWhileWriting = true;
					return;
				}
			}
			UpdateResourceManagerTokens();
		}

		void UpdateResourceManagerTokens() {
			var assemblies = mefAssemblies;
			var tokensOffset = Interlocked.Read(ref resourceManagerTokensOffset);
			if (assemblies is null || tokensOffset < 0)
				return;
			string filename = options.CacheFilename;
			lock (lockObj) {
				if (!File.Exists(filename))
					return;
				bool deleteFile = true;
				try {
					using (var cachedStream = File.OpenWrite(filename)) {
						new CachedMefInfo(assemblies, cachedStream).UpdateResourceManagerTokens(tokensOffset, resourceManagerTokenCacheImpl);
						deleteFile = false;
					}
				}
				catch (IOException) {
				}
				catch (UnauthorizedAccessException) {
				}
				catch (SecurityException) {
				}
				if (deleteFile) {
					try {
						File.Delete(filename);
					}
					catch { }
				}
			}
		}

		Assembly[] GetAssemblies() {
#if NET
			dotNetAssemblyLoader.AddSearchPath(AppDirectories.BinDirectory);
#endif
			var list = new List<Assembly>(options.Assemblies);
			// dnSpy.Core
			if (!list.Contains(typeof(MefHost).Assembly))
				list.Add(typeof(MefHost).Assembly);
			if (options.LoadExtensions) {
				foreach (var asm in LoadExtensionAssemblies())
					list.Add(asm);
			}
			return list.ToArray();
		}

		Assembly[] LoadExtensionAssemblies() {
			var dir = AppDirectories.BinDirectory;
			var unsortedFiles = GetExtensionFiles(dir);
			if (!string2.IsNullOrEmpty(options.ExtraExtensionDirectory))
				unsortedFiles = unsortedFiles.Concat(GetExtensionFiles(options.ExtraExtensionDirectory));

			// Load the modules in a predictable order or multicore-JIT could stop recording. See
			// "Understanding Background JIT compilation -> What can go wrong with background JIT compilation"
			// in the PerfView docs for more info.
			var files = unsortedFiles.OrderBy(a => a, StringComparer.OrdinalIgnoreCase).ToArray();
#if NET
			foreach (var file in files)
				dotNetAssemblyLoader.AddSearchPath(Path.GetDirectoryName(file)!);
#endif
			var asms = new List<Assembly>();
			foreach (var file in files) {
				try {
					if (!File.Exists(file))
						continue;
					if (!CanLoadExtension(file))
						continue;
					var asm = Assembly.LoadFrom(file);
					if (!CanLoadExtension(asm)) {
						Debug.WriteLine($"Old extension detected ({file})");
						continue;
					}
					asms.Add(asm);
					loadedExtensions.Add(new LoadedExtension(asm));
					foreach (var attr in asm.GetCustomAttributes<ExtensionPartsAssemblyAttribute>()) {
						var partsAsm = attr.Type.Assembly;
						if (!asms.Contains(partsAsm) && !options.Assemblies.Contains(partsAsm))
							asms.Add(partsAsm);
					}
				}
				catch (Exception ex) {
					Debug.WriteLine($"Failed to load file '{file}', msg: {ex.Message}");
				}
			}
			return asms.ToArray();
		}

		static IEnumerable<string> GetExtensionFiles(string baseDir) {
			const string EXTENSION_SEARCH_PATTERN = "*.x.dll";
			const string EXTENSIONS_SUBDIR = "Extensions";
			foreach (var f in GetFiles(baseDir, EXTENSION_SEARCH_PATTERN))
				yield return f;
			var extensionsSubDir = Path.Combine(baseDir, EXTENSIONS_SUBDIR);
			if (Directory.Exists(extensionsSubDir)) {
				foreach (var f in GetFiles(extensionsSubDir, EXTENSION_SEARCH_PATTERN))
					yield return f;
				foreach (var d in GetDirectories(extensionsSubDir)) {
					foreach (var f in GetFiles(d, EXTENSION_SEARCH_PATTERN))
						yield return f;
				}
			}
		}

		static string[] GetFiles(string dir, string searchPattern) {
			try {
				return Directory.GetFiles(dir, searchPattern);
			}
			catch {
				return Array.Empty<string>();
			}
		}

		static string[] GetDirectories(string dir) {
			try {
				return Directory.GetDirectories(dir);
			}
			catch {
				return Array.Empty<string>();
			}
		}

		bool CanLoadExtension(string file) {
			var xmlFile = file + ".xml";
			var config = ExtensionConfigReader.Read(xmlFile);
			return config.IsSupportedOSversion(Environment.OSVersion.Version) &&
				config.IsSupportedFrameworkVersion(Environment.Version) &&
				config.IsSupportedAppVersion(options.AppVersion);
		}

		static bool CanLoadExtension(Assembly asm) {
			var ourPublicKeyToken = typeof(MefHost).Assembly.GetName().GetPublicKeyToken();
			var minimumVersion = new Version(5, 0, 0, 0);
			foreach (var a in asm.GetReferencedAssemblies()) {
				if (!Equals(ourPublicKeyToken, a.GetPublicKeyToken()))
					continue;
				if (a.Version < minimumVersion)
					return false;
			}
			return true;
		}

		static bool Equals(byte[]? a, byte[]? b) {
			if (a is null || b is null || a.Length != b.Length)
				return false;
			for (int i = 0; i < a.Length; i++) {
				if (a[i] != b[i])
					return false;
			}
			return true;
		}
	}
}

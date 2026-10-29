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
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using dnSpy.Contracts.App;
using dnSpy.Contracts.Controls;
using dnSpy.Contracts.Decompiler;
using dnSpy.Contracts.Documents.Tabs;
using dnSpy.Contracts.ETW;
using dnSpy.Contracts.Images;
using dnSpy.Contracts.MVVM;
using dnSpy.Contracts.Settings;
using dnSpy.Composition;
using dnSpy.Controls;
using dnSpy.Culture;
using dnSpy.Documents.Tabs.Dialogs;
using dnSpy.Documents.TreeView;
using dnSpy.Extension;
using dnSpy.Images;
using dnSpy.Roslyn.Text.Classification;
using dnSpy.Scripting;
using Microsoft.VisualStudio.Composition;

namespace dnSpy.MainApp {
	sealed partial class App : Application {
		static void NotExecuted() {
			// Make sure we have a ref to the assembly. The file is copied to the correct location
			// but unless we have a reference to it in the code (or XAML), it could easily happen
			// that someone accidentally removes the reference. This code makes sure there'll be
			// a compilation error if that ever happens.
			var t = typeof(Images.Dummy);
		}

		static void InstallExceptionHandlers() {
			TaskScheduler.UnobservedTaskException += (s, e) => e.SetObserved();
			if (!System.Diagnostics.Debugger.IsAttached) {
				AppDomain.CurrentDomain.UnhandledException += (s, e) => ShowException(e.ExceptionObject as Exception);
				Dispatcher.CurrentDispatcher.UnhandledException += (s, e) => {
					ShowException(e.Exception);
					e.Handled = true;
				};
			}
		}

		static void ShowException(Exception? ex) {
			string msg = ex?.ToString() ?? "Unknown exception";
			MessageBox.Show(msg, Constants.DnSpy, MessageBoxButton.OK, MessageBoxImage.Error);
		}

		readonly MefHost mefHost;
		AppWindow? appWindow;
		ExtensionService? extensionService;
		IDsLoaderService? dsLoaderService;
		readonly IAppCommandLineArgs args;
		ExportProvider? exportProvider;

		Task<ExportProvider> initializeMEFTask;
		Stopwatch? startupStopwatch;
		public App(bool readSettings, Stopwatch startupStopwatch) {
			WineFixes.Initialize();
			CommandRequery.Initialize(handler => CommandManager.RequerySuggested += handler, handler => CommandManager.RequerySuggested -= handler);

			args = new AppCommandLineArgs();
			AppDirectories.SetSettingsFilename(args.SettingsFilename);
			mefHost = new MefHost(CreateMefHostOptions(readSettings));

			// PERF: Init MEF on a BG thread. Results in slightly faster startup, eg. InitializeComponent() becomes a 'free' call on this UI thread
			initializeMEFTask = Task.Run(InitializeMEF);
			this.startupStopwatch = startupStopwatch;

			AddAppContextFixes();
			InstallExceptionHandlers();
			InitializeComponent();
			UIFixes();

			Exit += App_Exit;
		}

		void AddAppContextFixes() {
			// This prevents a thin line between the tab item and its content when dpi is eg. 144.
			// It's hard to miss if you check the Options dialog box.
			AppContext.SetSwitch("Switch.MS.Internal.DoNotApplyLayoutRoundingToMarginsAndBorderThickness", true);
		}

		MefHostOptions CreateMefHostOptions(bool readSettings) {
			var options = new MefHostOptions {
				ExtraExtensionDirectory = args.ExtraExtensionDirectory,
				AppVersion = GetType().Assembly.GetName().Version!,
				UseCache = readSettings,
				ReadSettings = readSettings,
			};
			options.Assemblies.Add(GetType().Assembly);
			// dnSpy.Core
			options.Assemblies.Add(typeof(MefHost).Assembly);
			// dnSpy.Contracts.DnSpy
			options.Assemblies.Add(typeof(MetroWindow).Assembly);
			// dnSpy.Roslyn
			options.Assemblies.Add(typeof(RoslynClassifier).Assembly);
			// Microsoft.VisualStudio.Text.Logic (needed for the editor option definitions)
			options.Assemblies.Add(typeof(Microsoft.VisualStudio.Text.Editor.ConvertTabsToSpaces).Assembly);
			// Microsoft.VisualStudio.Text.UI (needed for the editor option definitions)
			options.Assemblies.Add(typeof(Microsoft.VisualStudio.Text.Editor.AutoScrollEnabled).Assembly);
			// Microsoft.VisualStudio.Text.UI.Wpf (needed for the editor option definitions)
			options.Assemblies.Add(typeof(Microsoft.VisualStudio.Text.Editor.HighlightCurrentLineOption).Assembly);
			// dnSpy.Roslyn.EditorFeatures
			options.Assemblies.Add(typeof(Roslyn.EditorFeatures.Dummy).Assembly);
			// dnSpy.Roslyn.CSharp.EditorFeatures
			options.Assemblies.Add(typeof(Roslyn.CSharp.EditorFeatures.Dummy).Assembly);
			// dnSpy.Roslyn.VisualBasic.EditorFeatures
			options.Assemblies.Add(typeof(Roslyn.VisualBasic.EditorFeatures.Dummy).Assembly);
			return options;
		}

		ExportProvider InitializeMEF() {
			var exportProvider = mefHost.CreateExportProvider();
			exportProvider.GetExportedValue<ServiceLocator>().SetExportProvider(Dispatcher, exportProvider);
			return exportProvider;
		}

		[return: MarshalAs(UnmanagedType.Bool)]
		[DllImport("user32")]
		static extern bool SetForegroundWindow(IntPtr hWnd);
		[DllImport("user32")]
		[return: MarshalAs(UnmanagedType.Bool)]
		static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
		[return: MarshalAs(UnmanagedType.Bool)]
		delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
		[DllImport("user32", CharSet = CharSet.Auto, SetLastError = true)]
		static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
		[DllImport("user32", CharSet = CharSet.Auto)]
		static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, ref COPYDATASTRUCT lParam);
		[StructLayout(LayoutKind.Sequential)]
		struct COPYDATASTRUCT {
			public IntPtr dwData;
			public int cbData;
			public IntPtr lpData;
		}
		static readonly IntPtr COPYDATASTRUCT_dwData = new IntPtr(0x11C9B152);
		static readonly IntPtr COPYDATASTRUCT_result = new IntPtr(0x615F9D6E);
		const string COPYDATASTRUCT_HEADER = Constants.DnSpy;	// One line only

		void SwitchToOtherInstance() => EnumWindows(EnumWindowsHandler, IntPtr.Zero);

		unsafe bool EnumWindowsHandler(IntPtr hWnd, IntPtr lParam) {
			var sb = new StringBuilder(256);
			GetWindowText(hWnd, sb, sb.Capacity);
			if (sb.ToString().StartsWith(Constants.DnSpy + " ", StringComparison.Ordinal)) {
				var args = Environment.GetCommandLineArgs();
				args[0] = COPYDATASTRUCT_HEADER;
				var msg = string.Join(Environment.NewLine, args);
				COPYDATASTRUCT data;
				data.dwData = COPYDATASTRUCT_dwData;
				data.cbData = msg.Length * 2;
				fixed (void* pmsg = msg) {
					data.lpData = new IntPtr(pmsg);
					var res = SendMessage(hWnd, 0x4A, IntPtr.Zero, ref data);
					if (res == COPYDATASTRUCT_result) {
						if (this.args.Activate)
							SetForegroundWindow(hWnd);
						Environment.Exit(0);
					}
				}
			}

			return true;
		}

		unsafe IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) {
			if (msg != 0x4A)
				return IntPtr.Zero;

			var data = *(COPYDATASTRUCT*)lParam;
			if (data.dwData != COPYDATASTRUCT_dwData)
				return IntPtr.Zero;

			var argsString = new string((char*)data.lpData, 0, data.cbData / 2);
			var args = argsString.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
			if (args[0] != COPYDATASTRUCT_HEADER)
				return IntPtr.Zero;

			HandleAppArgs(new AppCommandLineArgs(args.Skip(1).ToArray()));
			handled = true;
			return COPYDATASTRUCT_result;
		}

		void MainWindow_SourceInitialized(object? sender, EventArgs e) {
			Debug2.Assert(appWindow is not null);
			appWindow.MainWindow.SourceInitialized -= MainWindow_SourceInitialized;

			var hwndSource = PresentationSource.FromVisual(appWindow.MainWindow) as HwndSource;
			Debug2.Assert(hwndSource is not null);
			if (hwndSource is not null)
				hwndSource.AddHook(WndProc);
		}

		void App_Exit(object? sender, ExitEventArgs e) {
			extensionService?.OnAppExit();
			dsLoaderService?.Save();
			if (exportProvider is not null)
				MefHost.SaveSettings(exportProvider);
		}

		void UIFixes() {
			// Add Ctrl+Shift+Z as a redo command. Don't know why it isn't enabled by default.
			ApplicationCommands.Redo.InputGestures.Add(new KeyGesture(Key.Z, ModifierKeys.Control | ModifierKeys.Shift));
			FixEditorContextMenuStyle();
		}

		// The text editor creates an EditorContextMenu which derives from ContextMenu. This
		// class is private in the assembly and can't be referenced from XAML. In order to style
		// this class we must get the type at runtime and add its style to the Resources.
		void FixEditorContextMenuStyle() {
			var module = typeof(ContextMenu).Module;
			var type = module.GetType("System.Windows.Documents.TextEditorContextMenu+EditorContextMenu", false, false);
			Debug2.Assert(type is not null);
			if (type is null)
				return;
			const string styleKey = "EditorContextMenuStyle";
			var style = Resources[styleKey];
			Debug2.Assert(style is not null);
			if (style is null)
				return;
			Resources.Remove(styleKey);
			Resources.Add(type, style);
		}

		protected override void OnStartup(StartupEventArgs e) {
			base.OnStartup(e);

			exportProvider = initializeMEFTask.GetAwaiter().GetResult();

			if (args.SingleInstance && !exportProvider.GetExportedValue<AppSettingsImpl>().AllowMoreThanOneInstance)
				SwitchToOtherInstance();

			var cultureService = exportProvider.GetExportedValue<CultureService>();
			cultureService.Initialize(args);

			// Make sure IDpiService gets created before any MetroWindows
			exportProvider.GetExportedValue<IDpiService>();

			// It's needed very early, and an IAutoLoaded can't be used (it gets called too late for the first 64x64 image request)
			DsImageConverter.imageService = exportProvider.GetExportedValue<IImageService>();

			appWindow = exportProvider.GetExportedValue<AppWindow>();
			extensionService = exportProvider.GetExportedValue<ExtensionService>();
			dsLoaderService = exportProvider.GetExportedValue<IDsLoaderService>();

			extensionService.LoadedExtensions = mefHost.LoadedExtensions;
			appWindow.CommandLineArgs = args;

			var win = appWindow.InitializeMainWindow();
			appWindow.MainWindow.SourceInitialized += MainWindow_SourceInitialized;
			dsLoaderService.OnAppLoaded += DsLoaderService_OnAppLoaded;
			dsLoaderService.Initialize(appWindow, win, args);
			extensionService.LoadExtensions(() => AddExtensionResources(extensionService));
			win.Show();
		}

		void AddExtensionResources(ExtensionService extensionService) {
			// It's not an extension but it needs to show stuff in the options dialog box
			AddMergedDictionary(typeof(RoslynClassifier).Assembly.GetName(), "Themes/wpf.styles.templates.xaml");
			foreach (var extension in extensionService.Extensions) {
				var asm = extension.GetType().Assembly.GetName();
				foreach (var rsrc in extension.MergedResourceDictionaries)
					AddMergedDictionary(asm, rsrc);
			}

			void AddMergedDictionary(AssemblyName asm, string rsrc) {
				var uri = new Uri("pack://application:,,,/" + asm.Name + ";v" + asm.Version + ";component/" + rsrc, UriKind.Absolute);
				Resources.MergedDictionaries.Add(new ResourceDictionary { Source = uri });
			}
		}

		void DsLoaderService_OnAppLoaded(object? sender, EventArgs e) {
			startupStopwatch!.Stop();
			DnSpyEventSource.Log.StartupStop();
			var sw = startupStopwatch;
			startupStopwatch = null;

			if (args.ShowStartupTime)
				ShowElapsedTime(sw);

			dsLoaderService!.OnAppLoaded -= DsLoaderService_OnAppLoaded;
			appWindow!.AppLoaded = true;
			extensionService!.OnAppLoaded();
			HandleAppArgs(args);
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		static void ShowElapsedTime(Stopwatch sw) => MsgBox.Instance.Show($"{sw.ElapsedMilliseconds} ms, {sw.ElapsedTicks} ticks");

		void HandleAppArgs(IAppCommandLineArgs appArgs) {
			Debug2.Assert(exportProvider is not null);
			Debug2.Assert(appWindow is not null);
			if (appArgs.Activate && appWindow.MainWindow.WindowState == WindowState.Minimized)
				WindowUtils.SetState(appWindow.MainWindow, WindowState.Normal);

			var decompiler = GetDecompiler(appArgs.Language);
			if (decompiler is not null)
				exportProvider.GetExportedValue<IDecompilerService>().Decompiler = decompiler;

			if (appArgs.FullScreen is not null)
				appWindow.MainWindow.IsFullScreen = appArgs.FullScreen.Value;

			if (appArgs.NewTab)
				exportProvider.GetExportedValue<IDocumentTabService>().OpenEmptyTab();

			var files = appArgs.Filenames.ToArray();
			if (files.Length > 0) {
				var mruList = exportProvider.GetExportedValue<AssemblyExplorerMostRecentlyUsedList>();
				OpenDocumentsHelper.OpenDocuments(exportProvider.GetExportedValue<IDocumentTabService>().DocumentTreeView, appWindow.MainWindow, mruList, files, false);
			}

			// The files were lazily added to the treeview. Make sure they've been added to the TV
			// before we process the remaining command line args.
			if (files.Length > 0)
				Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => HandleAppArgs2(appArgs)));
			else
				HandleAppArgs2(appArgs);
		}

		void HandleAppArgs2(IAppCommandLineArgs appArgs) {
			Debug2.Assert(exportProvider is not null);
			foreach (var handler in exportProvider.GetExports<IAppCommandLineArgsHandler>().OrderBy(a => a.Value.Order))
				handler.Value.OnNewArgs(appArgs);
		}

		IDecompiler? GetDecompiler(string language) {
			Debug2.Assert(exportProvider is not null);
			if (string.IsNullOrEmpty(language))
				return null;

			var decompilerService = exportProvider.GetExportedValue<IDecompilerService>();
			if (Guid.TryParse(language, out var guid)) {
				var lang = decompilerService.Find(guid);
				if (lang is not null)
					return lang;
			}

			return decompilerService.AllDecompilers.FirstOrDefault(a => StringComparer.OrdinalIgnoreCase.Equals(a.UniqueNameUI, language)) ??
				decompilerService.AllDecompilers.FirstOrDefault(a => StringComparer.OrdinalIgnoreCase.Equals(a.GenericNameUI, language));
		}
	}
}

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

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace dnSpy.Avalonia {
	sealed partial class App : Application {
		DnSpyHost? host;

		public override void Initialize() {
			AvaloniaXamlLoader.Load(this);
			AsmEditor.DialogSetup.Initialize();
		}

		public override void OnFrameworkInitializationCompleted() {
			if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
				host = new DnSpyHost();
				desktop.MainWindow = new MainWindow(host, desktop.Args ?? System.Array.Empty<string>());
				desktop.Exit += (s, e) => host.SaveSettings();
			}
			base.OnFrameworkInitializationCompleted();
		}
	}
}

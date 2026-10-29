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
using dnSpy.Contracts.Settings;

namespace dnSpy.Avalonia.Editing {
	// The compiler options the WPF app saves (dnSpy.Roslyn's CSharpCompilerSettingsImpl). Same settings
	// section so both apps use the same options. There's no options dialog yet so they're only read.
	sealed class CSharpCompilerSettings {
		static readonly Guid SETTINGS_GUID = new Guid("F1634589-21AD-42DC-A729-E23CBD7072D2");

		public string PreprocessorSymbols { get; } = "TRACE";
		public bool Optimize { get; } = true;
		public bool CheckOverflow { get; }
		public bool AllowUnsafe { get; } = true;

		public CSharpCompilerSettings(ISettingsService settingsService) {
			var sect = settingsService.GetOrCreateSection(SETTINGS_GUID);
			PreprocessorSymbols = sect.Attribute<string>(nameof(PreprocessorSymbols)) ?? PreprocessorSymbols;
			Optimize = sect.Attribute<bool?>(nameof(Optimize)) ?? Optimize;
			CheckOverflow = sect.Attribute<bool?>(nameof(CheckOverflow)) ?? CheckOverflow;
			AllowUnsafe = sect.Attribute<bool?>(nameof(AllowUnsafe)) ?? AllowUnsafe;
		}
	}

	// dnSpy.Roslyn's VisualBasicCompilerSettingsImpl
	sealed class VisualBasicCompilerSettings {
		static readonly Guid SETTINGS_GUID = new Guid("2366A90D-C708-4A10-81AF-7887976BC3FB");

		public string PreprocessorSymbols { get; } = "TRACE";
		public bool Optimize { get; } = true;
		public bool OptionExplicit { get; } = true;
		public bool OptionInfer { get; } = true;
		public bool OptionStrict { get; }
		public bool OptionCompareBinary { get; } = true;
		public bool EmbedVBRuntime { get; }

		public VisualBasicCompilerSettings(ISettingsService settingsService) {
			var sect = settingsService.GetOrCreateSection(SETTINGS_GUID);
			PreprocessorSymbols = sect.Attribute<string>(nameof(PreprocessorSymbols)) ?? PreprocessorSymbols;
			Optimize = sect.Attribute<bool?>(nameof(Optimize)) ?? Optimize;
			OptionExplicit = sect.Attribute<bool?>(nameof(OptionExplicit)) ?? OptionExplicit;
			OptionInfer = sect.Attribute<bool?>(nameof(OptionInfer)) ?? OptionInfer;
			OptionStrict = sect.Attribute<bool?>(nameof(OptionStrict)) ?? OptionStrict;
			OptionCompareBinary = sect.Attribute<bool?>(nameof(OptionCompareBinary)) ?? OptionCompareBinary;
			EmbedVBRuntime = sect.Attribute<bool?>(nameof(EmbedVBRuntime)) ?? EmbedVBRuntime;
		}
	}
}

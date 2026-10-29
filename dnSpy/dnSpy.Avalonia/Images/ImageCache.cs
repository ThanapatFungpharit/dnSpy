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
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace dnSpy.Avalonia.Images {
	// Loads the dnSpy.Images 16x16 PNGs. The names are DsImageStrings names.
	static class ImageCache {
		static readonly Dictionary<string, Bitmap?> cache = new Dictionary<string, Bitmap?>(StringComparer.Ordinal);

		public static Bitmap? Get(string name) {
			if (cache.TryGetValue(name, out var bitmap))
				return bitmap;
			try {
				using (var stream = AssetLoader.Open(new Uri($"avares://dnSpy.Avalonia/Images/{name}.16.16.png")))
					bitmap = new Bitmap(stream);
			}
			catch (Exception ex) when (ex is System.IO.FileNotFoundException || ex is ArgumentException) {
				bitmap = null;
			}
			cache[name] = bitmap;
			return bitmap;
		}
	}
}

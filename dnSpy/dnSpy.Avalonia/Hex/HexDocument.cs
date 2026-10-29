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

namespace dnSpy.Avalonia.Hex {
	// The bytes shown by a hex editor. Bytes can only be overwritten, the length never changes,
	// same as the WPF hex editor.
	sealed class HexDocument {
		readonly byte[] data;
		byte[] savedData;
		readonly List<Change> undoList = new List<Change>();
		readonly List<Change> redoList = new List<Change>();
		// Each change gets a new version number. The current version is the version of the last change that
		// hasn't been undone (0 if none) and it's compared with savedVersion to know if it's modified.
		int version;
		int savedVersion;
		int lastVersionNumber;
		// The change that the next write can be merged with. Undo, redo and save stop the merging.
		Change? mergeableChange;

		sealed class Change {
			public long Offset { get; }
			public byte[] OldData { get; private set; }
			public byte[] NewData { get; private set; }
			public int Version { get; set; }

			public Change(long offset, byte[] oldData, byte[] newData) {
				Offset = offset;
				OldData = oldData;
				NewData = newData;
			}

			// Typing more bytes right after (or over) this change's bytes extends it so it's undone in one step
			public bool TryMerge(long offset, byte[] oldData, byte[] newData) {
				if (offset < Offset || offset > Offset + NewData.Length)
					return false;
				int start = (int)(offset - Offset);
				int newLength = Math.Max(NewData.Length, start + newData.Length);
				var mergedOld = new byte[newLength];
				var mergedNew = new byte[newLength];
				Array.Copy(OldData, mergedOld, OldData.Length);
				Array.Copy(NewData, mergedNew, NewData.Length);
				for (int i = 0; i < newData.Length; i++) {
					if (start + i >= OldData.Length)
						mergedOld[start + i] = oldData[i];
					mergedNew[start + i] = newData[i];
				}
				OldData = mergedOld;
				NewData = mergedNew;
				return true;
			}
		}

		public string? Filename { get; private set; }
		public string Name { get; }
		public long Length => data.Length;
		public bool IsModified => version != savedVersion;
		public bool CanUndo => undoList.Count > 0;
		public bool CanRedo => redoList.Count > 0;

		// Raised when bytes were changed and when the document was saved
		public event EventHandler? Changed;

		public HexDocument(string name, string? filename, byte[] data) {
			Name = name;
			Filename = filename;
			this.data = data;
			savedData = (byte[])data.Clone();
		}

		public static HexDocument Load(string filename) =>
			new HexDocument(Path.GetFileName(filename), filename, File.ReadAllBytes(filename));

		public byte this[long offset] => data[offset];

		// Bytes that are different from the bytes on disk
		public bool IsByteModified(long offset) => data[offset] != savedData[offset];

		public byte[] Read(long offset, int count) {
			if (offset < 0 || offset > data.Length)
				throw new ArgumentOutOfRangeException(nameof(offset));
			count = (int)Math.Min(count, data.Length - offset);
			var result = new byte[count];
			Array.Copy(data, offset, result, 0, count);
			return result;
		}

		// Overwrites bytes. Bytes after the end are ignored. If merge is true and the bytes are next to the
		// previous write, it's undone together with it.
		public void Write(long offset, byte[] bytes, bool merge = false) {
			if (offset < 0 || offset >= data.Length)
				return;
			int count = (int)Math.Min(bytes.Length, data.Length - offset);
			if (count <= 0)
				return;
			var oldData = Read(offset, count);
			var newData = count == bytes.Length ? (byte[])bytes.Clone() : bytes.AsSpan(0, count).ToArray();
			Array.Copy(newData, 0, data, offset, count);
			redoList.Clear();
			var last = mergeableChange;
			if (!merge || last is null || !last.TryMerge(offset, oldData, newData))
				undoList.Add(last = new Change(offset, oldData, newData));
			last.Version = version = ++lastVersionNumber;
			mergeableChange = last;
			Changed?.Invoke(this, EventArgs.Empty);
		}

		// Returns the offset of the undone change or null if there was nothing to undo
		public long? Undo() {
			if (undoList.Count == 0)
				return null;
			var change = undoList[undoList.Count - 1];
			undoList.RemoveAt(undoList.Count - 1);
			mergeableChange = null;
			Array.Copy(change.OldData, 0, data, change.Offset, change.OldData.Length);
			redoList.Add(change);
			version = undoList.Count == 0 ? 0 : undoList[undoList.Count - 1].Version;
			Changed?.Invoke(this, EventArgs.Empty);
			return change.Offset;
		}

		public long? Redo() {
			if (redoList.Count == 0)
				return null;
			var change = redoList[redoList.Count - 1];
			redoList.RemoveAt(redoList.Count - 1);
			mergeableChange = null;
			Array.Copy(change.NewData, 0, data, change.Offset, change.NewData.Length);
			undoList.Add(change);
			version = change.Version;
			Changed?.Invoke(this, EventArgs.Empty);
			return change.Offset;
		}

		public void Save(string filename) {
			// Write to a temp file first so a failed write doesn't destroy the original file
			var tempFilename = filename + ".tmp" + Guid.NewGuid().ToString("N").Substring(0, 8);
			File.WriteAllBytes(tempFilename, data);
			File.Move(tempFilename, filename, overwrite: true);
			Filename = filename;
			savedData = (byte[])data.Clone();
			savedVersion = version;
			mergeableChange = null;
			Changed?.Invoke(this, EventArgs.Empty);
		}
	}
}

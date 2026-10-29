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
using dnSpy.Contracts.Decompiler;
using Microsoft.VisualStudio.Text;

namespace dnSpy.Contracts.Documents.Tabs.DocViewer {
	/// <summary>
	/// A reference in the text
	/// </summary>
	public sealed class TextReference {
		/// <summary>
		/// Gets the reference or null
		/// </summary>
		public object? Reference { get; }

		/// <summary>
		/// Gets the flags
		/// </summary>
		public DecompilerReferenceFlags Flags { get; }

		/// <summary>
		/// true if it's a local, parameter, or label
		/// </summary>
		public bool IsLocal => (Flags & DecompilerReferenceFlags.Local) != 0;

		/// <summary>
		/// true if it's a definition
		/// </summary>
		public bool IsDefinition => (Flags & DecompilerReferenceFlags.Definition) != 0;

		/// <summary>
		/// true if it's a write to a reference
		/// </summary>
		public bool IsWrite => (Flags & DecompilerReferenceFlags.IsWrite) != 0;

		/// <summary>
		/// true if reference shouldn't be highlighted
		/// </summary>
		public bool IsHidden => (Flags & DecompilerReferenceFlags.Hidden) != 0;

		/// <summary>
		/// true if reference can't be followed
		/// </summary>
		public bool NoFollow => (Flags & DecompilerReferenceFlags.NoFollow) != 0;

		/// <summary>
		/// Gets the span or null if it's unknown
		/// </summary>
		[Obsolete("Use TextSpan instead. Text editor code can convert it with ToVsSpan(). This member will be removed in dnSpy 7.0.")]
		public Span? Span => TextSpan is TextSpan span ? new Span(span.Start, span.Length) : null;

		/// <summary>
		/// Gets the span or null if it's unknown
		/// </summary>
		public TextSpan? TextSpan { get; }

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="reference">Reference or null</param>
		public TextReference(object? reference)
			: this(reference, DecompilerReferenceFlags.None) {
		}

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="reference">Reference or null</param>
		/// <param name="span">Span</param>
		[Obsolete("Use TextReference(object?, TextSpan) instead. This member will be removed in dnSpy 7.0.")]
		public TextReference(object? reference, Span span)
			: this(reference, DecompilerReferenceFlags.None, span) {
		}

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="reference">Reference or null</param>
		/// <param name="span">Span</param>
		public TextReference(object? reference, TextSpan span)
			: this(reference, DecompilerReferenceFlags.None, span) {
		}

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="reference">Reference or null</param>
		/// <param name="flags">Flags</param>
		public TextReference(object? reference, DecompilerReferenceFlags flags) {
			Reference = reference;
			Flags = flags;
			TextSpan = null;
		}

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="reference">Reference or null</param>
		/// <param name="flags">Flags</param>
		/// <param name="span">Span</param>
		[Obsolete("Use TextReference(object?, DecompilerReferenceFlags, TextSpan) instead. This member will be removed in dnSpy 7.0.")]
		public TextReference(object? reference, DecompilerReferenceFlags flags, Span span)
			: this(reference, flags, new TextSpan(span.Start, span.Length)) {
		}

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="reference">Reference or null</param>
		/// <param name="flags">Flags</param>
		/// <param name="span">Span</param>
		public TextReference(object? reference, DecompilerReferenceFlags flags, TextSpan span) {
			Reference = reference;
			Flags = flags;
			TextSpan = span;
		}
	}
}

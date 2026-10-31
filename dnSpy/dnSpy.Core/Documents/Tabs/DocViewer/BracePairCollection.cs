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
using System.Diagnostics.CodeAnalysis;
using dnSpy.Contracts.Decompiler;
using dnSpy.Contracts.Text;

namespace dnSpy.Documents.Tabs.DocViewer {
	sealed class BracePairCollection {
		public static readonly BracePairCollection Empty = new BracePairCollection(Array.Empty<CodeBracesRange>());

		readonly SpanDataCollection<TextSpan> leftSorted;
		readonly SpanDataCollection<TextSpan> rightSorted;

		public BracePairCollection(CodeBracesRange[] ranges) {
			if (ranges is null)
				throw new ArgumentNullException(nameof(ranges));

			if (ranges.Length == 0) {
				leftSorted = SpanDataCollection<TextSpan>.Empty;
				rightSorted = SpanDataCollection<TextSpan>.Empty;
			}
			else {
				int prevEnd;
				var builder = SpanDataCollectionBuilder<TextSpan>.CreateBuilder(ranges.Length);

				Array.Sort(ranges, LeftSorter.Instance);
				prevEnd = 0;
				foreach (var p in ranges) {
					if (!p.Flags.IsBraces())
						continue;
					if (prevEnd <= p.Left.Start) {
						builder.Add(p.Left, p.Right);
						prevEnd = p.Left.End;
					}
				}
				leftSorted = builder.Create();

				builder.Clear();
				Array.Sort(ranges, RightSorter.Instance);
				prevEnd = 0;
				foreach (var p in ranges) {
					if (!p.Flags.IsBraces())
						continue;
					if (prevEnd <= p.Right.Start) {
						builder.Add(p.Right, p.Left);
						prevEnd = p.Right.End;
					}
				}
				rightSorted = builder.Create();
			}
		}

		sealed class LeftSorter : IComparer<CodeBracesRange> {
			public static readonly LeftSorter Instance = new LeftSorter();
			public int Compare([AllowNull] CodeBracesRange x, [AllowNull] CodeBracesRange y) => x.Left.Start - y.Left.Start;
		}

		sealed class RightSorter : IComparer<CodeBracesRange> {
			public static readonly RightSorter Instance = new RightSorter();
			public int Compare([AllowNull] CodeBracesRange x, [AllowNull] CodeBracesRange y) => x.Right.Start - y.Right.Start;
		}

		public BracePairResultCollection? GetBracePairs(int position) {
			var left = leftSorted.Find(position);
			var right = rightSorted.Find(position);
			if (left is not null && right is not null)
				return new BracePairResultCollection(new BracePairResult(left.Value.TextSpan, left.Value.Data), new BracePairResult(right.Value.Data, right.Value.TextSpan));
			if (left is not null)
				return new BracePairResultCollection(new BracePairResult(left.Value.TextSpan, left.Value.Data), null);
			if (right is not null)
				return new BracePairResultCollection(new BracePairResult(right.Value.Data, right.Value.TextSpan), null);
			return null;
		}
	}

	readonly struct BracePairResultCollection : IEquatable<BracePairResultCollection> {
		public readonly BracePairResult First;
		public readonly BracePairResult? Second;
		public BracePairResultCollection(BracePairResult first, BracePairResult? second) {
			First = first;
			Second = second;
		}

		public bool Equals(BracePairResultCollection other) => First.Equals(other.First) && Nullable.Equals(Second, other.Second);
		public override bool Equals(object? obj) => obj is BracePairResultCollection && Equals((BracePairResultCollection)obj);
		public override int GetHashCode() => First.GetHashCode() ^ (Second?.GetHashCode() ?? 0);
		public override string ToString() => Second is null ? First.ToString() : "{" + First.ToString() + "," + Second.Value.ToString() + "}";
	}

	readonly struct BracePairResult : IEquatable<BracePairResult> {
		public TextSpan Left { get; }
		public TextSpan Right { get; }
		public BracePairResult(TextSpan left, TextSpan right) {
			Debug.Assert(left.Start < right.Start);
			Left = left;
			Right = right;
		}

		public static bool operator ==(BracePairResult left, BracePairResult right) => left.Equals(right);
		public static bool operator !=(BracePairResult left, BracePairResult right) => !left.Equals(right);
		public bool Equals(BracePairResult other) => Left == other.Left && Right == other.Right;
		public override bool Equals(object? obj) => obj is BracePairResult && Equals((BracePairResult)obj);
		public override int GetHashCode() => Left.GetHashCode() ^ Right.GetHashCode();
		public override string ToString() => "[" + Left.ToString() + "," + Right.ToString() + "]";
	}
}

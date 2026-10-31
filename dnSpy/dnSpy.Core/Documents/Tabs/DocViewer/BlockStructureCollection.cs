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
	sealed class BlockStructureCollection {
		public static readonly BlockStructureCollection Empty = new BlockStructureCollection(Array.Empty<CodeBracesRange>());

		readonly SpanDataCollection<CodeBracesRange[]> coll;

		readonly struct Builder {
			readonly List<SpanData<CodeBracesRange[]>> infos;
			readonly List<CodeBracesRange> list;
			readonly Stack<CodeBracesRange[]> listStack;

			public Builder(CodeBracesRange[] ranges) {
				infos = new List<SpanData<CodeBracesRange[]>>();
				list = new List<CodeBracesRange>();
				listStack = new Stack<CodeBracesRange[]>();
				Array.Sort(ranges, Sorter.Instance);

				for (int i = 0; i < ranges.Length; i++) {
					var curr = ranges[i];
					if (!curr.Flags.IsBlock())
						continue;

					Close(curr.Left.Start);
					Add(curr);
				}
				Close(int.MaxValue);
				Debug.Assert(list.Count == 0);
				int write = 0;
				for (int read = 0; read < infos.Count; read++) {
					var info = infos[read];
					if (info.TextSpan.Length == 0)
						continue;
					if (read != write)
						infos[write] = info;
					write++;
				}
				if (write != infos.Count)
					infos.RemoveRange(write, infos.Count - write);
#if DEBUG
				for (int i = 1; i < infos.Count; i++) {
					if (infos[i - 1].TextSpan.End > infos[i].TextSpan.Start)
						throw new InvalidOperationException();
				}
#endif
			}

			void Add(CodeBracesRange curr) {
				list.Add(curr);
				listStack.Push(list.ToArray());
				infos.Add(new SpanData<CodeBracesRange[]>(new TextSpan(curr.Left.Start, 0), listStack.Peek()));
			}

			void Close(int pos) {
				while (list.Count > 0) {
					int index = list.Count - 1;
					var last = list[index];
					if (last.Right.End <= pos) {
						var tmp = infos[infos.Count - 1];
						infos[infos.Count - 1] = new SpanData<CodeBracesRange[]>(TextSpan.FromBounds(tmp.TextSpan.Start, last.Right.End), tmp.Data);
						list.RemoveAt(index);
						listStack.Pop();
						if (list.Count > 0)
							infos.Add(new SpanData<CodeBracesRange[]>(new TextSpan(last.Right.End, 0), listStack.Peek()));
					}
					else {
						var tmp = infos[infos.Count - 1];
						Debug.Assert(tmp.TextSpan.Length == 0);
						infos[infos.Count - 1] = new SpanData<CodeBracesRange[]>(TextSpan.FromBounds(tmp.TextSpan.Start, Math.Min(pos, last.Right.End)), tmp.Data);
						break;
					}
				}
			}

			public SpanData<CodeBracesRange[]>[] Build() => infos.ToArray();

			sealed class Sorter : IComparer<CodeBracesRange> {
				public static readonly Sorter Instance = new Sorter();
				public int Compare([AllowNull] CodeBracesRange x, [AllowNull] CodeBracesRange y) {
					int c = x.Left.Start - y.Left.Start;
					if (c != 0)
						return c;
					return y.Left.End - x.Left.End;
				}
			}
		}

		public BlockStructureCollection(CodeBracesRange[] ranges) {
			if (ranges.Length == 0)
				coll = SpanDataCollection<CodeBracesRange[]>.Empty;
			else {
				var builder = new Builder(ranges);
				coll = new SpanDataCollection<CodeBracesRange[]>(builder.Build());
			}
		}

		// Gets all blocks that intersect the line, except blocks that end where the line starts
		public IEnumerable<CodeBracesRange> GetRanges(TextSpan lineExtent) {
			foreach (var spanData in coll.Find(lineExtent)) {
				if (spanData.TextSpan.End == lineExtent.Start)
					continue;
				foreach (var info in spanData.Data)
					yield return info;
			}
		}
	}
}

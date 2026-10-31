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
using dnSpy.Contracts.Decompiler;
using dnSpy.Contracts.Documents.Tabs.DocViewer;
using dnSpy.Contracts.Text;
using dnSpy.Contracts.Text.Editor;
using Microsoft.VisualStudio.Text;

namespace dnSpy.Documents.Tabs.DocViewer {
	sealed class BlockStructureServiceDataProvider : IBlockStructureServiceDataProvider {
		readonly LazyBlockStructureCollection lazyCollection;

		BlockStructureServiceDataProvider(LazyBlockStructureCollection lazyCollection) => this.lazyCollection = lazyCollection ?? throw new ArgumentNullException(nameof(lazyCollection));

		public static IBlockStructureServiceDataProvider? TryCreate(IDocumentViewer documentViewer) {
			var lazyColl = documentViewer.Content.GetCustomData<LazyBlockStructureCollection>(DocumentViewerContentDataIds.BlockStructure);
			return lazyColl is null ? null : new BlockStructureServiceDataProvider(lazyColl);
		}

		public void GetData(SnapshotSpan lineExtent, List<BlockStructureData> list) {
			foreach (var info in lazyCollection.Collection.GetRanges(lineExtent.Span.ToDsTextSpan())) {
				var data = CreateBlockStructureData(info, lineExtent.Snapshot);
				if (data is not null)
					list.Add(data.Value);
			}
		}

		static BlockStructureData? CreateBlockStructureData(CodeBracesRange info, ITextSnapshot snapshot) {
			var blockKind = GetBlockKind(info.Flags);
			if (blockKind == BlockStructureKind.None)
				return null;

			if (info.Right.End > snapshot.Length)
				return null;
			if (info.Left.End > info.Right.Start)
				return null;
			var top = new SnapshotSpan(snapshot, info.Left.Start, info.Left.Length);
			var bottom = new SnapshotSpan(snapshot, info.Right.Start, info.Right.Length);
			return new BlockStructureData(top, bottom, blockKind);
		}

		static BlockStructureKind GetBlockKind(CodeBracesRangeFlags flags) {
			switch (flags.ToBlockKind()) {
			case CodeBracesRangeFlags.BlockKind_Namespace:		return BlockStructureKind.Namespace;
			case CodeBracesRangeFlags.BlockKind_Type:			return BlockStructureKind.Type;
			case CodeBracesRangeFlags.BlockKind_Module:			return BlockStructureKind.Module;
			case CodeBracesRangeFlags.BlockKind_ValueType:		return BlockStructureKind.ValueType;
			case CodeBracesRangeFlags.BlockKind_Interface:		return BlockStructureKind.Interface;
			case CodeBracesRangeFlags.BlockKind_Method:			return BlockStructureKind.Method;
			case CodeBracesRangeFlags.BlockKind_Accessor:		return BlockStructureKind.Accessor;
			case CodeBracesRangeFlags.BlockKind_AnonymousMethod:return BlockStructureKind.AnonymousMethod;
			case CodeBracesRangeFlags.BlockKind_Constructor:	return BlockStructureKind.Constructor;
			case CodeBracesRangeFlags.BlockKind_Destructor:		return BlockStructureKind.Destructor;
			case CodeBracesRangeFlags.BlockKind_Operator:		return BlockStructureKind.Operator;
			case CodeBracesRangeFlags.BlockKind_Conditional:	return BlockStructureKind.Conditional;
			case CodeBracesRangeFlags.BlockKind_Loop:			return BlockStructureKind.Loop;
			case CodeBracesRangeFlags.BlockKind_Property:		return BlockStructureKind.Property;
			case CodeBracesRangeFlags.BlockKind_Event:			return BlockStructureKind.Event;
			case CodeBracesRangeFlags.BlockKind_Try:			return BlockStructureKind.Try;
			case CodeBracesRangeFlags.BlockKind_Catch:			return BlockStructureKind.Catch;
			case CodeBracesRangeFlags.BlockKind_Filter:			return BlockStructureKind.Filter;
			case CodeBracesRangeFlags.BlockKind_Finally:		return BlockStructureKind.Finally;
			case CodeBracesRangeFlags.BlockKind_Fault:			return BlockStructureKind.Fault;
			case CodeBracesRangeFlags.BlockKind_Lock:			return BlockStructureKind.Lock;
			case CodeBracesRangeFlags.BlockKind_Using:			return BlockStructureKind.Using;
			case CodeBracesRangeFlags.BlockKind_Fixed:			return BlockStructureKind.Fixed;
			case CodeBracesRangeFlags.BlockKind_Switch:			return BlockStructureKind.Switch;
			case CodeBracesRangeFlags.BlockKind_Case:			return BlockStructureKind.Case;
			case CodeBracesRangeFlags.BlockKind_LocalFunction:	return BlockStructureKind.LocalFunction;
			case CodeBracesRangeFlags.BlockKind_Other:			return BlockStructureKind.Other;
			case CodeBracesRangeFlags.BlockKind_Xml:			return BlockStructureKind.Xml;
			case CodeBracesRangeFlags.BlockKind_Xaml:			return BlockStructureKind.Xaml;
			default:
				Debug.Fail($"Unknown block kind: {flags.ToBlockKind()}");
				return BlockStructureKind.None;
			}
		}
	}
}

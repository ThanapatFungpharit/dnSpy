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

using dnlib.DotNet;
using dnSpy.BamlDecompiler.Baml;
using dnSpy.Contracts.Decompiler;
using dnSpy.Contracts.Documents.Tabs.DocViewer.ToolTips;
using dnSpy.Contracts.Images;
using dnSpy.Contracts.Text;

namespace dnSpy.BamlDecompiler {
	[ExportDocumentViewerToolTipProvider]
	sealed class BamlDocumentViewerToolTipProvider : IDocumentViewerToolTipProvider {
		public object Create(IDocumentViewerToolTipProviderContext context, object @ref) {
			if (@ref is BamlStringReference bref) {
				var provider = context.Create();
				provider.Image = DsImages.String;
				provider.Output.Write(BoxedTextColor.String, bref.String);
				return provider.Create();
			}
			if (@ref is BamlAssemblyReference aref) {
				var provider = context.Create();
				provider.Image = DsImages.Assembly;
				provider.Output.Write(aref.Assembly);
				return provider.Create();
			}
			if (@ref is BamlRecordReference recRef) {
				var provider = context.Create();
				provider.Output.Write(BoxedTextColor.Keyword, recRef.Record.Type.ToString());
				provider.Output.WriteLine();
				provider.Output.Write(BoxedTextColor.Text, "Position");
				provider.Output.Write(BoxedTextColor.Punctuation, ":");
				provider.Output.Write(BoxedTextColor.Text, " ");
				provider.Output.Write(BoxedTextColor.Number, $"0x{recRef.Record.Position:x}");
				return provider.Create();
			}
			if (@ref is BamlResourceReference resRef) {
				var provider = context.Create();
				provider.Output.Write(BoxedTextColor.Type, resRef.TypeName);
				provider.Output.Write(BoxedTextColor.Punctuation, ".");
				if (resRef.KeyName is not null)
					provider.Output.Write(BoxedTextColor.Text, resRef.KeyName);
				else
					provider.Output.Write(BoxedTextColor.InstanceProperty, resRef.PropertyName);
				return provider.Create();
			}
			if (@ref is BamlTypeReference typeRef) {
				var provider = context.Create();
				if (typeRef.Type is TypeRef tr)
					provider.SetImage((ITypeDefOrRef)tr.Resolve() ?? tr);
				else
					provider.SetImage(typeRef.Type);
				context.Decompiler.WriteType(provider.Output, typeRef.Type, true);
				return provider.Create();
			}
			if (@ref is BamlAttributeReference attrRef) {
				var provider = context.Create();
				provider.Image = DsImages.Property;
				context.Decompiler.WriteType(provider.Output, attrRef.Type, true);
				provider.Output.Write(BoxedTextColor.Punctuation, ".");
				provider.Output.Write(BoxedTextColor.InstanceProperty, IdentifierEscaper.Escape(attrRef.MemberName));
				return provider.Create();
			}

			return null;
		}
	}
}

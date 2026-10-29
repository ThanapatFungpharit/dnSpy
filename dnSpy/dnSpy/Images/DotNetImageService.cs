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

using System.ComponentModel.Composition;
using dnlib.DotNet;
using dnlib.PE;
using dnSpy.Contracts.Images;

namespace dnSpy.Images {
	[Export(typeof(IDotNetImageService))]
	sealed class DotNetImageService : IDotNetImageService {
		static ImageReference Create(string name) => new ImageReference(DsImages.Assembly.Assembly, name);

		public ImageReference GetImageReference(IPEImage peImage) => Create(DotNetImageNames.GetName(peImage.ImageNTHeaders.FileHeader.Characteristics));
		public ImageReference GetNamespaceImageReference() => DsImages.Namespace;
		public ImageReference GetImageReference(ModuleDef module) => Create(DotNetImageNames.GetName(module));
		public ImageReference GetImageReference(TypeDef type) => Create(DotNetImageNames.GetName(type));
		public ImageReference GetImageReference(FieldDef field) => Create(DotNetImageNames.GetName(field));
		public ImageReference GetImageReference(MethodDef method) => Create(DotNetImageNames.GetName(method));
		public ImageReference GetImageReference(EventDef @event) => Create(DotNetImageNames.GetName(@event));
		public ImageReference GetImageReference(PropertyDef property) => Create(DotNetImageNames.GetName(property));
		public ImageReference GetImageReferenceModuleRef() => DsImages.Reference;
		public ImageReference GetImageReference(AssemblyDef assembly) => Create(DotNetImageNames.GetName(assembly));
		public ImageReference GetImageReferenceAssemblyRef() => DsImages.Reference;
		public ImageReference GetImageReferenceGenericParameter() => DsImages.Type;
		public ImageReference GetImageReferenceLocal() => DsImages.LocalVariable;
		public ImageReference GetImageReferenceParameter() => DsImages.LocalVariable;
		public ImageReference GetImageReferenceType() => DsImages.ClassPublic;
		public ImageReference GetImageReferenceMethod() => DsImages.MethodPublic;
		public ImageReference GetImageReferenceField() => DsImages.FieldPublic;
	}
}

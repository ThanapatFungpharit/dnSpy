/*
    Copyright (C) 2026 ThanapatFungpharit

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

#if NETFRAMEWORK || WINDOWS
using System.Resources;
#endif

namespace dnSpy.Decompiler.MSBuild {
	/// <summary>
	/// <c>System.Windows.Forms</c> ResX constants. WinForms isn't available on non-Windows platforms
	/// so the values are copied from the .NET WinForms implementation.
	/// </summary>
	static class ResXConstants {
		public const string WinFormsAssemblyName = "System.Windows.Forms";
		public const string WinFormsAssemblyFullName = "System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089";
		public const string ResourcesNamespace = "System.Resources";

#if NETFRAMEWORK || WINDOWS
		public static readonly string ResourceSchema = ResXResourceWriter.ResourceSchema;
		public static readonly string ResMimeType = ResXResourceWriter.ResMimeType;
		public static readonly string Version = ResXResourceWriter.Version;
		public static readonly string BinSerializedObjectMimeType = ResXResourceWriter.BinSerializedObjectMimeType;
		public static readonly string ByteArraySerializedObjectMimeType = ResXResourceWriter.ByteArraySerializedObjectMimeType;
#else
		public const string ResMimeType = "text/microsoft-resx";
		public const string Version = "2.0";
		public const string BinSerializedObjectMimeType = "application/x-microsoft.net.object.binary.base64";
		public const string ByteArraySerializedObjectMimeType = "application/x-microsoft.net.object.bytearray.base64";
		public const string ResourceSchema = @"<xsd:schema id=""root"" xmlns="""" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"" xmlns:msdata=""urn:schemas-microsoft-com:xml-msdata"">
    <xsd:import namespace=""http://www.w3.org/XML/1998/namespace""/>
    <xsd:element name=""root"" msdata:IsDataSet=""true"">
        <xsd:complexType>
            <xsd:choice maxOccurs=""unbounded"">
                <xsd:element name=""metadata"">
                    <xsd:complexType>
                        <xsd:sequence>
                            <xsd:element name=""value"" type=""xsd:string"" minOccurs=""0""/>
                        </xsd:sequence>
                        <xsd:attribute name=""name"" use=""required"" type=""xsd:string""/>
                        <xsd:attribute name=""type"" type=""xsd:string""/>
                        <xsd:attribute name=""mimetype"" type=""xsd:string""/>
                        <xsd:attribute ref=""xml:space""/>
                    </xsd:complexType>
                </xsd:element>
                <xsd:element name=""assembly"">
                    <xsd:complexType>
                        <xsd:attribute name=""alias"" type=""xsd:string""/>
                        <xsd:attribute name=""name"" type=""xsd:string""/>
                    </xsd:complexType>
                </xsd:element>
                <xsd:element name=""data"">
                    <xsd:complexType>
                        <xsd:sequence>
                            <xsd:element name=""value"" type=""xsd:string"" minOccurs=""0"" msdata:Ordinal=""1"" />
                            <xsd:element name=""comment"" type=""xsd:string"" minOccurs=""0"" msdata:Ordinal=""2"" />
                        </xsd:sequence>
                        <xsd:attribute name=""name"" type=""xsd:string"" use=""required"" msdata:Ordinal=""1"" />
                        <xsd:attribute name=""type"" type=""xsd:string"" msdata:Ordinal=""3"" />
                        <xsd:attribute name=""mimetype"" type=""xsd:string"" msdata:Ordinal=""4"" />
                        <xsd:attribute ref=""xml:space""/>
                    </xsd:complexType>
                </xsd:element>
                <xsd:element name=""resheader"">
                    <xsd:complexType>
                        <xsd:sequence>
                            <xsd:element name=""value"" type=""xsd:string"" minOccurs=""0"" msdata:Ordinal=""1"" />
                        </xsd:sequence>
                        <xsd:attribute name=""name"" type=""xsd:string"" use=""required"" />
                    </xsd:complexType>
                </xsd:element>
            </xsd:choice>
        </xsd:complexType>
    </xsd:element>
</xsd:schema>";
#endif
	}
}

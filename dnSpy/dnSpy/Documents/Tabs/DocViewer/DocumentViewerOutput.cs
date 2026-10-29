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
using System.Windows;
using System.Windows.Controls;
using dnSpy.Contracts.Documents.Tabs.DocViewer;

namespace dnSpy.Documents.Tabs.DocViewer {
	sealed class DocumentViewerOutput : DocumentViewerOutputBase, IDocumentViewerOutput {
		internal static DocumentViewerOutput Create() => new DocumentViewerOutput();

		DocumentViewerOutput() {
		}

		public void AddUIElement(Func<UIElement> createElement) {
			VerifyGeneratingContent();
			if (createElement is null)
				throw new ArgumentNullException(nameof(createElement));
			AddIndent();
			DisableCaching();
			AddCustomData(DocumentViewerUIElementConstants.CustomDataId, new DocumentViewerUIElement(NextPosition, createElement));
		}

		public void AddButton(string buttonText, Action clickHandler) {
			VerifyGeneratingContent();
			if (buttonText is null)
				throw new ArgumentNullException(nameof(buttonText));
			if (clickHandler is null)
				throw new ArgumentNullException(nameof(clickHandler));
			AddUIElement(() => {
				var button = new Button { Content = buttonText };
				button.SetResourceReference(FrameworkElement.StyleProperty, "TextEditorButton");
				button.Click += (s, e) => {
					e.Handled = true;
					clickHandler();
				};
				return button;
			});
		}
	}
}

/*
    Copyright (C) 2014-2026 de4dot@gmail.com

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
using System.Linq;
using System.Text;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnSpy.AsmEditor.Properties;
using dnSpy.Contracts.MVVM;
using dnSpy.Contracts.Text;

namespace dnSpy.AsmEditor.MethodBody {
	/// <summary>
	/// The UI independent parts of the method body editor's instructions, locals and exception handlers lists:
	/// copying items as text and fixing references when items are pasted
	/// </summary>
	static class MethodBodyListUtils {
		public static string InstructionsToText(InstructionVM[] instrs) {
			var output = new StringBuilderTextColorOutput();

			for (int i = 0; i < instrs.Length; i++) {
				if (i > 0)
					output.WriteLine();

				var instr = instrs[i];
				output.Write(BoxedTextColor.Number, instr.Index.ToString());
				output.Write(BoxedTextColor.Text, "\t");
				output.Write(BoxedTextColor.Label, instr.Offset.ToString("X4"));
				output.Write(BoxedTextColor.Text, "\t");
				output.Write(BoxedTextColor.OpCode, instr.Code.ToOpCode().ToString());

				switch (instr.InstructionOperandVM.InstructionOperandType) {
				case InstructionOperandType.None:
					break;

				case InstructionOperandType.SByte:
					output.Write(BoxedTextColor.Text, "\t");
					output.Write(BoxedTextColor.Number, instr.InstructionOperandVM.SByte.StringValue);
					break;

				case InstructionOperandType.Byte:
					output.Write(BoxedTextColor.Text, "\t");
					output.Write(BoxedTextColor.Number, instr.InstructionOperandVM.Byte.StringValue);
					break;

				case InstructionOperandType.Int32:
					output.Write(BoxedTextColor.Text, "\t");
					output.Write(BoxedTextColor.Number, instr.InstructionOperandVM.Int32.StringValue);
					break;

				case InstructionOperandType.Int64:
					output.Write(BoxedTextColor.Text, "\t");
					output.Write(BoxedTextColor.Number, instr.InstructionOperandVM.Int64.StringValue);
					break;

				case InstructionOperandType.Single:
					output.Write(BoxedTextColor.Text, "\t");
					output.Write(BoxedTextColor.Number, instr.InstructionOperandVM.Single.StringValue);
					break;

				case InstructionOperandType.Double:
					output.Write(BoxedTextColor.Text, "\t");
					output.Write(BoxedTextColor.Number, instr.InstructionOperandVM.Double.StringValue);
					break;

				case InstructionOperandType.String:
					output.Write(BoxedTextColor.Text, "\t");
					output.Write(BoxedTextColor.String, instr.InstructionOperandVM.String.StringValue);
					break;

				case InstructionOperandType.Field:
				case InstructionOperandType.Method:
				case InstructionOperandType.Token:
				case InstructionOperandType.Type:
				case InstructionOperandType.MethodSig:
				case InstructionOperandType.BranchTarget:
				case InstructionOperandType.SwitchTargets:
				case InstructionOperandType.Local:
				case InstructionOperandType.Parameter:
					output.Write(BoxedTextColor.Text, "\t");
					BodyUtils.WriteObject(output, instr.InstructionOperandVM.Value);
					break;

				default:
					throw new InvalidOperationException();
				}
			}
			if (instrs.Length > 1)
				output.WriteLine();
			return output.ToString();
		}

		public static string LocalsToText(LocalVM[] locals) {
			var output = new StringBuilderTextColorOutput();

			for (int i = 0; i < locals.Length; i++) {
				if (i > 0)
					output.WriteLine();

				var local = locals[i];
				output.Write(BoxedTextColor.Number, local.Index.ToString());
				output.Write(BoxedTextColor.Text, "\t");
				output.Write(BoxedTextColor.Text, local.IsPinned ? dnSpy_AsmEditor_Core_Resources.Local_Pinned_Character : string.Empty);
				output.Write(BoxedTextColor.Text, "\t");
				output.Write(BoxedTextColor.Text, local.DebuggerHidden ? dnSpy_AsmEditor_Core_Resources.Local_CompilerGenerated_Character : string.Empty);
				output.Write(BoxedTextColor.Text, "\t");
				output.Write(BoxedTextColor.Local, local.Name ?? string.Empty);
				output.Write(BoxedTextColor.Text, "\t");
				BodyUtils.WriteObject(output, local.Type);
			}
			if (locals.Length > 1)
				output.WriteLine();
			return output.ToString();
		}

		public static string ExceptionHandlersToText(ExceptionHandlerVM[] ehs) {
			var output = new StringBuilderTextColorOutput();

			for (int i = 0; i < ehs.Length; i++) {
				if (i > 0)
					output.WriteLine();

				var eh = ehs[i];
				output.Write(BoxedTextColor.Number, eh.Index.ToString());
				output.Write(BoxedTextColor.Text, "\t");
				BodyUtils.WriteObject(output, eh.TryStartVM.SelectedItem);
				output.Write(BoxedTextColor.Text, "\t");
				BodyUtils.WriteObject(output, eh.TryEndVM.SelectedItem);
				output.Write(BoxedTextColor.Text, "\t");
				BodyUtils.WriteObject(output, eh.FilterStartVM.SelectedItem);
				output.Write(BoxedTextColor.Text, "\t");
				BodyUtils.WriteObject(output, eh.HandlerStartVM.SelectedItem);
				output.Write(BoxedTextColor.Text, "\t");
				BodyUtils.WriteObject(output, eh.HandlerEndVM.SelectedItem);
				output.Write(BoxedTextColor.Text, "\t");
				output.Write(BoxedTextColor.Text, ((EnumVM)eh.HandlerTypeVM.Items[eh.HandlerTypeVM.SelectedIndex]).Name);
				output.Write(BoxedTextColor.Text, "\t");
				BodyUtils.WriteObject(output, eh.CatchType);
			}
			if (ehs.Length > 1)
				output.WriteLine();
			return output.ToString();
		}

		/// <summary>
		/// Returns one hex number per line, or an empty string if there are no numbers
		/// </summary>
		/// <param name="values">Numbers</param>
		public static string ToHexLines(IEnumerable<ulong> values, int digits = 8) {
			var sb = new StringBuilder();
			int lines = 0;
			foreach (var value in values) {
				if (lines++ > 0)
					sb.AppendLine();
				sb.Append("0x" + value.ToString("X" + digits.ToString()));
			}
			if (lines > 1)
				sb.AppendLine();
			return sb.ToString();
		}

		public static string InstructionOffsetsToText(ulong baseOffset, InstructionVM[] instrs) =>
			ToHexLines(instrs.Select(a => baseOffset + a.Offset));

		public static uint? GetOperandMDToken(InstructionOperandVM op) {
			switch (op.InstructionOperandType) {
			case InstructionOperandType.None:
			case InstructionOperandType.SByte:
			case InstructionOperandType.Byte:
			case InstructionOperandType.Int32:
			case InstructionOperandType.Int64:
			case InstructionOperandType.Single:
			case InstructionOperandType.Double:
			case InstructionOperandType.String:
			case InstructionOperandType.BranchTarget:
			case InstructionOperandType.SwitchTargets:
			case InstructionOperandType.Local:
			case InstructionOperandType.Parameter:
				return null;

			case InstructionOperandType.Field:
			case InstructionOperandType.Method:
			case InstructionOperandType.Token:
			case InstructionOperandType.Type:
				var token = op.Other as IMDTokenProvider;
				return token is null ? (uint?)null : token.MDToken.ToUInt32();

			case InstructionOperandType.MethodSig:
				var msig = op.Other as MethodSig;
				return msig is null ? (uint?)null : msig.OriginalToken;

			default:
				throw new InvalidOperationException();
			}
		}

		public static string OperandMDTokensToText(InstructionVM[] instrs) =>
			ToHexLines(instrs.Select(a => GetOperandMDToken(a.InstructionOperandVM)).OfType<uint>().Select(a => (ulong)a));

		public static uint? GetCatchTypeToken(ITypeDefOrRef? type) => type is null ? (uint?)null : type.MDToken.Raw;

		public static string CatchTypeMDTokensToText(ExceptionHandlerVM[] ehs) =>
			ToHexLines(ehs.Select(a => GetCatchTypeToken(a.CatchType)).OfType<uint>().Select(a => (ulong)a));

		/// <summary>
		/// Imports pasted instructions that were copied from another method
		/// </summary>
		public static InstructionVM[] ImportInstructions(CilBodyVM cilBodyVM, InstructionVM[] data, bool fromThisInstance) {
			if (fromThisInstance)
				return data;
			var newData = new InstructionVM[data.Length];
			for (int i = 0; i < data.Length; i++)
				newData[i] = data[i].Import(cilBodyVM.OwnerModule);
			return newData;
		}

		/// <summary>
		/// Fixes the branch targets, switch targets, locals and parameters of pasted instructions
		/// </summary>
		/// <param name="cilBodyVM">Body</param>
		/// <param name="data">Pasted instructions</param>
		/// <param name="origData">Copied instructions</param>
		/// <param name="fromThisInstance">true if they were copied from this list</param>
		public static void FixPastedInstructions(CilBodyVM cilBodyVM, InstructionVM[] data, InstructionVM[] origData, bool fromThisInstance) {
			var dict = new Dictionary<uint, InstructionVM>();
			for (int i = 0; i < data.Length; i++) {
				if (origData[i] == InstructionVM.Null)
					continue;
				Debug.Assert(!dict.ContainsKey(origData[i].Offset));
				dict[origData[i].Offset] = data[i];
			}
			var createdLocals = new Dictionary<LocalVM, LocalVM>();
			createdLocals[LocalVM.Null] = LocalVM.Null;

			// Need to fix references to instructions and locals
			InstructionVM oldInstr;
			InstructionVM? newInstr;
			for (int i = 0; i < data.Length; i++) {
				var instr = data[i];
				var origInstr = origData[i];
				var op = instr.InstructionOperandVM;
				switch (op.InstructionOperandType) {
				case InstructionOperandType.BranchTarget:
					oldInstr = (origInstr.InstructionOperandVM.OperandListItem as InstructionVM) ?? InstructionVM.Null;
					if (oldInstr == InstructionVM.Null || !dict.TryGetValue(oldInstr.Offset, out newInstr))
						newInstr = fromThisInstance ? oldInstr : InstructionVM.Null;
					op.OperandListItem = newInstr;
					break;

				case InstructionOperandType.SwitchTargets:
					var oldInstrs = (origInstr.InstructionOperandVM.Other as InstructionVM[]) ?? Array.Empty<InstructionVM>();
					var newInstrs = new InstructionVM[oldInstrs.Length];
					for (int j = 0; j < oldInstrs.Length; j++) {
						oldInstr = oldInstrs[j] ?? InstructionVM.Null;
						if (oldInstr == InstructionVM.Null || !dict.TryGetValue(oldInstr.Offset, out newInstr))
							newInstr = fromThisInstance ? oldInstr : InstructionVM.Null;
						newInstrs[j] = newInstr;
					}
					op.Other = newInstrs;
					break;

				case InstructionOperandType.Local:
					if (!fromThisInstance) {
						var oldLocal = (origInstr.InstructionOperandVM.OperandListItem as LocalVM) ?? LocalVM.Null;
						if (!createdLocals.TryGetValue(oldLocal, out var newLocal)) {
							newLocal = oldLocal.Import(cilBodyVM.TypeSigCreatorOptions, cilBodyVM.OwnerModule);
							cilBodyVM.LocalsListVM.Add(newLocal);
							createdLocals.Add(oldLocal, newLocal);
						}
						op.OperandListItem = newLocal;
					}
					break;

				case InstructionOperandType.Parameter:
					if (!fromThisInstance) {
						// Can't reference a parameter in another method
						op.OperandListItem = BodyUtils.NullParameter;
					}
					break;
				}
			}
		}

		/// <summary>
		/// Imports pasted locals that were copied from another method
		/// </summary>
		public static LocalVM[] ImportLocals(CilBodyVM cilBodyVM, LocalVM[] data, bool fromThisInstance) {
			if (fromThisInstance)
				return data;
			var newData = new LocalVM[data.Length];
			for (int i = 0; i < data.Length; i++)
				newData[i] = data[i].Import(cilBodyVM.TypeSigCreatorOptions, cilBodyVM.OwnerModule);
			return newData;
		}
	}
}

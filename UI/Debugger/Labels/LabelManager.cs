﻿using Mesen.GUI.Config;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Mesen.GUI.Debugger.Labels
{
	public class LabelManager
	{
		public static Regex LabelRegex { get; } = new Regex("^[@_a-zA-Z]+[@_.a-zA-Z0-9]*$", RegexOptions.Compiled);
		public static Regex AssertRegex { get; } = new Regex(@"assert\((.*)\)", RegexOptions.Compiled);

		private static Dictionary<UInt64, CodeLabel> _labelsByKey = new Dictionary<UInt64, CodeLabel>();
		private static HashSet<CodeLabel> _labels = new HashSet<CodeLabel>();
		private static Dictionary<string, CodeLabel> _reverseLookup = new Dictionary<string, CodeLabel>();

		public static event EventHandler OnLabelUpdated;

		public static void ResetLabels()
		{
			DebugApi.ClearLabels();
			_labels.Clear();
			_labelsByKey.Clear();
			_reverseLookup.Clear();
		}

		public static CodeLabel GetLabel(UInt32 address, SnesMemoryType type)
		{
			CodeLabel label;
			_labelsByKey.TryGetValue(GetKey(address, type), out label);
			return label;
		}

		public static CodeLabel GetLabel(AddressInfo relAddress)
		{
			AddressInfo absAddress = DebugApi.GetAbsoluteAddress(relAddress);
			if(absAddress.Address >= 0) {
				return GetLabel((UInt32)absAddress.Address, absAddress.Type);
			}
			return null;
		}

		public static CodeLabel GetLabel(string label)
		{
			return _reverseLookup.ContainsKey(label) ? _reverseLookup[label] : null;
		}

		public static void SetLabels(IEnumerable<CodeLabel> labels, bool raiseEvents = true)
		{
			foreach(CodeLabel label in labels) {
				SetLabel(label, false);
			}
			if(raiseEvents) {
				ProcessLabelUpdate();
			}
		}

		public static List<CodeLabel> GetLabels(CpuType cpu)
		{
			if(cpu == CpuType.Sa1 || cpu == CpuType.Gsu) {
				//Share label list between SNES CPU, SA1 and GSU (since the share the same PRG ROM)
				cpu = CpuType.Cpu;
			}

			return _labels.Where((lbl) => lbl.Matches(cpu)).ToList<CodeLabel>();
		}

		public static List<CodeLabel> GetAllLabels()
		{
			return _labels.ToList();
		}

		private static UInt64 GetKey(UInt32 address, SnesMemoryType memType)
		{
			switch(memType) {
				case SnesMemoryType.PrgRom: return address | ((ulong)1 << 32);
				case SnesMemoryType.WorkRam: return address | ((ulong)2 << 32);
				case SnesMemoryType.SaveRam: return address | ((ulong)3 << 32);
				case SnesMemoryType.Register: return address | ((ulong)4 << 32);
				case SnesMemoryType.SpcRam: return address | ((ulong)5 << 32);
				case SnesMemoryType.SpcRom: return address | ((ulong)6 << 32);
				case SnesMemoryType.Sa1InternalRam: return address | ((ulong)7 << 32);
				case SnesMemoryType.GsuWorkRam: return address | ((ulong)8 << 32);
				case SnesMemoryType.BsxPsRam: return address | ((ulong)9 << 32);
				case SnesMemoryType.BsxMemoryPack: return address | ((ulong)10 << 32);
				case SnesMemoryType.DspProgramRom: return address | ((ulong)11 << 32);
				case SnesMemoryType.GbPrgRom: return address | ((ulong)12 << 32);
				case SnesMemoryType.GbWorkRam: return address | ((ulong)13 << 32);
				case SnesMemoryType.GbCartRam: return address | ((ulong)14 << 32);
				case SnesMemoryType.GbHighRam: return address | ((ulong)15 << 32);
				case SnesMemoryType.GbBootRom: return address | ((ulong)16 << 32);
				case SnesMemoryType.GameboyMemory: return address | ((ulong)17 << 32);
			}
			throw new Exception("Invalid type");
		}

		private static void SetLabel(uint address, SnesMemoryType memType, string label, string comment)
		{
			LabelManager.SetLabel(new CodeLabel() {
				Address = address,
				MemoryType = memType,
				Label = label,
				Comment = comment
			}, false);
		}

		public static bool SetLabel(CodeLabel label, bool raiseEvent)
		{
			if(_reverseLookup.ContainsKey(label.Label)) {
				//Another identical label exists, we need to remove it
				DeleteLabel(_reverseLookup[label.Label], false);
			}

			string comment = label.Comment;
			for(UInt32 i = label.Address; i < label.Address + label.Length; i++) {
				UInt64 key = GetKey(i, label.MemoryType);
				CodeLabel existingLabel;
				if(_labelsByKey.TryGetValue(key, out existingLabel)) {
					DeleteLabel(existingLabel, false);
					_reverseLookup.Remove(existingLabel.Label);
				}

				_labelsByKey[key] = label;

				if(label.Length == 1) {
					DebugApi.SetLabel(i, label.MemoryType, label.Label, comment.Replace(Environment.NewLine, "\n"));
				} else {
					DebugApi.SetLabel(i, label.MemoryType, label.Label + "+" + (i - label.Address).ToString(), comment.Replace(Environment.NewLine, "\n"));

					//Only set the comment on the first byte of multi-byte comments
					comment = "";
				}
			}

			_labels.Add(label);
			if(label.Label.Length > 0) {
				_reverseLookup[label.Label] = label;
			}

			if(raiseEvent) {
				ProcessLabelUpdate();
				RefreshDisassembly(label);
			}

			return true;
		}

		public static void DeleteLabel(CodeLabel label, bool raiseEvent)
		{
			bool needEvent = false;

			_labels.Remove(label);
			for(UInt32 i = label.Address; i < label.Address + label.Length; i++) {
				UInt64 key = GetKey(i, label.MemoryType);
				if(_labelsByKey.ContainsKey(key)) {
					_reverseLookup.Remove(_labelsByKey[key].Label);
				}

				if(_labelsByKey.Remove(key)) {
					DebugApi.SetLabel(i, label.MemoryType, string.Empty, string.Empty);
					if(raiseEvent) {
						needEvent = true;
					}
				}
			}

			if(needEvent) {
				ProcessLabelUpdate();
				RefreshDisassembly(label);
			}
		}

		/*public static void CreateAutomaticJumpLabels()
		{
			byte[] cdlData = InteropEmu.DebugGetPrgCdlData();
			List<CodeLabel> labelsToAdd = new List<CodeLabel>();
			for(int i = 0; i < cdlData.Length; i++) {
				if((cdlData[i] & (byte)CdlPrgFlags.JumpTarget) != 0 && LabelManager.GetLabel((uint)i, SnesMemoryType.PrgRom) == null) {
					labelsToAdd.Add(new CodeLabel() { Flags = CodeLabelFlags.AutoJumpLabel, Address = (uint)i, SnesMemoryType = SnesMemoryType.PrgRom, Label = "L" + i.ToString("X4"), Comment = "" });
				}
			}
			if(labelsToAdd.Count > 0) {
				LabelManager.SetLabels(labelsToAdd, true);
			}
		}*/

		private static void RefreshDisassembly(CodeLabel label)
		{
			if(label.MemoryType.ToCpuType() == CpuType.Spc) {
				DebugApi.RefreshDisassembly(CpuType.Spc);
			} else if(label.MemoryType.ToCpuType() == CpuType.NecDsp) {
				DebugApi.RefreshDisassembly(CpuType.NecDsp);
			} else if(label.MemoryType.ToCpuType() == CpuType.Gameboy) {
				DebugApi.RefreshDisassembly(CpuType.Gameboy);
			} else {
				DebugApi.RefreshDisassembly(CpuType.Cpu);
				DebugApi.RefreshDisassembly(CpuType.Sa1);
				DebugApi.RefreshDisassembly(CpuType.Gsu);
			}
		}

		public static void RefreshLabels()
		{
			DebugApi.ClearLabels();
			LabelManager.SetLabels(new List<CodeLabel>(_labels), true);
		}

		private static void ProcessLabelUpdate()
		{
			OnLabelUpdated?.Invoke(null, null);
			UpdateAssertBreakpoints();
		}

		private static void UpdateAssertBreakpoints()
		{
			List<Breakpoint> asserts = new List<Breakpoint>();

			Action<CodeLabel, string, CpuType> addAssert = (CodeLabel label, string condition, CpuType cpuType) => {
				asserts.Add(new Breakpoint() {
					BreakOnExec = true,
					MemoryType = label.MemoryType,
					CpuType = cpuType,
					Address = label.Address,
					Condition = "!(" + condition + ")",
					IsAssert = true
				});
			};

			foreach(CodeLabel label in _labels) {
				foreach(string commentLine in label.Comment.Split('\n')) {
					Match m = LabelManager.AssertRegex.Match(commentLine);
					if(m.Success) {
						CpuType cpuType = label.MemoryType.ToCpuType();
						addAssert(label, m.Groups[1].Value, cpuType);
						if(cpuType == CpuType.Cpu) {
							addAssert(label, m.Groups[1].Value, CpuType.Sa1);
						}
					}
				}
			}

			BreakpointManager.Asserts = asserts;
			BreakpointManager.SetBreakpoints();
		}

		public static void SetDefaultLabels()
		{
			CoprocessorType coproc = EmuApi.GetRomInfo().CoprocessorType;
			if(coproc == CoprocessorType.Gameboy || coproc == CoprocessorType.SGB) {
				SetGameboyDefaultLabels();
			}

			if(coproc != CoprocessorType.Gameboy) {
				SetSnesDefaultLabels();
			}
		}

		private static void SetSnesDefaultLabels()
		{
			//B-Bus registers
			LabelManager.SetLabel(0x2100, SnesMemoryType.Register, "INIDISP", "Screen Display Register");
			LabelManager.SetLabel(0x2101, SnesMemoryType.Register, "OBSEL", "Object Size and Character Size Register");
			LabelManager.SetLabel(0x2102, SnesMemoryType.Register, "OAMADDL", "OAM Address Registers (Low)");
			LabelManager.SetLabel(0x2103, SnesMemoryType.Register, "OAMADDH", "OAM Address Registers (High)");
			LabelManager.SetLabel(0x2104, SnesMemoryType.Register, "OAMDATA", "OAM Data Write Register");
			LabelManager.SetLabel(0x2105, SnesMemoryType.Register, "BGMODE", "BG Mode and Character Size Register");
			LabelManager.SetLabel(0x2106, SnesMemoryType.Register, "MOSAIC", "Mosaic Register");
			LabelManager.SetLabel(0x2107, SnesMemoryType.Register, "BG1SC", "BG Tilemap Address Registers (BG1)");
			LabelManager.SetLabel(0x2108, SnesMemoryType.Register, "BG2SC", "BG Tilemap Address Registers (BG2)");
			LabelManager.SetLabel(0x2109, SnesMemoryType.Register, "BG3SC", "BG Tilemap Address Registers (BG3)");
			LabelManager.SetLabel(0x210A, SnesMemoryType.Register, "BG4SC", "BG Tilemap Address Registers (BG4)");
			LabelManager.SetLabel(0x210B, SnesMemoryType.Register, "BG12NBA", "BG Character Address Registers (BG1&2)");
			LabelManager.SetLabel(0x210C, SnesMemoryType.Register, "BG34NBA", "BG Character Address Registers (BG3&4)");
			LabelManager.SetLabel(0x210D, SnesMemoryType.Register, "BG1HOFS", "BG Scroll Registers (BG1)");
			LabelManager.SetLabel(0x210E, SnesMemoryType.Register, "BG1VOFS", "BG Scroll Registers (BG1)");
			LabelManager.SetLabel(0x210F, SnesMemoryType.Register, "BG2HOFS", "BG Scroll Registers (BG2)");
			LabelManager.SetLabel(0x2110, SnesMemoryType.Register, "BG2VOFS", "BG Scroll Registers (BG2)");
			LabelManager.SetLabel(0x2111, SnesMemoryType.Register, "BG3HOFS", "BG Scroll Registers (BG3)");
			LabelManager.SetLabel(0x2112, SnesMemoryType.Register, "BG3VOFS", "BG Scroll Registers (BG3)");
			LabelManager.SetLabel(0x2113, SnesMemoryType.Register, "BG4HOFS", "BG Scroll Registers (BG4)");
			LabelManager.SetLabel(0x2114, SnesMemoryType.Register, "BG4VOFS", "BG Scroll Registers (BG4)");
			LabelManager.SetLabel(0x2115, SnesMemoryType.Register, "VMAIN", "Video Port Control Register");
			LabelManager.SetLabel(0x2116, SnesMemoryType.Register, "VMADDL", "VRAM Address Registers (Low)");
			LabelManager.SetLabel(0x2117, SnesMemoryType.Register, "VMADDH", "VRAM Address Registers (High)");
			LabelManager.SetLabel(0x2118, SnesMemoryType.Register, "VMDATAL", "VRAM Data Write Registers (Low)");
			LabelManager.SetLabel(0x2119, SnesMemoryType.Register, "VMDATAH", "VRAM Data Write Registers (High)");
			LabelManager.SetLabel(0x211A, SnesMemoryType.Register, "M7SEL", "Mode 7 Settings Register");
			LabelManager.SetLabel(0x211B, SnesMemoryType.Register, "M7A", "Mode 7 Matrix Registers");
			LabelManager.SetLabel(0x211C, SnesMemoryType.Register, "M7B", "Mode 7 Matrix Registers");
			LabelManager.SetLabel(0x211D, SnesMemoryType.Register, "M7C", "Mode 7 Matrix Registers");
			LabelManager.SetLabel(0x211E, SnesMemoryType.Register, "M7D", "Mode 7 Matrix Registers");
			LabelManager.SetLabel(0x211F, SnesMemoryType.Register, "M7X", "Mode 7 Matrix Registers");
			LabelManager.SetLabel(0x2120, SnesMemoryType.Register, "M7Y", "Mode 7 Matrix Registers");
			LabelManager.SetLabel(0x2121, SnesMemoryType.Register, "CGADD", "CGRAM Address Register");
			LabelManager.SetLabel(0x2122, SnesMemoryType.Register, "CGDATA", "CGRAM Data Write Register");
			LabelManager.SetLabel(0x2123, SnesMemoryType.Register, "W12SEL", "Window Mask Settings Registers");
			LabelManager.SetLabel(0x2124, SnesMemoryType.Register, "W34SEL", "Window Mask Settings Registers");
			LabelManager.SetLabel(0x2125, SnesMemoryType.Register, "WOBJSEL", "Window Mask Settings Registers");
			LabelManager.SetLabel(0x2126, SnesMemoryType.Register, "WH0", "Window Position Registers (WH0)");
			LabelManager.SetLabel(0x2127, SnesMemoryType.Register, "WH1", "Window Position Registers (WH1)");
			LabelManager.SetLabel(0x2128, SnesMemoryType.Register, "WH2", "Window Position Registers (WH2)");
			LabelManager.SetLabel(0x2129, SnesMemoryType.Register, "WH3", "Window Position Registers (WH3)");
			LabelManager.SetLabel(0x212A, SnesMemoryType.Register, "WBGLOG", "Window Mask Logic registers (BG)");
			LabelManager.SetLabel(0x212B, SnesMemoryType.Register, "WOBJLOG", "Window Mask Logic registers (OBJ)");
			LabelManager.SetLabel(0x212C, SnesMemoryType.Register, "TM", "Screen Destination Registers");
			LabelManager.SetLabel(0x212D, SnesMemoryType.Register, "TS", "Screen Destination Registers");
			LabelManager.SetLabel(0x212E, SnesMemoryType.Register, "TMW", "Window Mask Destination Registers");
			LabelManager.SetLabel(0x212F, SnesMemoryType.Register, "TSW", "Window Mask Destination Registers");
			LabelManager.SetLabel(0x2130, SnesMemoryType.Register, "CGWSEL", "Color Math Registers");
			LabelManager.SetLabel(0x2131, SnesMemoryType.Register, "CGADSUB", "Color Math Registers");
			LabelManager.SetLabel(0x2132, SnesMemoryType.Register, "COLDATA", "Color Math Registers");
			LabelManager.SetLabel(0x2133, SnesMemoryType.Register, "SETINI", "Screen Mode Select Register");
			LabelManager.SetLabel(0x2134, SnesMemoryType.Register, "MPYL", "Multiplication Result Registers");
			LabelManager.SetLabel(0x2135, SnesMemoryType.Register, "MPYM", "Multiplication Result Registers");
			LabelManager.SetLabel(0x2136, SnesMemoryType.Register, "MPYH", "Multiplication Result Registers");
			LabelManager.SetLabel(0x2137, SnesMemoryType.Register, "SLHV", "Software Latch Register");
			LabelManager.SetLabel(0x2138, SnesMemoryType.Register, "OAMDATAREAD", "OAM Data Read Register");
			LabelManager.SetLabel(0x2139, SnesMemoryType.Register, "VMDATALREAD", "VRAM Data Read Register (Low)");
			LabelManager.SetLabel(0x213A, SnesMemoryType.Register, "VMDATAHREAD", "VRAM Data Read Register (High)");
			LabelManager.SetLabel(0x213B, SnesMemoryType.Register, "CGDATAREAD", "CGRAM Data Read Register");
			LabelManager.SetLabel(0x213C, SnesMemoryType.Register, "OPHCT", "Scanline Location Registers (Horizontal)");
			LabelManager.SetLabel(0x213D, SnesMemoryType.Register, "OPVCT", "Scanline Location Registers (Vertical)");
			LabelManager.SetLabel(0x213E, SnesMemoryType.Register, "STAT77", "PPU Status Register");
			LabelManager.SetLabel(0x213F, SnesMemoryType.Register, "STAT78", "PPU Status Register");
			LabelManager.SetLabel(0x2140, SnesMemoryType.Register, "APUIO0", "APU IO Registers");
			LabelManager.SetLabel(0x2141, SnesMemoryType.Register, "APUIO1", "APU IO Registers");
			LabelManager.SetLabel(0x2142, SnesMemoryType.Register, "APUIO2", "APU IO Registers");
			LabelManager.SetLabel(0x2143, SnesMemoryType.Register, "APUIO3", "APU IO Registers");
			LabelManager.SetLabel(0x2180, SnesMemoryType.Register, "WMDATA", "WRAM Data Register");
			LabelManager.SetLabel(0x2181, SnesMemoryType.Register, "WMADDL", "WRAM Address Registers");
			LabelManager.SetLabel(0x2182, SnesMemoryType.Register, "WMADDM", "WRAM Address Registers");
			LabelManager.SetLabel(0x2183, SnesMemoryType.Register, "WMADDH", "WRAM Address Registers");

			//A-Bus registers (CPU registers)
			LabelManager.SetLabel(0x4016, SnesMemoryType.Register, "JOYSER0", "Old Style Joypad Registers");
			LabelManager.SetLabel(0x4017, SnesMemoryType.Register, "JOYSER1", "Old Style Joypad Registers");

			LabelManager.SetLabel(0x4200, SnesMemoryType.Register, "NMITIMEN", "Interrupt Enable Register");
			LabelManager.SetLabel(0x4201, SnesMemoryType.Register, "WRIO", "IO Port Write Register");
			LabelManager.SetLabel(0x4202, SnesMemoryType.Register, "WRMPYA", "Multiplicand Registers");
			LabelManager.SetLabel(0x4203, SnesMemoryType.Register, "WRMPYB", "Multiplicand Registers");
			LabelManager.SetLabel(0x4204, SnesMemoryType.Register, "WRDIVL", "Divisor & Dividend Registers");
			LabelManager.SetLabel(0x4205, SnesMemoryType.Register, "WRDIVH", "Divisor & Dividend Registers");
			LabelManager.SetLabel(0x4206, SnesMemoryType.Register, "WRDIVB", "Divisor & Dividend Registers");
			LabelManager.SetLabel(0x4207, SnesMemoryType.Register, "HTIMEL", "IRQ Timer Registers (Horizontal - Low)");
			LabelManager.SetLabel(0x4208, SnesMemoryType.Register, "HTIMEH", "IRQ Timer Registers (Horizontal - High)");
			LabelManager.SetLabel(0x4209, SnesMemoryType.Register, "VTIMEL", "IRQ Timer Registers (Vertical - Low)");
			LabelManager.SetLabel(0x420A, SnesMemoryType.Register, "VTIMEH", "IRQ Timer Registers (Vertical - High)");
			LabelManager.SetLabel(0x420B, SnesMemoryType.Register, "MDMAEN", "DMA Enable Register");
			LabelManager.SetLabel(0x420C, SnesMemoryType.Register, "HDMAEN", "HDMA Enable Register");
			LabelManager.SetLabel(0x420D, SnesMemoryType.Register, "MEMSEL", "ROM Speed Register");
			LabelManager.SetLabel(0x4210, SnesMemoryType.Register, "RDNMI", "Interrupt Flag Registers");
			LabelManager.SetLabel(0x4211, SnesMemoryType.Register, "TIMEUP", "Interrupt Flag Registers");
			LabelManager.SetLabel(0x4212, SnesMemoryType.Register, "HVBJOY", "PPU Status Register");
			LabelManager.SetLabel(0x4213, SnesMemoryType.Register, "RDIO", "IO Port Read Register");
			LabelManager.SetLabel(0x4214, SnesMemoryType.Register, "RDDIVL", "Multiplication Or Divide Result Registers (Low)");
			LabelManager.SetLabel(0x4215, SnesMemoryType.Register, "RDDIVH", "Multiplication Or Divide Result Registers (High)");
			LabelManager.SetLabel(0x4216, SnesMemoryType.Register, "RDMPYL", "Multiplication Or Divide Result Registers (Low)");
			LabelManager.SetLabel(0x4217, SnesMemoryType.Register, "RDMPYH", "Multiplication Or Divide Result Registers (High)");
			LabelManager.SetLabel(0x4218, SnesMemoryType.Register, "JOY1L", "Controller Port Data Registers (Pad 1 - Low)");
			LabelManager.SetLabel(0x4219, SnesMemoryType.Register, "JOY1H", "Controller Port Data Registers (Pad 1 - High)");
			LabelManager.SetLabel(0x421A, SnesMemoryType.Register, "JOY2L", "Controller Port Data Registers (Pad 2 - Low)");
			LabelManager.SetLabel(0x421B, SnesMemoryType.Register, "JOY2H", "Controller Port Data Registers (Pad 2 - High)");
			LabelManager.SetLabel(0x421C, SnesMemoryType.Register, "JOY3L", "Controller Port Data Registers (Pad 3 - Low)");
			LabelManager.SetLabel(0x421D, SnesMemoryType.Register, "JOY3H", "Controller Port Data Registers (Pad 3 - High)");
			LabelManager.SetLabel(0x421E, SnesMemoryType.Register, "JOY4L", "Controller Port Data Registers (Pad 4 - Low)");
			LabelManager.SetLabel(0x421F, SnesMemoryType.Register, "JOY4H", "Controller Port Data Registers (Pad 4 - High)");

			//DMA registers
			for(uint i = 0; i < 8; i++) {
				LabelManager.SetLabel(0x4300 + i * 0x10, SnesMemoryType.Register, "DMAP" + i.ToString(), "(H)DMA Control");
				LabelManager.SetLabel(0x4301 + i * 0x10, SnesMemoryType.Register, "BBAD" + i.ToString(), "(H)DMA B-Bus Address");
				LabelManager.SetLabel(0x4302 + i * 0x10, SnesMemoryType.Register, "A1T" + i.ToString() + "L", "DMA A-Bus Address / HDMA Table Address (Low)");
				LabelManager.SetLabel(0x4303 + i * 0x10, SnesMemoryType.Register, "A1T" + i.ToString() + "H", "DMA A-Bus Address / HDMA Table Address (High)");
				LabelManager.SetLabel(0x4304 + i * 0x10, SnesMemoryType.Register, "A1B" + i.ToString(), "DMA A-Bus Address / HDMA Table Address (Bank)");
				LabelManager.SetLabel(0x4305 + i * 0x10, SnesMemoryType.Register, "DAS" + i.ToString() + "L", "DMA Size / HDMA Indirect Address (Low)");
				LabelManager.SetLabel(0x4306 + i * 0x10, SnesMemoryType.Register, "DAS" + i.ToString() + "H", "DMA Size / HDMA Indirect Address (High)");
				LabelManager.SetLabel(0x4307 + i * 0x10, SnesMemoryType.Register, "DAS" + i.ToString() + "B", "HDMA Indirect Address (Bank)");
				LabelManager.SetLabel(0x4308 + i * 0x10, SnesMemoryType.Register, "A2A" + i.ToString() + "L", "HDMA Mid Frame Table Address (Low)");
				LabelManager.SetLabel(0x4309 + i * 0x10, SnesMemoryType.Register, "A2A" + i.ToString() + "H", "HDMA Mid Frame Table Address (High)");
				LabelManager.SetLabel(0x430A + i * 0x10, SnesMemoryType.Register, "NTLR" + i.ToString(), "HDMA Line Counter");
			}

			//SPC registers
			LabelManager.SetLabel(0xF0, SnesMemoryType.SpcRam, "TEST", "Testing functions");
			LabelManager.SetLabel(0xF1, SnesMemoryType.SpcRam, "CONTROL", "I/O and Timer Control");
			LabelManager.SetLabel(0xF2, SnesMemoryType.SpcRam, "DSPADDR", "DSP Address");
			LabelManager.SetLabel(0xF3, SnesMemoryType.SpcRam, "DSPDATA", "DSP Data");
			LabelManager.SetLabel(0xF4, SnesMemoryType.SpcRam, "CPUIO1", "CPU I/O 1");
			LabelManager.SetLabel(0xF5, SnesMemoryType.SpcRam, "CPUIO2", "CPU I/O 1");
			LabelManager.SetLabel(0xF6, SnesMemoryType.SpcRam, "CPUIO3", "CPU I/O 1");
			LabelManager.SetLabel(0xF7, SnesMemoryType.SpcRam, "CPUIO4", "CPU I/O 1");
			LabelManager.SetLabel(0xF8, SnesMemoryType.SpcRam, "RAMREG1", "Memory Register 1");
			LabelManager.SetLabel(0xF9, SnesMemoryType.SpcRam, "RAMREG2", "Memory Register 2");
			LabelManager.SetLabel(0xFA, SnesMemoryType.SpcRam, "T0TARGET", "Timer 0 scaling target");
			LabelManager.SetLabel(0xFB, SnesMemoryType.SpcRam, "T1TARGET", "Timer 1 scaling target");
			LabelManager.SetLabel(0xFC, SnesMemoryType.SpcRam, "T2TARGET", "Timer 2 scaling target");
			LabelManager.SetLabel(0xFD, SnesMemoryType.SpcRam, "T0OUT", "Timer 0 output");
			LabelManager.SetLabel(0xFE, SnesMemoryType.SpcRam, "T1OUT", "Timer 1 output");
			LabelManager.SetLabel(0xFF, SnesMemoryType.SpcRam, "T2OUT", "Timer 2 output");
		}

		private static void SetGameboyDefaultLabels()
		{
			//LCD
			LabelManager.SetLabel(0xFF40, SnesMemoryType.GameboyMemory, "LCDC_FF40", "LCD Control");
			LabelManager.SetLabel(0xFF41, SnesMemoryType.GameboyMemory, "STAT_FF41", "LCD Status");
			LabelManager.SetLabel(0xFF42, SnesMemoryType.GameboyMemory, "SCY_FF42", "Scroll Y");
			LabelManager.SetLabel(0xFF43, SnesMemoryType.GameboyMemory, "SCX_FF43", "Scroll X");
			LabelManager.SetLabel(0xFF44, SnesMemoryType.GameboyMemory, "LY_FF44", "LCD Y-Coordinate");
			LabelManager.SetLabel(0xFF45, SnesMemoryType.GameboyMemory, "LYC_FF45", "LY Compare");

			LabelManager.SetLabel(0xFF47, SnesMemoryType.GameboyMemory, "BGP_FF47", "BG Palette Data");
			LabelManager.SetLabel(0xFF48, SnesMemoryType.GameboyMemory, "OBP0_FF48", "Object Palette 0 Data");
			LabelManager.SetLabel(0xFF49, SnesMemoryType.GameboyMemory, "OBP1_FF49", "Object Palette 1 Data");

			LabelManager.SetLabel(0xFF4A, SnesMemoryType.GameboyMemory, "WY_FF4A", "Window Y Position");
			LabelManager.SetLabel(0xFF4B, SnesMemoryType.GameboyMemory, "WX_FF4B", "Window X Position");

			//APU
			LabelManager.SetLabel(0xFF10, SnesMemoryType.GameboyMemory, "NR10_FF10", "Channel 1 Sweep");
			LabelManager.SetLabel(0xFF11, SnesMemoryType.GameboyMemory, "NR11_FF11", "Channel 1 Length/Wave Pattern Duty");
			LabelManager.SetLabel(0xFF12, SnesMemoryType.GameboyMemory, "NR12_FF12", "Channel 1 Volume Envelope");
			LabelManager.SetLabel(0xFF13, SnesMemoryType.GameboyMemory, "NR13_FF13", "Channel 1 Frequency Low");
			LabelManager.SetLabel(0xFF14, SnesMemoryType.GameboyMemory, "NR14_FF14", "Channel 1 Frequency High");

			LabelManager.SetLabel(0xFF16, SnesMemoryType.GameboyMemory, "NR21_FF16", "Channel 2 Length/Wave Pattern Duty");
			LabelManager.SetLabel(0xFF17, SnesMemoryType.GameboyMemory, "NR22_FF17", "Channel 2 Volume Envelope");
			LabelManager.SetLabel(0xFF18, SnesMemoryType.GameboyMemory, "NR23_FF18", "Channel 2 Frequency Low");
			LabelManager.SetLabel(0xFF19, SnesMemoryType.GameboyMemory, "NR24_FF19", "Channel 2 Frequency High");
			
			LabelManager.SetLabel(0xFF1A, SnesMemoryType.GameboyMemory, "NR30_FF1A", "Channel 3 On/Off ");
			LabelManager.SetLabel(0xFF1B, SnesMemoryType.GameboyMemory, "NR31_FF1B", "Channel 3 Length");
			LabelManager.SetLabel(0xFF1C, SnesMemoryType.GameboyMemory, "NR32_FF1C", "Channel 3 Output Level");
			LabelManager.SetLabel(0xFF1D, SnesMemoryType.GameboyMemory, "NR33_FF1D", "Channel 3 Frequency Low");
			LabelManager.SetLabel(0xFF1E, SnesMemoryType.GameboyMemory, "NR34_FF1E", "Channel 3 Frequency High");

			LabelManager.SetLabel(0xFF20, SnesMemoryType.GameboyMemory, "NR41_FF20", "Channel 4 Length");
			LabelManager.SetLabel(0xFF21, SnesMemoryType.GameboyMemory, "NR42_FF21", "Channel 4 Volume Envelope");
			LabelManager.SetLabel(0xFF22, SnesMemoryType.GameboyMemory, "NR43_FF22", "Channel 4 Polynomial Counter");
			LabelManager.SetLabel(0xFF23, SnesMemoryType.GameboyMemory, "NR44_FF23", "Channel 4 Loop");
			
			LabelManager.SetLabel(0xFF24, SnesMemoryType.GameboyMemory, "NR50_FF24", "Channel Volume");
			LabelManager.SetLabel(0xFF25, SnesMemoryType.GameboyMemory, "NR51_FF25", "Channel Left/Right");
			LabelManager.SetLabel(0xFF26, SnesMemoryType.GameboyMemory, "NR52_FF26", "Channel On/Off");
			
			//Others
			LabelManager.SetLabel(0xFF00, SnesMemoryType.GameboyMemory, "JOYP_FF00", "Joypad");
			LabelManager.SetLabel(0xFF01, SnesMemoryType.GameboyMemory, "SB_FF01", "Serial Data");
			LabelManager.SetLabel(0xFF02, SnesMemoryType.GameboyMemory, "SC_FF02", "Serial Control");
			
			LabelManager.SetLabel(0xFF04, SnesMemoryType.GameboyMemory, "DIV_FF04", "Divider");
			LabelManager.SetLabel(0xFF05, SnesMemoryType.GameboyMemory, "TIMA_FF05", "Timer Counter");
			LabelManager.SetLabel(0xFF06, SnesMemoryType.GameboyMemory, "TMA_FF06", "Timer Modulo");
			LabelManager.SetLabel(0xFF07, SnesMemoryType.GameboyMemory, "TAC_FF07", "Timer Control");

			LabelManager.SetLabel(0xFF0F, SnesMemoryType.GameboyMemory, "IF_FF0F", "Interrupt Flag");
			LabelManager.SetLabel(0xFFFF, SnesMemoryType.GameboyMemory, "IE_FFFF", "Interrupt Enable");
			
			LabelManager.SetLabel(0xFF46, SnesMemoryType.GameboyMemory, "DMA_FF46", "OAM DMA Start");
		}
	}

	[Flags]
	public enum CodeLabelFlags
	{
		None = 0,
		AutoJumpLabel = 1
	}
}

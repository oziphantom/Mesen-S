using Mesen.GUI.Debugger.Labels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Mesen.GUI.Debugger
{
	public class Tass64DumpLabels
	{
		public static void Import(string path, bool silent = false)
		{
			List<CodeLabel> labels = new List<CodeLabel>(1000);
			List<Breakpoint> breakpoints = new List<Breakpoint>(1000);

			int errorCount = 0;
			foreach (string row in File.ReadAllLines(path, Encoding.UTF8))
			{
				string lineData = row.Trim();
				if (lineData.StartsWith("<")) continue; //this is a <command line>: operation and we don't want it
				if (lineData.Contains(":=")) continue; //this is a "variable" we dont' want it

				int splitIndex = lineData.IndexOf(' ');

				string[] parts = lineData.Substring(splitIndex + 1).Split('=');
				
				UInt32 address;
				string value = parts[1].Trim();
				NumberStyles type = NumberStyles.Integer;
				if(value.StartsWith("$"))
				{
					type = NumberStyles.HexNumber;
					value = value.Substring(1); //remove the $
				}
				else if(value.StartsWith("%"))
				{
					continue; // Binary values are not labels 99.99999999999999% of the time
				}

				if (!UInt32.TryParse(value, type, null, out address))
				{
					errorCount++;
					continue;
				}

				AddressInfo absAddress = DebugApi.GetAbsoluteAddress(new AddressInfo() { Address = (int)address, Type = SnesMemoryType.CpuMemory });

				if (absAddress.Address >= 0)
				{
					if (parts[0].Contains("BREAK"))
					{
						//we have a break point
						Breakpoint breakpoint = new Breakpoint();
						breakpoint.Address = address;
						breakpoint.AddressType = BreakpointAddressType.SingleAddress;
						breakpoint.BreakOnExec = true;
						breakpoint.CpuType = CpuType.Cpu;
						breakpoints.Add(breakpoint);
					}
					else if (parts[0].Contains("ASSERT_"))
					{
						string assert_field = parts[0].Trim().ToLower().Substring(parts[0].IndexOf("ASSERT_")+7);
						string cond = string.Empty;
						if(assert_field == "a8")
						{
							cond = "(PS & 32) == 32";
						}
						else if(assert_field == "a16")
						{
							cond = "(PS & 32) == 0";
						}
						else if (assert_field == "xy8")
						{
							cond = "(PS & 16) == 16";
						}
						else if (assert_field == "xy16")
						{
							cond = "(PS & 16) == 0";
						}
						else if (assert_field == "axy8")
						{
							cond = "(PS & 48) == 48";
						}
						else if (assert_field == "axy16")
						{
							cond = "(PS & 48) == 32";
						}
						else if (assert_field == "jsl")
						{
							cond = "jslf == 1";
						}
						else if (assert_field == "jsr")
						{
							cond = "jslf == 0";
						}
						else
						{
							cond = assert_field.Replace("_0x", "_$");
							cond = cond.Replace("_eq_", "==");
							cond = cond.Replace("_lt_", "<");
							cond = cond.Replace("_lte_", "<=");
							cond = cond.Replace("_gt_", ">");
							cond = cond.Replace("_gte_", ">=");
							cond = cond.Replace("_ne_", "!=");
							cond = cond.Replace("_and_", "&&");
							cond = cond.Replace("_or_", "||");
							cond = cond.Replace("_not_", "!");
							cond = cond.Replace("_lbrac_", "(");
							cond = cond.Replace("_rbrac_", ")");
							cond = cond.Replace("_", " ");
						}

						Breakpoint breakpoint = new Breakpoint();
						breakpoint.Address = address;
						breakpoint.AddressType = BreakpointAddressType.SingleAddress;
						breakpoint.BreakOnExec = true;
						breakpoint.CpuType = CpuType.Cpu;
						breakpoint.IsAssert = true;
						breakpoint.Condition = "!(" +cond+")";
						breakpoints.Add(breakpoint);
					}
					else if (parts[0].Contains("WATCH_"))
					{
						string[] watchParts = parts[0].Trim().ToLower().Split('_');
						Breakpoint breakpoint = new Breakpoint();
						breakpoint.CpuType = CpuType.Cpu;
						breakpoint.IsAssert = false;
						breakpoint.Condition = String.Empty;
						breakpoint.BreakOnExec = false;
						int range = 1;
						for (int i = 1; i < watchParts.Length; ++i)
						{
							switch (watchParts[i])
							{
								case "load":
								case "read":
									breakpoint.BreakOnRead = true;
									break;
								case "store":
								case "write":
									breakpoint.BreakOnWrite = true;
									break;
								case "readwrite":
								case "writeread":
								case "loadstore":
								case "storeload":
									breakpoint.BreakOnRead = true;
									breakpoint.BreakOnWrite = true;
									break;
								case "word":
									range = 2;
									break;
								case "long":
									range = 3;
									break;
							}
						}
						breakpoint.EndAddress = address - 1;
						switch (range)
						{
							case 1:
								breakpoint.StartAddress = address - 1;								
								breakpoint.AddressType = BreakpointAddressType.SingleAddress;
								break;
							case 2:
								breakpoint.StartAddress = address - 2;
								breakpoint.AddressType = BreakpointAddressType.AddressRange;
								break;
							case 3:
								breakpoint.StartAddress = address - 3;
								breakpoint.AddressType = BreakpointAddressType.AddressRange;
								break;
						}
						breakpoint.Address = breakpoint.StartAddress;
						breakpoints.Add(breakpoint);
					}
					else
					{
						CodeLabel label = new CodeLabel();
						label.Address = (UInt32)absAddress.Address;
						label.MemoryType = absAddress.Type;
						label.Comment = "";
						string labelName = parts[0].Trim();
						if (string.IsNullOrEmpty(labelName) || !LabelManager.LabelRegex.IsMatch(labelName))
						{
							errorCount++;
						}
						else
						{
							label.Label = labelName;
							labels.Add(label);
						}
					}
				}
			}

			LabelManager.SetLabels(labels);
			BreakpointManager.SetBreakpoints(breakpoints);

			if (!silent)
			{
				string message = $"Import completed with {labels.Count} labels imported";
				if (errorCount > 0)
				{
					message += $" and {errorCount} error(s)";
				}
				MessageBox.Show(message, "Mesen-S", MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
		}
	}
}
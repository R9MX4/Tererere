using HarmonyLib;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System;

namespace Horizon
{
	public class S_Help
	{
		public static float Temp2Sunlight(float temperature)
		{
			//0.557: Simulation factor
			//Plan 1:
			return (temperature - 5) * (temperature - 5) / 4.45f / 0.557f;

			//Plan 2: (Choose)
			//return Mathf.Pow((temperature - 5), 1.5f) / 0.257f / 0.557f;

			/* ================== Python3 Simulation ==================
			import math

			totalstep = 600
			PI = 3.141527
			curtemp = 292
			sunlight = 30000

			totaltemp = 0
			max_val = 0
			min_val = 9999
			for step in range(totalstep):
				cursunlight = 0
				if step < 0.875 * totalstep:
					angle = math.sin(step / 0.875 * totalstep * PI)
					cursunlight = angle * sunlight
				if False:
					factor = cursunlight * 0.3 - pow((curtemp - 5), 1.5);
						curtemp += factor * 128 / 1.1e7
				else:
					factor = cursunlight * 5 - pow((curtemp - 5), 2);
						curtemp += factor * 128 / 2e8
				max_val = max(curtemp, max_val)
				min_val = min(curtemp, min_val)
				totaltemp += curtemp
			print("\n")
			print(totaltemp / step, curtemp)
			print(max_val, min_val)
			======================================================== */
		}
		public static string GetModPath()
		{
			return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
		}
		//up 0, down 1, right 2, left 3
		public static int CellShift(int cell, int dir)
		{
			switch (dir)
			{
				case 0: return cell + Grid.WidthInCells;
				case 1: return cell - Grid.WidthInCells;
				case 2: return cell + 1;
				case 3: return cell - 1;
				case 4: return cell + Grid.WidthInCells + 1;
				case 5: return cell - Grid.WidthInCells + 1;
				case 6: return cell + Grid.WidthInCells - 1;
				case 7: return cell - Grid.WidthInCells - 1;
				default: return cell;
			}
		}
		public static void Logger(object content)
		{
			Console.WriteLine(content);
		}
		public static void TLogger(object content)
		{
			//Debug.Log(content);
		}

		public static void DebugON()
		{
			DebugHandler.SetDebugEnabled(true);
			Logger("MOD-Horizon_Launcher: Debug On");
		}

		public static void IL_Dump(List<CodeInstruction> IL_List)
		{
			for (int i = 0; i < IL_List.Count; i++)
			{
				if (IL_List[i].operand == null)
					Console.WriteLine(i + ". " + IL_List[i].opcode);
				else
					Console.WriteLine(i + ". " + IL_List[i].opcode + "\t" + IL_List[i].operand.ToString());
			}
		}
	}
}

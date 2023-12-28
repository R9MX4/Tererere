using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System;
using UnityEngine;

namespace Horizon
{
	public class C_ShowMore
	{
		public class Float_DIG
		{
			[HarmonyPatch(typeof(GameUtil), "GetFormattedThermalConductivity")]
			public class Float_DIG_TC
			{
				public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
				{
					List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
					for (int i = 0; i < codes.Count; i++)
					{
						if (codes[i].opcode == OpCodes.Ldstr && codes[i].operand.ToString() == "0.000")
						{
							codes[i].operand = "0.0#######";
							S_Help.Logger("MOD-Horizon_ShowMore: Float_DIG_TC 0.000=>" + codes[i].operand);
						}
					}
					return codes.AsEnumerable();
				}
			}
			[HarmonyPatch(typeof(GameUtil), "GetFormattedSHC")]
			public class Float_DIG_HC
			{
				public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
				{
					List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
					for (int i = 0; i < codes.Count; i++)
					{
						if (codes[i].opcode == OpCodes.Ldstr && codes[i].operand.ToString() == "0.000")
						{
							codes[i].operand = "0.0#######";
							S_Help.Logger("MOD-Horizon_ShowMore: Float_DIG_HC 0.000=>" + codes[i].operand);
						}
					}
					return codes.AsEnumerable();
				}
			}

			[HarmonyPatch(typeof(AdditionalDetailsPanel), "RefreshDetails")]
			public class Float_DIG_PL
			{
				public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
				{
					List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
					for (int i = 0; i < codes.Count; i++)
					{
						if (codes[i].opcode == OpCodes.Ldstr && codes[i].operand.ToString() == "0.000")
						{
							codes[i].operand = "0.0#######";
							S_Help.Logger("MOD-Horizon_ShowMore: Float_DIG_PL 0.000=>" + codes[i].operand);
						}
					}
					return codes.AsEnumerable();
				}
			}

			[HarmonyPatch(typeof(GameUtil), "FloatToString")]
			public class GameUtil_FloatToString_Patch
			{
				private static void Prefix(ref string format)
				{
					if (format == null)
					{
						return;
					}
					if (format.IndexOf(".") != -1)
					{
						string[] arr = format.Split('.');
						format = arr[0] + ".0" + arr[1].Replace('0', '#');
					}
				}
			}
		}

		public class Toolbar_AddPos
		{
			[HarmonyPatch(typeof(SelectToolHoverTextCard), "UpdateHoverElements")]
			public class Get_Flag
			{
				private static bool Prefix(ref SelectToolHoverTextCard __instance)
				{
					//init
					if (DrawStyle == null)
					{
						DrawStyle = __instance.Styles_BodyText.Standard;
						DrawIcon = __instance.iconDash;
					}

					DrawCell = Grid.PosToCell(Camera.main.ScreenToWorldPoint(KInputManager.GetMousePos()));
					DrawFlag = Grid.IsVisible(DrawCell) && (int)Grid.WorldIdx[DrawCell] == ClusterManager.Instance.activeWorldId;

					return true;
				}
			}

			[HarmonyPatch(typeof(HoverTextDrawer), "EndDrawing")]
			public class Add_Text
			{
				private static void Prefix(ref HoverTextDrawer __instance)
				{
					if (!DrawFlag) return;

					DrawFlag = false;

					__instance.BeginShadowBar(false);
					__instance.DrawIcon(DrawIcon, 18);
					__instance.DrawText(string.Format(S_Text.HOVER_BAR.COORD, DrawCell, DrawCell % Grid.WidthInCells, DrawCell / Grid.WidthInCells), DrawStyle);

					if (Grid.IsValidCell(DrawCell))
					{
						__instance.NewLine(18);
						__instance.DrawIcon(DrawIcon, 18);
						float resist = N_Resistance.EleResistList[Grid.ElementIdx[DrawCell]];
						if (resist < 10)
							__instance.DrawText(string.Format(S_Text.HOVER_BAR.ELEMENT, N_Ruler.ReflectList[Grid.ElementIdx[DrawCell]], resist), DrawStyle);
						else
							__instance.DrawText(string.Format(S_Text.HOVER_BAR.ELEMENT, N_Ruler.ReflectList[Grid.ElementIdx[DrawCell]], S_Text.POWER_RESISTANCE.RESIST_DESC0.text), DrawStyle);
						if (N_Darkness.BackUpdater.is_inited)
						{
							__instance.NewLine(18);
							__instance.DrawIcon(DrawIcon, 18);
							__instance.DrawText(string.Format(S_Text.HOVER_BAR.VISUAL, N_Darkness.BackUpdater.visible[DrawCell]), DrawStyle);
						}

						String text = "";
						if (N_Ruler.Supporter.is_surface(DrawCell)) text += S_Text.HOVER_BAR.SURFACE + " ";
						if (N_Ruler.Supporter.is_crash(DrawCell)) text += S_Text.HOVER_BAR.CRASH + " ";
						if (N_Ruler.Supporter.is_chaos(DrawCell)) text += S_Text.HOVER_BAR.CHAOS + " ";

						if (text != "")
						{
							__instance.NewLine(18);
							__instance.DrawIcon(DrawIcon, 18);
							__instance.DrawText(text, DrawStyle);
						}

						if (A_Launcher.isdebug)
						{
							float pressure = N_Pressure.Supporter.get_pressure(DrawCell, false);
							if (pressure != float.MaxValue && pressure != float.NaN)
							{
								__instance.NewLine(18);
								__instance.DrawIcon(DrawIcon, 18);
								__instance.DrawText(string.Format(S_Text.HOVER_BAR.PRESSURE, pressure), DrawStyle);
							}
							text = (int)Grid.Element[DrawCell].state + " " + Grid.Properties[DrawCell] + " " + N_Ruler.Supporter.DebrisMassCounter(DrawCell) + " " + Grid.ExposedToSunlight[DrawCell];
							__instance.NewLine(18);
							__instance.DrawIcon(DrawIcon, 18);
							__instance.DrawText(text, DrawStyle);
							text = (N_Ruler.Supporter.GasBolck(DrawCell) ? "GB " : "") + (N_Ruler.Supporter.LiquidBolck(DrawCell) ? "LB " : "") + (N_Ruler.Supporter.DebrisBolck(DrawCell) ? "DB" : "");
							__instance.NewLine(18);
							__instance.DrawIcon(DrawIcon, 18);
							__instance.DrawText(text, DrawStyle);
						}
						__instance.EndShadowBar();
					}
				}
			}

			private static bool DrawFlag = false;
			private static int DrawCell = 0;
			private static TextStyleSetting DrawStyle = null;
			private static Sprite DrawIcon = null;
		}
	}
}
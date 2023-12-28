using HarmonyLib;
using STRINGS;
using System.Collections.Generic;

namespace Horizon
{
	public class N_Pressure
	{
		const int Leave_Liquid_Ratio = 10;
		public class Patch
		{
			[HarmonyPatch(typeof(Vent), "IsValidOutputCell")]
			public class Vent_IsValidOutputCell_Patch
			{
				private static bool Prefix(ref int output_cell, ref bool __result, ref Vent __instance)
				{
					__result = false;
					if (__instance.structure == null || !__instance.structure.IsEntombed() || !__instance.Closed())
					{
						bool is_gaspipe = __instance.conduitType == ConduitType.Gas;
						float limit_pressure = __instance.overpressureMass;
						float current_pressure = Supporter.get_pressure(output_cell, is_gaspipe);
						if (!is_gaspipe)
						{
							if (!Grid.IsLiquid(output_cell))
							{
								limit_pressure /= Leave_Liquid_Ratio;
							}
						}

						__result = current_pressure <= limit_pressure;
					}
					return false;
				}
			}

			[HarmonyPatch(typeof(Vent), "GetDescriptors")]
			public class Vent_GetDescriptors_Patch
			{
				private static bool Prefix(ref List<Descriptor> __result, ref Vent __instance)
				{
					var instance = Traverse.Create(__instance);
					int cell = instance.Field("cell").GetValue<int>();
					if (!Grid.IsValidCell(cell))
					{
						return true;
					}

					if (Grid.Solid[cell])
					{
						string text = string.Format(S_Text.VENT_ENVIRONMENT.WRONG_AMBIENCE, GameTags.Solid.Name);
						__result = new List<Descriptor>
						{
							new Descriptor(string.Format(text), string.Format(text), Descriptor.DescriptorType.Effect, false)
						};
					}
					else if (Grid.IsLiquid(cell) && __instance.conduitType == ConduitType.Gas)
					{
						string text = string.Format(S_Text.VENT_ENVIRONMENT.WRONG_AMBIENCE, GameTags.Liquid.Name);
						__result = new List<Descriptor>
						{
							new Descriptor(string.Format(text), string.Format(text), Descriptor.DescriptorType.Effect, false)
						};
					}
					else
					{
						bool is_gaspipe = __instance.conduitType == ConduitType.Gas;
						float limit_pressure = __instance.overpressureMass;
						float current_pressure = Supporter.get_pressure(cell, is_gaspipe);
						float limit_mass = __instance.overpressureMass;
						if (!is_gaspipe)
						{
							if (Grid.IsLiquid(cell))
							{
								limit_mass = Grid.Element[cell].molarMass;
							}
							else
							{
								limit_mass /= Leave_Liquid_Ratio;
								limit_pressure /= Leave_Liquid_Ratio;
							}
						}
						string formattedMass = GameUtil.GetFormattedMass(limit_mass, GameUtil.TimeSlice.None, GameUtil.MetricMassFormat.UseThreshold, true, "{0:0.#}");

						string text = string.Format(S_Text.VENT_ENVIRONMENT.AMBIENCE, Grid.Element[cell].name, current_pressure, limit_pressure);
						string tips = string.Format(UI.BUILDINGEFFECTS.TOOLTIPS.OVER_PRESSURE_MASS, formattedMass);
						__result = new List<Descriptor>
						{
							new Descriptor(string.Format(text), string.Format(tips), Descriptor.DescriptorType.Effect, false)
						};
					}
					return false;
				}
			}
		}

		public static class Supporter
		{
			static public float get_pressure(int cell, bool force_gas)
			{
				if (!Grid.IsValidCell(cell))
				{
					return float.NaN;
				}
				if (Grid.Mass[cell] == 0)
				{
					return 0;
				}
				if (Grid.IsGas(cell))
				{
					return Grid.Mass[cell];
				}
				if (!force_gas && Grid.IsLiquid(cell))
				{
					float defaultmass = Grid.Element[cell].molarMass;
					if (Grid.Mass[cell] < defaultmass)
					{
						return 1000 * Grid.Mass[cell] / defaultmass;
					}
					else
					{
						return 1000 * ((Grid.Mass[cell] / defaultmass) * 100 - 99); //One more layer +1% mass
					}
				}
				return float.MaxValue;
			}
		}
	}
}
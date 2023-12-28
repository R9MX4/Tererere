using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Horizon
{
	public class N_Resistance
	{
		private static float[] NetResist = null;
		public static float[] EleResistList = null;

		public class Generator_Patch
		{
			[HarmonyPatch(typeof(WoodGasGeneratorConfig), "CreateBuildingDef")]
			public class WoodGasGeneratorConfig_Patch
			{
				private static void Postfix(BuildingDef __result)
				{
					__result.GeneratorWattageRating = 360;
					S_Help.Logger("MOD-Horizon_Resistance: Boost WoodGasGenerator to " + __result.GeneratorWattageRating);
				}
			}

			[HarmonyPatch(typeof(SteamTurbineConfig2), "CreateBuildingDef")]
			public class SteamTurbineConfig2_Patch
			{
				private static void Postfix(BuildingDef __result)
				{
					__result.GeneratorWattageRating = __result.GeneratorBaseCapacity = 1000;
					S_Help.Logger("MOD-Horizon_Resistance: Boost SteamTurbine to " + __result.GeneratorWattageRating);
				}
			}

			[HarmonyPatch(typeof(SolarPanelConfig), "CreateBuildingDef")]
			public class SolarPanelConfig_Patch
			{
				private static void Postfix(BuildingDef __result)
				{
					__result.GeneratorWattageRating = __result.GeneratorBaseCapacity = 500;
					S_Help.Logger("MOD-Horizon_Resistance: Boost SolarPanel to " + __result.GeneratorWattageRating);
				}
			}

			[HarmonyPatch(typeof(ManualGeneratorConfig), "CreateBuildingDef")]
			public class ManualGeneratorConfig_Patch
			{
				private static void Postfix(BuildingDef __result)
				{
					__result.GeneratorWattageRating = 480;
					S_Help.Logger("MOD-Horizon_Resistance: Boost ManualGenerator to " + __result.GeneratorWattageRating);
				}
			}

			[HarmonyPatch(typeof(GeneratorConfig), "CreateBuildingDef")]
			public class GeneratorConfig_Patch
			{
				private static void Postfix(BuildingDef __result)
				{
					__result.GeneratorWattageRating = 750;
					S_Help.Logger("MOD-Horizon_Resistance: Boost Generator to " + __result.GeneratorWattageRating);
				}
			}
		}

		public class Describer
		{
			private static StatusItem WireResistStatus = null;
			[HarmonyPatch(typeof(Wire), "OnSpawn")]
			public class Wire_OnSpawn_Patch
			{
				private static void Postfix(ref Wire __instance)
				{
					if (WireResistStatus == null)
					{
						WireResistStatus = new StatusItem("WireResistStatus", S_Text.POWER_RESISTANCE.LOSS, S_Text.POWER_RESISTANCE.WIRE_TOOLTIP, "", StatusItem.IconType.Info, NotificationType.Neutral, false, OverlayModes.None.ID, 129022, true, null);

						WireResistStatus.resolveStringCallback = new Func<string, object, string>(ResolveStrings);
						WireResistStatus.resolveTooltipCallback = new Func<string, object, string>(ResolveStrings);
					}
					__instance.GetComponent<KSelectable>().AddStatusItem(WireResistStatus, __instance);
				}
				private static string ResolveStrings(string str, object data)
				{
					Wire wire = (Wire)data;
					int cell = Grid.PosToCell(wire.transform.GetPosition());
					CircuitManager circuitManager = Game.Instance.circuitManager;
					ushort circuitID = circuitManager.GetCircuitID(cell);

					float wattsUsedByCircuit = circuitManager.GetWattsUsedByCircuit(circuitID);
					float netResist = NetResist[circuitID];
					float netLostPercent = netResist / (1000f + netResist);
					float netLostPower = wattsUsedByCircuit * netLostPercent;

					str = str.Replace("{0}", GameUtil.GetFormattedWattage(netLostPower, GameUtil.WattageFormatterUnit.Automatic, true));
					str = str.Replace("{1}", GameUtil.FloatToString(netResist, "###0.#"));
					str = str.Replace("{2}", GameUtil.FloatToString(netLostPercent * 100, "###0.#"));
					return str;
				}
			}

			[HarmonyPatch(typeof(EnergyInfoScreen), "Refresh")]
			public class EnergyInfoScreen_Refresh_Patch
			{
				private static void Postfix(ref EnergyInfoScreen __instance)
				{
					ushort circuitID = ushort.MaxValue;
					GameObject selectedTarget = Traverse.Create(__instance).Field("selectedTarget").GetValue<GameObject>();

					if (selectedTarget.GetComponent<EnergyConsumer>() != null)
						circuitID = selectedTarget.GetComponent<EnergyConsumer>().CircuitID;
					else if (selectedTarget.GetComponent<Generator>() != null)
						circuitID = selectedTarget.GetComponent<Generator>().CircuitID;

					if (circuitID == 65535)
					{
						int cell = Grid.PosToCell(selectedTarget.transform.GetPosition());
						circuitID = Game.Instance.circuitManager.GetCircuitID(cell);
					}
					if (circuitID != 65535)
					{
						Dictionary<string, GameObject> overviewLabels = Traverse.Create(__instance).Field("overviewLabels").GetValue<Dictionary<string, GameObject>>();
						GameObject overviewPanel = Traverse.Create(__instance).Field("overviewPanel").GetValue<GameObject>();

						GameObject gameObject = AddOrGetLabel(overviewLabels, overviewPanel, "wire", __instance);
						float netResist = NetResist[circuitID];
						float netLostPercent = netResist / (1000f + netResist);
						float netLostPower = Game.Instance.circuitManager.GetWattsUsedByCircuit(circuitID) * netLostPercent;

						gameObject.GetComponent<LocText>().text = string.Format(S_Text.POWER_RESISTANCE.LOSS, GameUtil.GetFormattedWattage(netLostPower, GameUtil.WattageFormatterUnit.Automatic, true));
						gameObject.GetComponent<ToolTip>().toolTip = string.Format(S_Text.POWER_RESISTANCE.RESIST_NET, GameUtil.FloatToString(netResist, "###0.#"), GameUtil.FloatToString(netLostPercent * 100, "###0.#"));
						gameObject.SetActive(true);
					}
				}

				private static GameObject AddOrGetLabel(Dictionary<string, GameObject> labels, GameObject panel, string id, EnergyInfoScreen __instance)
				{
					GameObject gameObject;
					if (labels.ContainsKey(id))
					{
						gameObject = labels[id];
					}
					else
					{
						gameObject = Util.KInstantiate(__instance.labelTemplate, panel.GetComponent<CollapsibleDetailContentPanel>().Content.gameObject, null);
						gameObject.transform.localScale = new Vector3(1f, 1f, 1f);
						labels[id] = gameObject;
						gameObject.SetActive(true);
					}
					return gameObject;
				}
			}

			[HarmonyPatch(typeof(CircuitManager), "PowerFromGenerator")]
			public class CircuitManager_PowerFromGenerator_Patch
			{
				private static bool Prefix(float joules_needed, ref Generator g, IEnergyConsumer c, ref float __result)
				{
					float netResist = NetResist[g.CircuitID];
					float JoulesAvailable = Mathf.Min(g.JoulesAvailable * 1000f / (1000f + netResist), joules_needed);
					joules_needed -= JoulesAvailable;
					g.ApplyDeltaJoules(-JoulesAvailable / 1000f * (1000f + netResist), false);
					if (g.JoulesAvailable < 0.01f)
					{
						g.ApplyDeltaJoules(-g.JoulesAvailable, false);
					}
					ReportManager.Instance.ReportValue(ReportManager.ReportType.EnergyCreated, -JoulesAvailable, c.Name, null);
					ReportManager.Instance.ReportValue(ReportManager.ReportType.EnergyWasted, -JoulesAvailable * netResist / (1000f + netResist), S_Text.POWER_RESISTANCE.DAILYREPORT, null);

					__result = joules_needed;
					return false;
				}
			}
		}

		public class Executor
		{
			static float GetResist(bool is_bridge, Wire.WattageRating WattageRating, ushort elementidx)
			{
				float resist;
				resist = (WattageRating == Wire.WattageRating.Max20000 || WattageRating == Wire.WattageRating.Max2000) ? 1 : 2;
				resist *= is_bridge ? 3 : 2;
				resist *= EleResistList[elementidx];
				return resist / 3;
			}

			[HarmonyPatch(typeof(CircuitManager), "Refresh")]
			public class CircuitManager_Refresh_Patch
			{
				private static void Prefix(ref CircuitManager __instance, out bool __state)
				{
					UtilityNetworkManager<ElectricalUtilityNetwork, Wire> electricalConduitSystem = Game.Instance.electricalConduitSystem;
					bool dirty = Traverse.Create(__instance).Field("dirty").GetValue<bool>();
					__state = electricalConduitSystem.IsDirty || dirty;
				}

				private static void Postfix(ref CircuitManager __instance, bool __state)
				{
					if (__state)
					{
						int ListCount = Traverse.Create(Game.Instance.electricalConduitSystem).Field("networks").GetValue<List<UtilityNetwork>>().Count;
						NetResist = new float[ListCount];
						for (int netidx = 0; netidx < ListCount; netidx++)
						{
							ElectricalUtilityNetwork electricalUtilityNetwork = Game.Instance.electricalConduitSystem.GetNetworkByID(netidx) as ElectricalUtilityNetwork;
							for (int wireidx = 0; wireidx < electricalUtilityNetwork.allWires.Count; wireidx++)
							{
								Wire wire = electricalUtilityNetwork.allWires[wireidx];
								NetResist[netidx] += GetResist(false, wire.GetMaxWattageRating(), wire.GetComponent<PrimaryElement>().Element.idx);
							}
						}
						HashSet<WireUtilityNetworkLink> bridges = Traverse.Create(__instance).Field("bridges").GetValue<HashSet<WireUtilityNetworkLink>>();
						foreach (WireUtilityNetworkLink bridge in bridges)
						{
							bridge.GetCells(out int cell, out int cell2);
							ushort netidx2 = __instance.GetCircuitID(cell);
							if (netidx2 != 65535)
							{
								NetResist[netidx2] += GetResist(true, bridge.GetMaxWattageRating(), bridge.GetComponent<PrimaryElement>().Element.idx);
							}
						}
					}
				}
			}


			[HarmonyPatch(typeof(CircuitManager), "PowerFromBatteries")]
			public class CircuitManager_PowerFromBatteries_Patch
			{
				private static bool Prefix(float joules_needed, List<Battery> batteries, IEnergyConsumer c, ref float __result)
				{
					if (batteries.Count == 0)
					{
						return true;
					}

					int num_powered;
					float netResist = NetResist[batteries[0].CircuitID];
					do
					{
						float JoulesTotalAvailable = GetBatteryJoulesAvailable(batteries, out num_powered) * (float)num_powered * 1000f / (1000f + netResist);
						float num3 = (JoulesTotalAvailable < joules_needed) ? JoulesTotalAvailable : joules_needed;
						joules_needed -= num3;
						ReportManager.Instance.ReportValue(ReportManager.ReportType.EnergyCreated, -num3, c.Name, null);
						float joules = num3 / (float)num_powered;
						for (int i = batteries.Count - num_powered; i < batteries.Count; i++)
						{
							batteries[i].ConsumeEnergy(joules / 1000f * (1000f + netResist));
						}
					}
					while (joules_needed >= 0.01f && num_powered > 0);
					__result = joules_needed;
					return false;
				}

				private static float GetBatteryJoulesAvailable(List<Battery> batteries, out int num_powered)
				{
					float result = 0f;
					num_powered = 0;
					for (int i = 0; i < batteries.Count; i++)
					{
						if (batteries[i].JoulesAvailable > 0f)
						{
							result = batteries[i].JoulesAvailable;
							num_powered = batteries.Count - i;
							break;
						}
					}
					return result;
				}
			}
		}
	}
}
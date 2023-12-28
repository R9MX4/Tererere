using HarmonyLib;
using STRINGS;
using System.Collections.Generic;
using System.Reflection;
using System;
using UnityEngine;

namespace Horizon
{
	public class F_BuildHeat
	{
		public class Patch_SHC
		{
			[HarmonyPatch(typeof(BuildingTemplates), "CreateBuildingDef")]
			public class BuildingTemplates_CreateBuildingDef_Patch
			{
				private static void Prefix(string id, ref float temperature_modification_mass_scale)
				{
					if (temperature_modification_mass_scale != 1)
					{
						S_Help.TLogger("MOD-Horizon_BuildHeat: CreateBuildingDef: " + id + " temperature_modification_mass_scale " + temperature_modification_mass_scale + "=>1");
					}
					temperature_modification_mass_scale = 1f;
				}
			}
		}
		//public class Patch_EXH_NEW
		//{
		//	//Fix exhaust heat
		//	static bool is_monitor = false;
		//	static float _OperatingKilowatts = 0f;
		//	static float _ExhaustKilowatts = 0f;
		//	static float _ExhaustKilowatts_dt = 0f;
		//	static int? _handle = null;
		//	static List<StructureTemperaturePayload.EnergySource> _sources = null;

		//	[HarmonyPatch(typeof(StructureTemperatureComponents), "Sim200ms")]
		//	public class StructureTemperatureComponents_Sim200ms_Patch
		//	{
		//		private static void Prefix()
		//		{
		//			is_monitor = true;
		//		}
		//		private static void Postfix()
		//		{
		//			is_monitor = false;
		//		}
		//	}

		//	[HarmonyPatch(typeof(StructureTemperaturePayload), "GetExtents")]
		//	public class StructureTemperaturePayload_GetExtents_Patch
		//	{
		//		private static void Prefix(StructureTemperaturePayload __instance)
		//		{
		//			if (is_monitor)
		//			{
		//				_ExhaustKilowatts = __instance.ExhaustKilowatts; //Save total ExhaustKilowatts
		//				_ExhaustKilowatts_dt = 0f;
		//				_handle = __instance.simHandleCopy;
		//			}
		//		}
		//	}

		//	[HarmonyPatch(typeof(SimMessages), "ModifyEnergy")]
		//	public class SimMessages_ModifyEnergy_Patch
		//	{
		//		private static void Prefix(float kilojoules, SimMessages.EnergySourceID id)
		//		{
		//			if (is_monitor && id == SimMessages.EnergySourceID.StructureTemperature) _ExhaustKilowatts_dt += kilojoules;
		//		}
		//	}

		//	[HarmonyPatch(typeof(StructureTemperatureComponents), "AccumulateProducedEnergyKW")]
		//	public class StructureTemperatureComponents_AccumulateProducedEnergyKW_Patch
		//	{
		//		private static bool Prefix(List<StructureTemperaturePayload.EnergySource> sources, float kw, string source)
		//		{
		//			if (!is_monitor) return true;

		//			if (source == BUILDING.STATUSITEMS.OPERATINGENERGY.OPERATING)
		//			{
		//				_sources = sources;
		//				_OperatingKilowatts = kw;
		//				return false;
		//			}
		//			else if (source == BUILDING.STATUSITEMS.OPERATINGENERGY.EXHAUSTING)
		//			{
		//				if (_sources == null || _handle == null) return true;
		//				float _ExhaustKilowatts_remain = _ExhaustKilowatts * 0.2f - _ExhaustKilowatts_dt;
		//				if (_ExhaustKilowatts_remain > 0) SimMessages.ModifyBuildingEnergy((int)_handle, _ExhaustKilowatts_remain, 0f, 10000f);
		//				AccumulateProducedEnergyKW(sources, _ExhaustKilowatts_dt / 0.2f, BUILDING.STATUSITEMS.OPERATINGENERGY.EXHAUSTING);
		//				AccumulateProducedEnergyKW(sources, _OperatingKilowatts + _ExhaustKilowatts_remain / 0.2f, BUILDING.STATUSITEMS.OPERATINGENERGY.OPERATING);

		//				sources = null;
		//				_handle = null;
		//				return false;
		//			}
		//			return true;
		//		}
		//	}
		//	private static List<StructureTemperaturePayload.EnergySource> AccumulateProducedEnergyKW(List<StructureTemperaturePayload.EnergySource> sources, float kw, string source)
		//	{
		//		if (sources == null) sources = new List<StructureTemperaturePayload.EnergySource>();

		//		bool flag = false;
		//		for (int i = 0; i < sources.Count; i++)
		//		{
		//			if (sources[i].source == source)
		//			{
		//				sources[i].Accumulate(kw);
		//				flag = true;
		//				break;
		//			}
		//		}
		//		if (!flag) sources.Add(new StructureTemperaturePayload.EnergySource(kw, source));

		//		return sources;
		//	}
		//}
		public class Patch_EXH_OLD
		{
			[HarmonyPatch(typeof(StructureTemperatureComponents), "Sim200ms")]
			public class StructureTemperatureComponents_Sim200ms_Patch
			{
				private static bool Prefix(ref float dt, ref StructureTemperatureComponents __instance)
				{
					__instance.GetDataLists(out List<StructureTemperatureHeader> headersList, out List<StructureTemperaturePayload> payloadsList);
					ListPool<int, StructureTemperatureComponents>.PooledList validBuildListPool = ListPool<int, StructureTemperatureComponents>.Allocate();
					validBuildListPool.Capacity = Math.Max(validBuildListPool.Capacity, headersList.Count);
					ListPool<int, StructureTemperatureComponents>.PooledList ditryBuildListPool = ListPool<int, StructureTemperatureComponents>.Allocate();
					ditryBuildListPool.Capacity = Math.Max(ditryBuildListPool.Capacity, headersList.Count);
					ListPool<int, StructureTemperatureComponents>.PooledList activBuildListPool = ListPool<int, StructureTemperatureComponents>.Allocate();
					activBuildListPool.Capacity = Math.Max(activBuildListPool.Capacity, headersList.Count);

					var instance = Traverse.Create(__instance);
					StatusItem _operatingEnergyStatusItem = instance.Field("operatingEnergyStatusItem").GetValue<StatusItem>();

					for (int idx = 0; idx != headersList.Count; idx++)
					{
						StructureTemperatureHeader structureTemperatureHeader = headersList[idx];
						if (Sim.IsValidHandle(structureTemperatureHeader.simHandle))
						{
							validBuildListPool.Add(idx);
							if (structureTemperatureHeader.dirty)
							{
								ditryBuildListPool.Add(idx);
								structureTemperatureHeader.dirty = false;
								headersList[idx] = structureTemperatureHeader;
							}
							if (structureTemperatureHeader.isActiveBuilding)
							{
								activBuildListPool.Add(idx);
							}
						}
					}
					foreach (int index in ditryBuildListPool)
					{
						StructureTemperaturePayload structureTemperaturePayload = payloadsList[index];
						UpdateSimState(ref structureTemperaturePayload);
					}
					foreach (int index in ditryBuildListPool)
					{
						if (payloadsList[index].pendingEnergyModifications != 0f)
						{
							StructureTemperaturePayload structureTemperaturePayload2 = payloadsList[index];
							SimMessages.ModifyBuildingEnergy(structureTemperaturePayload2.simHandleCopy, structureTemperaturePayload2.pendingEnergyModifications, 0f, 10000f);
							structureTemperaturePayload2.pendingEnergyModifications = 0f;
							payloadsList[index] = structureTemperaturePayload2;
						}
					}
					foreach (int index in activBuildListPool)
					{
						StructureTemperaturePayload structureTemperaturePayload3 = payloadsList[index];
						if (structureTemperaturePayload3.operational == null || structureTemperaturePayload3.operational.IsActive)
						{
							if (!structureTemperaturePayload3.isActiveStatusItemSet)
							{
								structureTemperaturePayload3.primaryElement.GetComponent<KSelectable>().SetStatusItem(Db.Get().StatusItemCategories.OperatingEnergy, _operatingEnergyStatusItem, structureTemperaturePayload3.simHandleCopy);
								structureTemperaturePayload3.isActiveStatusItemSet = true;
							}

							float exHeatRemain = structureTemperaturePayload3.ExhaustKilowatts * dt;
							if (structureTemperaturePayload3.ExhaustKilowatts != 0f && dt != 0)
							{
								float exHeatTotal = 0;
								Extents extents = structureTemperaturePayload3.GetExtents();
								float exHeatGrid = structureTemperaturePayload3.ExhaustKilowatts * dt / (float)(extents.width * extents.height);
								for (int i = 0; i < extents.height; i++)
								{
									int PosY = extents.y + i;
									for (int j = 0; j < extents.width; j++)
									{
										int PosX = extents.x + j;
										int PosXY = PosY * Grid.WidthInCells + PosX;
										float exRatio = Mathf.Min(Grid.Mass[PosXY], 2f) / 2f;
										if (structureTemperaturePayload3.primaryElement.Temperature + 5 < Grid.Temperature[PosXY])
										{
											exRatio = 0;
										}
										float exHeat = exHeatGrid * exRatio;
										exHeatTotal += exHeat;
										SimMessages.ModifyEnergy(PosXY, exHeat, structureTemperaturePayload3.maxTemperature, SimMessages.EnergySourceID.StructureTemperature);
									}
								}
								structureTemperaturePayload3.energySourcesKW = AccumulateProducedEnergyKW(__instance, structureTemperaturePayload3.energySourcesKW, exHeatTotal / dt, BUILDING.STATUSITEMS.OPERATINGENERGY.EXHAUSTING);
								if (structureTemperaturePayload3.ExhaustKilowatts * dt > exHeatTotal)
								{
									exHeatRemain = structureTemperaturePayload3.ExhaustKilowatts * dt - exHeatTotal;
									SimMessages.ModifyBuildingEnergy(structureTemperaturePayload3.simHandleCopy, exHeatRemain, 0f, 10000f);
								}
							}
							structureTemperaturePayload3.energySourcesKW = AccumulateProducedEnergyKW(__instance, structureTemperaturePayload3.energySourcesKW, structureTemperaturePayload3.OperatingKilowatts + exHeatRemain / dt, BUILDING.STATUSITEMS.OPERATINGENERGY.OPERATING);
						}
						else if (structureTemperaturePayload3.isActiveStatusItemSet)
						{
							structureTemperaturePayload3.primaryElement.GetComponent<KSelectable>().SetStatusItem(Db.Get().StatusItemCategories.OperatingEnergy, null, null);
							structureTemperaturePayload3.isActiveStatusItemSet = false;
						}
						payloadsList[index] = structureTemperaturePayload3;
					}
					activBuildListPool.Recycle();
					ditryBuildListPool.Recycle();
					validBuildListPool.Recycle();
					return false;
				}
			}
			private static void UpdateSimState(ref StructureTemperaturePayload payload)
			{
				var args = new object[] { payload };
				var flags = BindingFlags.NonPublic | BindingFlags.Static;
				typeof(StructureTemperatureComponents).GetMethod("UpdateSimState", flags).Invoke(null, args);
			}

			private static List<StructureTemperaturePayload.EnergySource> AccumulateProducedEnergyKW(StructureTemperatureComponents __instance, List<StructureTemperaturePayload.EnergySource> sources, float kw, string source)
			{
				var args = new object[] { sources, kw, source };
				var flags = BindingFlags.Instance | BindingFlags.NonPublic;
				return (List<StructureTemperaturePayload.EnergySource>)typeof(StructureTemperatureComponents).GetMethod("AccumulateProducedEnergyKW", flags).Invoke(__instance, args);
			}
		}
	}
}
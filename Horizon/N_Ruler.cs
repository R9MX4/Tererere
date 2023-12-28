using Klei;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Threading;
using System;
using UnityEngine;
using HarmonyLib;
using static BuildingHP;

namespace Horizon
{
	public class N_Ruler
	{
		const int surface_interval = 5;
		const int crash_interval_C = 20;
		const int chaos_interval_C = 50;
		static private Phase phase = Phase.idle;
		static int frameCounter = 0;
		internal enum Phase
		{
			idle,
			surface_scan,
			surface_done,
			crash_scan,
			crash_done,
			chaos_scan,
			chaos_done,
		}
		public static List<int> surface_list = new List<int>();
		//public static List<int> crash_list = new List<int>();
		public static Dictionary<BuildingHP, DamageSourceInfo> crash_Build_Dic = new Dictionary<BuildingHP, DamageSourceInfo>();
		public static List<Vector4> crash_World_List = new List<Vector4>();//Damaged Cell, Source Cell, Damage Type [1 = Gas, 2 = Liquid, 3 = Solid]
		public static List<int> chaos_list = new List<int>();
		public static float[] ReflectList = null;
		private static AutoResetEvent resetEvent = new AutoResetEvent(false);

		public static SYNC _ruler;
		public static void CreateSyncTask()
		{
			_ruler = new SYNC();
			SimAndRenderScheduler.instance.sim200ms.Add(_ruler, false);
		}

		public class SYNC : KMonoBehaviour, ISim200ms
		{
			public void Sim200ms(float dt)
			{
				S_Help.TLogger("MOD-Horizon_Ruler: Sim200ms");
				BackScaner.TryUpdate();
			}
		}

		static bool BlockUpdateNavGrids = false;
		public static void UnsafeSim200ms(float dt, Game __instance)
		{
			if (dt > 0)
			{
				if ((phase == Phase.surface_done || phase == Phase.crash_done || phase == Phase.chaos_done) &&
					BackScaner.isBusy)
				{
					S_Help.TLogger("MOD-Horizon_Ruler: BackScaner isBusy. Phase: " + phase);
					resetEvent.WaitOne();
					S_Help.TLogger("MOD-Horizon_Ruler: BackScaner ready");
				}
				if (phase == Phase.crash_done)
				{
					Executor.do_crash_all();
					phase = Phase.chaos_scan;
					S_Help.TLogger("MOD-Horizon_Ruler: Sync_Executor Crash Finish");
				}
				else if (phase == Phase.chaos_done && chaos_list.Count != 0)
				{
					BlockUpdateNavGrids = true;
					typeof(Game).GetMethod("UnsafeSim200ms", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(__instance, new object[] { 0 });
					Executor.do_chaos_all(true);
					BlockUpdateNavGrids = false;

					frameCounter++;
					phase = Phase.surface_scan;
					S_Help.TLogger("MOD-Horizon_Ruler: Sync_Executor Chaos Finish");
				}
			}
		}

		[HarmonyPatch(typeof(Pathfinding), "UpdateNavGrids")]
		public class Pathfinding_UpdateNavGrids_Block
		{
			private static bool Prefix()
			{
				return !BlockUpdateNavGrids;
			}
		}

		public static class BackScaner
		{
			static BackgroundWorker backUpdater = new BackgroundWorker();
			public static bool isBusy
			{
				get
				{
					return backUpdater.IsBusy;
				}
			}
			public static void Init()
			{
				S_Help.Logger("MOD-Horizon_Ruler: Init BackUpdater");
				backUpdater.DoWork += new DoWorkEventHandler(back_Scaner);
			}
			public static void Clear()
			{
				surface_list.Clear();
				//crash_list.Clear();
				crash_Build_Dic.Clear();
				crash_World_List.Clear();
				chaos_list.Clear();
				phase = Phase.idle;
				frameCounter = 0;
			}
			public static void TryUpdate()
			{
				if (BackScaner.isBusy) return;

				backUpdater.RunWorkerAsync(1);
			}

			public static void back_Scaner(object sender, EventArgs e)
			{
				S_Help.TLogger("MOD-Horizon_Ruler: Back_Scaner start. Phase: " + phase + " ,frameCounter: " + frameCounter);

				if (phase == Phase.surface_scan || phase == Phase.idle)
				{
					surface_list.Clear();
					for (int index = 0; index < ClusterManager.Instance.WorldContainers.Count; index++)
					{
						WorldContainer worldContainer = ClusterManager.Instance.WorldContainers[index];
						for (int xIdx = frameCounter % surface_interval; xIdx < worldContainer.WorldSize.X; xIdx += surface_interval)
						{
							for (int yIdx = worldContainer.WorldSize.Y - 1; yIdx > 0; yIdx--)
							{
								int cell = (xIdx + worldContainer.WorldOffset.X) + (yIdx + worldContainer.WorldOffset.Y) * Grid.WidthInCells;
								if (Supporter.is_surface(cell))
								{
									surface_list.Add(cell);
								}
								if (Grid.ExposedToSunlight[cell - Grid.WidthInCells] == 0)
								{
									break;
								}
							}
						}
					}

					phase = Phase.surface_done;
					S_Help.TLogger("MOD-Horizon_Ruler: Async_Scanner Surface Finish");
				}
				else if (phase == Phase.surface_done)
				{
					Executor.do_surface_all(true);

					phase = Phase.crash_scan;
					S_Help.TLogger("MOD-Horizon_Ruler: Async_Executor Surface Finish");
				}
				else if (phase == Phase.crash_scan)
				{
					crash_Build_Dic.Clear();
					crash_World_List.Clear();
					for (int cell = frameCounter % crash_interval_C; cell < Grid.CellCount; cell += crash_interval_C)
					{
						Supporter.examine_crash(cell);
					}

					phase = Phase.crash_done;
					S_Help.TLogger("MOD-Horizon_Ruler: Async_Scanner Crash Finish");
				}
				else if (phase == Phase.chaos_scan)
				{
					chaos_list.Clear();
					for (int cell = frameCounter % chaos_interval_C; cell < Grid.CellCount; cell += chaos_interval_C)
					{
						if (Supporter.is_chaos(cell))
						{
							chaos_list.Add(cell);
						}
					}

					phase = Phase.chaos_done;
					S_Help.TLogger("MOD-Horizon_Ruler: Async_Scanner Chaos Finish");
				}
				resetEvent.Set();
			}
		}

		public static class Executor
		{
			private struct GridInfo
			{
				public int cell;
				public ushort elementidx;
				public SimHashes element;
				public float temperature;
				public float mass;
				public SimUtil.DiseaseInfo disease;
				public int Info
				{
					set
					{
						cell = value;
						elementidx = Grid.ElementIdx[cell];
						element = Grid.Element[cell].id;
						temperature = Grid.Temperature[cell];
						mass = Grid.Mass[cell];
						disease.idx = Grid.DiseaseIdx[cell];
						disease.count = Grid.DiseaseCount[cell];
					}
				}
			};
			public static void do_surface_all(bool is_check)
			{
				foreach (int cell in surface_list)
					if (!is_check || Supporter.is_surface(cell))
						do_surface(cell);
			}
			//Must be done sync
			public static void do_crash_all()
			{
				foreach (KeyValuePair<BuildingHP, DamageSourceInfo> keyValuePair in crash_Build_Dic)
					if (keyValuePair.Key != null)
						keyValuePair.Key.gameObject.Trigger((int)GameHashes.DoBuildingDamage, keyValuePair.Value);

				foreach (Vector4 vector4 in crash_World_List)
					if (Grid.IsSolidCell((int)vector4.x))
					{
						LocString popText = null;
						switch (vector4.w)
						{
							case 1: popText = S_Text.PRESSURE_DAMAGE.GAS_PRESSURE; break;
							case 2: popText = S_Text.PRESSURE_DAMAGE.LIQUID_PRESSURE; break;
							case 3: popText = S_Text.PRESSURE_DAMAGE.SOLID_PRESSURE; break;
						}
						WorldDamage.Instance.ApplyDamage((int)vector4.x, vector4.z, (int)vector4.y, popText, popText);
					}
			}
			//Must be done by inserting
			public static void do_chaos_all(bool is_check)
			{
				foreach (int cell in chaos_list)
					if (!is_check || Supporter.is_chaos(cell))
						do_chaos(cell, cell + Grid.WidthInCells);
			}

			public static void do_surface(int cell)
			{
				if (Grid.Temperature[cell] <= 5) return;
				float World_Light = ClusterManager.Instance.GetWorld((int)Grid.WorldIdx[cell]).currentSunlightIntensity;
				float Heat_Capacity = Grid.Temperature[cell] * Grid.Element[cell].specificHeatCapacity * Grid.Mass[cell];
				//Plan 1: (Choose)
				float factor = World_Light * 4.45f * ReflectList[Grid.ElementIdx[cell]] - (Grid.Temperature[cell] - 4) * (Grid.Temperature[cell] - 4);
				float heat = factor * Heat_Capacity * Grid.ExposedToSunlight[cell] / 8e9f;
				//Plan 2: 
				//float factor = World_Light * 0.257f * ReflectList[Grid.ElementIdx[cell]] - Mathf.Pow((Grid.Temperature[cell] - 4), 1.5f);
				//float heat = factor * Heat_Capacity * Grid.ExposedToSunlight[cell] / 4e8f;

				if (heat > 1f || heat < -1f)
				{
					SimMessages.ModifyEnergy(cell, heat, 1000f, SimMessages.EnergySourceID.StructureTemperature);
				}
			}

			private static void do_chaos(int cell1, int cell2)
			{
				GridInfo gridInfo1 = new GridInfo();
				GridInfo gridInfo2 = new GridInfo();
				gridInfo1.Info = cell1;
				gridInfo2.Info = cell2;

				SimMessages.ModifyCell(gridInfo1.cell, gridInfo2.elementidx, gridInfo2.temperature, gridInfo2.mass, gridInfo2.disease.idx, gridInfo2.disease.count, SimMessages.ReplaceType.Replace, false, -1);
				SimMessages.ModifyCell(gridInfo2.cell, gridInfo1.elementidx, gridInfo1.temperature, gridInfo1.mass, gridInfo1.disease.idx, gridInfo1.disease.count, SimMessages.ReplaceType.Replace, false, -1);
			}
			//public static void do_chaos_all(bool is_check)
			//{
			//    foreach (int cell in chaos_list)
			//        if (!is_check || Supporter.is_chaos(cell))
			//            do_chaos(cell, cell + Grid.WidthInCells);
			//}
			//private static void do_crash(int cell)
			//{
			//	S_Help.Logger("do_crash " + cell);
			//	if (Grid.IsGas(cell) && Grid.Mass[cell] > 200)
			//	{
			//		for (int dir = 0; dir < 4; dir++)
			//		{
			//			int cell2 = S_Help.CellShift(cell, dir);
			//			if (Supporter.GasBolck(cell2))
			//			{
			//				float damage = Grid.Mass[cell] / 200 - 1;
			//				do_crash_dig(cell2, cell, damage, false);
			//			}
			//		}
			//	}
			//	if (Grid.IsLiquid(cell) && Grid.Mass[cell] > Grid.Element[cell].molarMass * 5f)
			//	{
			//		for (int dir = 0; dir < 4; dir++)
			//		{
			//			int cell2 = S_Help.CellShift(cell, dir);
			//			if (Supporter.LiquidBolck(cell2))
			//			{
			//				float damage = Grid.Mass[cell] / Grid.Element[cell].molarMass / 5f - 1;
			//				do_crash_dig(cell2, cell, damage, true);
			//			}
			//		}
			//	}
			//	if (Supporter.DebrisBolck(cell - Grid.WidthInCells))
			//	{
			//		if (Supporter.DebrisMassCounter(cell) > 100001)
			//		{
			//			do_crash_bury(cell - Grid.WidthInCells);
			//		}
			//	}
			//}

			//private static void do_crash_dig(int targetCell, int sourceCell, float damage, bool isliquid)
			//{
			//	S_Help.Logger("do_crash_dig " + (isliquid ? "L " : "G ") + targetCell);
			//	GameObject gameObject = Grid.Objects[targetCell, 9];
			//	LocString popText = isliquid ? S_Text.PRESSURE_DAMAGE.LIQUID_PRESSURE : S_Text.PRESSURE_DAMAGE.GAS_PRESSURE;
			//	bool isBuild = false;

			//	if (gameObject != null)
			//	{
			//		SimCellOccupier occupier = gameObject.GetComponent<SimCellOccupier>();
			//		isBuild = occupier != null && !occupier.doReplaceElement;
			//	}
			//	S_Help.Logger("do_crash_dig " + (isBuild ? "Build" : "Not Build"));

			//	float strength = isBuild ? gameObject.GetComponent<PrimaryElement>().Element.strength
			//							 : Grid.Element[targetCell].strength;
			//	if (strength == 0f || damage < strength)
			//		return;
			//	float damage_tunned = Mathf.Min(damage / strength - 0.99f, 0.1f); //0.01 ~ 0.

			//	if (isBuild)
			//	{
			//		BuildingHP buildingHP = gameObject.GetComponent<BuildingHP>();
			//		buildingHP.gameObject.Trigger((int)GameHashes.DoBuildingDamage, new BuildingHP.DamageSourceInfo
			//		{
			//			damage = Math.Max(1, Mathf.RoundToInt(damage_tunned * (float)buildingHP.MaxHitPoints)),
			//			source = popText,
			//			popString = popText
			//		});
			//	}
			//	else
			//	{
			//		WorldDamage.Instance.ApplyDamage(targetCell, damage_tunned * 1.5f, sourceCell, popText, popText);
			//	}
			//	S_Help.Logger("do_crash_dig DONE");
			//}

			//private static void do_crash_bury(int targetCell)
			//{
			//	GameObject gameObject = Grid.Objects[targetCell, 9];
			//	LocString popText = S_Text.PRESSURE_DAMAGE.SOLID_PRESSURE;
			//	bool isBuild = false;
			//	bool isNature = Grid.Properties[targetCell] == 0;

			//	if (gameObject != null)
			//	{
			//		SimCellOccupier occupier = gameObject.GetComponent<SimCellOccupier>();
			//		isBuild = occupier != null && !occupier.doReplaceElement;
			//	}

			//	S_Help.Logger("do_crash_bury " + targetCell + (isBuild ? "Build, " : "Not Build, ") + (isNature ? "Nature" : "Not Nature"));

			//	if (isBuild) // i.e.: Mesh Tile
			//	{
			//		S_Help.Logger("do_crash_bury TYPE." + 1);
			//		BuildingHP buildingHP = gameObject.GetComponent<BuildingHP>();
			//		buildingHP.gameObject.Trigger((int)GameHashes.DoBuildingDamage, new BuildingHP.DamageSourceInfo
			//		{
			//			damage = Math.Max(1, Mathf.RoundToInt(0.1f * (float)buildingHP.MaxHitPoints)),
			//			source = popText,
			//			popString = popText
			//		});
			//		S_Help.Logger("do_crash_bury DONE." + 1);
			//	}
			//	else if (Grid.Mass[targetCell] > 0 && Grid.Element[targetCell].strength == 0)
			//	{
			//		S_Help.Logger("do_crash_bury TYPE." + 2);
			//		return;
			//	}
			//	else if (!isNature) // Human-made Tile
			//	{
			//		S_Help.Logger("do_crash_bury TYPE." + 3);
			//		WorldDamage.Instance.ApplyDamage(targetCell, 0.2f, targetCell + Grid.WidthInCells, popText, popText);
			//	}
			//	else // Natural cell
			//	{
			//		S_Help.Logger("do_crash_bury TYPE." + 4);
			//		GameObject pickupList = Grid.Objects[targetCell + Grid.WidthInCells, 3];
			//		Pickupable pickupable = pickupList?.GetComponent<Pickupable>();
			//		if (pickupable != null)
			//		{
			//			GameObject debris = pickupable.objectLayerListItem.gameObject;
			//			Vector3 position = debris.transform.GetPosition();
			//			position.y -= 1;
			//			debris.transform.SetPosition(position);
			//		}
			//	}
			//	S_Help.Logger("do_crash_bury DONE");
			//}
		}

		public static class Supporter
		{
			public static bool is_surface(int cell)
			{
				Element.State state = Grid.Element[cell].state;
				//Gas 0x01, Liquid 0x02, Solid x03
				if ((state & Element.State.Liquid) == Element.State.Liquid)
				{
					if (Grid.ExposedToSunlight[cell] > Grid.ExposedToSunlight[cell - Grid.WidthInCells])
					{
						return true;
					}
				}
				return false;
			}
			public static bool is_chaos(int cell_b)
			{
				int cell_a = cell_b + Grid.WidthInCells;
				if (!Grid.IsValidCell(cell_a))
				{
					return false;
				}
				if (Grid.ElementIdx[cell_a] != Grid.ElementIdx[cell_b] &&
					Grid.IsGas(cell_a) && Grid.IsGas(cell_b) &&
					Grid.Mass[cell_a] > 0 && Grid.Mass[cell_b] > 0)
				{
					return true;
				}

				return false;
			}
			public static bool is_crash(int cell)
			{
				if (Grid.IsGas(cell) && Grid.Mass[cell] > 200)
				{
					for (int dir = 0; dir < 4; dir++)
					{
						int cell2 = S_Help.CellShift(cell, dir);
						if (GasBolck(cell2))
						{
							return true;
						}
					}
				}
				else if (Grid.IsLiquid(cell) && Grid.Mass[cell] > Grid.Element[cell].molarMass * 5f)
				{
					for (int dir = 0; dir < 4; dir++)
					{
						int cell2 = S_Help.CellShift(cell, dir);
						if (LiquidBolck(cell2))
						{
							return true;
						}
					}
				}
				else if (DebrisBolck(cell - Grid.WidthInCells))
				{
					if (DebrisMassCounter(cell) > 100001)
					{
						return true;
					}
				}
				return false;
			}
			//Sim.Cell.Properties
			//	GasImpermeable = 1
			//	LiquidImpermeable = 2
			//	SolidImpermeable = 4
			//	Unbreakable = 8
			//	Transparent = 16
			//	Opaque = 32
			//	NotifyOnMelt = 64
			//	ConstructedTile = 128
			//Elemnt.State
			//	Vacuum = 0
			//	Gas = 1
			//	Liquid = 2
			//	Solid = 3
			//	Unbreakable = 4
			//	Unstable = 8
			//	TemperatureInsulated = 16
			public static bool GasBolck(int cell)
			{
				if (!Grid.IsValidCell(cell))
					return false;
				if (((int)Grid.Element[cell].state & 0x07) == 0x07)
					return false;
				return ((int)Grid.Element[cell].state & 0x03) == 0x03 || (Grid.Properties[cell] & 0x01) == 0x01;
			}

			public static bool LiquidBolck(int cell)
			{
				if (!Grid.IsValidCell(cell))
					return false;
				if (((int)Grid.Element[cell].state & 0x07) == 0x07)
					return false;
				return ((int)Grid.Element[cell].state & 0x03) == 0x03 || (Grid.Properties[cell] & 0x02) == 0x02;
			}

			public static bool DebrisBolck(int cellb)
			{
				int cella = cellb + Grid.WidthInCells;

				if (!Grid.IsValidCell(cella) || !Grid.IsSolidCell(cellb))
					return false;
				if (((int)Grid.Element[cellb].state & 0x07) == 0x07)
					return false;

				return (Grid.Solid[cellb] || ((Grid.Properties[cellb] & 0x04) != 0x00)) &&
					  !(Grid.Solid[cella] || ((Grid.Properties[cella] & 0x04) != 0x00));
			}

			public static float DebrisMassCounter(int cell)
			{
				float mass = 0f;

				Pickupable component = Grid.Objects[cell, 3]?.GetComponent<Pickupable>();
				if (component != null)
				{
					for (ObjectLayerListItem objectLayerListItem = component.objectLayerListItem; objectLayerListItem != null; objectLayerListItem = objectLayerListItem.nextItem)
					{
						KPrefabID component2 = objectLayerListItem.gameObject.GetComponent<KPrefabID>();
						PrimaryElement component3 = component2.GetComponent<PrimaryElement>();
						mass += component3.Mass;
					}
				}

				return mass;
			}
			public static void examine_crash(int cell)
			{
				if (Grid.IsGas(cell) && Grid.Mass[cell] > 200)
				{
					for (int dir = 0; dir < 4; dir++)
					{
						int cell2 = S_Help.CellShift(cell, dir);
						if (GasBolck(cell2))
						{
							float damage = Grid.Mass[cell] / 200 - 1;
							examine_crash_dig(cell2, cell, damage, false);
						}
					}
				}
				if (Grid.IsLiquid(cell) && Grid.Mass[cell] > Grid.Element[cell].molarMass * 5f)
				{
					for (int dir = 0; dir < 4; dir++)
					{
						int cell2 = S_Help.CellShift(cell, dir);
						if (LiquidBolck(cell2))
						{
							float damage = Grid.Mass[cell] / Grid.Element[cell].molarMass / 5f - 1;
							examine_crash_dig(cell2, cell, damage, true);
						}
					}
				}
				if (DebrisBolck(cell - Grid.WidthInCells))
				{
					if (DebrisMassCounter(cell) > 100001)
					{
						examine_crash_bury(cell - Grid.WidthInCells);
					}
				}
			}

			private static void examine_crash_dig(int targetCell, int sourceCell, float damage, bool isliquid)
			{
				GameObject gameObject = Grid.Objects[targetCell, 9];
				LocString popText = isliquid ? S_Text.PRESSURE_DAMAGE.LIQUID_PRESSURE : S_Text.PRESSURE_DAMAGE.GAS_PRESSURE;
				bool isBuild = false;
				if (gameObject != null)
				{
					SimCellOccupier occupier = gameObject.GetComponent<SimCellOccupier>();
					MakeBaseSolid.Def def = gameObject.GetDef<MakeBaseSolid.Def>();
					isBuild = occupier != null && !occupier.doReplaceElement;
					isBuild |= def != null && def.occupyFoundationLayer;
				}

				float strength = isBuild ? gameObject.GetComponent<PrimaryElement>().Element.strength
										 : Grid.Element[targetCell].strength;
				if (strength == 0f || damage < strength)
					return;
				float damage_tunned = Mathf.Min(damage / strength - 0.99f, 0.1f); //0.01 ~ 0.

				if (isBuild)
				{
					BuildingHP buildingHP = gameObject.GetComponent<BuildingHP>();
					DamageSourceInfo damageSourceInfo = new BuildingHP.DamageSourceInfo
					{
						damage = Math.Max(1, Mathf.RoundToInt(damage_tunned * (float)buildingHP.MaxHitPoints)),
						source = popText,
						popString = popText
					};
					crash_Build_Dic.Add(buildingHP, damageSourceInfo);
				}
				else
				{
					crash_World_List.Add(new Vector4(targetCell, sourceCell, damage_tunned * 1.5f, isliquid ? 2 : 1));
				}
			}

			private static void examine_crash_bury(int targetCell)
			{
				GameObject gameObject = Grid.Objects[targetCell, 9];
				bool isNature = Grid.Properties[targetCell] == 0;
				bool isBuild = false;
				if (gameObject != null)
				{
					SimCellOccupier occupier = gameObject.GetComponent<SimCellOccupier>();
					MakeBaseSolid.Def def = gameObject.GetDef<MakeBaseSolid.Def>();
					isBuild = occupier != null && !occupier.doReplaceElement;
					isBuild |= def != null && def.occupyFoundationLayer;
				}

				if (isBuild) // i.e.: Mesh Tile
				{
					BuildingHP buildingHP = gameObject.GetComponent<BuildingHP>();
					DamageSourceInfo damageSourceInfo = new BuildingHP.DamageSourceInfo
					{
						damage = Math.Max(1, Mathf.RoundToInt(0.1f * (float)buildingHP.MaxHitPoints)),
						source = S_Text.PRESSURE_DAMAGE.SOLID_PRESSURE,
						popString = S_Text.PRESSURE_DAMAGE.SOLID_PRESSURE
					};
					crash_Build_Dic.Add(buildingHP, damageSourceInfo);
				}
				else if (Grid.Mass[targetCell] > 0 && Grid.Element[targetCell].strength == 0)
				{
					return;
				}
				else if (!isNature) // Human-made Tile
				{
					crash_World_List.Add(new Vector4(targetCell, targetCell + Grid.WidthInCells, 0.2f, 3));
				}
				else // Natural cell
				{
					GameObject pickupList = Grid.Objects[targetCell + Grid.WidthInCells, 3];
					Pickupable pickupable = pickupList?.GetComponent<Pickupable>();
					if (pickupable != null)
					{
						GameObject debris = pickupable.objectLayerListItem.gameObject;
						Vector3 position = debris.transform.GetPosition();
						position.y -= 1;
						debris.transform.SetPosition(position);
					}
				}
			}
		}
	}
}
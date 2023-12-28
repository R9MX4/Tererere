using HarmonyLib;
using System;
using UnityEngine;
using static Horizon.S_Text;

namespace Horizon
{
	public class F_MakeBaseSolid
	{
		[HarmonyPatch(typeof(SolarPanelConfig), "DoPostConfigureComplete")]
		public class SolarPanelConfig_DoPostConfigureComplete_Patch
		{
			private static void Postfix(GameObject go)
			{
				MakeBaseSolid.Def def = go.GetDef<MakeBaseSolid.Def>();
				if (def != null)
					def.occupyFoundationLayer = true;
			}
		}

		[HarmonyPatch(typeof(MakeBaseSolid), "InitializeStates")]
		public class MakeBaseSolid_InitializeStates_Patch
		{
			private static bool Prefix(MakeBaseSolid __instance, out StateMachine.BaseState default_state)
			{
				default_state = __instance.root;
				__instance.root
					.Enter(new StateMachine<MakeBaseSolid, MakeBaseSolid.Instance, IStateMachineTarget, MakeBaseSolid.Def>.State.Callback(ConvertToSolid))
					.Exit(new StateMachine<MakeBaseSolid, MakeBaseSolid.Instance, IStateMachineTarget, MakeBaseSolid.Def>.State.Callback(ConvertToVacuum))
					.EventHandler(GameHashes.BuildingBroken, new StateMachine<MakeBaseSolid, MakeBaseSolid.Instance, IStateMachineTarget, MakeBaseSolid.Def>.State.Callback(ConvertToVacuum))
					.EventHandler(GameHashes.BuildingFullyRepaired, new StateMachine<MakeBaseSolid, MakeBaseSolid.Instance, IStateMachineTarget, MakeBaseSolid.Def>.State.Callback(ConvertToSolid));
				return false;
			}
		}

		private static void ConvertToSolid(MakeBaseSolid.Instance smi)
		{
			if (smi?.buildingComplete == null)
				return;

			int baseCell = Grid.PosToCell(smi.gameObject);
			PrimaryElement primaryElement = smi.GetComponent<PrimaryElement>();
			Building building = smi.GetComponent<Building>();

			foreach (CellOffset offset in smi.def.solidOffsets)
			{
				CellOffset rotatedOffset = building.GetRotatedOffset(offset);
				int cell = Grid.OffsetCell(baseCell, rotatedOffset);
				if (smi.def.occupyFoundationLayer)
				{
					SimMessages.ReplaceAndDisplaceElement(cell, primaryElement.ElementID, CellEventLogger.Instance.SimCellOccupierOnSpawn, primaryElement.Mass / smi.def.solidOffsets.Length, primaryElement.Temperature, primaryElement.DiseaseIdx, primaryElement.DiseaseCount, -1);
					Grid.Objects[cell, 9] = smi.gameObject;
				}
				else
				{
					SimMessages.ReplaceAndDisplaceElement(cell, SimHashes.Vacuum, CellEventLogger.Instance.SimCellOccupierOnSpawn, 0f, 0f, byte.MaxValue, 0, -1);
				}

				Grid.Foundation[cell] = true;
				Grid.SetSolid(cell, true, CellEventLogger.Instance.SimCellOccupierForceSolid);
				SimMessages.SetCellProperties(cell, (byte)floorCellProperties);
				Grid.RenderedByWorld[cell] = false;
				World.Instance.OnSolidChanged(cell);

                GameScenePartitioner.Instance.TriggerEvent(cell, GameScenePartitioner.Instance.solidChangedLayer, null);
            }
		}

		private static void ConvertToVacuum(MakeBaseSolid.Instance smi)
		{
			if (smi?.buildingComplete == null)
				return;

			int baseCell = Grid.PosToCell(smi.gameObject);
			Building building = smi.GetComponent<Building>();

			foreach (CellOffset offset in smi.def.solidOffsets)
			{
				CellOffset rotatedOffset = building.GetRotatedOffset(offset);
				int cell = Grid.OffsetCell(baseCell, rotatedOffset);

				if (Grid.Objects[cell, 9] != null)
				{
					SimMessages.ReplaceAndDisplaceElement(cell, SimHashes.Vacuum, CellEventLogger.Instance.SimCellOccupierOnSpawn, 0f, 0f, byte.MaxValue, 0, -1);
					Grid.Objects[cell, 9] = null;
				}

				if ((Grid.Properties[cell] & (byte)floorCellProperties) == (byte)floorCellProperties)
				{
					Grid.Foundation[cell] = false;
					Grid.SetSolid(cell, false, CellEventLogger.Instance.SimCellOccupierDestroy);
					SimMessages.ClearCellProperties(cell, (byte)floorCellProperties);
				}

				Grid.RenderedByWorld[cell] = true;
				World.Instance.OnSolidChanged(cell);

				try { GameScenePartitioner.Instance.TriggerEvent(cell, GameScenePartitioner.Instance.solidChangedLayer, null); }
				catch (Exception error) { S_Help.Logger("[ERROR] MOD-Horizon_MakeBaseSolid: ConvertToVacuum:\n" + error + "\n"); }
			}
		}

		private const Sim.Cell.Properties floorCellProperties = (Sim.Cell.Properties)103;
	}
}
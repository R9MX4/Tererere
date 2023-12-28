using HarmonyLib;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System;
using UnityEngine;

namespace Horizon
{
	public class N_Darkness
	{
		public static SYNC _darkness;
		public static void CreateSyncTask()
		{
			_darkness = new SYNC();
			SimAndRenderScheduler.instance.render200ms.Add(_darkness, false);
		}

		public class SYNC : KMonoBehaviour, IRender200ms
		{
			public void Render200ms(float dt)
			{
				S_Help.TLogger("MOD-Horizon_Darkness: Render200ms");
				BackUpdater.TryUpdate();
			}
		}

		public class Patch
		{
			[HarmonyPatch(typeof(GridVisibility), "Reveal")]
			public class GridVisibility_Reveal_Patch
			{
				private static void Prefix(ref float innerRadius)
				{
					innerRadius /= 3;
				}
			}
			[HarmonyPatch(typeof(Grid), "IsVisible")]
			public class Grid_IsVisible_Patch
			{
				private static void Postfix(int cell, ref bool __result)
				{
					if (N_Darkness.BackUpdater.is_inited)
					{
						__result &= BackUpdater.visible[cell] > 20 || !PropertyTextures.IsFogOfWarEnabled;
					}
				}
			}

			[HarmonyPatch(typeof(PropertyTextures), "UpdateFogOfWar")]
			public class PropertyTextures_GUpdateFogOfWar_Patch
			{
				private static bool Prefix(TextureRegion region, int x0, int y0, int x1, int y1)
				{
					if (!N_Darkness.BackUpdater.is_inited)
					{
						S_Help.TLogger("MOD-Horizon_Darkness: UpdateFogOfWar Fail, Darkness Null");
						return true;
					}

					S_Help.TLogger("MOD-Horizon_Darkness: UpdateFogOfWar");
					WorldContainer worldContainer = ClusterManager.Instance?.activeWorld;
					int worldsky = (worldContainer != null) ? (worldContainer.WorldSize.y + worldContainer.WorldOffset.y - 1) : Grid.HeightInCells - 1;
					for (int i = y0; i <= y1; i++)
					{
						for (int j = x0; j <= x1; j++)
						{
							int cell = Grid.XYToCell(j, i);
							if (!Grid.IsActiveWorld(cell))
							{
								int skycell = Grid.XYToCell(j, worldsky);
								if (Grid.IsValidCell(skycell))
								{
									region.SetBytes(j, i, BackUpdater.visible[skycell]);
								}
								else
								{
									region.SetBytes(j, i, 0);
								}
							}
							else
							{
								region.SetBytes(j, i, BackUpdater.visible[cell]);
							}
						}
					}
					return false;
				}
			}

			[HarmonyPatch(typeof(TimeOfDay), "UpdateVisuals")]
			public class Night_Shader
			{
				public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
				{
					List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
					for (int i = 0; i < codes.Count; i++)
					{
						if (codes[i].opcode == OpCodes.Ldc_R4 && codes[i].operand.ToString() == "1")
						{
							codes[i].operand = 0.2f;
							S_Help.Logger("MOD-Horizon_Darkness: Night Filter 1=>" + codes[i].operand);
							return codes.AsEnumerable();
						}
					}
					S_Help.Logger("MOD-Horizon_Darkness: !!! Adjust Night Filter FAIL !!!");
					return codes.AsEnumerable();
				}
			}
			[HarmonyPatch(typeof(MeshTileConfig), "ConfigureBuildingTemplate")]
			public class MeshTileConfig_ConfigureBuildingTemplate_Patch
			{
				private static void Postfix(GameObject go)
				{
					SimCellOccupier occupier = go.GetComponent<SimCellOccupier>();
					if (occupier != null)
					{
						occupier.setTransparent = false;
						occupier.setOpaque = true;
					}
				}
			}
			[HarmonyPatch(typeof(GasPermeableMembraneConfig), "ConfigureBuildingTemplate")]
			public class GasPermeableMembraneConfig_ConfigureBuildingTemplate_Patch
			{
				private static void Postfix(GameObject go)
				{
					SimCellOccupier occupier = go.GetComponent<SimCellOccupier>();
					if (occupier != null)
					{
						occupier.setTransparent = false;
						occupier.setOpaque = true;
					}
				}
			}
		}

		public static class BackUpdater
		{
			static BackgroundWorker backUpdater = new BackgroundWorker();
			static int[]   brightCopy; 
			static byte[]  visualCopy;
			static byte[]  exposeCopy;
			static short[] eleIdxCopy;
			static float[] temperCopy;
			//static float timeOfDay;
			//static float[] radiatCopy;

			static bool[] isEleEmitLight;
			static unsafe byte* resultDATA;
			static public unsafe byte* VisIdx;
			const int _VisEdge = 1;
			//static unsafe int*  WorldData;

			static public N_Darkness.BackUpdater.visibleIndexer visible;
			public struct visibleIndexer
			{
				public unsafe byte this[int i]
				{
					get
					{
						return N_Darkness.BackUpdater.VisIdx[i];
					}
				}
			}

			static public unsafe bool is_inited
			{
				get
				{
					return VisIdx != null;
				}
			}

			static public void Init()
			{
				S_Help.Logger("MOD-Horizon_Darkness: Init BackUpdater");
				backUpdater.DoWork += new DoWorkEventHandler(back_Updater);
				isEleEmitLight = new bool[ElementLoader.elements.Count];
				for (int idx = 0; idx < ElementLoader.elements.Count; idx++)
					isEleEmitLight[idx] = ElementLoader.elements[idx].HasTag(GameTags.EmitsLight);
			}

			static unsafe public void InitWorld()
			{
				if (is_inited)
				{
					Marshal.FreeHGlobal((IntPtr)resultDATA);
					VisIdx = null;
					backUpdater.CancelAsync();
				}
				backUpdater.WorkerReportsProgress = true;
				backUpdater.WorkerSupportsCancellation = true;

				brightCopy = new int[Grid.CellCount];
				visualCopy = new byte[Grid.CellCount];
				exposeCopy = new byte[Grid.CellCount];
				eleIdxCopy = new short[Grid.CellCount];
				temperCopy = new float[Grid.CellCount];
				//Apply for two memory spaces, one for calculate newest result, one for save and OUTPUT previous result
				resultDATA = (byte*)Marshal.AllocHGlobal(Grid.CellCount * 2);
			}

			static unsafe public void TryUpdate()
			{
				if (backUpdater.IsBusy)
				{
					S_Help.TLogger("MOD-Horizon_Darkness: Async busy");
				}
				else
				{
					// Estimate time consume:  around 1 ms
					Array.Copy(Grid.LightCount, brightCopy, Grid.CellCount);
					Array.Copy(Grid.Visible, visualCopy, Grid.CellCount);
					Marshal.Copy((IntPtr)Grid.exposedToSunlight, exposeCopy, Grid.WidthInCells, Grid.CellCount - Grid.WidthInCells); //Y Offset = -1
					Marshal.Copy((IntPtr)Grid.elementIdx, eleIdxCopy, 0, Grid.CellCount);
					Marshal.Copy((IntPtr)Grid.temperature, temperCopy, 0, Grid.CellCount);

					//timeOfDay = GameClock.Instance.GetCurrentCycleAsPercentage();
					backUpdater.RunWorkerAsync(1);
				}
			}

			static unsafe private void back_Updater(object sender, EventArgs e)
			{
				// Estimate time consume: < 33 ms
				S_Help.TLogger("MOD-Horizon_Darkness: Back_Updater start");
				byte* targetDATA = resultDATA == VisIdx ? resultDATA + Grid.CellCount : resultDATA;

				for (int worldidx = 0; worldidx < ClusterManager.Instance.WorldContainers.Count; worldidx++)
				{
					if (backUpdater.CancellationPending == true)
					{
						VisIdx = null;
						return;
					}
					WorldContainer worldContainer = ClusterManager.Instance.WorldContainers[worldidx];
					int ExtendSizeX = worldContainer.WorldSize.x + _VisEdge * 2;
					int ExtendSizeY = worldContainer.WorldSize.Y + _VisEdge * 2;
					int[,] tempData1 = new int[ExtendSizeX, ExtendSizeY];
					int[,] tempData2 = new int[ExtendSizeX, ExtendSizeY];
					int worldlight = (int)ClusterManager.Instance.GetWorld(worldidx).currentSunlightIntensity;
					for (int xIdx = 0; xIdx < worldContainer.WorldSize.x; xIdx++)
					{
						for (int yIdx = 0; yIdx < worldContainer.WorldSize.y; yIdx++)
						{
							int cell = (xIdx + worldContainer.WorldOffset.X) + (yIdx + worldContainer.WorldOffset.Y) * Grid.WidthInCells;
							int brightness = brightCopy[cell] + exposeCopy[cell] * worldlight / 510;
							if (temperCopy[cell] > 723.15) brightness += (int)(temperCopy[cell] - 673.15f);
							if (isEleEmitLight[eleIdxCopy[cell]]) brightness += 500;
							tempData1[xIdx + _VisEdge, yIdx + _VisEdge] = Math.Min(brightness, 1000) + 600;
						}
					}
					//															   Remind_Lux Brightness
					HALO_Inner(tempData1, tempData2, worldContainer.WorldSize, true); // Max = 1600 1.15
					HALO_Outer(tempData2, tempData1, worldContainer.WorldSize, 0.85f, 0, false); //1360
					HALO_Outer(tempData1, tempData2, worldContainer.WorldSize, 0.85f, 0, true); //1156
					HALO_Outer(tempData2, tempData1, worldContainer.WorldSize, 0.85f, 0, false); // 982
					HALO_Outer(tempData1, tempData2, worldContainer.WorldSize, 0.85f, 0, true); // 834
					HALO_Outer(tempData2, tempData1, worldContainer.WorldSize, 0.85f, 0, false); // 708 0.208

					for (int yIdx = 0; yIdx < worldContainer.WorldSize.y; yIdx++)
					{
						//float baselight = Mathf.Clamp(0.3f * yIdx / worldContainer.WorldSize.y, 0.1f, 0.2f);
						for (int xIdx = 0; xIdx < worldContainer.WorldSize.x; xIdx++)
						{
							int cell = (xIdx + worldContainer.WorldOffset.X) + (yIdx + worldContainer.WorldOffset.Y) * Grid.WidthInCells;
							float brightness = tempData1[xIdx + _VisEdge, yIdx + _VisEdge] / 1000f - 0.6f;
							brightness = Mathf.Max(brightness, 0f) + 0.1f;
							brightness *= visualCopy[cell];
							targetDATA[cell] = (byte)(brightness > 255 ? 255 : brightness);
						}
					}
				}

				VisIdx = targetDATA;
				S_Help.TLogger("MOD-Horizon_Darkness: Back_Updater Finish" + (int)(IntPtr)VisIdx);
			}

			static private void HALO_Inner(int[,] source, int[,] result, Vector2I worldSize, bool is_corner)
			{
				for (int xIdx = _VisEdge; xIdx < worldSize.x + _VisEdge; xIdx++)
				{
					for (int yIdx = _VisEdge; yIdx < worldSize.y + _VisEdge; yIdx++)
					{
						int brightness = source[xIdx, yIdx];
						if (brightness == 0) continue;
						result[xIdx    , yIdx    ] = Math.Max(result[xIdx    , yIdx    ], brightness);
						result[xIdx + 1, yIdx    ] = Math.Max(result[xIdx + 1, yIdx    ], brightness);
						result[xIdx - 1, yIdx    ] = Math.Max(result[xIdx - 1, yIdx    ], brightness);
						result[xIdx    , yIdx + 1] = Math.Max(result[xIdx    , yIdx + 1], brightness);
						result[xIdx    , yIdx - 1] = Math.Max(result[xIdx    , yIdx - 1], brightness);
						if (!is_corner) continue;
						result[xIdx + 1, yIdx + 1] = Math.Max(result[xIdx + 1, yIdx + 1], brightness);
						result[xIdx - 1, yIdx - 1] = Math.Max(result[xIdx - 1, yIdx - 1], brightness);
						result[xIdx - 1, yIdx + 1] = Math.Max(result[xIdx - 1, yIdx + 1], brightness);
						result[xIdx + 1, yIdx - 1] = Math.Max(result[xIdx + 1, yIdx - 1], brightness);
					}
				}
			}
			static private void HALO_Outer(int[,] source, int[,] result, Vector2I worldSize, float factor, int delta, bool is_corner)
			{
				for (int xIdx = _VisEdge; xIdx < worldSize.x + _VisEdge; xIdx++)
				{
					for (int yIdx = _VisEdge; yIdx < worldSize.y + _VisEdge; yIdx++)
					{
						result[xIdx, yIdx] = Math.Max(result[xIdx, yIdx], source[xIdx, yIdx]);

						int brightness = (int)((float)source[xIdx, yIdx] * factor) - delta;
						if (brightness <= 0) continue;
						result[xIdx + 1, yIdx    ] = Math.Max(result[xIdx + 1, yIdx    ], brightness);
						result[xIdx - 1, yIdx    ] = Math.Max(result[xIdx - 1, yIdx    ], brightness);
						result[xIdx    , yIdx + 1] = Math.Max(result[xIdx    , yIdx + 1], brightness);
						result[xIdx    , yIdx - 1] = Math.Max(result[xIdx    , yIdx - 1], brightness);
						if (!is_corner) continue;
						result[xIdx + 1, yIdx + 1] = Math.Max(result[xIdx + 1, yIdx + 1], brightness);
						result[xIdx - 1, yIdx - 1] = Math.Max(result[xIdx - 1, yIdx - 1], brightness);
						result[xIdx - 1, yIdx + 1] = Math.Max(result[xIdx - 1, yIdx + 1], brightness);
						result[xIdx + 1, yIdx - 1] = Math.Max(result[xIdx + 1, yIdx - 1], brightness);
					}
				}
			}
		}
	}
}
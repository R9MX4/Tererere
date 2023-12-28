using HarmonyLib;
using ProcGen;
using ProcGenGame;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using System.Reflection;
using System;
using UnityEngine;
using Klei;
using ObjectCloner;
using TUNING;

namespace Horizon
{
	public class H_Horizon
	{
		static Vector2 startPos = default;
		static Vector2 hillPos = default;
		static Vector2 skyPos = default;
		static List<Vector2> CoreListS1 = new List<Vector2>();
		static float[] Horizon = null;

		public class ClusterGen_Init
		{
			[HarmonyPatch(typeof(ClusterLayouts), "UpdateClusterCache")]
			public class Mark_Horizon
			{
				private static void Postfix(ref ClusterLayouts __instance)
				{
					int count = 0;
					foreach (KeyValuePair<string, ClusterLayout> clusterCache in __instance.clusterCache)
					{
						string start_world = clusterCache.Value.worldPlacements[clusterCache.Value.startWorldIndex].world;

						if (!start_world.Contains("-horizon"))
						{
							string start_horizon = start_world + "-horizon";
							clusterCache.Value.worldPlacements[clusterCache.Value.startWorldIndex].world = start_horizon;
							count++;
							S_Help.TLogger("MOD-Horizon_Terr: Create horizon world: " + start_horizon);
						}
					}
					S_Help.Logger("MOD-Horizon_Terr: Create " + count + " horizon worlds");
				}
			}

			[HarmonyPatch(typeof(Worlds), "UpdateWorldCache")]
			public class Create_Horizon
			{
				private static bool Prefix(ref Worlds __instance, ISet<string> referencedWorlds, List<YamlIO.Error> errors)
				{
					ProcGen.World terr_world = YamlIO.LoadFile<ProcGen.World>(S_Help.GetModPath() + "/worldgen/worlds/Terr_Start.yaml", delegate (YamlIO.Error error, bool force_log_as_warning) { errors.Add(error); });

					foreach (string referencedWorld in referencedWorlds)
					{
						string world_orig = referencedWorld;
						string world_hori = null;
						if (referencedWorld.Contains("-horizon"))
						{
							world_orig = referencedWorld.Replace("-horizon", "");
							world_hori = referencedWorld;
						}
						if (!__instance.worldCache.ContainsKey(world_orig))
						{
							string path_orig = SettingsCache.RewriteWorldgenPathYaml(world_orig);
							ProcGen.World world = YamlIO.LoadFile<ProcGen.World>(path_orig, delegate (YamlIO.Error error, bool force_log_as_warning) { errors.Add(error); });
							if (world == null)
							{
								DebugUtil.LogWarningArgs("Failed to load world: ", path_orig);
							}
							else if (world.skip != ProcGen.World.Skip.Always && (world.skip != ProcGen.World.Skip.EditorOnly || UnityEngine.Application.isEditor))
							{
								world.filePath = world_orig;
								__instance.worldCache[world.filePath] = world;
							}

							if (world_hori != null)
							{
								ProcGen.World combined_world = World_Combine(world, terr_world);
								combined_world.filePath = world_hori;
								__instance.worldCache[combined_world.filePath] = combined_world;

								//Save for debug
								if (A_Launcher.isdebug)
								{
									string path_hori = S_Help.GetModPath() + "/worldgen/" + world_hori.Replace("expansion1::", "") + "_debug.yaml";
									if (!Directory.Exists(System.IO.Path.GetDirectoryName(path_hori)))
										Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path_hori));

									YamlIO.Save<ProcGen.World>(combined_world, path_hori, null);
								}
							}
						}
					}

					return false;
				}
			}

			[HarmonyPatch(typeof(SettingsCache), "LoadFiles")]
			[HarmonyPatch(new Type[] { typeof(string), typeof(string), typeof(List<YamlIO.Error>) })]
			public class Tuning_Horizon
			{
				private static void Postfix()
				{
					int count = 0;
					foreach (KeyValuePair<string, ProcGen.World> subworldPair in SettingsCache.worlds.worldCache)
					{
						bool is_horizon = subworldPair.Key.Contains("-horizon");

						if (!is_horizon)
						{
							count++;
							World_Sunlight(subworldPair.Value);
							List<string> fixedTraits = SettingsCache.worlds.worldCache[subworldPair.Key].fixedTraits;
						}
					}
					S_Help.Logger("MOD-Horizon_Terr: " + count + " worlds sunlight adjusted");
				}
			}
			private static void World_Sunlight(ProcGen.World world)
			{
				Dictionary<int, List<string>> dic = new Dictionary<int, List<string>>();
				foreach (ProcGen.World.AllowedCellsFilter allowedCellsFilter in world.unknownCellsAllowedSubworlds)
				{
					if (allowedCellsFilter.tag == "AtSurface" && allowedCellsFilter.minDistance > 0 &&
						allowedCellsFilter.command == ProcGen.World.AllowedCellsFilter.Command.Replace)
					{
						List<string> selected_subworld = allowedCellsFilter.subworldNames.FindAll(x => !x.Contains("/space/Space"));
						if (selected_subworld.Count == 0) continue;

						if (dic.ContainsKey(allowedCellsFilter.minDistance)) dic[allowedCellsFilter.minDistance].AddRange(allowedCellsFilter.subworldNames);
						else dic[allowedCellsFilter.minDistance] = allowedCellsFilter.subworldNames;
					}
				}
				if (dic.Count > 0)
				{
					int minDistance = dic.Keys.Min();
					List<string> contentList = dic[minDistance];
					//Contain top 2 surface layers
					//if (dic.ContainsKey(minDistance + 1)) contentList.AddRange(dic[minDistance + 1]);

					List<float> avg_Temp_List = new List<float>();
					foreach (string content in contentList)
					{
						if (SettingsCache.subworlds.ContainsKey(content))
						{
							Temperature.Range range = SettingsCache.subworlds[content].temperatureRange;
							if (SettingsCache.temperatures.ContainsKey(range))
							{
								Temperature temperature = SettingsCache.temperatures[range];
								avg_Temp_List.Add((temperature.min + temperature.max) / 2);
							}
						}
					}

					if (avg_Temp_List.Count > 0)
					{
						float avg_Temp = avg_Temp_List.Average();
						float sunlight = S_Help.Temp2Sunlight(avg_Temp);
						string sunTraits = Get_SunTraits(sunlight);

						string originTrait = world.fixedTraits.Find(x => x.Contains("sunlight"));
						List<string> fixedTraits = world.fixedTraits.FindAll(x => !x.Contains("sunlight"));
						fixedTraits.Add(sunTraits);
						world.GetType().GetProperty("fixedTraits").SetValue(world, fixedTraits);

						S_Help.TLogger("MOD-Horizon_Terr: Sunlight of [" + world.filePath + "]: " + originTrait + " => " + sunTraits);
					}
				}
			}
			private static string Get_SunTraits(float sunlight)
			{
				string sunTraits;
				if (sunlight < 15000) sunTraits = FIXEDTRAITS.SUNLIGHT.NAME.VERY_VERY_LOW;			//10000
				else if (sunlight < 25000) sunTraits = FIXEDTRAITS.SUNLIGHT.NAME.VERY_LOW;			//20000
				else if (sunlight < 32500) sunTraits = FIXEDTRAITS.SUNLIGHT.NAME.LOW;				//30000
				else if (sunlight < 37500) sunTraits = FIXEDTRAITS.SUNLIGHT.NAME.MED_LOW;			//35000
				else if (sunlight < 45000) sunTraits = FIXEDTRAITS.SUNLIGHT.NAME.MED;				//40000
				else if (sunlight < 55000) sunTraits = FIXEDTRAITS.SUNLIGHT.NAME.MED_HIGH;			//50000
				else if (sunlight < 70000) sunTraits = FIXEDTRAITS.SUNLIGHT.NAME.HIGH;				//60000
				else if (sunlight < 100000) sunTraits = FIXEDTRAITS.SUNLIGHT.NAME.VERY_HIGH;		//80000
				else if (sunlight < 1000000) sunTraits = FIXEDTRAITS.SUNLIGHT.NAME.VERY_VERY_HIGH;	//100000
				else sunTraits = FIXEDTRAITS.SUNLIGHT.NAME.VERY_VERY_VERY_HIGH;						//120000

				return sunTraits;
			}
			private static ProcGen.World World_Combine(ProcGen.World orig_world, ProcGen.World terr_world)
			{
				ProcGen.World comb_world = SerializingCloner.Copy<ProcGen.World>(orig_world);
				List<ProcGen.World.AllowedCellsFilter> unknownCellsAllowedSubworlds = new List<ProcGen.World.AllowedCellsFilter>(terr_world.unknownCellsAllowedSubworlds);

				List<string> subworldNames = Collect_Subworlds(ref unknownCellsAllowedSubworlds, comb_world.unknownCellsAllowedSubworlds);
				List<WeightedSubworldName> subworldFiles = Collect_SubworldFiles(subworldNames, terr_world.subworldFiles, comb_world.subworldFiles);
				List<ProcGen.World.TemplateSpawnRules> worldTemplateRules = Collect_Templates(terr_world.worldTemplateRules, comb_world.worldTemplateRules);
				PropertyInfo[] propertyInfos = comb_world.GetType().GetProperties();
				Vector2I worldsize = new Vector2I(Math.Max(orig_world.worldsize.x, terr_world.worldsize.x),
												  Math.Min(Math.Max(orig_world.worldsize.y, terr_world.worldsize.y), (int)(terr_world.worldsize.y * 1.5f)));
				foreach (PropertyInfo propertyInfo in propertyInfos)
				{
					switch (propertyInfo.Name)
					{
						case "worldTraitScale":
							propertyInfo.SetValue(comb_world, terr_world.worldTraitScale);
							break;
						case "worldsize":
							propertyInfo.SetValue(comb_world, worldsize);
							break;
						case "defaultsOverrides":
							propertyInfo.SetValue(comb_world, terr_world.defaultsOverrides);
							break;
						case "subworldFiles":
							propertyInfo.SetValue(comb_world, subworldFiles);
							break;
						case "unknownCellsAllowedSubworlds":
							propertyInfo.SetValue(comb_world, unknownCellsAllowedSubworlds);
							break;
						case "worldTemplateRules":
							propertyInfo.SetValue(comb_world, worldTemplateRules);
							break;
						case "startSubworldName":
							propertyInfo.SetValue(comb_world, terr_world.startSubworldName);
							break;
						case "startingBaseTemplate":
							propertyInfo.SetValue(comb_world, terr_world.startingBaseTemplate);
							break;
						case "startingBasePositionHorizontal":
							propertyInfo.SetValue(comb_world, terr_world.startingBasePositionHorizontal);
							break;
						case "startingBasePositionVertical":
							float min = 1 - (1 - terr_world.startingBasePositionVertical.min) * terr_world.worldsize.y / worldsize.y;
							float max = 1 - (1 - terr_world.startingBasePositionVertical.max) * terr_world.worldsize.y / worldsize.y;
							propertyInfo.SetValue(comb_world, new MinMax(min, max));
							break;
						case "seasons":
							//Allowed Events: ClusterIceShower, ClusterOxyliteShower, ClusterBiologicalShower
							propertyInfo.SetValue(comb_world, terr_world.seasons);
							break;
						case "fixedTraits":
							propertyInfo.SetValue(comb_world, terr_world.fixedTraits);
							break;
					}
				}
				return comb_world;
			}
			private static List<string> Collect_Subworlds(ref List<ProcGen.World.AllowedCellsFilter> AllowedSubworlds, List<ProcGen.World.AllowedCellsFilter> RawAllowedSubworlds)
			{
				List<string> subworlds = new List<string>();

				foreach (ProcGen.World.AllowedCellsFilter allowedCellsFilter in RawAllowedSubworlds)
				{
					if (allowedCellsFilter.tagcommand == ProcGen.World.AllowedCellsFilter.TagCommand.Default)
					{
						AllowedSubworlds[0] = allowedCellsFilter;
						subworlds.AddRange(allowedCellsFilter.subworldNames);
					}
					else if (allowedCellsFilter.tag == "AtStart" && allowedCellsFilter.maxDistance >= 1)
					{
						allowedCellsFilter.GetType().GetProperty("tag").SetValue(allowedCellsFilter, "AtSurface");
						allowedCellsFilter.GetType().GetProperty("minDistance").SetValue(allowedCellsFilter, allowedCellsFilter.minDistance + 3);
						allowedCellsFilter.GetType().GetProperty("maxDistance").SetValue(allowedCellsFilter, allowedCellsFilter.maxDistance + 3);
						AllowedSubworlds.Add(allowedCellsFilter);
						subworlds.AddRange(allowedCellsFilter.subworldNames);
					}
					else if (allowedCellsFilter.tag == "AtSurface" && allowedCellsFilter.maxDistance >= 2 && allowedCellsFilter.command == ProcGen.World.AllowedCellsFilter.Command.Replace)
					{
						allowedCellsFilter.GetType().GetProperty("minDistance").SetValue(allowedCellsFilter, Math.Max(3, allowedCellsFilter.minDistance + 2));
						allowedCellsFilter.GetType().GetProperty("maxDistance").SetValue(allowedCellsFilter, Math.Max(3, allowedCellsFilter.maxDistance + 2));
						if (allowedCellsFilter.maxDistance < 90) //Not default subworld
							allowedCellsFilter.GetType().GetProperty("command").SetValue(allowedCellsFilter, ProcGen.World.AllowedCellsFilter.Command.UnionWith);
						AllowedSubworlds.Add(allowedCellsFilter);
						subworlds.AddRange(allowedCellsFilter.subworldNames);
					}
					else if (allowedCellsFilter.tag == "AtSurface" && allowedCellsFilter.maxDistance > 3 && allowedCellsFilter.command == ProcGen.World.AllowedCellsFilter.Command.ExceptWith)
					{
						allowedCellsFilter.GetType().GetProperty("minDistance").SetValue(allowedCellsFilter, 0);
						allowedCellsFilter.GetType().GetProperty("maxDistance").SetValue(allowedCellsFilter, allowedCellsFilter.maxDistance);
						AllowedSubworlds.Add(allowedCellsFilter);
						subworlds.AddRange(allowedCellsFilter.subworldNames);
					}
					else if (allowedCellsFilter.tag == "AtDepths")
					{
						AllowedSubworlds.Add(allowedCellsFilter);
						subworlds.AddRange(allowedCellsFilter.subworldNames);
					}
				}

				return subworlds;
			}
			private static List<WeightedSubworldName> Collect_SubworldFiles(List<string> subworldNames, List<WeightedSubworldName> SubworldFiles_terr, List<WeightedSubworldName> SubworldFiles_orig)
			{
				List<WeightedSubworldName> subworldFiles = new List<WeightedSubworldName>(SubworldFiles_terr);
				foreach (WeightedSubworldName subworldName in SubworldFiles_terr)
				{
					subworldNames.Add(subworldName.name);
				}
				subworldNames = subworldNames.Distinct().ToList();

				foreach (string subworldName in subworldNames)
				{
					bool is_existed = false;
					foreach (WeightedSubworldName subworldFile in subworldFiles)
					{
						if (subworldName == subworldFile.name)
						{
							is_existed = true;
							break;
						}

					}
					if (!is_existed)
					{
						foreach (WeightedSubworldName subworldFile in SubworldFiles_orig)
						{
							if (subworldName == subworldFile.name)
							{
								subworldFile.GetType().GetProperty("minCount").SetValue(subworldFile, 0);
								subworldFiles.Add(subworldFile);
								break;
							}
						}
					}
				}
				//remove unused subworld
				for (int idx = subworldFiles.Count - 1; idx < 0; idx--)
				{
					WeightedSubworldName subworldFile = subworldFiles[idx];
					bool is_existed = false;
					for (int idx2 = 0; idx2 < subworldNames.Count; idx2++)
					{
						if (subworldNames[idx2] == subworldFile.name)
						{
							is_existed = true;
							break;
						}
					}
					if (is_existed) subworldFiles.RemoveAt(idx);
				}
				return subworldFiles;
			}
			private static List<ProcGen.World.TemplateSpawnRules> Collect_Templates(List<ProcGen.World.TemplateSpawnRules> WorldTemplateRules_terr, List<ProcGen.World.TemplateSpawnRules> WorldTemplateRules_orig)
			{
				List<ProcGen.World.TemplateSpawnRules> worldTemplateRules = new List<ProcGen.World.TemplateSpawnRules>(WorldTemplateRules_terr);
				ProcGen.World.AllowedCellsFilter surfaceRule_replace = WorldTemplateRules_terr[0].allowedCellsFilter[0];
				ProcGen.World.AllowedCellsFilter surfaceRule_insert = SerializingCloner.Copy(surfaceRule_replace); ;
				surfaceRule_insert.GetType().GetProperty("command").SetValue(surfaceRule_insert, ProcGen.World.AllowedCellsFilter.Command.IntersectWith);

				foreach (ProcGen.World.TemplateSpawnRules worldTemplateRule in WorldTemplateRules_orig)
				{
					if (worldTemplateRule.names[0].Contains("warp")) continue;

					switch (worldTemplateRule.listRule)
					{
						case ProcGen.World.TemplateSpawnRules.ListRule.GuaranteeOne:
							worldTemplateRule.GetType().GetProperty("listRule").SetValue(worldTemplateRule, ProcGen.World.TemplateSpawnRules.ListRule.TryOne);
							break;
						case ProcGen.World.TemplateSpawnRules.ListRule.GuaranteeSome:
							worldTemplateRule.GetType().GetProperty("listRule").SetValue(worldTemplateRule, ProcGen.World.TemplateSpawnRules.ListRule.TrySome);
							break;
						case ProcGen.World.TemplateSpawnRules.ListRule.GuaranteeSomeTryMore:
							worldTemplateRule.GetType().GetProperty("listRule").SetValue(worldTemplateRule, ProcGen.World.TemplateSpawnRules.ListRule.TrySome);
							worldTemplateRule.GetType().GetProperty("someCount").SetValue(worldTemplateRule, worldTemplateRule.moreCount);
							break;
						case ProcGen.World.TemplateSpawnRules.ListRule.GuaranteeAll:
							worldTemplateRule.GetType().GetProperty("listRule").SetValue(worldTemplateRule, ProcGen.World.TemplateSpawnRules.ListRule.TryAll);
							break;
					}

					if (worldTemplateRule.allowedCellsFilter.Count > 0)
					{
						bool is_replace = false;
						for (int idx = worldTemplateRule.allowedCellsFilter.Count - 1; idx < 0; idx--)
						{
							ProcGen.World.AllowedCellsFilter allowedCellsFilter = worldTemplateRule.allowedCellsFilter[idx];

							if (allowedCellsFilter.tagcommand == ProcGen.World.AllowedCellsFilter.TagCommand.DistanceFromTag)
							{
								is_replace |= allowedCellsFilter.command == ProcGen.World.AllowedCellsFilter.Command.Replace;
								worldTemplateRule.allowedCellsFilter.RemoveAt(idx);
							}
						}
						if (is_replace) worldTemplateRule.allowedCellsFilter.Insert(0, surfaceRule_replace);
						else worldTemplateRule.allowedCellsFilter.Add(surfaceRule_insert);
					}

					worldTemplateRules.Add(worldTemplateRule);
				}
				return worldTemplateRules;
			}
		}

		public class WorldGen_Init
		{
			[HarmonyPatch(typeof(WorldLayout), "CreateTreeNodeWithFeatureAndBiome")]
			public class Init_Horizon
			{
				private static void Prefix(Vector2 pos, WorldLayout __instance)
				{
					bool isStartWorld = __instance.worldGen.isStartingWorld;
					if (startPos == default && isStartWorld)
					{
						startPos = pos;
						S_Help.Logger("MOD-Horizon_Terr: Print Gate Position:" + startPos.x + "," + startPos.y);
						Horizon = new float[__instance.mapWidth];
						float minheight = __instance.mapHeight - 3.5f * __instance.worldGen.Settings.GetFloatSetting("OverworldDensityMax") + 6;//start template Yshift 6
						Draw_Horizon(startPos, __instance.GetVoronoiTree(), minheight);
					}
					else if (startPos.y != 0 && !isStartWorld)
					{
						startPos = default;
					}
				}
			}
			static private void Draw_Horizon(Vector2 pos, VoronoiTree.Tree tree, float minHeight)
			{
				CoreListS1 = new List<Vector2>();
				List<Vector2> CoreListS2 = new List<Vector2>();
				for (int idx = 0; idx < tree.ChildCount(); idx++)
				{
					VoronoiTree.Node child = tree.GetChild(idx);
					if (child.minDistanceToTag["AtSurface"] == 2)
					{
						Vector2 position = child.site.position;
						position.y = Math.Max(position.y, minHeight);
						if (position.x > pos.x + 6 || position.x < pos.x - 6)
						{
							CoreListS2.Add(position);
						}
					}
					else if (child.minDistanceToTag["AtSurface"] == 1)
					{
						Vector2 position = child.site.position;
						CoreListS1.Add(position);
					}
				}

				CoreListS2.Sort((Vector2 a, Vector2 b) => a.x.CompareTo(b.x));
				CoreListS2.Add(new Vector2(pos.x - 5, pos.y));//start template X=[-3,4]
				CoreListS2.Add(new Vector2(pos.x + 6, pos.y));
				CoreListS2.Add(new Vector2(-1, CoreListS2[0].y));
				CoreListS2.Add(new Vector2(Horizon.Length, CoreListS2[CoreListS2.Count - 1].y));
				CoreListS2.Sort((Vector2 a, Vector2 b) => a.x.CompareTo(b.x));

				for (int x_pos = 0, x_idx = 0; x_pos < Horizon.Length; x_pos++)
				{
					x_idx += x_pos > CoreListS2[x_idx].x ? 1 : 0;
					Horizon[x_pos] = x_idx == 0 ? CoreListS2[0].y :
						(CoreListS2[x_idx].y * (x_pos - CoreListS2[x_idx - 1].x) + CoreListS2[x_idx - 1].y * (CoreListS2[x_idx].x - x_pos)) / (CoreListS2[x_idx].x - CoreListS2[x_idx - 1].x);
				}
			}
		}

		public class WorldGen_Draw
		{
			[HarmonyPatch(typeof(Graph<ProcGen.Map.Cell, ProcGen.Map.Edge>), "AddNode")]
			public class Feature_Horizon
			{
				private static void Prefix(ref string type, Vector2 position)
				{
					if (startPos.y <= 0f) return;

					if (type.Contains("biomes"))
					{
						if (position.y > Horizon[(int)position.x] + 6)//start template Yshift 6
						{
							type = "biomes/Terr/Air";
						}
					}
					else if (type.Contains("features"))
					{
						if (type == "features/Terr/SkyIsland")
						{
							skyPos = position;
							S_Help.Logger("MOD-Horizon_Terr: Sky Island Position:" + skyPos.x + "," + skyPos.y);
							CoreListS1.Sort((Vector2 a, Vector2 b) => a.x.CompareTo(b.x));
							hillPos = position.x > startPos.x ? CoreListS1[1] : CoreListS1[CoreListS1.Count - 2];
						}
						else if (position.y + 5 > Horizon[(int)position.x] && type != "features/generic/StartLocation")
						{
							type = "features/DefaultRoom";
						}
					}
				}
			}

			[HarmonyPatch(typeof(WorldGen), "DrawWorldBorder")]
			public class Fill_Horizon
			{
				private static void Postfix(ref Sim.Cell[] cells, Chunk world, ref HashSet<int> borderCells, WorldGen __instance)
				{
					if (__instance == null || !__instance.isStartingWorld)
					{
						return;
					}
					if (skyPos.x == 0)
					{
						S_Help.Logger("MOD-Horizon_Terr: Missing Skyland, Hill and Lake");
						return;
					}

					ushort elemAbyss = ElementLoader.FindElementByHash(SimHashes.Katairite).idx;
					SeededRandom random = new SeededRandom(__instance.data.globalWorldSeed);

					//Build Ice Hill
					Vector2I heatsinkPos = Draw_Hill(cells, world.size, random);
					Build_HeatSink(heatsinkPos, __instance);

					//Draw Lake
					int direction = skyPos.x < startPos.x ? 1 : -1;
					int tempY = (int)skyPos.y - 12;
					Find_Bottom(cells, (int)skyPos.x, world.size, ref tempY);
					Vector2 lakePos = new Vector2(skyPos.x + direction * (random.RandomValue() * 5.0f), tempY - 2 - random.RandomValue() * 3f);

					float xDelta = random.RandomValue() * 4 + 6;
					float yDelta = random.RandomValue() * 4 + 6;
					float xL = Math.Max(lakePos.x - xDelta, 0);
					float xR = Math.Min(lakePos.x + xDelta, world.size.x - 1);
					float yD = Math.Max(lakePos.y - yDelta, 3);
					float yU = Math.Min(lakePos.y + yDelta, world.size.y - 3);

					List<Vector2I> LakeInner = new List<Vector2I>();
					for (int PosX = (int)xL; PosX <= xR; PosX++)
					{
						for (int PosY = (int)yD; PosY <= yU; PosY++)
						{
							//int cell = PosX + PosY * world.size.x;
							float dist = Mathf.Pow((PosX - lakePos.x) / xDelta, 2) + Mathf.Pow((PosY - lakePos.y) / yDelta, 2);
							if (dist < 1)
							{
								LakeInner.Add(new Vector2I(PosX, PosY));
							}
						}
					}
					S_Help.Logger("MOD-Horizon_Terr: Fresh Lake Position:" + lakePos.x + "," + lakePos.y);

					//Draw River
					HashSet<Vector2I> RiverInnerHash = new HashSet<Vector2I>();
					for (int idxR = 0; idxR < 2; idxR++) //Draw two rivers
					{
						Vector2 riverPos = new Vector2(lakePos.x - random.RandomValue() * xDelta + xDelta / 2, lakePos.y - random.RandomValue() * yDelta / 2 - 1);
						int segment = random.RandomRange(15 - idxR * 7, 20 - idxR * 7);
						direction = idxR % 2 == 0 ? direction : -direction;
						S_Help.TLogger("MOD-Horizon_Terr: River No." + idxR + " Position:" + riverPos.x + "," + riverPos.y + " Segment:" + segment);

						int borderFlag = 0; //0 Normal; 1 Abyss; 2 WorldBorder
						for (int idxS = 0; idxS < segment && borderFlag < 2; idxS++)
						{
							float angle = random.RandomValue() * 90 - idxR * 20 - 60;
							if (riverPos.y > startPos.y && direction * (riverPos.x - startPos.x) / (riverPos.y - startPos.y) < -2)
							{
								S_Help.TLogger("\tRiver=" + idxR + ",Segment=" + idxS);
								S_Help.TLogger("\t\tPoint (" + riverPos.x + "," + riverPos.y + ")");
								angle -= 30f;
							}
							if (borderFlag == 1)
							{
								float preangle = angle;
								angle = random.RandomValue() * 360;
								S_Help.TLogger("\t\tAngle Try, River=" + idxR + ",Segment=" + idxS + " Angle:" + preangle + "->" + angle);
							}
							float step = random.RandomValue() * 3 + 6;
							borderFlag = 0;

							Vector2 deltaPos = step * new Vector2(Mathf.Cos(angle * 3.1415926f / 180) * direction, Mathf.Sin(angle * 3.1415926f / 180));
							Vector2 SegmentPos = riverPos + deltaPos;
							List<Vector2I> line = ProcGen.Util.GetLine(riverPos, SegmentPos);
							for (int idxL = 0; idxL < line.Count; idxL++)
							{
								if (riverPos.x < 5 || riverPos.x > world.size.x - 6 || riverPos.y < 5)
								{
									S_Help.TLogger("\t\tWorld End, Point (" + line[idxL].x + "," + line[idxL].y + ")");
									S_Help.TLogger("\t\tWorld End, River=" + idxR + ",Segment=" + idxS + ",Line=" + idxL);
									borderFlag = 2;
									break;
								}
								if (cells[line[idxL].x + line[idxL].y * world.size.x].elementIdx == elemAbyss)
								{
									S_Help.TLogger("\t\tAbyss End, Point (" + line[idxL].x + "," + line[idxL].y + ") Back to(" + riverPos.x + "," + riverPos.y + ")");
									S_Help.TLogger("\t\tAbyss End, River=" + idxR + ",Segment=" + idxS + ",Line=" + idxL);
									borderFlag = 1;
									break;
								}
								RiverInnerHash.Add(line[idxL]);
							}
							if (borderFlag == 0)
							{
								riverPos = SegmentPos;
							}
						}
					}

					//Fill River and Lake
					List<Vector2I> RiverInner = new List<Vector2I>(RiverInnerHash);
					List<Vector2I> RiverBorder = Find_River_Border(cells, world.size, RiverInner);
					List<Vector2I> LakeBorder = Find_Border(LakeInner, 0x0F);
					Fill_River_Lake(cells, world.size, random, RiverBorder, LakeBorder, RiverInner, LakeInner, (int)lakePos.y);

					//Remove Border
					Remove_Border(cells, ref borderCells, world.size);

					//Finish
					hillPos = default;
				}
			}

			static private Vector2I Draw_Hill(Sim.Cell[] cells, Vector2I worldSize, SeededRandom random)
			{
				ushort elemOxygen	= ElementLoader.FindElementByHash(SimHashes.Oxygen).idx;
				ushort elemAbyss	= ElementLoader.FindElementByHash(SimHashes.Katairite).idx;
				ushort elemSnow		= ElementLoader.FindElementByHash(SimHashes.Snow).idx;
				ushort elemIce		= ElementLoader.FindElementByHash(SimHashes.Ice).idx;
				ushort elemCIce		= ElementLoader.FindElementByHash(SimHashes.CrushedIce).idx;
				ushort elemBIce		= ElementLoader.FindElementByHash(SimHashes.BrineIce).idx;
				ushort elemDIce		= ElementLoader.FindElementByHash(SimHashes.DirtyIce).idx;
				ushort elemGranit	= ElementLoader.FindElementByHash(SimHashes.Granite).idx;

				Vector2I TOP = new Vector2I((int)hillPos.x, Math.Min((int)hillPos.y + random.RandomRange(10, 20), worldSize.y - 10));
				Vector2I LEFT = new Vector2I(Math.Max((int)hillPos.x - random.RandomRange(12, 20), 2), TOP.y);
				Vector2I RIGHT = new Vector2I(Math.Min((int)hillPos.x + random.RandomRange(12, 20), worldSize.x - 2), TOP.y);

				Find_Bottom(cells, LEFT.x, worldSize, ref LEFT.y);
				Find_Bottom(cells, RIGHT.x, worldSize, ref RIGHT.y);
				float slopeA = 0.6f + random.RandomValue() * 0.8f;
				float slopeL = slopeA + random.RandomValue() * 0.4f;
				float slopeR = slopeA + random.RandomValue() * 0.4f;

				List<HillLine> hillLines = new List<HillLine>();
				for (int PosX = LEFT.x; PosX <= RIGHT.x; PosX++)
				{
					int y_Surface = TOP.y + random.RandomRange(0, 6);
					y_Surface -= PosX < TOP.x ? (int)((TOP.y - LEFT.y - 3.0f) / Mathf.Pow((TOP.x - LEFT.x), slopeL) * Mathf.Pow((TOP.x - PosX), slopeL)) :
						(int)((TOP.y - RIGHT.y - 3.0f) / Mathf.Pow((RIGHT.x - TOP.x), slopeR) * Mathf.Pow((PosX - TOP.x), slopeR));
					int y_Bottom = TOP.y;
					bool dirty = Find_Bottom(cells, PosX, worldSize, ref y_Bottom);
					hillLines.Add(new HillLine(PosX, y_Surface, y_Bottom, dirty));
				}

				//Fill Hill
				for (int IdxX = 0; IdxX < hillLines.Count; IdxX++)
				{
					HillLine hillSlice = hillLines[IdxX];
					int height = hillSlice.yU - hillSlice.yD;
					if (height <= 0)
					{
						continue;
					}
					int[] slice = new int[_Slice.GetLength(0)];
					for (int i = 0; i < _Slice.GetLength(0); i++)
					{
						slice[_Slice[i, 2]] = random.RandomRange(_Slice[i, 0], _Slice[i, 1] + 1);
						if (height < slice[_Slice[i, 2]])
						{
							slice[_Slice[i, 2]] = height;
						}
						height -= slice[_Slice[i, 2]];
					}
					int PosY = hillSlice.yU;
					for (int i = 0; i < _Slice.GetLength(0); i++)
					{
						for (int j = 0; j < slice[i]; j++)
						{
							int cell = hillSlice.x + PosY * worldSize.x;
							switch (i)
							{
								case 0:
									cells[cell].SetValues(elemSnow, random.RandomRange(190, 210), random.RandomRange(70f, 100f));
									break;
								case 1:
									cells[cell].SetValues(elemCIce, random.RandomRange(180, 200), random.RandomRange(150f, 200f));
									break;
								case 2:
									cells[cell].SetValues(hillSlice.dirty ? elemDIce : elemIce, random.RandomRange(170, 190), random.RandomRange(180f, 250f));
									break;
								case 3:
									float rand = random.RandomValue();
									if (rand < 0.1) cells[cell].SetValues(elemOxygen, random.RandomRange(160, 190), random.RandomRange(2f, 5f));
									else if (rand < 0.3) cells[cell].SetValues(elemGranit, random.RandomRange(160, 190), random.RandomRange(200f, 300f));
									else if (rand < 0.5) cells[cell].SetValues(elemSnow, random.RandomRange(160, 190), random.RandomRange(100f, 150f));
									else cells[cell].SetValues(elemIce, random.RandomRange(160, 190), random.RandomRange(200f, 500f));
									break;
								case 4:
									int dirtratio = hillSlice.dirty ? 2 : 1;
									ushort element = random.RandomRange(0, 3) < dirtratio ? elemBIce : elemCIce;
									cells[cell].SetValues(element, random.RandomRange(170, 190), random.RandomRange(250, 400));
									break;
								case 5:
								default:
									cells[cell].SetValues(elemAbyss, 200, random.RandomRange(450, 550));
									break;
							}
							PosY--;
						}
					}
				}
				S_Help.Logger("MOD-Horizon_Terr: Snowy HIll Position:" + hillPos.x + "," + hillPos.y);

				//Find Hill Center
				Vector2I heatsinkPos = new Vector2I();
				int CoreX = hillLines.Count / 2 + random.RandomRange(-2, 3);
				CoreX = MathUtil.Clamp(1, hillLines.Count - 2, CoreX);
				heatsinkPos.x = hillLines[CoreX].x;
				heatsinkPos.y = hillLines[CoreX].yD + random.RandomRange(1, 4);

				return heatsinkPos;
			}
			static private void Build_HeatSink(Vector2I heatsinkPos, WorldGen __instance)
			{
				TemplateContainer template = TemplateCache.GetTemplate("Terr/heatsink");
				__instance.data.gameSpawnData.AddTemplate(template, heatsinkPos, ref __instance.claimedPOICells);
				S_Help.Logger("MOD-Horizon_Terr: Heat Sink  Position:" + heatsinkPos.x + "," + heatsinkPos.y);
			}
			static private List<Vector2I> Find_River_Border(Sim.Cell[] cells, Vector2I WorldSize, List<Vector2I> RiverInner)
			{
				HashSet<Vector2I> RiverBorderHash = new HashSet<Vector2I>();
				ushort elemWater = ElementLoader.FindElementByHash(SimHashes.Water).idx;

				//Up, unstable solid
				List<Vector2I> RiverBorder_Up = Find_Border(RiverInner, 0x01);
				for (int idx = 0; idx < RiverBorder_Up.Count; idx++)
				{
					int cell = RiverBorder_Up[idx].x + RiverBorder_Up[idx].y * WorldSize.x;
					if (ElementLoader.elements[cells[cell].elementIdx].IsUnstable)
					{
						RiverBorderHash.Add(new Vector2I(RiverBorder_Up[idx].x, RiverBorder_Up[idx].y));
					}
				}
				//Sides, unstable solid, vacuum, gas
				List<Vector2I> RiverBorder_Sides = Find_Border(RiverInner, 0x02 | 0x04);
				for (int idx = 0; idx < RiverBorder_Sides.Count; idx++)
				{
					int cell = RiverBorder_Sides[idx].x + RiverBorder_Sides[idx].y * WorldSize.x;
					if (ElementLoader.elements[cells[cell].elementIdx].IsUnstable || (((byte)ElementLoader.elements[cells[cell].elementIdx].state & 0x03) <= 0x01))
					{
						RiverBorderHash.Add(new Vector2I(RiverBorder_Sides[idx].x, RiverBorder_Sides[idx].y));
					}
				}
				//Bottom, vacuum, gas, liquid not water
				List<Vector2I> RiverBorder_Bottom = Find_Border(RiverInner, 0x08);
				for (int idx = 0; idx < RiverBorder_Bottom.Count; idx++)
				{
					int cell = RiverBorder_Bottom[idx].x + RiverBorder_Bottom[idx].y * WorldSize.x;
					if (((byte)ElementLoader.elements[cells[cell].elementIdx].state & 0x03) <= 0x02 && cells[cell].elementIdx != elemWater)
					{
						RiverBorderHash.Add(new Vector2I(RiverBorder_Bottom[idx].x, RiverBorder_Bottom[idx].y));
					}
				}

				return new List<Vector2I>(RiverBorderHash);
			}
			static private void Fill_River_Lake(Sim.Cell[] cells, Vector2I WorldSize, SeededRandom random,
				List<Vector2I> RiverBorder, List<Vector2I> LakeBorder, List<Vector2I> RiverInner, List<Vector2I> LakeInner, int LakeHighLimit)
			{
				ushort elemAbyss = ElementLoader.FindElementByHash(SimHashes.Katairite).idx;
				ushort elemIce = ElementLoader.FindElementByHash(SimHashes.Ice).idx;
				ushort elemWater = ElementLoader.FindElementByHash(SimHashes.Water).idx;
				ushort[] lakeShell = {
									ElementLoader.FindElementByHash(SimHashes.Algae).idx,
									ElementLoader.FindElementByHash(SimHashes.SedimentaryRock).idx,
									ElementLoader.FindElementByHash(SimHashes.SandStone).idx };
				for (int idx = 0; idx < RiverBorder.Count; idx++)
				{
					int cell = RiverBorder[idx].x + RiverBorder[idx].y * WorldSize.x;
					if (cells[cell].elementIdx == elemAbyss) continue;

					ushort ele = lakeShell[random.RandomRange(0, 3)];
					float mass = random.RandomRange(800, 1200);
					cells[cell].SetValues(ele, 295, mass);
				}

				for (int idx = 0; idx < LakeBorder.Count; idx++)
				{
					int cell = LakeBorder[idx].x + LakeBorder[idx].y * WorldSize.x;
					if (((byte)ElementLoader.elements[cells[cell].elementIdx].state & 0x02) == 0x02 || LakeBorder[idx].y <= LakeHighLimit)
					{
						if (cells[cell].temperature < 279f)
						{
							cells[cell].SetValues(elemAbyss, 295, random.RandomRange(100, 200));
						}
						else
						{
							ushort ele = lakeShell[random.RandomRange(0, 3)];
							float mass = random.RandomRange(500, 800);
							cells[cell].SetValues(ele, 295, mass);
						}
					}
				}

				for (int idx = 0; idx < RiverInner.Count; idx++)
				{
					int cell = RiverInner[idx].x + RiverInner[idx].y * WorldSize.x;
					if (cells[cell].elementIdx == elemAbyss || cells[cell].mass == 0) continue;

					if (cells[cell].temperature < 274f)
						cells[cell].SetValues(elemIce, cells[cell].temperature, 750);
					else
						cells[cell].SetValues(elemWater, 300, 1050);
				}

				for (int idx = 0; idx < LakeInner.Count; idx++)
				{
					int cell = LakeInner[idx].x + LakeInner[idx].y * WorldSize.x;
					if (cells[cell].elementIdx == elemAbyss) continue;

					if (((byte)ElementLoader.elements[cells[cell].elementIdx].state & 0x02) == 0x02)
					{
						cells[cell].SetValues(elemWater, 295, 1000);
					}
				}

				S_Help.Logger("MOD-Horizon_Terr: Create River and Lake");
			}

			static private void Remove_Border(Sim.Cell[] cells, ref HashSet<int> borderCells, Vector2I WorldSize)
			{
				ushort elemOxygen = ElementLoader.FindElementByHash(SimHashes.Oxygen).idx;
				for (int x = 1; x < WorldSize.x - 1; x++)
				{
					int cell = x + (WorldSize.y - 1) * WorldSize.x;
					borderCells.Remove(cell);
					cells[cell].SetValues(elemOxygen, 300.0f, 1.0f);
				}
				S_Help.Logger("MOD-Horizon_Terr: World Dome has been removed");
			}
			static private bool Find_Bottom(Sim.Cell[] cells, int x0, Vector2I WorldSize, ref int y)
			{
				for (; y > 0; y--)
				{
					int cell = x0 + y * WorldSize.x;
					if (((byte)ElementLoader.elements[cells[cell].elementIdx].state & 0x02) == 0x02)
					{
						return ((byte)ElementLoader.elements[cells[cell].elementIdx].state & 0x03) == 0x02;
					}
				}
				return false;
			}
			static private List<Vector2I> Find_Border(List<Vector2I> Inner, int direction)
			{
				HashSet<Vector2I> BorderHash = new HashSet<Vector2I>();
				if ((direction & 0x01) == 0x01)//up
					for (int idx = 0; idx < Inner.Count; idx++)
						BorderHash.Add(new Vector2I(Inner[idx].x, Inner[idx].y + 1));
				if ((direction & 0x02) == 0x02)//right
					for (int idx = 0; idx < Inner.Count; idx++)
						BorderHash.Add(new Vector2I(Inner[idx].x + 1, Inner[idx].y));
				if ((direction & 0x04) == 0x04)//left
					for (int idx = 0; idx < Inner.Count; idx++)
						BorderHash.Add(new Vector2I(Inner[idx].x - 1, Inner[idx].y));
				if ((direction & 0x08) == 0x08)//down
					for (int idx = 0; idx < Inner.Count; idx++)
						BorderHash.Add(new Vector2I(Inner[idx].x, Inner[idx].y - 1));
				if ((direction & 0x10) == 0x10)//down 2 tile
					for (int idx = 0; idx < Inner.Count; idx++)
						BorderHash.Add(new Vector2I(Inner[idx].x, Inner[idx].y - 2));

				List<Vector2I> Border = new List<Vector2I>(BorderHash);
				for (int idx = 0; idx < Inner.Count; idx++)
					Border.Remove(Inner[idx]);

				return Border;
			}

			static int[,] _Slice = new int[6, 3] //min,max,seq from high to low
			{
				{ 1, 2, 5 },//bottom
				{ 4, 6, 0 },//surface
				{ 3, 5, 1 },//shallow
				{ 1, 3, 4 },//basement
				{ 4, 8, 2 },//middle
				{ 99, 99, 3 }//core
			};
			struct HillLine
			{
				public int x, yU, yD;
				public bool dirty;
				public HillLine(int x, int yU, int yD, bool dirty)
				{
					this.x = x;
					this.yU = yU;
					this.yD = yD;
					this.dirty = dirty;
				}
			}
		}

		public class WorldGen_Mob
		{
			[HarmonyPatch(typeof(MobSpawning), "PlaceBiomeAmbientMobs")]
			public class MobSpawning_Accumulator_Patch
			{
				public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
				{
					MethodInfo methodInfo = AccessTools.Method(typeof(WorldGen_Mob), nameof(RoundAccumulator), new System.Type[] { typeof(float) });
					List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
					for (int i = 0; i < codes.Count; i++)
					{
						if (codes[i].opcode == OpCodes.Call && codes[i].operand.ToString().Contains("RoundToInt"))
						{
							codes[i].operand = methodInfo;
							break;
						}
					}
					return codes.AsEnumerable();
				}
			}

			static float roundAccumulator = 0;
			static int RoundAccumulator(float value)
			{
				roundAccumulator += value;
				int round = (int)(roundAccumulator + 0.5);
				roundAccumulator -= round;
				return round;
			}
		}

		public class Patch
		{
			static public void Add_Invisual_Border()
			{
				WorldContainer startWorld = null;

				for (int idx = 0; idx < ClusterManager.Instance.WorldContainers.Count; idx++)
				{
					if (ClusterManager.Instance.WorldContainers[idx].IsStartWorld)
					{
						startWorld = ClusterManager.Instance.WorldContainers[idx];
						break;
					}
				}
				if (startWorld == null)
				{
					S_Help.Logger("MOD-Horizon_Terr: Add Invisual Border Fail!!! Can't find start world.");
					return;
				}

				for (int x = 2; x < startWorld.WorldSize.x - 2; x++)
				{
					// Sim.Cell.Properties.GasImpermeable || Sim.Cell.Properties.LiquidImpermeable || Sim.Cell.Properties.SolidImpermeable
					int cell;
					cell = x + (startWorld.WorldSize.y - 1) * Grid.WidthInCells;
					if (!Grid.IsSolidCell(cell))
						SimMessages.SetCellProperties(cell, 7);

					cell = x + (startWorld.WorldSize.y - 2) * Grid.WidthInCells;
					if (!Grid.IsSolidCell(cell))
						SimMessages.SetCellProperties(cell, 7);
				}
				S_Help.Logger("MOD-Horizon_Terr: Add Invisual Border");
			}

			static public void Add_Spirte()
			{
				string spirteName = "biomeIconTerr";

				if (Assets.GetSprite(spirteName) == null)
				{
					KAnimFile kanimFile;
					Assets.TryGetAnim(spirteName + "_kanim", out kanimFile);
					if (kanimFile != null)
					{
						Sprite sprite = Def.GetUISpriteFromMultiObjectAnim(kanimFile, "ui", false, "");
						Assets.Sprites.Add(spirteName, sprite);
						S_Help.Logger("MOD-Horizon_Terr: Add sprite");
						return;
					}
				}
				S_Help.Logger("MOD-Horizon_Terr: !!! Miss Sprite - biomeIconTerr !!!");
			}
		}
	}
}
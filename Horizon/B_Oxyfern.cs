using System.Collections.Generic;
using UnityEngine;
using HarmonyLib;
using TUNING;
using System;
using System.Linq;
using System.Reflection.Emit;

namespace Horizon
{
	public class B_Oxyfern
	{
		static float consumerCO2 = 0.001f;
		static float converterCO2 = consumerCO2 * 2f;
		static float converterRatio = 25f;
		static float plantDirty = 0.01f;
		static float plantWater = 0.05f;
		public class OxyfernPlus : StateMachineComponent<OxyfernPlus.StatesInstance>
		{
			protected void DestroySelf(object callbackParam)
			{
				CreatureHelpers.DeselectCreature(gameObject);
				Util.KDestroyGameObject(gameObject);
			}

			protected override void OnSpawn()
			{
				base.OnSpawn();
				smi.StartSM();
			}

			protected override void OnCleanUp()
			{
				base.OnCleanUp();
				if (Tutorial.Instance.oxygenGenerators.Contains(gameObject))
					Tutorial.Instance.oxygenGenerators.Remove(gameObject);
			}

			protected override void OnPrefabInit()
			{
				Subscribe<OxyfernPlus>((int)GameHashes.PlanterStorage, OxyfernPlus.OnReplantedDelegate);
				if (OxyfernPlus.ElementSynthesis == null)
				{
					OxyfernPlus.ElementSynthesis = new StatusItem("elementSynthesis", S_Text.SYNTHESIS.NAME, S_Text.SYNTHESIS.TOOLTIP, "", StatusItem.IconType.Info, NotificationType.Neutral, false, OverlayModes.None.ID, 129022, true, null)
						.SetResolveStringCallback(delegate (string str, object data)
						{
							KeyValuePair<SimHashes, float> elementInfo = (KeyValuePair<SimHashes, float>)data;
							Element element = ElementLoader.FindElementByHash(elementInfo.Key);
							str = str.Replace("{ElementTypes}", element.name);
							str = str.Replace("{FlowRate}", GameUtil.GetFormattedMass(elementInfo.Value, GameUtil.TimeSlice.PerSecond, GameUtil.MetricMassFormat.UseThreshold, true, "{0:0.#}"));
							return str;
						});
				}
				base.OnPrefabInit();
			}

			private void OnReplanted(object data = null)
			{
				this.SetConsumptionRate();
				if (this.receptacleMonitor.Replanted)
				{
					Tutorial.Instance.oxygenGenerators.Add(gameObject);
				}
			}

			public void SetConsumptionRate()
			{
				// Never assign any value to elementConverter.OutputMultiplier!!
				// The actuall output will be massGenerationRate * OutputMultiplier
				this.elementConverter.outputElements[0].massGenerationRate = converterCO2 * converterRatio;
				if (this.receptacleMonitor.Replanted) this.elementConverter.outputElements[0].massGenerationRate *= 2f;
			}

			private static StatusItem ElementSynthesis;
			Guid StatusHandles = Guid.Empty;
			[MyCmpReq]
			private WiltCondition wiltCondition;
			[MyCmpReq]
			private ElementConsumer elementConsumer;
			[MyCmpReq]
			private ElementConverter elementConverter;
			[MyCmpReq]
			private ReceptacleMonitor receptacleMonitor;

			private static readonly EventSystem.IntraObjectHandler<OxyfernPlus> OnReplantedDelegate =
				new EventSystem.IntraObjectHandler<OxyfernPlus>(
					delegate (OxyfernPlus component, object data) { component.OnReplanted(data); });

			public class StatesInstance : GameStateMachine<States, StatesInstance, OxyfernPlus, object>.GameInstance
			{
				public StatesInstance(OxyfernPlus master) : base(master)
				{
					this.selectable = base.GetComponent<KSelectable>();
				}

				private KSelectable selectable;

				public void UpdateStatusItem(SimHashes simhashes, float value)
				{
					if (base.master.StatusHandles != null)
						this.selectable.RemoveStatusItem(base.master.StatusHandles, true);

					if (value > 0)
						base.master.StatusHandles = this.selectable.AddStatusItem(OxyfernPlus.ElementSynthesis, new KeyValuePair<SimHashes, float>(simhashes, value));
					else
						base.master.StatusHandles = Guid.Empty;
				}
			}

			public class States : GameStateMachine<OxyfernPlus.States, OxyfernPlus.StatesInstance, OxyfernPlus>
			{
				public override void InitializeStates(out StateMachine.BaseState default_state)
				{
					serializable = StateMachine.SerializeType.Both_DEPRECATED;
					default_state = this.grow;

					this.dead
						.ToggleStatusItem(STRINGS.CREATURES.STATUSITEMS.DEAD.NAME, STRINGS.CREATURES.STATUSITEMS.DEAD.TOOLTIP, "", StatusItem.IconType.Info, NotificationType.Neutral, false, default, StatusItem.ALL_OVERLAYS, null, null, Db.Get().StatusItemCategories.Main)
						.Enter(delegate (OxyfernPlus.StatesInstance smi)
						{
							GameUtil.KInstantiate(Assets.GetPrefab(EffectConfigs.PlantDeathId), smi.master.transform.GetPosition(), Grid.SceneLayer.FXFront, null, 0).SetActive(true);
							smi.master.Trigger((int)GameHashes.Died, null);
							smi.master.GetComponent<KBatchedAnimController>().StopAndClear();
							Destroy(smi.master.GetComponent<KBatchedAnimController>());
							smi.Schedule(0.5f, new Action<object>(smi.master.DestroySelf), null);
						});
					this.blocked_from_growing
						.ToggleStatusItem(Db.Get().MiscStatusItems.RegionIsBlocked)
						.EventTransition(GameHashes.EntombedChanged, alive, (StatesInstance smi) => alive.ForceUpdateStatus(smi.master.gameObject))
						.EventTransition(GameHashes.TooColdWarning, alive, (StatesInstance smi) => alive.ForceUpdateStatus(smi.master.gameObject))
						.EventTransition(GameHashes.TooHotWarning, alive, (StatesInstance smi) => alive.ForceUpdateStatus(smi.master.gameObject))
						.TagTransition(GameTags.Uprooted, dead);

					this.grow
						.Enter(delegate (OxyfernPlus.StatesInstance smi)
						{
							if (smi.master.receptacleMonitor.HasReceptacle() && !this.alive.ForceUpdateStatus(smi.master.gameObject))
							{
								smi.GoTo(this.blocked_from_growing);
							}
						})
						.PlayAnim("grow_pst", KAnim.PlayMode.Once)
						.EventTransition(GameHashes.AnimQueueComplete, this.alive, null);
					this.alive
						.InitializeStates(this.masterTarget, this.dead).DefaultState(this.alive.mature);
					this.alive.mature
						.EventTransition(GameHashes.Wilt, this.alive.wilting, (OxyfernPlus.StatesInstance smi) => smi.master.wiltCondition.IsWilting()).PlayAnim("idle_full", KAnim.PlayMode.Loop)
						.Exit(delegate (OxyfernPlus.StatesInstance smi) { smi.master.elementConsumer.EnableConsumption(false); })
						.Update(new Action<OxyfernPlus.StatesInstance, float>(States.Update_Conventer), UpdateRate.SIM_1000ms, false);
					this.alive.wilting
						.PlayAnim("wilt3")
						.EventTransition(GameHashes.WiltRecover, this.alive.mature, (OxyfernPlus.StatesInstance smi) => !smi.master.wiltCondition.IsWilting());
				}

				private static void Update_Conventer(OxyfernPlus.StatesInstance smi, float dt)
				{
					int num = Grid.PosToCell(smi.master.gameObject);
					Storage storage = smi.master.GetComponent<Storage>();
					ReceptacleMonitor receptacleMonitor = smi.master.GetComponent<ReceptacleMonitor>();

					if (Grid.IsValidCell(num) && storage != null)
					{
						smi.master.elementConsumer.EnableConsumption(Grid.LightIntensity[num] <= 0);

						//40000Lux = Max (double default value)
						float massIcr = Mathf.Min(Mathf.Sqrt(Grid.LightIntensity[num]) / 200000f, consumerCO2);
						if (receptacleMonitor != null && receptacleMonitor.Replanted) massIcr *= 2;
						massIcr = Mathf.Min(massIcr, storage.capacityKg - storage.ExactMassStored());

						storage.AddGasChunk(SimHashes.CarbonDioxide, massIcr, 295, byte.MaxValue, 0, false, true);
						smi.UpdateStatusItem(SimHashes.CarbonDioxide, massIcr);
					}
				}

				public States() { }
				public GameStateMachine<OxyfernPlus.States, OxyfernPlus.StatesInstance, OxyfernPlus, object>.State grow;
				public GameStateMachine<OxyfernPlus.States, OxyfernPlus.StatesInstance, OxyfernPlus, object>.State blocked_from_growing;
				public GameStateMachine<OxyfernPlus.States, OxyfernPlus.StatesInstance, OxyfernPlus, object>.State dead;
				public OxyfernPlus.States.AliveStates alive;

				public class AliveStates : GameStateMachine<OxyfernPlus.States, OxyfernPlus.StatesInstance, OxyfernPlus, object>.PlantAliveSubState
				{
					public GameStateMachine<OxyfernPlus.States, OxyfernPlus.StatesInstance, OxyfernPlus, object>.State mature;
					public GameStateMachine<OxyfernPlus.States, OxyfernPlus.StatesInstance, OxyfernPlus, object>.State wilting;
				}
			}
		}

		public class Patch
		{
			[HarmonyPatch(typeof(OxyfernConfig), "CreatePrefab")]
			public class OxyfernConfig_CreatePrefab_Patch
			{
				private static bool Prefix(ref GameObject __result)
				{
					string id = "Oxyfern";
					string name = STRINGS.CREATURES.SPECIES.OXYFERN.NAME;
					string desc = S_Text.OXYFERN.DESC;
					float mass = 1f;
					EffectorValues tier = DECOR.PENALTY.TIER1;

					SimHashes[] atmosphere = new SimHashes[] { SimHashes.CarbonDioxide, SimHashes.Oxygen };
					GameObject gameObject = EntityTemplates.CreatePlacedEntity(
						id, name, desc, mass, Assets.GetAnim("oxy_fern_kanim"), "idle_full", Grid.SceneLayer.BuildingBack, 1, 2, tier, default, SimHashes.Creature, null, 293f);
					gameObject = EntityTemplates.ExtendEntityToBasicPlant(
						gameObject, 253.15f, 273.15f, 313.15f, 373.15f, atmosphere, true, 0f, 0.025f, null, true, false, true, true, 2400f, 0f, 2200f, "OxyfernOriginal", name);

					EntityTemplates.ExtendPlantToIrrigated(gameObject,
						new PlantElementAbsorber.ConsumeInfo[] {
							new PlantElementAbsorber.ConsumeInfo {
								tag = GameTags.Water, massConsumptionRate = plantWater } });
					EntityTemplates.ExtendPlantToFertilizable(gameObject,
						new PlantElementAbsorber.ConsumeInfo[] {
							new PlantElementAbsorber.ConsumeInfo {
								tag = GameTags.Dirt, massConsumptionRate = plantDirty } });

					gameObject.AddOrGet<OxyfernPlus>();
					gameObject.AddOrGet<LoopingSounds>();

					Storage storage = gameObject.AddOrGet<Storage>();
					storage.showInUI = true;
					storage.capacityKg = 1f;

					ElementConsumer elementConsumer = gameObject.AddOrGet<ElementConsumer>();
					//elementConsumer.showInStatusPanel = false;
					elementConsumer.storeOnConsume = true;
					elementConsumer.storage = storage;
					elementConsumer.elementToConsume = SimHashes.CarbonDioxide;
					elementConsumer.configuration = ElementConsumer.Configuration.Element;
					elementConsumer.consumptionRadius = 2;
					elementConsumer.EnableConsumption(true);
					elementConsumer.consumptionRate = consumerCO2;

					ElementConverter elementConverter = gameObject.AddOrGet<ElementConverter>();
					elementConverter.consumedElements =
						new ElementConverter.ConsumedElement[] {
							new ElementConverter.ConsumedElement(
								SimHashes.CarbonDioxide.ToString().ToTag(), converterCO2, true) };
					elementConverter.outputElements =
						new ElementConverter.OutputElement[] {
							new ElementConverter.OutputElement(
								converterCO2 * converterRatio, SimHashes.Oxygen, 0f, true, false, 0f, 1f, 0.75f, byte.MaxValue, 0, true) };

					EntityTemplates.CreateAndRegisterPreviewForPlant(
						EntityTemplates.CreateAndRegisterSeedForPlant(
							gameObject, SeedProducer.ProductionType.Hidden, "OxyfernSeed",
							STRINGS.CREATURES.SPECIES.SEEDS.OXYFERN.NAME, STRINGS.CREATURES.SPECIES.SEEDS.OXYFERN.DESC,
							Assets.GetAnim("seed_oxyfern_kanim"), "object", 1, new List<Tag> { GameTags.CropSeed },
							SingleEntityReceptacle.ReceptacleDirection.Top, default, 20,
							S_Text.OXYFERN.DOMESTICATEDDESC, EntityTemplates.CollisionShape.CIRCLE, 0.3f, 0.3f, null, "", false
						),
						"Oxyfern_preview", Assets.GetAnim("oxy_fern_kanim"), "place", 1, 2);

					SoundEventVolumeCache.instance.AddVolume("oxy_fern_kanim", "MealLice_harvest", NOISE_POLLUTION.CREATURES.TIER3);
					SoundEventVolumeCache.instance.AddVolume("oxy_fern_kanim", "MealLice_LP", NOISE_POLLUTION.CREATURES.TIER4);
					__result = gameObject;

					S_Help.Logger("MOD-Horizon_Oxyfern: Create OxyfernPlus.");
					return false;
				}
			}

			[HarmonyPatch(typeof(OxyfernConfig), "OnSpawn")]
			public class OxyfernConfig_OnSpawn_Patch
			{
				private static bool Prefix(ref GameObject inst)
				{
					if (inst.GetComponent<Oxyfern>() != null) return true;
					inst.AddOrGet<OxyfernPlus>().SetConsumptionRate();
					return false;
				}
			}
		}
	}
}
//public class Patch
//{
//	[HarmonyPatch(typeof(OxyfernConfig), "CreatePrefab")]
//	public class OxyfernConfig_CreatePrefab_Patch
//	{
//		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
//		{
//			List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
//			int safeele_idx = -1;
//			for (int i = 0; i < codes.Count; i++)
//			{
//				if (codes[i].opcode == OpCodes.Ldc_I4 && (int)codes[i].operand == (int)SimHashes.CarbonDioxide)
//				{
//					safeele_idx = i;
//					break;
//				}
//			}
//			if (safeele_idx >= 0)
//			{
//				List<CodeInstruction> codeinsert = new List<CodeInstruction>
//					{
//						new CodeInstruction(OpCodes.Dup, null),
//						new CodeInstruction(OpCodes.Ldc_I4_1, null),
//						new CodeInstruction(OpCodes.Ldc_I4, (int)SimHashes.Oxygen),
//						new CodeInstruction(OpCodes.Stelem_I4, null)
//					};
//				codes.InsertRange(safeele_idx + 2, codeinsert);
//				codes[safeele_idx - 4].opcode = OpCodes.Ldc_I4_2;
//				S_Help.Logger("MOD-Horizon_Oxyfern: Insert alive atmosphere: Oxygen");
//			}
//			else
//			{
//				S_Help.Logger("MOD-Horizon_Oxyfern: !!! insert alive atmosphere fail !!!");
//			}
//			return codes.AsEnumerable();
//		}

//		private static void Postfix(ref GameObject __result)
//		{
//			//PressureVulnerable: Only update template data
//			PressureVulnerable pressureVulnerable = __result.GetComponent<PressureVulnerable>();
//			pressureVulnerable.safe_atmospheres.Add(ElementLoader.FindElementByHash(SimHashes.Oxygen));

//			ElementConsumer elementConsumer = __result.GetComponent<ElementConsumer>();
//			elementConsumer.consumptionRate = consumerCO2;

//			ElementConverter elementConverter = __result.GetComponent<ElementConverter>();
//			elementConverter.OutputMultiplier = 5f;
//			for (int idx = 0; idx < elementConverter.consumedElements.Length; idx++)
//			{
//				elementConverter.consumedElements[idx].MassConsumptionRate = converterCO2;
//			}
//			for (int idx = 0; idx < elementConverter.outputElements.Length; idx++)
//			{
//				elementConverter.outputElements[idx].massGenerationRate = converterO2;
//			}

//			S_Help.Logger("MOD-Horizon_Oxyfern: Adjust OxyfernConfig");
//		}
//	}

//	[HarmonyPatch(typeof(Oxyfern), "SetConsumptionRate")]
//	public class Oxyfern_SetConsumptionRate
//	{
//		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
//		{
//			int injectFlag = 0;
//			List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
//			for (int i = 0; i < codes.Count; i++)
//			{
//				if (codes[i].opcode == OpCodes.Ldc_R4)
//				{
//					float value = (float)codes[i].operand;
//					S_Help.Logger("Horizon_Oxyfern" + value);
//					if (value.ToString() == "0.000625")
//					{
//						injectFlag += 1;
//						codes[i].operand = converterCO2;
//					}
//					else if (value.ToString() == "0.00015625")
//					{
//						injectFlag += 4; //Not use 2 to capture flag_bit0 execute twice.
//						codes[i].operand = consumerCO2;
//					}
//				}
//			}
//			if (injectFlag == 5)
//				S_Help.Logger("MOD-Horizon_Oxyfern: Adjust SetConsumptionRate success");
//			else
//				S_Help.Logger("MOD-Horizon_Oxyfern: !!! Adjust SetConsumptionRate FAIL !!! injectFlag = " + injectFlag);
//			return codes.AsEnumerable();
//		}
//	}
//}

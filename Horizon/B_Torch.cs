using System;
using System.Collections.Generic;
using TUNING;
using UnityEngine;
using HarmonyLib;
using KSerialization;
using System.Runtime.Serialization;
using STRINGS;
using Database;
using System.Reflection;

namespace Horizon
{
	public class B_Torch
	{
		private const int luxDefault = 600;
		private const float radDefault = 4f;
		private static Vector2 Torch_Offset = new Vector2(0f, 0.5f);
		private static Vector2 TorchOxy_Offset = new Vector2(0f, 1.5f);

		public class Torch : StateMachineComponent<Torch.StatesInstance>, IGameObjectEffectDescriptor, IToggleHandler
		{
			protected override void OnPrefabInit()
			{
				base.OnPrefabInit();
				this.ToggleIdx = this.toggleable.SetTarget(this);
				base.Subscribe<Torch>((int)GameHashes.RefreshUserMenu, Torch.OnRefreshUserMenuDelegate);
			}
			protected override void OnSpawn()
			{
				base.OnSpawn();
				base.smi.StartSM();

				if (!this.operational.Flags.ContainsKey(BurnFalg))
					this.IsEnabled = true;

				if (this.queuedToggle)
					this.OnMenuToggle();
				// Add init 50g oxygen
				// Building Deserialize after OnPrefabInit and before OnSpawn
				//if (storage.items.Count == 0) storage.AddGasChunk(SimHashes.Oxygen, 0.1f, 300f, byte.MaxValue, 0, true, false);
			}
			[OnDeserialized]
			public void OnDeserialized()
			{
				if (this.lux == int.MinValue) return;

				if (this.lux <= 400)
				{
					this.operational.SetFlag(BurnFalg, false);
					this.IsEnabled = false;
				}
				else
				{
					light2D.Lux = this.lux;
					light2D.Range = this.rad;
					this.operational.SetFlag(BurnFalg, true);
					//this.IsEnabled = true;
				}

				S_Help.Logger("MOD-Horizon_Torch: Loading" + lux + " " + rad + " " + buildingHP.IsBroken);
			}
			public virtual List<Descriptor> GetDescriptors(GameObject go)
			{
				if (buildingHP.IsBroken)
					return new List<Descriptor>
					{
						new Descriptor(S_Text.TORCH.BURNOUT_DESC, S_Text.TORCH.BURNOUT_TOOLTIP, Descriptor.DescriptorType.Effect, false)
					};
				else if (isNeedOxygen)
					return new List<Descriptor>
					{
						new Descriptor(string.Format(UI.GAMEOBJECTEFFECTS.EMITS_LIGHT, "3-5"), UI.GAMEOBJECTEFFECTS.TOOLTIPS.EMITS_LIGHT, Descriptor.DescriptorType.Effect, false),
						new Descriptor(string.Format(UI.GAMEOBJECTEFFECTS.EMITS_LIGHT_LUX, "400-1000"), UI.GAMEOBJECTEFFECTS.TOOLTIPS.EMITS_LIGHT_LUX, Descriptor.DescriptorType.Effect, false)
					};
				else
					return new List<Descriptor>
					{
						new Descriptor(string.Format(UI.GAMEOBJECTEFFECTS.EMITS_LIGHT, light2D.Lux), UI.GAMEOBJECTEFFECTS.TOOLTIPS.EMITS_LIGHT, Descriptor.DescriptorType.Effect, false),
						new Descriptor(string.Format(UI.GAMEOBJECTEFFECTS.EMITS_LIGHT_LUX, light2D.Range), UI.GAMEOBJECTEFFECTS.TOOLTIPS.EMITS_LIGHT_LUX, Descriptor.DescriptorType.Effect, false)
					};
			}

			[Serialize]
			private float rad = float.NaN;
			[Serialize]
			private int lux = int.MinValue;
			[Serialize]
			private bool queuedToggle = false;
			[Serialize]
			public bool isNeedOxygen = true;
			[MyCmpReq]
			private BuildingHP buildingHP;
			[MyCmpReq]
			private Deconstructable deconstructable;
			[MyCmpReq]
			private Light2D light2D;
			[MyCmpReq]
			private Operational operational;
			[MyCmpReq]
			protected Storage storage;
			[MyCmpReq]
			private PrimaryElement primaryElement;
			[MyCmpAdd]
			private Toggleable toggleable;

			private int ToggleIdx;
			private static readonly Operational.Flag BurnFalg = new Operational.Flag("Torch_Burn", Operational.Flag.Type.Requirement);
			private static readonly EventSystem.IntraObjectHandler<Torch> OnRefreshUserMenuDelegate =
				new EventSystem.IntraObjectHandler<Torch>(delegate (Torch component, object data) { component.OnRefreshUserMenu(data); });

			public bool IsEnabled
			{
				get { return this.operational != null && this.operational.GetFlag(BurnFalg); }

				set
				{
					this.operational.SetFlag(BurnFalg, value);
					if (value)
					{
						light2D.Range = radDefault;
						light2D.Lux = luxDefault;
					}
					else
					{
						light2D.Range = 0;
						light2D.Lux = 0;
					}
					light2D.FullRefresh();
					Game.Instance.userMenu.Refresh(base.gameObject);
					base.GetComponent<KSelectable>().ToggleStatusItem(Db.Get().BuildingStatusItems.BuildingDisabled, !value, null);
				}
			}

			public bool WaitingForDisable { get { return this.IsEnabled && this.toggleable.IsToggleQueued(this.ToggleIdx); } }

			public void HandleToggle()
			{
				this.queuedToggle = false;
				Prioritizable.RemoveRef(base.gameObject);
				this.OnToggle();
			}
			public bool IsHandlerOn()
			{
				return this.IsEnabled;
			}
			private void OnToggle()
			{
				this.IsEnabled = !this.IsEnabled;
				Game.Instance.userMenu.Refresh(base.gameObject);
			}
			private void OnMenuToggle()
			{
				if (!this.toggleable.IsToggleQueued(this.ToggleIdx))
				{
					if (this.IsEnabled)
						base.Trigger((int)GameHashes.WorkChoreDisabled, "BuildingDisabled");

					this.queuedToggle = true;
					Prioritizable.AddRef(base.gameObject);
				}
				else
				{
					this.queuedToggle = false;
					Prioritizable.RemoveRef(base.gameObject);
				}
				this.toggleable.Toggle(this.ToggleIdx);
				Game.Instance.userMenu.Refresh(base.gameObject);
			}
			private void OnRefreshUserMenu(object data)
			{
				if (buildingHP.IsBroken) return;

				bool isEnabled = this.IsEnabled;
				bool flag = this.toggleable.IsToggleQueued(this.ToggleIdx);
				KIconButtonMenu.ButtonInfo button;
				if ((isEnabled && !flag) || (!isEnabled && flag))
					button = new KIconButtonMenu.ButtonInfo("action_building_disabled", S_Text.TORCH.EXTIN_DESC, new System.Action(this.OnMenuToggle), global::Action.ToggleEnabled, null, null, null, S_Text.TORCH.EXTIN_TOOLTIP, true);
				else
					button = new KIconButtonMenu.ButtonInfo("action_building_disabled", S_Text.TORCH.LIGHT_DESC, new System.Action(this.OnMenuToggle), global::Action.ToggleEnabled, null, null, null, S_Text.TORCH.LIGHT_TOOLTIP, true);

				Game.Instance.userMenu.AddButton(base.gameObject, button, 1f);
			}
			public class States : GameStateMachine<Torch.States, Torch.StatesInstance, Torch>
			{
				public override void InitializeStates(out StateMachine.BaseState default_state)
				{
					serializable = StateMachine.SerializeType.Both_DEPRECATED;
					default_state = this.online;
					this.burnout
						.PlayAnim("out")
						.Enter(delegate (Torch.StatesInstance smi)
						{
							smi.master.deconstructable.allowDeconstruction = true;
							smi.master.operational.SetActive(false, false);
						});

					this.offline
						.PlayAnim("off")
						.EventTransition(GameHashes.OperationalChanged, this.online, (Torch.StatesInstance smi) => smi.master.operational.IsOperational)
						.Enter(delegate (Torch.StatesInstance smi)
						 {
							 smi.master.operational.SetActive(false, false);
						 });

					this.online
						.PlayAnim("on", KAnim.PlayMode.Loop)
						.EventTransition(GameHashes.OperationalChanged, this.burnout, (Torch.StatesInstance smi) => smi.master.buildingHP.IsBroken)
						.EventTransition(GameHashes.OperationalChanged, this.offline, (Torch.StatesInstance smi) => !smi.master.operational.IsOperational && !smi.master.buildingHP.IsBroken)
						.Enter(delegate (Torch.StatesInstance smi)
						{
							//smi.master.light2D.Range = radDefault;
							//smi.master.light2D.Lux = luxDefault;
							smi.master.operational.SetActive(true, false);
						})
						.Update(new Action<Torch.StatesInstance, float>(States.Update), UpdateRate.SIM_1000ms, false);
				}
				private static void Update(Torch.StatesInstance smi, float dt)
				{
					if (smi.master.isNeedOxygen)
					{
						Storage storage = smi?.master.storage;
						Light2D light2D = smi?.master.light2D;
						if (storage == null | light2D == null) return;

						float massUse = 0;
						PrimaryElement component = null;
						for (int idx = 0; idx < storage.items.Count; idx++)
						{
							if (storage.items[idx] != null)
							{
								component = storage.items[idx].GetComponent<PrimaryElement>();
								if (component.ElementID == SimHashes.Oxygen)
								{
									break;
								}
							}
						}
						if (component != null)
						{
							float massTotal = component.Mass;
							massUse = massTotal > 0.005f ? Mathf.Clamp(massTotal / 10f, 0.005f, 0.01f) : massTotal;
							component.Mass -= massUse;
						}

						// Update Light
						// light2D.Lux = (int)(light2D.Lux * 0.8f + massUse * 20000);
						light2D.Lux = (int)(light2D.Lux * 0.95f + massUse * 5000);

						if (light2D.Lux > 400)
						{
							if (dt >= 1) smi.master.buildingHP.DoDamage(1);
							if (smi.master.buildingHP.IsBroken)
							{
								light2D.Range = 0;
								light2D.Lux = 0;
								smi.master.operational.SetFlag(BurnFalg, false);

								smi.master.primaryElement.Mass /= 2;
								smi.master.primaryElement.GetType().GetField("_Element", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(smi.master.primaryElement, null);
								smi.master.primaryElement.SetElement(SimHashes.Carbon, true);
							}
							else if (light2D.Lux >= 900) light2D.Range = 5;
							else if (light2D.Lux >= 600) light2D.Range = 4;
							else light2D.Range = 3;
						}
						else
						{
							light2D.Range = 0;
							light2D.Lux = 0;
							smi.master.operational.SetFlag(BurnFalg, false);
						}
						light2D.FullRefresh();
						smi.master.lux = light2D.Lux;
						smi.master.rad = light2D.Range;
					}
					else
					{
						if (dt >= 1) smi.master.buildingHP.DoDamage(1);
						if (smi.master.buildingHP.IsBroken)
						{
							Light2D light2D = smi?.master.light2D;
							light2D.Range = 0;
							light2D.Lux = 0;
							smi.master.operational.SetFlag(BurnFalg, false);
							light2D.FullRefresh();
							smi.master.lux = light2D.Lux;
							smi.master.rad = light2D.Range;

							smi.master.primaryElement.GetType().GetField("_Element", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(smi.master.primaryElement, null);
							smi.master.primaryElement.SetElement(SimHashes.RefinedCarbon, true);
						}
					}
				}
				public GameStateMachine<Torch.States, Torch.StatesInstance, Torch, object>.State burnout;
				public GameStateMachine<Torch.States, Torch.StatesInstance, Torch, object>.State offline;
				public GameStateMachine<Torch.States, Torch.StatesInstance, Torch, object>.State online;
			}
			public class StatesInstance : GameStateMachine<Torch.States, Torch.StatesInstance, Torch, object>.GameInstance
			{
				public StatesInstance(Torch master) : base(master)
				{
				}
			}
		}
		public class Torch_Config : IBuildingConfig
		{
			public override BuildingDef CreateBuildingDef()
			{
				int width = 1;
				int height = 1;
				string anim = "torch_kanim";
				int hitpoints = TUNING.BUILDINGS.HITPOINTS.TIER4 * 2;
				float construction_time = TUNING.BUILDINGS.CONSTRUCTION_TIME_SECONDS.TIER0;
				float[] construction_mass = new float[1] { 10f };
				string[] construction_materials = new string[] { MATERIALS.WOOD };
				float melting_point = 1600f;
				BuildLocationRule build_location_rule = BuildLocationRule.Anywhere;
				EffectorValues noise = NOISE_POLLUTION.NONE;

				BuildingDef buildingDef = BuildingTemplates.CreateBuildingDef(
					ID, width, height, anim, hitpoints, construction_time, construction_mass, construction_materials, melting_point, build_location_rule, TUNING.BUILDINGS.DECOR.NONE, noise, 1f);
				buildingDef.Entombable = true;
				buildingDef.Floodable = true;
				buildingDef.IsFoundation = false;
				buildingDef.Overheatable = false;
				buildingDef.Repairable = false;

				buildingDef.PermittedRotations = PermittedRotations.FlipH;
				buildingDef.ViewMode = OverlayModes.Light.ID;
				buildingDef.AudioCategory = "HollowMetal";
				buildingDef.AudioSize = "small";
				buildingDef.SelfHeatKilowattsWhenActive = 0.5f;

				return buildingDef;
			}

			public override void DoPostConfigureComplete(GameObject go)
			{
				go.GetComponent<Deconstructable>().allowDeconstruction = false;
				go.AddOrGet<Demolishable>().allowDemolition = false;
				UnityEngine.Object.DestroyImmediate(go.GetComponent<BuildingEnabledButton>());
			}

			public override void DoPostConfigurePreview(BuildingDef def, GameObject go)
			{
				LightShapePreview lightShapePreview = go.AddComponent<LightShapePreview>();
				lightShapePreview.lux = B_Torch.luxDefault;
				lightShapePreview.radius = B_Torch.radDefault;
				lightShapePreview.shape = LightShape.Circle;
				lightShapePreview.offset = new CellOffset(B_Torch.Torch_Offset);
			}

			public override void ConfigureBuildingTemplate(GameObject go, Tag prefab_tag)
			{
				go.GetComponent<KPrefabID>().AddTag(RoomConstraints.ConstraintTags.LightSource, false);
				Light2D light2D = go.AddOrGet<Light2D>();
				light2D.overlayColour = LIGHT2D.LIGHT_OVERLAY;
				light2D.Color = LIGHT2D.LIGHTBUG_COLOR_ORANGE;
				light2D.Lux = B_Torch.luxDefault;
				light2D.Range = B_Torch.radDefault;
				light2D.Offset = B_Torch.Torch_Offset;
				light2D.shape = LightShape.Circle;
				light2D.drawOverlay = true;

				Storage storage = go.AddOrGet<Storage>();
				storage.showInUI = true;
				storage.capacityKg = 0.2f;

				ElementConsumer elementConsumer = go.AddOrGet<ElementConsumer>();
				elementConsumer.elementToConsume = SimHashes.Oxygen;
				elementConsumer.showInStatusPanel = true;
				elementConsumer.storeOnConsume = true;
				elementConsumer.consumptionRate = 0.01f;
				elementConsumer.showDescriptor = true;
				elementConsumer.consumptionRadius = 2;

				go.AddOrGet<Torch>();
			}

			public const string ID = "Torch";
		}
		public class TorchOxy_Config : IBuildingConfig
		{
			public override BuildingDef CreateBuildingDef()
			{
				int width = 1;
				int height = 2;
				string anim = "torchoxy_kanim";
				int hitpoints = TUNING.BUILDINGS.HITPOINTS.TIER4 * 2;
				float construction_time = TUNING.BUILDINGS.CONSTRUCTION_TIME_SECONDS.TIER1;
				float[] construction_mass = new float[] { 10f, 10f };
				string[] construction_materials = new string[] { MATERIALS.WOOD, "OxyRock" };
				float melting_point = 1600f;
				BuildLocationRule build_location_rule = BuildLocationRule.OnFloor;
				EffectorValues noise = NOISE_POLLUTION.NONE;

				BuildingDef buildingDef = BuildingTemplates.CreateBuildingDef(
					ID, width, height, anim, hitpoints, construction_time, construction_mass, construction_materials, melting_point, build_location_rule, TUNING.BUILDINGS.DECOR.NONE, noise, 1f);
				buildingDef.Entombable = true;
				buildingDef.Floodable = false;
				buildingDef.IsFoundation = false;
				buildingDef.Overheatable = false;
				buildingDef.Repairable = false;

				buildingDef.PermittedRotations = PermittedRotations.FlipH;
				buildingDef.ViewMode = OverlayModes.Light.ID;
				buildingDef.AudioCategory = "HollowMetal";
				buildingDef.AudioSize = "small";
				buildingDef.SelfHeatKilowattsWhenActive = 0.5f;

				return buildingDef;
			}

			public override void DoPostConfigureComplete(GameObject go)
			{
				go.GetComponent<Deconstructable>().allowDeconstruction = false;
				go.AddOrGet<Demolishable>().allowDemolition = false;
				UnityEngine.Object.DestroyImmediate(go.GetComponent<BuildingEnabledButton>());
			}

			public override void DoPostConfigurePreview(BuildingDef def, GameObject go)
			{
				LightShapePreview lightShapePreview = go.AddComponent<LightShapePreview>();
				lightShapePreview.lux = B_Torch.luxDefault;
				lightShapePreview.radius = B_Torch.radDefault;
				lightShapePreview.shape = LightShape.Circle;
				lightShapePreview.offset = new CellOffset(B_Torch.TorchOxy_Offset);
			}

			public override void ConfigureBuildingTemplate(GameObject go, Tag prefab_tag)
			{
				go.GetComponent<KPrefabID>().AddTag(RoomConstraints.ConstraintTags.LightSource, false);
				Light2D light2D = go.AddOrGet<Light2D>();
				light2D.overlayColour = LIGHT2D.LIGHT_OVERLAY;
				light2D.Color = LIGHT2D.LIGHTBUG_COLOR_ORANGE;
				light2D.Lux = B_Torch.luxDefault;
				light2D.Range = B_Torch.radDefault;
				light2D.Offset = B_Torch.TorchOxy_Offset;
				light2D.shape = LightShape.Circle;
				light2D.drawOverlay = true;

				Storage storage = go.AddOrGet<Storage>();
				storage.showInUI = false;
				storage.capacityKg = 0f;

				Torch torch = go.AddOrGet<Torch>();
				torch.isNeedOxygen = false;
			}

			public const string ID = "TorchOxy";
		}

		public class Patch
		{
			[HarmonyPatch(typeof(ElementLoader), "GetElement")]
			public class ElementLoader_GetElement_Patch
			{
				private static void Prefix(ref Tag tag)
				{
					if (tag == "WoodLog") tag = "Creature"; //RefinedCarbon / Carbon
				}
			}
			[HarmonyPatch(typeof(Light2D), "GetDescriptors")]
			public class Light2D_GetDescriptors_Block
			{
				private static bool Prefix(GameObject go, ref List<Descriptor> __result)
				{
					if (go.name.Contains("Torch"))
					{
						__result = new List<Descriptor>();
						return false;
					}
					return true;
				}
			}
			public static void PanelLoad()
			{
				//ModUtil.AddBuildingToPlanScreen("Base", "Torch");
				ModUtil.AddBuildingToPlanScreen("Base", "TorchOxy", "uncategorized", "Ladder", ModUtil.BuildingOrdering.After);
				ModUtil.AddBuildingToPlanScreen("Base", "Torch", "uncategorized", "Ladder", ModUtil.BuildingOrdering.Before);
				S_Help.Logger("MOD-Horizon_Torch: Add Torch & TorchOxy to Building Screen - Base");
			}
			public static void TechLoad()
			{
				string techName = "Jobs";
				Techs techs = Db.Get().Techs;
				if (techs.Exists(techName))
				{
					techs.Get("Jobs").unlockedItemIDs.Add("TorchOxy");
					S_Help.Logger("MOD-Horizon_Torch: Add Torch & TorchOxy to Tech Tree - " + techName);
				}
				else
				{
					S_Help.Logger("MOD-Horizon_Torch: !!! Can't Find Tech Tree " + techName + " !!!");
				}
			}
		}
	}
}
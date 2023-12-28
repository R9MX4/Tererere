using HarmonyLib;
using System;
using Klei.AI;
using KSerialization;
using STRINGS;

namespace Horizon
{
	public class N_DarkMonitor
	{
		public static Effect darkEffect0;
		public static Effect darkEffect1;
		public static Effect darkEffect2;
		public class Patch
		{
			[HarmonyPatch(typeof(RationalAi), "InitializeStates")]
			public class RationalAi_InitializeStates_Patch
			{
				private static void Postfix(ref RationalAi __instance)
				{
					S_Help.Logger("MOD-Horizon_DarkMonitor: InitializeStates");
					__instance.alive.ToggleStateMachine(smi => new Monitor.Instance(smi.master));
				}
			}
			[HarmonyPatch(typeof(ModifierSet), "Initialize")]
			public class Patched_ModifierSet_Initialize
			{
				private static void Postfix(ModifierSet __instance)
				{
					Effect effect = __instance.effects.Get("NewCrewArrival");
					effect.duration = 600;
					S_Help.Logger("MOD-Horizon_DarkMonitor: NewCrewArrival duration 1800->600.");

					darkEffect0 = new Effect("darkEffect0", S_Text.DARKNESS.LEVEL_NAME_0, S_Text.DARKNESS.LEVEL_TOOLTIP_0, 0f, true, false, false);
					darkEffect1 = new Effect("darkEffect1", S_Text.DARKNESS.LEVEL_NAME_1, S_Text.DARKNESS.LEVEL_TOOLTIP_1, 0f, true, false, true);
					darkEffect2 = new Effect("darkEffect2", S_Text.DARKNESS.LEVEL_NAME_2, S_Text.DARKNESS.LEVEL_TOOLTIP_2, 0f, true, false, true);

					darkEffect0.Add(new AttributeModifier("Athletics",		 2f, S_Text.DARKNESS.LEVEL_NAME_0, false, false, true));
					darkEffect0.Add(new AttributeModifier("Learning",		 2f, S_Text.DARKNESS.LEVEL_NAME_0, false, false, true));
					darkEffect0.Add(new AttributeModifier("Strength",		 2f, S_Text.DARKNESS.LEVEL_NAME_0, false, false, true));

					darkEffect1.Add(new AttributeModifier("Art",			-2f, S_Text.DARKNESS.LEVEL_NAME_1, false, false, true));
					darkEffect1.Add(new AttributeModifier("Athletics",		-2f, S_Text.DARKNESS.LEVEL_NAME_1, false, false, true));
					darkEffect1.Add(new AttributeModifier("Learning",		-2f, S_Text.DARKNESS.LEVEL_NAME_1, false, false, true));
					darkEffect1.Add(new AttributeModifier("Machinery",		-2f, S_Text.DARKNESS.LEVEL_NAME_1, false, false, true));
					darkEffect1.Add(new AttributeModifier("StressDelta",  0.01f, S_Text.DARKNESS.LEVEL_NAME_1, false, false, true));

					darkEffect2.Add(new AttributeModifier("Art",			-5f, S_Text.DARKNESS.LEVEL_NAME_2, false, false, true));
					darkEffect2.Add(new AttributeModifier("Athletics",		-3f, S_Text.DARKNESS.LEVEL_NAME_2, false, false, true));
					darkEffect2.Add(new AttributeModifier("Learning",		-5f, S_Text.DARKNESS.LEVEL_NAME_2, false, false, true));
					darkEffect2.Add(new AttributeModifier("Machinery",		-5f, S_Text.DARKNESS.LEVEL_NAME_2, false, false, true));
					darkEffect2.Add(new AttributeModifier("StressDelta", 0.025f, S_Text.DARKNESS.LEVEL_NAME_2, false, false, true));

					__instance.effects.Add(darkEffect0);
					__instance.effects.Add(darkEffect1);
					__instance.effects.Add(darkEffect2);
					S_Help.Logger("MOD-Horizon_DarkMonitor: Dark Effect inited.");
				}
			}
		}
		[SerializationConfig(MemberSerialization.OptIn)]
		public class Monitor : GameStateMachine<Monitor, Monitor.Instance>, ISaveLoadable
		{
			[Serialize]
			public FloatParameter dark_val;

			public State light;
			public State norml;
			public State darky;
			public State blind;

			public override void InitializeStates(out StateMachine.BaseState default_state)
			{
				default_state = this.norml;

				this.root.Update(new Action<Monitor.Instance, float>(Monitor.Update_Visual), UpdateRate.SIM_200ms, false);
				this.light.ParamTransition<float>(this.dark_val, this.norml, (Monitor.Instance smi, float p) => p < 200)
						  .Enter(smi => smi.effects.Add(darkEffect0.Id, true))
						  .Exit(smi => smi.effects.Remove(darkEffect0.Id));
				this.norml.ParamTransition<float>(this.dark_val, this.light, (Monitor.Instance smi, float p) => p > 200)
						  .ParamTransition<float>(this.dark_val, this.darky, (Monitor.Instance smi, float p) => p < 100);
				this.darky.ParamTransition<float>(this.dark_val, this.norml, (Monitor.Instance smi, float p) => p > 100)
						  .ParamTransition<float>(this.dark_val, this.blind, (Monitor.Instance smi, float p) => p < 50)
						  .Enter(smi => smi.effects.Add(darkEffect1.Id, true))
						  .Exit(smi => smi.effects.Remove(darkEffect1.Id));
				this.blind.ParamTransition<float>(this.dark_val, this.darky, (Monitor.Instance smi, float p) => p > 50)
						  .Enter(smi => smi.effects.Add(darkEffect2.Id, true))
						  .Exit(smi => smi.effects.Remove(darkEffect2.Id));
			}

			private static void Update_Visual(Monitor.Instance smi, float dt)
			{
				if (smi.effects.HasEffect("SunburnSickness"))
				{
					S_Help.Logger("MOD-Horizon_DarkMonitor: SunburnSickness.");
					smi.sm.dark_val.Set(150, smi);
				}
				StaminaMonitor.Instance staminaMonitor = smi.master.gameObject.GetSMI<StaminaMonitor.Instance>();
				if (staminaMonitor != null && staminaMonitor.IsSleeping())
				{
					smi.sm.dark_val.Set(150f, smi);
					return;
				}
				int cell = Grid.PosToCell(smi.gameObject);
				float visible = N_Darkness.BackUpdater.is_inited ? N_Darkness.BackUpdater.visible[cell] : Math.Max(255, Grid.LightIntensity[cell] / 5.0f + 50.0f);
				float darkval = smi.sm.dark_val.Get(smi);
				if (darkval != 0) visible = darkval * 0.9f + visible * 0.1f;
				smi.sm.dark_val.Set(visible, smi);
			}

			public new class Instance : GameStateMachine<Monitor, Monitor.Instance>.GameInstance
			{
				public Instance(IStateMachineTarget master) : base(master)
				{
					effects = this.GetComponent<Effects>();
				}
				public Effects effects;
			}
		}
	}
}
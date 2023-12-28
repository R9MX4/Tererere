using HarmonyLib;
using Klei.AI;
using Klei;
using System.Collections.Generic;
using System.IO;

namespace Horizon
{
	public class C_Element
	{
		[HarmonyPatch(typeof(ElementLoader), "FinaliseElementsTable")]
		public class ElementLoader_FinaliseElementsTable_Patch
		{
			static C_ElementList C_ElementCollection = null;
			private static void Prefix()
			{
				string full_path = Path.Combine(S_Help.GetModPath(), "elemenet.yaml");
				if (!File.Exists(full_path))
				{
					S_Help.Logger("MOD-Horizon_Element: " + full_path + " not exist\n");
					return;
				}
				//part1
				C_ElementCollection = YamlIO.LoadFile<C_ElementList>(full_path, null, null);
				foreach (C_ElementEntry elementEntry in C_ElementCollection.elements)
				{
					Element element = ElementLoader.FindElementByName(elementEntry.elementId);
					if (element != null)
					{
						if (elementEntry.maxMass != 0)								element.maxMass								= elementEntry.maxMass;
						if (elementEntry.molarMass != 0)							element.molarMass							= elementEntry.molarMass;
						if (elementEntry.hardness != 0)								element.hardness							= elementEntry.hardness;
						if (elementEntry.thermalConductivity != 0)					element.thermalConductivity					= elementEntry.thermalConductivity;
						if (elementEntry.lightAbsorptionFactor != 0)				element.lightAbsorptionFactor				= elementEntry.lightAbsorptionFactor;
						if (elementEntry.lowTemp != 0)								element.lowTemp								= elementEntry.lowTemp;
						if (elementEntry.lowTempTransitionOreID != null)			element.lowTempTransitionOreID				= (SimHashes)Hash.SDBMLower(elementEntry.lowTempTransitionOreID);
						if (elementEntry.lowTempTransitionTarget != null)			element.lowTempTransitionTarget				= (SimHashes)Hash.SDBMLower(elementEntry.lowTempTransitionTarget);
						if (elementEntry.lowTempTransitionOreMassConversion != 0)	element.lowTempTransitionOreMassConversion	= elementEntry.lowTempTransitionOreMassConversion;
						if (elementEntry.highTemp != 0)								element.highTemp							= elementEntry.highTemp;
						if (elementEntry.highTempTransitionOreID != null)			element.highTempTransitionOreID				= (SimHashes)Hash.SDBMLower(elementEntry.highTempTransitionOreID);
						if (elementEntry.highTempTransitionTarget != null)			element.highTempTransitionTarget			= (SimHashes)Hash.SDBMLower(elementEntry.highTempTransitionTarget);
						if (elementEntry.highTempTransitionOreMassConversion != 0)	element.highTempTransitionOreMassConversion	= elementEntry.highTempTransitionOreMassConversion;

						if (elementEntry.overheat != 0)								EleHeatList.Add (new EleHeatInfo ((SimHashes)Hash.SDBMLower(elementEntry.elementId), elementEntry.overheat));
						if (elementEntry.decor != 0)								EleDecorList.Add(new EleDecorInfo((SimHashes)Hash.SDBMLower(elementEntry.elementId), elementEntry.decor));

						if (elementEntry.tagsAdd != null)
							foreach (string tagAdd in elementEntry.tagsAdd)
								if (!element.HasTag(tagAdd))
									element.oreTags = element.oreTags.Append(TagManager.Create(tagAdd));
					}
				}
				S_Help.Logger("MOD-Horizon_Element: " + C_ElementCollection.elements.Length + " Elements Property Updated");

				//part2
				foreach (Element element in ElementLoader.elements)
				{
					if (element != null && element.IsLiquid)
					{
						element.molarMass = element.maxMass;
						element.maxCompression = 1f / element.viscosity + 1;
						element.defaultValues.mass = element.maxMass;
						if (element.minVerticalFlow > 1)
							element.minVerticalFlow = 1f;
						if (element.minHorizontalFlow > 1)
							element.minHorizontalFlow = 1f;
					}
				}
				S_Help.Logger("MOD-Horizon_Element: Density Updated");
			}

			private static void Postfix()
			{
				//Use element.idx. Must be excuted after ElementLoader.FinaliseElementsTable
				N_Resistance.EleResistList = new float[ElementLoader.elements.Count];
				N_Ruler.ReflectList = new float[ElementLoader.elements.Count];
				for (int idx = 0; idx < ElementLoader.elements.Count; idx++)
				{
					N_Resistance.EleResistList[idx] = 10f;
					N_Ruler.ReflectList[idx] = 1f;
				}

				foreach (C_ElementEntry elementEntry in C_ElementCollection.elements)
				{
					Element element = ElementLoader.FindElementByName(elementEntry.elementId);
					if (element != null)
					{
						if (elementEntry.resist != 0) N_Resistance.EleResistList[element.idx] = elementEntry.resist;
						if (elementEntry.reflect != 0) N_Ruler.ReflectList[element.idx] = elementEntry.reflect;
					}
				}
				S_Help.Logger("MOD-Horizon_Element: Resist  Updated");
				S_Help.Logger("MOD-Horizon_Element: Reflect Updated");
			}
		}

		[HarmonyPatch(typeof(LegacyModMain), "ConfigElements")]
		public class LegacyModMain_ConfigElements_Patch
		{
			private static void Postfix()
			{
				//part3
				foreach (EleDecorInfo eleDecorInfo in EleDecorList)
				{
					Element element = ElementLoader.FindElementByHash(eleDecorInfo.id);
					if (element != null)
					{
						AttributeModifier attributeModifier = element.attributeModifiers.Find((AttributeModifier m) => m.AttributeId == Db.Get().BuildingAttributes.Decor.Id);
						if (attributeModifier != null && eleDecorInfo.decor != 0)
						{
							attributeModifier.SetValue(eleDecorInfo.decor);
						}
						else if (attributeModifier == null && eleDecorInfo.decor != 0)
						{
							AttributeModifier item = new AttributeModifier(Db.Get().BuildingAttributes.Decor.Id, eleDecorInfo.decor, element.name, true, false, true);
							element.attributeModifiers.Add(item);
						}
					}
				}
				foreach (EleHeatInfo eleHeatInfo in EleHeatList)
				{
					Element element = ElementLoader.FindElementByHash(eleHeatInfo.id);
					if (element != null)
					{
						AttributeModifier attributeModifier = element.attributeModifiers.Find((AttributeModifier m) => m.AttributeId == Db.Get().BuildingAttributes.OverheatTemperature.Id);
						if (attributeModifier != null && eleHeatInfo.overheat != 0)
						{
							attributeModifier.SetValue(eleHeatInfo.overheat);
						}
						else if (attributeModifier == null && eleHeatInfo.overheat != 0)
						{
							AttributeModifier item = new AttributeModifier(Db.Get().BuildingAttributes.OverheatTemperature.Id, eleHeatInfo.overheat, element.name, false, false, true);
							element.attributeModifiers.Add(item);
						}
					}
				}
				S_Help.Logger("MOD-Horizon_Element: " + EleDecorList.Count + " Elements Decor Updated");
				S_Help.Logger("MOD-Horizon_Element: " + EleHeatList.Count + " Elements Heat  Updated");
				return;
			}
		}

		[HarmonyPatch(typeof(GameUtil), "GetSignificantMaterialPropertyDescriptors")]
		public class GameUtil_GetSignificantMaterialPropertyDescriptors_Patch
		{
			private static void Postfix(Element element, ref List<Descriptor> __result)
			{
				if (N_Resistance.EleResistList[element.idx] != 10f)
				{
					Descriptor item = default;
					item.SetupDescriptor(N_Resistance.EleResistList[element.idx] < 4f ? S_Text.POWER_RESISTANCE.RESIST_DESC2 : S_Text.POWER_RESISTANCE.RESIST_DESC1,
										 string.Format(S_Text.POWER_RESISTANCE.RESIST_TOOLTIP, element.name, N_Resistance.EleResistList[element.idx]),
										 Descriptor.DescriptorType.Effect);
					item.IncreaseIndent();
					__result.Add(item);
				}
				if (N_Ruler.ReflectList[element.idx] != 1f)
				{
					Descriptor item = default;
					item.SetupDescriptor(string.Format(S_Text.REFLECT.REFLECT_DESC, N_Ruler.ReflectList[element.idx]),
										 string.Format(S_Text.REFLECT.REFLECT_TOOLTIP, element.name, (int)(100 - N_Ruler.ReflectList[element.idx] * 100)),
										 Descriptor.DescriptorType.Effect);
					item.IncreaseIndent();
					__result.Add(item);
				}
			}
		}

		private static List<EleDecorInfo> EleDecorList = new List<EleDecorInfo>();
		private static List<EleHeatInfo> EleHeatList = new List<EleHeatInfo>();

		private struct EleDecorInfo
		{
			public SimHashes id;
			public float decor;
			public EleDecorInfo(SimHashes ID, float DECOR)
			{
				this.id = ID;
				this.decor = DECOR;
			}
		}
		private struct EleHeatInfo
		{
			public SimHashes id;
			public float overheat;
			public EleHeatInfo(SimHashes ID, float OVERHEAT)
			{
				this.id = ID;
				this.overheat = OVERHEAT;
			}
		}

		public class C_ElementList
		{
			public C_ElementEntry[] elements { get; set; }
		}
		public class C_ElementEntry
		{
			public string elementId { get; set; }
			public float maxMass { get; set; }
			public float molarMass { get; set; }
			public byte hardness { get; set; }
			public float thermalConductivity { get; set; }
			public float lightAbsorptionFactor { get; set; }
			public float lowTemp { get; set; }
			public string lowTempTransitionOreID { get; set; }
			public string lowTempTransitionTarget { get; set; }
			public float lowTempTransitionOreMassConversion { get; set; }
			public float highTemp { get; set; }
			public string highTempTransitionOreID { get; set; }
			public string highTempTransitionTarget { get; set; }
			public float highTempTransitionOreMassConversion { get; set; }
			public float overheat { get; set; }
			public float decor { get; set; }
			public float resist { get; set; }
			public float reflect { get; set; }
			public string[] tagsAdd { get; set; }
		}
	}
}
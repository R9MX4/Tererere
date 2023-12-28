using HarmonyLib;
using System.IO;
using System.Collections.Generic;
using STRINGS;

namespace Horizon
{
	public class S_Text
	{
		public static class POWER_RESISTANCE
		{
			public static LocString LOSS = "Trans Loss: {0}";
			public static LocString RESIST_NET = "{0}Ω Net Resistance causes {1}% Trans Loss";
			public static LocString WIRE_TOOLTIP = "The <style=\"KKeyword\">Trans Loss</style> is caused by wire resistance. Resistance depends on wire length, type and material.\n" +
				"Currenmt Resistance is {1}Ω and causes {2}% <style=\"KKeyword\">Trans Loss</style>";
            public static LocString DAILYREPORT = "Elect Trans";
			public static LocString RESIST_DESC0 = "Insulation";
			public static LocString RESIST_DESC1 = "Conductive";
			public static LocString RESIST_DESC2 = "Good conductive";
			public static LocString RESIST_TOOLTIP = "The resist of {0} is {1}";
		}
		public static class REFLECT
		{
			public static LocString REFLECT_DESC = "Reflect sunlight";
			public static LocString REFLECT_TOOLTIP = "{0} reflects {1}% sunlight";
		}
		public static class PRESSURE_DAMAGE
		{
			public static LocString GAS_PRESSURE = "Air Pressure Damage";
			public static LocString LIQUID_PRESSURE = "Liquid Pressure Damage";
			public static LocString SOLID_PRESSURE = "Crushed";
			public static LocString SOLID_PRESSUREBURY = "Buried";
		}
		public static class VENT_ENVIRONMENT
		{
			public static LocString WRONG_AMBIENCE = "Forbidden Ambience: {0}";
			public static LocString AMBIENCE = "{0} Ambience, Pressure: {1}/{2}";
		}
		public static class HATER_TARGETTEMP
		{
			public static LocString TARGETTEMPERATURE = "Target Temperaature";
		}
		public static class TERR_LAND
		{
			public static LocString NAME = "-Horizon";
			public static LocString DESCRIPTION = "\nDuplicants born in the surface of {0}.";
		}
		public static class DARKNESS
		{
			public static LocString LEVEL_NAME_0 = "Bright";
			public static LocString LEVEL_NAME_1 = "Gloomy";
			public static LocString LEVEL_NAME_2 = "Blind";
			public static LocString LEVEL_TOOLTIP_0 = "The duplicant can see a bright future.";
			public static LocString LEVEL_TOOLTIP_1 = "Poor lighting affects the quality of work.";
			public static LocString LEVEL_TOOLTIP_2 = "Duplicant feels to be blind and has trouble to most work.";
		}
		public static class HOVER_BAR
		{
			public static LocString COORD = "Coordinate: No.{0},X={1},Y={2}";
			public static LocString PRESSURE = "Pressure={0}";
			public static LocString ELEMENT = "Reflect={0}, Resist={1}";
			public static LocString VISUAL = "Visual={0}";
			public static LocString SURFACE = "Surface";
			public static LocString CRASH = "Crash";
			public static LocString CHAOS = "Chaos";
		}
		public static class TORCH
		{
			public static Dictionary<string, string> dic = new Dictionary<string, string>()
			{
				{ "STRINGS.BUILDINGS.PREFABS.TORCH.NAME", "Wall Torch" },
				{ "STRINGS.BUILDINGS.PREFABS.TORCH.DESC", "Wall Torch provide the basic <link=\"LIGHT\">lighting</link> for duplicants." },
				{ "STRINGS.BUILDINGS.PREFABS.TORCH.EFFECT", "Wall Torch consume <link=\"OXYGEN\">Oxygen</link> to maintain combustion.\n" +
					"After burning out, a piece of <link=\"CARBON\">Coal</link> be left." },
				{ "STRINGS.BUILDINGS.PREFABS.TORCHOXY.NAME", "Pig Torch" },
				{ "STRINGS.BUILDINGS.PREFABS.TORCHOXY.DESC", "Pig Torch can burn anywhere." },
				{ "STRINGS.BUILDINGS.PREFABS.TORCHOXY.EFFECT", "Pig Torch designed to burn in outer space.\n" +
					"After burning out, a piece of <link=\"REFINEDCARBON\">Refined Carbon</link> be left." }
			};
			public static LocString BURNOUT_DESC = "This torch is burned out.";
			public static LocString BURNOUT_TOOLTIP = "It's time to deconstruct it...";
			public static LocString LIGHT_DESC = "Light";
			public static LocString LIGHT_TOOLTIP = "Light the torch";
			public static LocString EXTIN_DESC = "Extinguish";
			public static LocString EXTIN_TOOLTIP = "Extinguish the torch";
		}
		public static class OXYFERN
		{
			public static LocString DESC = "Oxyferns exudes breathable <link=\"OXYGEN\">Oxygen</link> from <link=\"CARBONDIOXIDE\">Carbon Dioxide</link>.\n" +
				"It synthesis Carbon Dioxide Gas in the light and absorb Carbon Dioxide Gas in the dark.";
			public static LocString DOMESTICATEDDESC = "This plant photosynthesizes or absorbs <link=\"CARBONDIOXIDE\">Carbon Dioxide</link> from atmosphere and convert it to <link=\"OXYGEN\">Oxygen</link>.";
		}
		public static class SYNTHESIS
		{
			public static LocString NAME = "Synthesis {ElementTypes}: {FlowRate}";
			public static LocString TOOLTIP = "This object synthesis {ElementTypes} a rate of " + UI.FormatAsNegativeRate("{FlowRate}");
		}
		public static class TERR
		{
			public static Dictionary<string, string> dic = new Dictionary<string, string>()
			{
				{ "STRINGS.SUBWORLDS.TERR.NAME", "Terr" },
				{ "STRINGS.SUBWORLDS.TERR.DESC", "The Terr Biome contains various sub-biomes. The underground river, warm sunlight and plenty of plants are also helpful to get your colony up and running." },
				{ "STRINGS.SUBWORLDS.TERR.UTILITY", string.Concat(new string[]
					{
						"<link=\"OXYGEN\">Oxygen</link> in the atmosphere and oxyfern are enough to sustain your colony for a long time.\n\n",
						"<link=\"LIGHT\">Sunlight</link> provides a reliable light source in day time.\n\n",
						"This biome is the perfect starting spot for my colony to establish a base full of essentials, from which they can then venture out and explore."
					})
				}
			};
		}
	}
	public class Patch
	{
		private static List<Dictionary<string, string>> Dictionaries = new List<Dictionary<string, string>>() { S_Text.TORCH.dic, S_Text.TERR.dic };

		[HarmonyPatch(typeof(Localization), "Initialize")]
		public class Localization_Initialize_Patch
		{
			private static void Postfix()
			{
				Localization.RegisterForTranslation(typeof(S_Text));
				string textpath = Path.Combine(S_Help.GetModPath(), "string");
				Localization.Locale locale = Localization.GetLocale();
				string textpath2 = Path.Combine(textpath, locale?.Code + ".po");

				Dictionary<string, string> locDic = new Dictionary<string, string>();
				if (File.Exists(textpath2))
				{
					locDic = Localization.LoadStringsFile(textpath2, false);
					TextLoad(locDic);
					Localization.OverloadStrings(locDic);
					Localization.GenerateStringsTemplate(typeof(S_Text), textpath);
					S_Help.Logger("MOD-Horizon_Text: Update language: " + locale);
				}
				else
				{
					TextLoad(locDic);
					Localization.OverloadStrings(locDic);
					S_Help.Logger("MOD-Horizon_Text: !!! " + textpath + " not exist !!!");
				}
			}
		}

		public static void TextLoad(Dictionary<string, string> locDic)
		{
			foreach (Dictionary<string, string> dic in Patch.Dictionaries)
			{
				foreach (KeyValuePair<string, string> keyValuePair in dic)
				{
					if (!locDic.ContainsKey(keyValuePair.Key))
						locDic.Add(keyValuePair.Key, keyValuePair.Value);

					string[] text = new string[] { keyValuePair.Key, locDic[keyValuePair.Key] };
					Strings.Add(text);
				}
			}
		}

		//[HarmonyPatch(typeof(Localization), "HasSameOrLessLinkCountAsEnglish")]
		//public class Localization_HasSameOrLessLinkCountAsEnglish_Patch
		//{
		//	private static void Postfix(string translated_string, bool __result)
		//	{
		//		if (__result) S_Help.Logger("MOD-Horizon_Text: !!! HasSameOrLessLinkCountAsEnglish ByPass: " + translated_string + " !!!");
		//		__result = false;
		//	}
		//}
	}
}
using HarmonyLib;
using Database;

namespace Horizon
{
	public class A_Launcher
	{
		static bool worldInited = false;
		public const bool isdebug = false;
		[HarmonyPatch(typeof(Assets), "OnPrefabInit")]
		public class Assets_OnPrefabInit
		{
			private static void Postfix()
			{
				S_Help.Logger("MOD-Horizon_Launcher: Action.0 Load Game.");
				N_Darkness.BackUpdater.Init();
				N_Ruler.BackScaner.Init();
				B_Torch.Patch.PanelLoad();
				H_Horizon.Patch.Add_Spirte();
			}
		}

		[HarmonyPatch(typeof(MainMenu), "OnSpawn")]
		public class MainMenu_OnSpawn
		{
			private static void Postfix()
			{
				S_Help.Logger("MOD-Horizon_Launcher: Action.1 MainMenu");
				N_Ruler.BackScaner.Clear();
			}
		}

		[HarmonyPatch(typeof(SaveLoader), "OnSpawn")]
		public class SyncWorld
		{
			private static void Postfix()
			{
				S_Help.Logger("MOD-Horizon_Launcher: Action.2 Load Save File. InitWorld");
				N_Darkness.BackUpdater.InitWorld();
				worldInited = false;
			}
		}

		[HarmonyPatch(typeof(Game), "OnSpawn")]
		public class Game_OnSpawn
		{
			private static void Postfix()
			{
				S_Help.Logger("MOD-Horizon_Launcher: Action.3 World Start");
				if(isdebug) S_Help.DebugON();
				N_Darkness.CreateSyncTask();
				N_Ruler.CreateSyncTask();
			}
		}

		[HarmonyPatch(typeof(Game), "SimEveryTick")]
		public class Games_SimEveryTick
		{
			private static void Prefix()
			{
				if (!worldInited)
				{
					H_Horizon.Patch.Add_Invisual_Border();
					worldInited = true;
				}
			}
		}

		[HarmonyPatch(typeof(Game), "UnsafeSim200ms")]
		public class Games_UnsafeSim200ms
		{
			private static void Prefix(float dt, Game __instance)
			{
				if (dt > 0)
					N_Ruler.UnsafeSim200ms(dt, __instance);
			}
		}

		[HarmonyPatch(typeof(Techs), "Init")]
		public class Techs_Init
		{
			private static void Postfix()
			{
				B_Torch.Patch.TechLoad();
			}
		}
	}
}
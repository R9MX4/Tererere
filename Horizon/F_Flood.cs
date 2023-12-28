using HarmonyLib;

namespace Horizon
{
	public class F_Flood
	{
		public class Patch
		{
			[HarmonyPatch(typeof(Floodable), "OnElementChanged")]
			public class Floodable_OnElementChanged_Patch
			{
				private static bool Prefix(ref Floodable __instance)
				{
					var instance = Traverse.Create(__instance);
					int[] PlacementCells = instance.Field("building").GetValue<Building>().PlacementCells;
					bool isFlooded = instance.Field("isFlooded").GetValue<bool>();
					Operational operational = instance.Field("operational").GetValue<Operational>();

					bool flag = false;
					for (int i = 0; i < PlacementCells.Length; i++)
					{
						if (Grid.IsSubstantialLiquid(PlacementCells[i], 0.35f) || (Grid.IsValidCell(PlacementCells[i] + Grid.WidthInCells) && Grid.IsLiquid(PlacementCells[i] + Grid.WidthInCells)))
						{
							flag = true;
							break;
						}
					}
					if (flag != isFlooded)
					{
						isFlooded = flag;
						operational.SetFlag(Floodable.notFloodedFlag, !isFlooded);
						__instance.GetComponent<KSelectable>().ToggleStatusItem(Db.Get().BuildingStatusItems.Flooded, isFlooded, __instance);
					}
					instance.Field("isFlooded").SetValue(isFlooded);
					instance.Field("operational").SetValue(operational);
					return false;
				}
			}
		}
	}
}
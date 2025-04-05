namespace LabyrinthianFacilities.Compatibility;

public static class GenericInteriors {
	
	public static void Initialize() {
		Plugin.LogInfo($"Loading compatbility for GenericInteriors");
		Moon.OnNewMap += NewMapHandler;
	}
	
	private static void NewMapHandler(DGameMap map) {
		if (map.name != "interior:TowerFlow") return;
		map.TilePlacementVerifiers += PlacementValidator;
	}
	
	private static bool PlacementValidator(DTile newTile, DTile oldTile, DDoorway newDoorway, DDoorway oldDoorway) {
		return (
			newTile.name != "Towermainstairs(Clone)"
			|| !newTile.transform.Find("Doorwayme (1)").GetComponent<DDoorway>().IsVacant
			|| oldTile.name == "Towermainstairs(Clone)" 
			|| oldTile.name == "TowerEntry(Clone)"
		);
	}
}
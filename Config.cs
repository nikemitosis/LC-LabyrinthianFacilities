namespace LabyrinthianFacilities;

using System;

using BepInEx.Configuration;
using BepInEx.Logging;

public sealed class Config {
	private static Config singleton = null;
	public static Config Singleton {get => singleton ??= new Config();}
	
	// Features
	private ConfigEntry<bool> m_GlobalEnable;
	
	// Features.NoncollectionPenalty
	private ConfigEntry<bool> m_EnableNoncollectionPenalty;
	private ConfigEntry<bool> m_ExcludeCruiserScrap;
	private ConfigEntry<bool> m_ExcludeSurfaceScrap;
	private ConfigEntry<bool> m_ExcludeHive;
	
	// Features.MapObjects
	private ConfigEntry<bool> m_SaveMapObjects;
	private ConfigEntry<bool> m_SaveEquipment;
	private ConfigEntry<bool> m_SaveScrap;
	private ConfigEntry<bool> m_SaveHives;
	private ConfigEntry<bool> m_SaveCruisers;
	
	// Features.Hazards
	private ConfigEntry<bool> m_SaveHazards;
	private ConfigEntry<bool> m_SaveTurrets;
	private ConfigEntry<bool> m_SaveLandmines;
	private ConfigEntry<bool> m_SaveSpikeTraps;
	
	// Features.InteriorGeneration
	private ConfigEntry<bool>  m_UseCustomGeneration;
	private ConfigEntry<bool>  m_SaveMaps;
	private ConfigEntry<float> m_MinimumTileMultiplier;
	private ConfigEntry<float> m_MaximumTileMultiplier;
	
	// Features.EasterEggs
	private ConfigEntry<bool> m_BouncyCruisers;
	private ConfigEntry<bool> m_ForbiddenPassages;
	
	// Debug.Logging
	private ConfigEntry<LogLevel> m_LogLevels;
	private ConfigEntry<bool> m_EnableVerboseGeneration;
	private ConfigEntry<bool> m_EnableVerboseSerialization;
	private ConfigEntry<bool> m_EnableVerboseDeserialization;
	
	// Features.SetSeed
	private ConfigEntry<bool> m_UseSetSeed;
	private ConfigEntry<int > m_Seed;
	private ConfigEntry<bool> m_IncrementSetSeed;
	
	public bool GlobalEnable                 {get => m_GlobalEnable.Value;}
	
	public bool EnableNoncollectionPenalty   {get => m_EnableNoncollectionPenalty.Value;}
	public bool ExcludeCruiserScrap          {get => m_ExcludeCruiserScrap.Value;}
	public bool ExcludeSurfaceScrap          {get => m_ExcludeSurfaceScrap.Value;}
	public bool ExcludeHive                  {get => m_ExcludeHive.Value;}
	
	public bool SaveMapObjects               {get => m_SaveMapObjects.Value;}
	public bool SaveEquipment                {get => m_SaveEquipment.Value;}
	public bool SaveScrap                    {get => m_SaveScrap.Value;}
	public bool SaveHives                    {get => m_SaveHives.Value;}
	public bool SaveCruisers                 {get => m_SaveCruisers.Value;}
	
	public bool SaveHazards                  {get => m_SaveHazards.Value;}
	public bool SaveTurrets                  {get => m_SaveTurrets.Value;}
	public bool SaveLandmines                {get => m_SaveLandmines.Value;}
	public bool SaveSpikeTraps               {get => m_SaveSpikeTraps.Value;}
	
	public bool UseCustomGeneration          {get => m_UseCustomGeneration.Value;}
	public bool SaveMaps                     {get => m_SaveMaps.Value;}
	public float MinimumTileMultiplier       {get => m_MinimumTileMultiplier.Value;}
	public float MaximumTileMultiplier       {get => m_MaximumTileMultiplier.Value;}
	
	public bool BouncyCruisers               {get => m_BouncyCruisers.Value;}
	public bool ForbiddenPassages            {get => m_ForbiddenPassages.Value;}
	
	public LogLevel LogLevels                {get => m_LogLevels.Value;}
	public bool EnableVerboseGeneration      {get => m_EnableVerboseGeneration.Value;}
	public bool EnableVerboseSerialization   {get => m_EnableVerboseSerialization.Value;}
	public bool EnableVerboseDeserialization {get => m_EnableVerboseDeserialization.Value;}
	
	public bool UseSetSeed                   {get => m_UseSetSeed.Value;}
	public int  Seed                         {get => m_Seed.Value;}
	public bool IncrementSetSeed             {get => m_IncrementSetSeed.Value;}
	
	public Config() {
		if (singleton != null) throw new InvalidOperationException("Singleton violation");
		singleton = this;
		
		var Config = Plugin.Singleton.Config;
		
		string section = "Features";
		m_GlobalEnable = Config.Bind(
			section,
			"GlobalEnable",
			true,
			"Enables the mod. Set this to false if you want to hurt my feelings. "
		);
		
		section = "Features.NoncollectionPenalty";
		m_EnableNoncollectionPenalty = Config.Bind(
			section,
			"EnableNoncollectionPenalty",
			true,
			"NOT YET IMPLEMENTED; Generates less scrap the next day if there is already scrap from days prior. "
		);
		m_ExcludeSurfaceScrap = Config.Bind(
			section,
			"ExcludeSurfaceScrap",
			false,
			"NOT YET IMPLEMENTED; Exclude scrap at the surface when considering noncollection penalty\n"
			+"(Requires EnableNoncollectionPenalty)"
		);
		m_ExcludeCruiserScrap = Config.Bind(
			section,
			"ExcludeCruiserScrap",
			false,
			"NOT YET IMPLEMENTED; Exclude scrap in cruisers when considering noncollection penalty\n"
			+"(Requires EnableNoncollectionPenalty)"
		);
		m_ExcludeHive = Config.Bind(
			section,
			"ExcludeHive",
			true,
			"NOT YET IMPLEMENTED; Exclude beehives when considering noncollection penalty\n"
			+"(Requires EnableNoncollectionPenalty)"
		);
		
		section = "Features.MapObjects";
		m_SaveMapObjects = Config.Bind(
			section,
			"SaveMapObjects",
			true,
			"Save 'MapObjects' (basically scrap & equipment)"
		);
		m_SaveEquipment = Config.Bind(
			section,
			"SaveEquipment",
			true,
			"Save Equipment (Requires SaveMapObjects)"
		);
		m_SaveScrap = Config.Bind(
			section,
			"SaveScrap",
			true,
			"Save Scrap (Requires SaveMapObjects)"
		);
		m_SaveHives = Config.Bind(
			section,
			"SaveHives",
			true,
			"Save Beehives (Requires SaveMapObjects)"
		);
		m_SaveCruisers = Config.Bind(
			section,
			"SaveCruisers",
			true,
			"Save Cruiser (Requires SaveMapObjects)"
		);
		
		section = "Features.Hazards";
		m_SaveHazards = Config.Bind(
			section,
			"SaveHazards",
			true,
			"NOT YET IMPLEMENTED; Save Hazards (turrets, landmines, etc.)"
		);
		m_SaveTurrets = Config.Bind(
			section,
			"SaveTurrets",
			true,
			"NOT YET IMPLEMENTED; Save Turrets (Requires SaveHazards)"
		);
		m_SaveLandmines = Config.Bind(
			section,
			"SaveLandmines",
			true,
			"NOT YET IMPLEMENTED; Save Landmines (Requires SaveHazards)"
		);
		m_SaveSpikeTraps = Config.Bind(
			section,
			"SaveSpikeTraps",
			true,
			"NOT YET IMPLEMENTED; Save Spike Traps (Requires SaveHazards)"
		);
		
		section = "Features.InteriorGeneration";
		m_UseCustomGeneration = Config.Bind(
			section,
			"UseCustomGeneration",
			true,
			"NOT YET IMPLEMENTED; Use Custom Generation"
		);
		m_SaveMaps = Config.Bind(
			section,
			"SaveMaps",
			true,
			"NOT YET IMPLEMENTED; Save interiors (Requires UseCustomGeneration)"
		);
		m_MinimumTileMultiplier = Config.Bind(
			section,
			"MinimumTileMultiplier",
			3.5f,
			"NOT YET IMPLEMENTED; Multiplier for the minimum amount of tiles. Multiplies by the average amount of main tiles that would be generated by DunGen. (Requires UseCustomGeneration)"
		);
		m_MaximumTileMultiplier = Config.Bind(
			section,
			"MaximumTileMultiplier",
			8.0f,
			"NOT YET IMPLEMENTED; Multiplier for the maximum amount of tiles. Multiplies by the average amount of main tiles that would be generated by DunGen. (Requires UseCustomGeneration)"
		);
		
		section = "Features.EasterEggs";
		m_BouncyCruisers = Config.Bind(
			section,
			"BouncyCruisers",
			true,
			"NOT YET IMPLEMENTED; "
		);
		m_ForbiddenPassages = Config.Bind(
			section,
			"ForbiddenPassages",
			true,
			"NOT YET IMPLEMENTED; "
		);
		
		section = "Debug.Logging";
		m_LogLevels = Config.Bind(
			section,
			"LogLevels",
			LogLevel.All,
			"NOT YET IMPLEMENTED; Which log levels to show in the console"
		);
		m_EnableVerboseGeneration = Config.Bind(
			section,
			"EnableVerboseGeneration",
			false,
			"NOT YET IMPLEMENTED; Enables verbose logging for generation\n"
			+"(Requires LogLevels contains Debug)"
		);
		m_EnableVerboseSerialization = Config.Bind(
			section,
			"EnableVerboseSerialization",
			false,
			"NOT YET IMPLEMENTED; Enables verbose logging for serialization (used for saving maps & sending data to clients)\n"
			+"(Requires LogLevels contains Debug)"
		);
		m_EnableVerboseDeserialization = Config.Bind(
			section,
			"EnableVerboseDeserialization",
			false,
			"NOT YET IMPLEMENTED; Enables verbose logging for deserialization (used for loading maps & receiving data from server)\n"
			+"(Requires LogLevels contains Debug)"
		);
		
		section = "Features.SetSeed";
		m_UseSetSeed = Config.Bind(
			section,
			"UseSetSeed",
			false,
			"Enables the set seed\n"
			+"(The set seed only affects tile generation; "
			+"it does not affect weather, sales, or which interior is chosen)"
		);
		m_Seed = Config.Bind(
			section,
			"Seed",
			0,
			"The seed to use for set seed"
		);
		m_IncrementSetSeed = Config.Bind(
			section,
			"IncrementSetSeed",
			true,
			"Whether to increment the set seed daily"
		);
	}
}
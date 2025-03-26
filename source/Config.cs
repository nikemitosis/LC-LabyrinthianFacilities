namespace LabyrinthianFacilities;

using Serialization;
using Util;

using System;
using System.Collections.Generic;

using BepInEx.Configuration;
using BepInEx.Logging;

using Unity.Netcode;

using LogLevel = BepInEx.Logging.LogLevel;

public class Config {
	internal static ConfigFile ConfigFile;
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
	private ConfigEntry<bool> m_SaveGrabbableMapObjects;
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
	private ConfigEntry<float> m_LowerIterationMultiplier;
	private ConfigEntry<float> m_UpperIterationMultiplier;
	
	// Features.EasterEggs
	private ConfigEntry<bool> m_BouncyCruisers;
	private ConfigEntry<bool> m_ForbiddenPassages;
	
	// Debug.Logging
	private ConfigEntry<LogLevel> m_LogLevels;
	private ConfigEntry<bool> m_EnableVerboseGeneration;
	private ConfigEntry<bool> m_EnableVerboseSerialization;
	private ConfigEntry<bool> m_EnableVerboseDeserialization;
	
	// Debug.History
	private ConfigEntry<bool> m_EnableHistory;
	
	// Features.SetSeed
	private ConfigEntry<bool> m_UseSetSeed;
	private ConfigEntry<int > m_Seed;
	private ConfigEntry<bool> m_IncrementSetSeed;
	
	public bool GlobalEnable                 {get; set;}
	
	public bool EnableNoncollectionPenalty   {get; set;}
	public bool ExcludeCruiserScrap          {get; set;}
	public bool ExcludeSurfaceScrap          {get; set;}
	public bool ExcludeHive                  {get; set;}
	
	public bool SaveGrabbableMapObjects      {get; set;}
	public bool SaveEquipment                {get; set;}
	public bool SaveScrap                    {get; set;}
	public bool SaveHives                    {get; set;}
	public bool SaveCruisers                 {get; set;}
	
	public bool SaveHazards                  {get; set;}
	public bool SaveTurrets                  {get; set;}
	public bool SaveLandmines                {get; set;}
	public bool SaveSpikeTraps               {get; set;}
	
	public bool UseCustomGeneration          {get; set;}
	public bool SaveMaps                     {get; set;}
	public float MinimumTileMultiplier       {get; set;}
	public float MaximumTileMultiplier       {get; set;}
	public float LowerIterationMultiplier    {get; set;}
	public float UpperIterationMultiplier    {get; set;}
	
	public bool BouncyCruisers               {get; set;}
	public bool ForbiddenPassages            {get; set;}
	
	public LogLevel LogLevels                {get; set;}
	public bool EnableVerboseGeneration      {get; set;}
	public bool EnableVerboseSerialization   {get; set;}
	public bool EnableVerboseDeserialization {get; set;}
	
	public bool EnableHistory                {get; set;}
	
	public bool UseSetSeed                   {get; set;}
	public int  Seed                         {get; set;}
	public bool IncrementSetSeed             {get; set;}
	
	public Config() {
		if (singleton != null) throw new InvalidOperationException("Singleton violation");
		singleton = this;
		
		var Config = ConfigFile;
		Config.SaveOnConfigSet = false;
		
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
		m_SaveGrabbableMapObjects = Config.Bind(
			section,
			"SaveGrabbableMapObjects",
			true,
			"Save 'GrabbableMapObjects' (basically scrap & equipment)"
		);
		m_SaveEquipment = Config.Bind(
			section,
			"SaveEquipment",
			true,
			"Save Equipment (Requires SaveGrabbableMapObjects)"
		);
		m_SaveScrap = Config.Bind(
			section,
			"SaveScrap",
			true,
			"Save Scrap (Requires SaveGrabbableMapObjects)"
		);
		m_SaveHives = Config.Bind(
			section,
			"SaveHives",
			true,
			"Save Beehives (Requires SaveGrabbableMapObjects)"
		);
		m_SaveCruisers = Config.Bind(
			section,
			"SaveCruisers",
			true,
			"Save Cruisers"
		);
		
		section = "Features.Hazards";
		m_SaveHazards = Config.Bind(
			section,
			"SaveHazards",
			false,
			"Save Hazards (turrets, landmines, etc.)"
		);
		m_SaveTurrets = Config.Bind(
			section,
			"SaveTurrets",
			true,
			"Save Turrets (Requires SaveHazards)"
		);
		m_SaveLandmines = Config.Bind(
			section,
			"SaveLandmines",
			true,
			"Save Landmines (Requires SaveHazards)"
		);
		m_SaveSpikeTraps = Config.Bind(
			section,
			"SaveSpikeTraps",
			true,
			"Save Spike Traps (Requires SaveHazards)"
		);
		
		section = "Features.InteriorGeneration";
		m_UseCustomGeneration = Config.Bind(
			section,
			"UseCustomGeneration",
			true,
			"Use Custom Generation"
		);
		m_SaveMaps = Config.Bind(
			section,
			"SaveMaps",
			true,
			"Save interiors (Requires UseCustomGeneration)"
		);
		m_MinimumTileMultiplier = Config.Bind(
			section,
			"MinimumTileMultiplier",
			2.5f,
			"Multiplier for the minimum amount of tiles. Multiplies by the average amount of main tiles that would be generated by DunGen. (Requires UseCustomGeneration)"
		);
		m_MaximumTileMultiplier = Config.Bind(
			section,
			"MaximumTileMultiplier",
			6f,
			"Multiplier for the maximum amount of tiles. Multiplies by the average amount of main tiles that would be generated by DunGen. (Requires UseCustomGeneration)"
		);
		m_LowerIterationMultiplier = Config.Bind(
			section,
			"LowerIterationMultiplier",
			1.0f,
			"Multiplier for the minimum amount of tiles to generate in a single day. Multiplies by the the result of MinimumTileMultiplier's multiplication. (Requires UseCustomGeneration)"
		);
		m_UpperIterationMultiplier = Config.Bind(
			section,
			"UpperIterationMultiplier",
			1.5f,
			"Multiplier for the maximum amount of tiles to generate in a single day. Multiplies by the the result of *Minimum*TileMultiplier's multiplication. (Requires UseCustomGeneration)"
		);
		
		section = "Features.EasterEggs";
		m_BouncyCruisers = Config.Bind(
			section,
			"BouncyCruisers",
			false,
			$"The magic word is '{Patches.EasterEggDetection.cruiserMagicWord}'"
		);
		m_ForbiddenPassages = Config.Bind(
			section,
			"ForbiddenPassages",
			false,
			"...?"
		);
		
		section = "Debug.Logging";
		m_LogLevels = Config.Bind(
			section,
			"LogLevels",
			LogLevel.All,
			"Which log levels to show in the console"
		);
		m_EnableVerboseGeneration = Config.Bind(
			section,
			"EnableVerboseGeneration",
			false,
			"Enables verbose logging for generation\n"
			+"(Requires LogLevels contains Debug)"
		);
		m_EnableVerboseSerialization = Config.Bind(
			section,
			"EnableVerboseSerialization",
			false,
			"Enables verbose logging for serialization (used for saving maps & sending data to clients)\n"
			+"(Requires LogLevels contains Debug)"
		);
		m_EnableVerboseDeserialization = Config.Bind(
			section,
			"EnableVerboseDeserialization",
			false,
			"Enables verbose logging for deserialization (used for loading maps & receiving data from server)\n"
			+"(Requires LogLevels contains Debug)"
		);
		
		section = "Debug.History";
		m_EnableHistory = Config.Bind(
			section,
			"EnableHistory",
			false,
			"Enables recording of seeds, interiors, and moons each day, for each save. Located in the same place as savedata. "
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
		
		Config.Save();
		Config.SaveOnConfigSet = true;
		
		InitFromConfigFile();
	}
	
	public void InitFromConfigFile() {
		GlobalEnable                 = m_GlobalEnable                .Value;
		EnableNoncollectionPenalty   = m_EnableNoncollectionPenalty  .Value;
		ExcludeCruiserScrap          = m_ExcludeCruiserScrap         .Value;
		ExcludeSurfaceScrap          = m_ExcludeSurfaceScrap         .Value;
		ExcludeHive                  = m_ExcludeHive                 .Value;
		SaveGrabbableMapObjects      = m_SaveGrabbableMapObjects     .Value;
		SaveEquipment                = m_SaveEquipment               .Value;
		SaveScrap                    = m_SaveScrap                   .Value;
		SaveHives                    = m_SaveHives                   .Value;
		SaveCruisers                 = m_SaveCruisers                .Value;
		SaveHazards                  = m_SaveHazards                 .Value;
		SaveTurrets                  = m_SaveTurrets                 .Value;
		SaveLandmines                = m_SaveLandmines               .Value;
		SaveSpikeTraps               = m_SaveSpikeTraps              .Value;
		UseCustomGeneration          = m_UseCustomGeneration         .Value;
		SaveMaps                     = m_SaveMaps                    .Value;
		MinimumTileMultiplier        = m_MinimumTileMultiplier       .Value;
		MaximumTileMultiplier        = m_MaximumTileMultiplier       .Value;
		LowerIterationMultiplier     = m_LowerIterationMultiplier    .Value;
		UpperIterationMultiplier     = m_UpperIterationMultiplier    .Value;
		BouncyCruisers               = m_BouncyCruisers              .Value;
		ForbiddenPassages            = m_ForbiddenPassages           .Value;
		LogLevels                    = m_LogLevels                   .Value;
		EnableVerboseGeneration      = m_EnableVerboseGeneration     .Value;
		EnableVerboseSerialization   = m_EnableVerboseSerialization  .Value;
		EnableVerboseDeserialization = m_EnableVerboseDeserialization.Value;
		EnableHistory                = m_EnableHistory               .Value;
		UseSetSeed                   = m_UseSetSeed                  .Value;
		Seed                         = m_Seed                        .Value;
		IncrementSetSeed             = m_IncrementSetSeed            .Value;
	}
}

public class ConfigNetworkSerializer<T> : Serializer<T> where T : Config {
	/* Format:
	 * bool GlobalEnable
	 * bool EnableNoncollectionPenalty
	 * bool ExcludeCruiserScrap
	 * bool ExcludeSurfaceScrap
	 * bool ExcludeHive
	 * bool SaveGrabbableMapObjects
	 * bool SaveEquipment
	 * bool SaveScrap
	 * bool SaveHives
	 * bool SaveCruisers
	 * bool SaveHazards
	 * bool SaveTurrets
	 * bool SaveLandmines
	 * bool SaveSpikeTraps
	 * bool UseCustomGeneration
	 * bool SaveMaps
	 * bool BouncyCruisers
	 * bool ForbiddenPassages
	 * bool EnableHistory
	 * bool UseSetSeed
	 * bool IncrementSetSeed
	 * 
	 * float MinimumTileMultiplier
	 * float MaximumTileMultiplier
	 * float LowerIterationMultiplier
	 * float UpperIterationMultiplier
	 * int   Seed
	 *     
	 * Note that these config options are *not* sent to clients, because they don't matter to keeping 
	 *  server/client synced
	 * LogLevel (int) LogLevels
	 * bool EnableVerboseGeneration
	 * bool EnableVerboseSerialization
	 * bool EnableVerboseDeserialization
	*/
	public override void Serialize(SerializationContext sc, T tgt) {
		sc.AddBools<bool>(
			[
				tgt.GlobalEnable,
				tgt.EnableNoncollectionPenalty,
				tgt.ExcludeCruiserScrap,
				tgt.ExcludeSurfaceScrap,
				tgt.ExcludeHive,
				tgt.SaveGrabbableMapObjects,
				tgt.SaveEquipment,
				tgt.SaveScrap,
				tgt.SaveHives,
				tgt.SaveCruisers,
				tgt.SaveHazards,
				tgt.SaveTurrets,
				tgt.SaveLandmines,
				tgt.SaveSpikeTraps,
				tgt.UseCustomGeneration,
				tgt.SaveMaps,
				tgt.BouncyCruisers,
				tgt.ForbiddenPassages,
				tgt.EnableHistory,
				tgt.UseSetSeed,
				tgt.IncrementSetSeed
			], 
			(bool b) => b
		);
		
		sc.Add(tgt.MinimumTileMultiplier);
		sc.Add(tgt.MaximumTileMultiplier);
		sc.Add(tgt.LowerIterationMultiplier);
		sc.Add(tgt.UpperIterationMultiplier);
		sc.Add(tgt.Seed);
	}
	
	protected override T Deserialize(T rt, DeserializationContext dc) {
		IEnumerator<bool> bools = dc.ConsumeBools(21).GetEnumerator();
		bools.MoveNext(); rt.GlobalEnable               = bools.Current;
		bools.MoveNext(); rt.EnableNoncollectionPenalty = bools.Current;
		bools.MoveNext(); rt.ExcludeCruiserScrap        = bools.Current;
		bools.MoveNext(); rt.ExcludeSurfaceScrap        = bools.Current;
		bools.MoveNext(); rt.ExcludeHive                = bools.Current;
		bools.MoveNext(); rt.SaveGrabbableMapObjects    = bools.Current;
		bools.MoveNext(); rt.SaveEquipment              = bools.Current;
		bools.MoveNext(); rt.SaveScrap                  = bools.Current;
		bools.MoveNext(); rt.SaveHives                  = bools.Current;
		bools.MoveNext(); rt.SaveCruisers               = bools.Current;
		bools.MoveNext(); rt.SaveHazards                = bools.Current;
		bools.MoveNext(); rt.SaveTurrets                = bools.Current;
		bools.MoveNext(); rt.SaveLandmines              = bools.Current;
		bools.MoveNext(); rt.SaveSpikeTraps             = bools.Current;
		bools.MoveNext(); rt.UseCustomGeneration        = bools.Current;
		bools.MoveNext(); rt.SaveMaps                   = bools.Current;
		bools.MoveNext(); rt.BouncyCruisers             = bools.Current;
		bools.MoveNext(); rt.ForbiddenPassages          = bools.Current;
		bools.MoveNext(); rt.EnableHistory              = bools.Current;
		bools.MoveNext(); rt.UseSetSeed                 = bools.Current;
		bools.MoveNext(); rt.IncrementSetSeed           = bools.Current;
		
		dc.Consume(sizeof(float)).CastInto(out float x); rt.MinimumTileMultiplier    = x;
		dc.Consume(sizeof(float)).CastInto(out       x); rt.MaximumTileMultiplier    = x;
		dc.Consume(sizeof(float)).CastInto(out       x); rt.LowerIterationMultiplier = x;
		dc.Consume(sizeof(float)).CastInto(out       x); rt.UpperIterationMultiplier = x;
		dc.Consume(sizeof(int  )).CastInto(out int   y); rt.Seed = y;
		
		return rt;
	}
	
	public override T Deserialize(DeserializationContext dc) => Deserialize((T)Config.Singleton, dc);
}
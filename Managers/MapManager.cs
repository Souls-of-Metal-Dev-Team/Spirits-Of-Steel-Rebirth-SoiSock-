using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using Sosr;

namespace Sosr;

// No [GlobalClass]: this script is an autoload named MapManager and would clash
// with the global class name.
public partial class MapManager : Node
{
	public static MapManager Instance => GameState.Instance.mapManager;

	public bool DEBUG_MODE = false;

	[Signal]
	public delegate void ProvinceHoveredEventHandler(int provinceId, string countryName);

	[Signal]
	public delegate void CountryClickedEventHandler(string countryName);

	[Signal]
	public delegate void ProvinceOwnershipChangedEventHandler(int pid, string oldOwner, string newOwner);

	// Emitted when a click couldn't be processed (so likely sea or border)
	[Signal]
	public delegate void CloseSidemenuEventHandler();

	// Emitted when the active map view changes (value is a MapView)
	[Signal]
	public delegate void MapViewChangedEventHandler(int view);

	// Emitted when a country is picked in SELECT_COUNTRY mode
	[Signal]
	public delegate void CountrySelectedEventHandler(string countryName);

	// The exact colors you provided
	public static readonly Color SEA_MAIN = Color.FromHtml("#7e8e9e");
	public static readonly Color SEA_RASTER = Color.FromHtml("#697684");
	public static readonly Vector2I[] ALL_DIRECTIONS =
	{
		new Vector2I(1, 0),
		new Vector2I(-1, 0),
		new Vector2I(0, 1),
		new Vector2I(0, -1),
	};

	// Relation of a country to the player, as bitflags.
	// NEUTRAL bit = foreign country. Player and puppets are never foreign, so they lack it.
	public const int RELATION_NEUTRAL_BIT = 0b001;
	public const int RELATION_ALLY_BIT = 0b010;
	public const int RELATION_ENEMY_BIT = 0b100;

	public enum CountryRelation
	{
		PLAYER = 0b000,
		NEUTRAL = 0b001,
		PUPPET = 0b010, // ally bit, no neutral bit
		ALLY = 0b011,
		ENEMY = 0b101,
	}

	public string hoveredCountry = "Sea";

	// --- DATA ---
	public Image idMapImage;
	public Image stateColorImage;
	public ImageTexture stateColorTexture;
	public int maxProvinceId = 0;

	public Godot.Collections.Dictionary ethnicNameToColor = new();
	public Godot.Collections.Dictionary gdpMap = new();

	public Dictionary<string, List<int>> countryToProvinces
	{
		get => ProvinceManager.Instance.CountryToProvinces;
		set => ProvinceManager.Instance.CountryToProvinces = value;
	}
	public Dictionary<string, List<int>> countryToOwnedProvinces
	{
		get => ProvinceManager.Instance.CountryToOwnedProvinces;
		set => ProvinceManager.Instance.CountryToOwnedProvinces = value;
	}
	public Dictionary<string, List<int>> countryToOccupiedProvinces
	{
		get => ProvinceManager.Instance.CountryToOccupiedProvinces;
		set => ProvinceManager.Instance.CountryToOccupiedProvinces = value;
	}
	public Dictionary<string, List<int>> countryToRightfulProvinces
	{
		get => ProvinceManager.Instance.CountryToRightfulProvinces;
		set => ProvinceManager.Instance.CountryToRightfulProvinces = value;
	}
	public Dictionary<string, List<int>> countryToCities
	{
		get => ProvinceManager.Instance.CountryToCities;
		set => ProvinceManager.Instance.CountryToCities = value;
	}
	public Dictionary<int, Province> provinceObjects => Provinces;
	private Dictionary<int, Province> Provinces => ProvinceManager.Instance.Provinces;
	public Godot.Collections.Dictionary buildingTypesToCountryCount = new(); // Stores {BuildingID: {CountryName: nuumber}}

	public Dictionary<string, BiomeData> biomes = new();
	public Dictionary<string, ResourceData> resources = new();

	public int currentHoveredPid = -1;
	public int lastHoveredPid = -1;
	public Color originalColor = Colors.Black;
	public Dictionary<int, Vector2> provinceCenters = new(); // Stores {ID: Vector2(x, y)}
	public Dictionary<int, int> uniqueRegions = new(); // NOTE(soi): eh
	public SoiStar provinceGraph = new SoiStar(); // NOTE(soi): ehh

	// This will look like: {"french_empire": [101, 102, 103], "canada": [1, 2, 5]}
	public Dictionary<string, List<int>> globalClaimsRegistry
	{
		get => ProvinceManager.Instance.GlobalClaimsRegistry;
		set => ProvinceManager.Instance.GlobalClaimsRegistry = value;
	}
	public float worldTension = 0.1f; // Global tension level (0.1 to 1.0)

	public Godot.Collections.Array<Godot.Collections.Dictionary> allCities = new();

	public Label gay = new Label();
	// soiladin time
	public Dictionary<string, List<int>> allowedPids
	{
		get => ProvinceManager.Instance.AllowedPids;
		set => ProvinceManager.Instance.AllowedPids = value;
	}

	public void IncreaseWorldTension(float aAmount)
	{
		worldTension = Mathf.Clamp(worldTension + aAmount, 0.1f, 1.0f);
	}

	public const string CACHE_FOLDER = "res://map_data/";

	// NOTE(soi): these should probably be in like world or smthng
	[Export] public Texture2D regionTexture;
	[Export] public Texture2D cultureTexture;
	[Export] public Texture2D populationTexture;
	[Export] public Texture2D cityTexture;
	[Export] public Texture2D gdpTexture;
	[Export] public Texture2D ethnicityTexture;
	[Export] public Texture2D claimsTexture;

	[Export] public int MAP_WIDTH = 0;
	[Export] public int MAP_HEIGHT = 625;

	// GDScript cannot read C# static constants through an instance.
	public int mapWidth => MAP_WIDTH;
	public int mapHeight => MAP_HEIGHT;

	public Dictionary<string, Texture2D> iconCache = new();

	public void LoadBiomes(Godot.Collections.Array aBiomeData)
	{
		biomes.Clear();
		foreach (var biomeVariant in aBiomeData)
		{
			var biome = biomeVariant.AsGodotDictionary();
			biomes[biome["name"].AsString()] = BiomeData.FromDict(biome.AsState());
		}
	}

	public void LoadResources(Godot.Collections.Array aResourceData)
	{
		resources.Clear();
		foreach (var resourceVariant in aResourceData)
		{
			var resource = resourceVariant.AsGodotDictionary();
			resources[resource["name"].AsString()] = ResourceData.FromDict(resource.AsState());
		}
	}

	// Helper to check both custom and default locations
	public string FindResourceResource(string subPath)
	{
		var fullDefault = "res://assets/icons/Resources/" + subPath;
		if (ResourceLoader.Exists(fullDefault))
			return fullDefault;
		return "";
	}

	public Texture2D GetResourceIcon(string aResourceType)
	{
		var cacheKey = aResourceType;

		// If already cached -> return it
		if (iconCache.TryGetValue(cacheKey, out var cachedIcon))
			return cachedIcon;

		// Cache key needs to include ideology if provided
		if (!resources.ContainsKey(aResourceType))
		{
			var tex = GD.Load<Texture2D>(FindResourceResource("Droplet.svg"));
			iconCache[cacheKey] = tex;
			return tex;
		}

		// 0. Check Redirects
		// 4. Fallback to old flat structure (just in case): {path}/country_flag.png
		var resource = resources[aResourceType];
		var path = FindResourceResource($"{resource?.icon}.svg");

		if (path == "")
			path = FindResourceResource("Droplet.svg");

		if (path != "")
		{
			var tex = GD.Load<Texture2D>(path);
			iconCache[cacheKey] = tex;
			return tex;
		}

		return null;
	}

	public void _ClearInternalData()
	{
		allCities.Clear();
		uniqueRegions.Clear();
		Provinces.Clear();
		countryToProvinces.Clear();
		countryToOwnedProvinces.Clear();
		countryToOccupiedProvinces.Clear();
		allowedPids.Clear();
		provinceCenters.Clear();
		// Clear graph and global registry
		globalClaimsRegistry.Clear();
		FigureManager.Instance.Clear();
		// Reset state variables
		maxProvinceId = 0;
		currentHoveredPid = -1;
		lastHoveredPid = -1;
		hoveredCountry = "Sea";
	}

	public void Initialize(
		Texture2D aMap,
		Godot.Collections.Dictionary aProvinceData,
		Godot.Collections.Array aProgress
	)
	{
		_ClearInternalData();

		MAP_WIDTH = aMap.GetWidth();
		MAP_HEIGHT = aMap.GetHeight();

		idMapImage = Image.CreateEmpty(MAP_WIDTH, MAP_HEIGHT, false, Image.Format.Rgb8);
		var nextId = 2;

		var mapImage = aMap.GetImage();
		if (mapImage.GetFormat() != Image.Format.Rgb8)
			mapImage.Convert(Image.Format.Rgb8);

		var cultureSprite = GetNodeOrNull<Sprite2D>("/root/Main/MapContainer/CultureSprite");
		if (cultureSprite != null)
			cultureSprite.Texture = aMap;

		provinceGraph.NeighborFilterEnabled = true;
		provinceGraph.Clear();

		var inc = 0.1f / (MAP_WIDTH * MAP_HEIGHT);

		// Flat per-province arrays, indexed by pid. Positions are interleaved: [x0, y0, x1, y1, ...].
		// pid 0 and 1 are unused placeholders, so real province IDs index directly.
		var firstPos = new List<int> { 0, 0, 0, 0 };
		var lastPos = new List<int> { 0, 0, 0, 0 };
		var pixelCount = new List<int> { 0, 0 };

		// RGB8 raw bytes: 3 per pixel, row-major. Index = packed RGB, same as old to_rgba32() >> 8.
		var mapData = mapImage.GetData();
		// pid of every pixel scanned so far, indexed by i. Border pixels stay 0.
		// Left / top / two-left / two-up neighbours have already been scanned when we read them.
		var pidBuf = new int[MAP_WIDTH * MAP_HEIGHT];

		for (var i = 0; i < MAP_WIDTH * MAP_HEIGHT; i++)
		{
			var x = i % MAP_WIDTH;
			var y = i / MAP_WIDTH;
			var byteOffset = i * 3;
			var index = (mapData[byteOffset] << 16) | (mapData[byteOffset + 1] << 8) | mapData[byteOffset + 2];

			// Check for Borders/Grid (ID 1)
			if (index == 0)
			{
				// NOTE: int 1 converts to Color.hex(1), so this pixel reads back as pid 0.
				idMapImage.SetPixel(x, y, new Color(1u));
				continue;
			}

			// If this is a new region (Unique Sea Zone or Land Province)
			if (uniqueRegions.TryAdd(index, nextId))
			{

				var provinceDict = aProvinceData.ContainsKey(index.ToString())
					? aProvinceData[index.ToString()].AsGodotDictionary()
					: new Godot.Collections.Dictionary();
				var province = Province.FromDict(provinceDict.AsState());
				province.id = nextId;

				Provinces[nextId] = province;

				// First pixel of a province in scan order. Last starts here and moves on.
				firstPos.Add(x);
				firstPos.Add(y);
				lastPos.Add(x);
				lastPos.Add(y);
				pixelCount.Add(0);

				nextId += 1;
			}

			// Write the unique ID to your id_map_image
			var pid = uniqueRegions[index];
			idMapImage.SetPixel(x, y, new Color((uint)((pid << 8) | 0x000000FF)));

			// NOTE(soi): its soiladin time 2
			if (!provinceGraph.HasPoint(pid))
				provinceGraph.AddPoint(pid, new Vector2(x, y));

			pidBuf[i] = pid;

			// Connect to the left / top pixel. If that pixel is a 1px border (0),
			// look one pixel further across it.
			if (x != 0)
			{
				var leftPid = pidBuf[i - 1];
				if (leftPid == 0 && x >= 2)
					leftPid = pidBuf[i - 2];
				if (leftPid > 1 && leftPid != pid && !provinceGraph.ArePointsConnected(pid, leftPid))
					provinceGraph.ConnectPoints(pid, leftPid);
			}

			if (y != 0)
			{
				var topPid = pidBuf[i - MAP_WIDTH];
				if (topPid == 0 && y >= 2)
					topPid = pidBuf[i - 2 * MAP_WIDTH];
				if (topPid > 1 && topPid != pid && !provinceGraph.ArePointsConnected(pid, topPid))
					provinceGraph.ConnectPoints(pid, topPid);
			}

			var k = pid << 1;
			lastPos[k] = x;
			lastPos[k + 1] = y;
			pixelCount[pid] += 1;
		}

		aProgress[0] = aProgress[0].AsDouble() + inc * MAP_WIDTH * MAP_HEIGHT;
		maxProvinceId = nextId - 1;
		BuildLookupTexture();
		aProgress[0] = aProgress[0].AsDouble() + 0.015;

		_calculate_province_centroids(firstPos, lastPos, pixelCount);
		aProgress[0] = aProgress[0].AsDouble() + 0.01;

		// Pass 2: Load troops now that centroids (positions) are definitely known
		foreach (var iIndex in aProvinceData.Keys)
		{
			var key = iIndex.AsInt32();
			if (uniqueRegions.TryGetValue(key, out var regionPid))
			{
				var pDict = aProvinceData[iIndex].AsGodotDictionary();
				if (pDict.ContainsKey("troops"))
					TroopManager.Instance.LoadTroopsForProvince(regionPid, pDict["troops"].AsGodotArray());
			}
		}

		aProgress[0] = aProgress[0].AsDouble() + 0.025;
		_build_country_to_provinces();
		aProgress[0] = aProgress[0].AsDouble() + 0.025;
		_BuildGlobalRegistry();
		aProgress[0] = aProgress[0].AsDouble() + 0.025;
	}

	public void LoadProvinces(
		CompressedTexture2D regionMap,
		Godot.Collections.Dictionary aProvinceData,
		Godot.Collections.Array aProgress
	)
	{
		Initialize(regionMap, aProvinceData, aProgress);
	}

	public Godot.Collections.Dictionary SaveProvinces()
	{
		var provinces = new Godot.Collections.Dictionary();
		foreach (var index in uniqueRegions.Keys)
		{
			var nextId = uniqueRegions[index];
			var province = Provinces[nextId];
			var troops = TroopManager.Instance.GetSerializedTroopsForProvince(nextId);
			provinces[index.ToString()] = VariantState.ToVariant(province.ToDictWithTroops(((Variant)troops).AsState().List()));
		}
		return provinces;
	}

	public Godot.Collections.Array SaveResourcesData()
	{
		var returnResources = new Godot.Collections.Array();
		foreach (var resourceVariant in resources.Values)
		{
			var resource = resourceVariant;
			returnResources.Add(VariantState.ToVariant(resource.ToDict()));
		}
		return returnResources;
	}

	public Godot.Collections.Array SaveBiomeData()
	{
		var returnBiomes = new Godot.Collections.Array();
		foreach (var biomeVariant in biomes.Values)
		{
			var biome = biomeVariant;
			returnBiomes.Add(VariantState.ToVariant(biome.ToDict()));
		}
		return returnBiomes;
	}

	public Godot.Collections.Dictionary SaveBuildingData()
	{
		var returnBuildings = new Godot.Collections.Dictionary();
		var designs = BuildingManager.Instance.buildingDesigns;
		foreach (var iCountry in designs.Keys)
		{
			var country = iCountry;
			var countryBuildings = new Godot.Collections.Array();
			foreach (var iBuilding in designs[iCountry].Values)
			{
				var template = BuildingTemplate.FromDict(iBuilding.AsGodotDictionary().AsState());
				countryBuildings.Add(VariantState.ToVariant(template.ToDict()));
			}
			returnBuildings[country] = countryBuildings;
		}
		return returnBuildings;
	}

	public void _build_country_to_provinces()
	{
		var result = new Dictionary<string, List<int>>();
		var owned = new Dictionary<string, List<int>>();
		var occupied = new Dictionary<string, List<int>>();
		var rightful = new Dictionary<string, List<int>>();
		var cities = new Dictionary<string, List<int>>();

		void EnsureCountry(string country)
		{
			if (result.ContainsKey(country))
				return;
			result[country] = new List<int>();
			owned[country] = new List<int>();
			occupied[country] = new List<int>();
			rightful[country] = new List<int>();
			cities[country] = new List<int>();
		}

		foreach (var pid in Provinces.Keys)
		{
			var province = Provinces[pid];
			var country = province.country;
			var occupier = province.occupier;

			EnsureCountry(country);
			owned[country].Add(pid);
			if (occupier != "")
			{
				EnsureCountry(occupier);

				result[occupier].Add(pid);
				occupied[occupier].Add(pid);
				rightful[country].Add(pid);
				if (province.city != "")
					cities[occupier].Add(pid);
			}
			else
			{
				result[country].Add(pid);
				if (province.city != "")
					cities[country].Add(pid);
			}
		}

		countryToProvinces = result;
		allowedPids = new Dictionary<string, List<int>>();
		foreach (var pair in result)
			allowedPids[pair.Key] = new List<int>(pair.Value);
		countryToOwnedProvinces = owned;
		countryToOccupiedProvinces = occupied;
		countryToRightfulProvinces = rightful;
		countryToCities = cities;
	}

	public void BuildLookupTexture()
	{
		stateColorImage = Image.CreateEmpty(maxProvinceId + 1, 2, false, Image.Format.Rgba8);

		for (var pid = 0; pid <= maxProvinceId; pid++)
		{
			if (pid <= 1)
			{
				stateColorImage.SetPixel(pid, 0, Colors.Black);
				stateColorImage.SetPixel(pid, 1, Colors.Black);
				continue;
			}

			Provinces.TryGetValue(pid, out var province);

			if (province == null || province.type == 0) // 0 is province.SEA
			{
				stateColorImage.SetPixel(pid, 0, Colors.Black);
				stateColorImage.SetPixel(pid, 1, Colors.Black);
				continue;
			}

			stateColorImage.SetPixel(pid, 0, CountryColor(province.country, Colors.Gray));
			stateColorImage.SetPixel(pid, 1, CountryColor(province.GetFunctionalOwner(), Colors.Gray));
		}

		stateColorTexture = ImageTexture.CreateFromImage(stateColorImage);
	}

	public void SetProvinceColor(int pid, Color primaryColor)
	{
		if (pid <= 1 || pid > maxProvinceId)
			return;

		var province = Provinces[pid];
		UpdateLookup(pid, primaryColor, CountryColor(province.country, Colors.Gray));
	}

	public void SetProvinceColors(int aPid, Color aPrimaryColor, Color aSecondaryColor)
	{
		if (aPid <= 1 || aPid > maxProvinceId)
			return;

		UpdateLookup(aPid, aPrimaryColor, aSecondaryColor);
	}

	public void ResetProvinceColor(int pid)
	{
		if (pid <= 1 || pid > maxProvinceId)
			return;

		var province = Provinces[pid];
		UpdateLookup(pid,
			CountryColor(province.GetFunctionalOwner(), Colors.Gray),
			CountryColor(province.country, Colors.Gray));
	}

	public void SetCountryColor(string countryName, Color customColor = default)
	{
		var newColor = customColor;
		if (newColor == default)
			newColor = Colors.Transparent;
		if (newColor == Colors.Transparent)
			newColor = CountryColor(countryName, Colors.Gray);

		var provinces = GetArray(countryToProvinces, countryName);

		if (provinces.Count == 0)
		{
			GD.Print("Warning: No provinces found for country: ", countryName);
			return;
		}

		foreach (var pidVariant in provinces)
		{
			var pid = pidVariant;
			UpdateLookup(pid, newColor, newColor);

			if (pid == lastHoveredPid)
			{
				originalColor = newColor;
				UpdateLookup(pid,
					newColor + new Color(0.15f, 0.15f, 0.15f, 0),
					newColor + new Color(0.15f, 0.15f, 0.15f, 0));
			}
		}
	}

	public int GetProvinceAtPos(Vector2 pos, Sprite2D mapSprite = null)
	{
		if (idMapImage == null)
			return 0;

		int x;
		int y;

		if (mapSprite != null)
		{
			var local = mapSprite.ToLocal(pos);
			var spriteSize = mapSprite.Texture.GetSize();

			// If sprite is centered, offset the local position to be top-left based
			if (mapSprite.Centered)
				local += spriteSize * 0.5f;

			// --- INFINITE SCROLL MATH ---
			x = (int)local.X % (int)spriteSize.X;
			if (x < 0)
				x += (int)spriteSize.X;
			y = (int)local.Y;
		}
		else
		{
			x = (int)pos.X;
			y = (int)pos.Y;
		}

		// Y is not infinite, so we strictly check bounds
		if (y < 0 || y >= MAP_HEIGHT || x < 0 || x >= MAP_WIDTH)
			return 0;

		return (int)(idMapImage.GetPixel(x, y).ToRgba32() >> 8);
	}

	private string _GetHoverViewer()
	{
		if (InteractionType() == 1) // GameState.InteractionType.SELECT_COUNTRY
			return "";
		return CountryData.NameOf(CountryManager.Instance.playerCountry);
	}

	public void UnhighlightCountry()
	{
		if (hoveredCountry == "Sea")
			return;

		var countryColor = GetCountryDisplayColor(hoveredCountry);
		foreach (var provinceVariant in GetArray(countryToProvinces, hoveredCountry))
		{
			var province = provinceVariant;
			UpdateLookup(
				province,
				GetCountryDisplayColor((Provinces[province]).country),
				countryColor);
		}
		foreach (var provinceVariant in GetArray(countryToRightfulProvinces, hoveredCountry))
		{
			var province = provinceVariant;
			UpdateLookup(
				province,
				countryColor,
				GetCountryDisplayColor((Provinces[province]).GetFunctionalOwner()));
		}
		hoveredCountry = "Sea";
	}

	public void HighlightCountry(int aPid, Color aHighlightColor)
	{
		var newHoveredCountry = (Provinces[aPid]).GetFunctionalOwner();

		foreach (var provinceVariant in GetArray(countryToProvinces, newHoveredCountry))
		{
			var province = provinceVariant;
			var provinceObject = Provinces[province];
			UpdateLookup(
				province,
				provinceObject.occupier == newHoveredCountry
					? GetCountryDisplayColor(provinceObject.country)
					: aHighlightColor,
				aHighlightColor);
		}
		foreach (var provinceVariant in GetArray(countryToRightfulProvinces, newHoveredCountry))
		{
			var province = provinceVariant;
			var provinceObject = Provinces[province];
			UpdateLookup(
				province,
				aHighlightColor,
				GetCountryDisplayColor(provinceObject.GetFunctionalOwner()));
		}
		hoveredCountry = newHoveredCountry;
	}

	public void HoverCountry(int aPid, Color aHighlightColor)
	{
		// Still inside the same country, so the highlight is already correct.
		if (Provinces.TryGetValue(aPid, out var hoveredProvince)
			&& hoveredProvince.GetFunctionalOwner() == hoveredCountry)
		{
			lastHoveredPid = aPid;
			return;
		}

		ResetHover();

		Provinces.TryGetValue(aPid, out var province);
		if (aHighlightColor == Colors.Transparent || province == null || province.country == "Sea")
		{
			Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
			EmitSignal(SignalName.ProvinceHovered, -1, "");
			return;
		}

		if (currentView != MapView.COUNTRIES)
			return;

		HighlightCountry(aPid, aHighlightColor);
		lastHoveredPid = aPid;
		Input.SetDefaultCursorShape(Input.CursorShape.PointingHand);
		EmitSignal(SignalName.ProvinceHovered, aPid, _GetHoverViewer());
	}

	public void HoverProvince(int aPid, Color aHighlightColor)
	{
		ResetHover();

		if (aPid <= 1)
		{
			Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
			EmitSignal(SignalName.ProvinceHovered, -1, "");
			SwitchTooltip(-1);
			return;
		}

		switch (currentView)
		{
			case MapView.COUNTRIES:
				SwitchTooltip(1);
				break;
			case MapView.RESOURCES:
				SwitchTooltip(0);
				break;
			default:
				SwitchTooltip(-1);
				break;
		}

		if (aHighlightColor != Colors.Transparent)
		{
			originalColor = stateColorImage.GetPixel(aPid, 0);
			UpdateLookup(aPid, aHighlightColor, aHighlightColor);
			lastHoveredPid = aPid;
			Input.SetDefaultCursorShape(Input.CursorShape.PointingHand);
			EmitSignal(SignalName.ProvinceHovered, aPid, _GetHoverViewer());
		}
	}

	public void ClearHover()
	{
		SwitchTooltip(-1);
		ResetHover();
	}

	public void ResetHover()
	{
		if (InteractionType() == 1) // SELECT_COUNTRY
			UnhighlightCountry();
		else if (lastHoveredPid > 1)
			UpdateLookup(lastHoveredPid, originalColor, originalColor);
		lastHoveredPid = -1;
	}

	public Color GetContextualHighlight(int pid)
	{
		if (pid <= 1)
			return Colors.Transparent;

		if (InteractionType() == 1) // SELECT_COUNTRY
			return Colors.AntiqueWhite;

		var playerName = CountryData.NameOf(CountryManager.Instance.playerCountry);
		var province = Provinces[pid];
		var isPlayerOwned = province.GetFunctionalOwner() == playerName;

		if (!isPlayerOwned)
			return Colors.Transparent;

		if (IndustryBuilding() == 2) // PORT
		{
			var coastalProvinces = GetProvincesNearSea(playerName);
			var hasPort = false;
			foreach (var building in province.buildings)
			{
				if (building.type == "Port")
				{
					hasPort = true;
					break;
				}
			}
			if (Array.IndexOf(coastalProvinces, pid) >= 0 && hasPort)
				return Colors.Cyan;
			return Colors.Transparent;
		}
		if (InteractionType() == 3) // DEPLOY_PROVINCE
		{
			if (province.city.Length > 0)
				return Colors.Cyan.Lightened(0.3f);
			return Colors.Transparent; // Don't highlight non-city provinces during deploy
		}
		if (IndustryBuilding() != 0) // DEFAULT
			return stateColorImage.GetPixel(pid, 0).Lightened(0.2f).Blend(Colors.GreenYellow);

		return Colors.Transparent;
	}

	// Relation of a_country1 as seen from a_country2. Returns CountryRelation bitflags.
	public int GetRelativeCountryRelation(GodotObject aCountry1, GodotObject aCountry2)
	{
		if (aCountry1.Get("countryName").AsString() == aCountry2.Get("countryName").AsString())
			return (int)CountryRelation.PLAYER;
		if (aCountry2.Get("puppets").AsGodotArray().Contains(aCountry1.Get("countryName").AsString()))
			return (int)CountryRelation.PUPPET; // never neutral

		var relation = RELATION_NEUTRAL_BIT;
		if (FactionManager.Instance.InFaction(aCountry2, aCountry1))
			relation |= RELATION_ALLY_BIT;
		if (WarManager.Instance.IsAtWar(aCountry1, aCountry2))
			relation |= RELATION_ENEMY_BIT;
		return relation;
	}

	public bool IsLandProvince(int pid)
	{
		var province = Provinces[pid];
		return pid > 1 && province.type != 0; // 0 is SEA
	}

	public void ClickSea()
	{
		if (IndustryBuilding() != 0) // DEFAULT
		{
			GameState.Instance.ResetIndustryBuilding();
			ShowCountriesMap();
		}
		else
		{
			EmitSignal(SignalName.CloseSidemenu);
		}
	}

	public void SelectCountryAt(int pid)
	{
		var province = Provinces[pid];
		EmitSignal(SignalName.CountrySelected, province.GetFunctionalOwner());
		ShowCountriesMap();
	}

	public void DeployAt(int pid)
	{
		var playerCountryName = CountryData.NameOf(CountryManager.Instance.playerCountry);
		var province = Provinces[pid];
		var isPlayerOwned = province.GetFunctionalOwner() == playerCountryName;
		_SetDeploymentPID(isPlayerOwned, pid, playerCountryName);
	}

	public void BuildAt(int pid)
	{
		if (BuildingManager.Instance.IsProvinceBuilding(pid))
			return;

		var province = Provinces[pid];
		var ownerName = province.GetFunctionalOwner();
		var countries = CountryManager.Instance.countries;
		var relation = GetRelativeCountryRelation(
			countries.TryGetValue(ownerName, out var ownerValue) ? ownerValue.AsGodotObject() : null,
			CountryManager.Instance.playerCountry);
		// Only player and puppets (no neutral bit) can be built in.
		var countryName = (relation & RELATION_NEUTRAL_BIT) == 0 ? ownerName : "";
		if (countryName != "" && _ProvinceBuildIndustry(pid, countryName, IndustryBuilding()))
		{
			_cleanup_interaction_state();
			ShowIndustryCountry(countryName);
			EmitSignal(SignalName.CountryClicked, countryName);
		}
		else
		{
			GD.Print("Action Failed: Cannot build in foreign territory.");
			GameState.Instance.ResetIndustryBuilding();
			ShowCountriesMap();
		}
	}

	// move this to CountryManager?
	public void _SetDeploymentPID(
		bool aIsPlayerOwned,
		int aPid,
		string aPlayerName
	)
	{
		var province = Provinces[aPid];
		if (aIsPlayerOwned && province.city != "")
		{
			EmitSignal(SignalName.CountryClicked, aPlayerName);
			CountryManager.Instance.playerCountry.Set("deployPid", aPid);
			GameState.Instance.interactionType = 0; // DEFAULT
			_cleanup_interaction_state();
		}
	}

	public bool _ProvinceBuildIndustry(int pid, string aCountryName, int type) =>
		BuildingManager.Instance.PlaceBuilding(pid, aCountryName, type);

	public void _cleanup_interaction_state()
	{
		Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
		if (lastHoveredPid > 1)
		{
			UpdateLookup(lastHoveredPid, originalColor, originalColor);
			lastHoveredPid = -1;
		}
	}

	// To probe around and still register a click if we hit province/country border
	public int GetProvinceWithRadius(Vector2 center, Sprite2D mapSprite, int radius)
	{
		var offsets = new Vector2[]
		{
			new Vector2(0, 0),
			new Vector2(radius, 0),
			new Vector2(-radius, 0),
			new Vector2(0, radius),
			new Vector2(0, -radius),
			new Vector2(radius, radius),
			new Vector2(radius, -radius),
			new Vector2(-radius, radius),
			new Vector2(-radius, -radius),
		};

		foreach (var off in offsets)
		{
			var pid = GetProvinceAtPos(center + off, mapSprite);
			if (pid > 1)
				return pid;
		}

		return -1;
	}

	public void UpdateLookup(int pid, Color colorMain, Color colorSecondary)
	{
		stateColorImage.SetPixel(pid, 0, colorMain);
		stateColorImage.SetPixel(pid, 1, colorSecondary);
		stateColorTexture.Update(stateColorImage);
	}

	// Center = midpoint of first and last pixel seen in scan order (top-left-most, bottom-right-most).
	private void _calculate_province_centroids(
		List<int> aFirstPos,
		List<int> aLastPos,
		List<int> aPixelCount
	)
	{
		for (var pid = 2; pid < aPixelCount.Count; pid++)
		{
			if (aPixelCount[pid] <= 0)
				continue;

			var k = pid * 2;
			var center = new Vector2(
				(aFirstPos[k] + aLastPos[k]) * 0.5f,
				(aFirstPos[k + 1] + aLastPos[k + 1]) * 0.5f);

			provinceCenters[pid] = center;
			// Graph point starts at the province's first scanned pixel. Move it to the center
			// so SoiStar path costs use real province distances.
			if (provinceGraph.HasPoint(pid))
				provinceGraph.SetPointPosition(pid, center);
			if (Provinces.TryGetValue(pid, out var province))
				province.center = center.ToCore();
		}

		GD.Print($"MapManager: Centroids calculated for {provinceCenters.Count} provinces.");
	}

	// Faster direct pid fetch
	public int _get_pid_fast(int x, int y)
	{
		return (int)(idMapImage.GetPixel(x, y).ToRgba32() >> 8);
	}

	// --- Pathfinding (delegated to SoiStar) ---//

	public Dictionary<Vector2I, long[]> pathCache = new();

	public long[] FindPath(int startPid, int endPid, Godot.Collections.Array allowedCountries = null)
	{
		if (startPid == endPid)
			return new long[] { startPid };

		allowedCountries ??= new Godot.Collections.Array();
		var useCache = allowedCountries.Count == 0;
		var cacheKey = new Vector2I(startPid, endPid);

		if (useCache && pathCache.TryGetValue(cacheKey, out var cachedPath))
			return (long[])cachedPath.Clone();

		// Set context for SoiStar filtering
		provinceGraph.contextAllowedCountries.Clear();
		foreach (var country in allowedCountries)
			provinceGraph.contextAllowedCountries.Add(country.AsString());
		var pathResult = provinceGraph.GetIdPath(startPid, endPid);
		provinceGraph.contextAllowedCountries.Clear();

		var pathList = new List<long>();
		foreach (var id in pathResult)
			pathList.Add(id);
		var path = pathList.ToArray();

		if (useCache && path.Length != 0)
			pathCache[cacheKey] = path;

		return path;
	}

	private Gradient _population_gradient;
	private Gradient _gdp_gradient;

	private Gradient _get_population_gradient()
	{
		if (_population_gradient == null)
		{
			_population_gradient = new Gradient();
			_population_gradient.Offsets = new[] { 0.0f, 0.5f, 1.0f };
			_population_gradient.Colors = new[] { Colors.DarkCyan, Colors.Yellow, Colors.Red };
		}
		return _population_gradient;
	}

	private Gradient _get_gdp_gradient()
	{
		if (_gdp_gradient == null)
		{
			_gdp_gradient = new Gradient();
			_gdp_gradient.Offsets = new[] { 0.0f, 0.5f, 1.0f };
			_gdp_gradient.Colors = new[] { new Color(0.6f, 0.1f, 0.1f), Colors.AliceBlue, new Color(0.0f, 0.4f, 1.0f) };
		}
		return _gdp_gradient;
	}

	private Color _get_heatmap_color(int pop, float maxPop)
	{
		if (pop <= 0)
			return new Color(0.1f, 0.1f, 0.15f, 1.0f);
		var intensity = Mathf.Clamp(pop / maxPop, 0.0f, 1.0f);
		return _get_population_gradient().Sample(intensity);
	}

	// soilad aneurysm time
	public void _ShowFactionMap()
	{
		var factionColor = Colors.Gray;
		if (Provinces.Count == 0)
			return;

		var currentMaxGdp = 1.0f;
		foreach (var provinceVariant in Provinces.Values)
		{
			var province = provinceVariant;
			if (province.gdp > currentMaxGdp)
				currentMaxGdp = province.gdp;
		}

		var factions = FactionManager.Instance.factions;
		foreach (var pid in Provinces.Keys)
		{
			if (pid <= 1)
				continue;

			var province = Provinces[pid];
			var total = 0;
			factionColor = Colors.Gray;
			foreach (var factionKey in factions.Keys)
			{
				var faction = (FactionData)factions[factionKey];
				if (faction == null)
					continue;
				foreach (var member in faction.members)
				{
					if (member != null && member.polity == province.country)
					{
						total += 1;
						factionColor = faction.color.ToColor();
						break;
					}
				}
			}
			stateColorImage.SetPixel(pid, 0, factionColor / total);
			stateColorImage.SetPixel(pid, 1, factionColor / total);
		}

		stateColorTexture.Update(stateColorImage);
		currentView = MapView.FACTION;
		EmitSignal(SignalName.MapViewChanged, (int)currentView);
		GD.Print("MapManager: Faction View Updated");
	}

	private void _ShowPopulationMap()
	{
		if (Provinces.Count == 0)
			return;
		var currentMaxPop = 1.0f;
		foreach (var provinceVariant in Provinces.Values)
		{
			var province = provinceVariant;
			if (province.GetPopulation() > currentMaxPop)
				currentMaxPop = province.GetPopulation();
		}

		foreach (var pid in Provinces.Keys)
		{
			if (pid <= 1)
				continue;
			var province = Provinces[pid];
			var color = _get_heatmap_color(province.GetPopulation(), currentMaxPop);
			stateColorImage.SetPixel(pid, 0, color);
			stateColorImage.SetPixel(pid, 1, color);
		}

		stateColorTexture.Update(stateColorImage);
		GD.Print("MapManager: Population View Updated. Max Pop found: ", currentMaxPop);
	}

	private void _ShowInfrastructureMap()
	{
		if (Provinces.Count == 0)
			return;

		var player = CountryManager.Instance.playerCountry;
		var allowed = player == null ? new Godot.Collections.Array() : player.Call("GetAllAllowedCountries").AsGodotArray();
		var playerName = CountryData.NameOf(player);
		var countries = CountryManager.Instance.countries;

		foreach (var pid in Provinces.Keys)
		{
			var province = Provinces[pid];
			if (pid <= 1 || province.country == "Sea")
				continue;
			if (InteractionType() != 1 && playerName != province.country && !allowed.Contains(province.country))
			{
				stateColorImage.SetPixel(pid, 0, CountryColor(province.country, Colors.Black));
			}
			else
			{
				var infra = province.infrastructure;
				var countryColor = countries.TryGetValue(province.country, out var colorValue)
					? colorValue.AsGodotObject().Get("countryColor").AsColor()
					: Colors.White;
				if (infra == 0)
					stateColorImage.SetPixel(pid, 0, countryColor * 0.5f + Colors.White * 0.5f);
				else if (infra == province.maxInfrastructure)
					stateColorImage.SetPixel(pid, 0, countryColor * 0.5f + Colors.Blue * 0.5f);
				else
					stateColorImage.SetPixel(pid, 0,
						countryColor * 0.5f + Colors.Yellow * 0.5f * ((float)infra / province.maxInfrastructure));
			}
		}

		stateColorTexture.Update(stateColorImage);
		GD.Print("MapManager: Infrastructure View Updated.");
	}

	private void _ShowEthnicMap()
	{
		if (Provinces.Count == 0)
			return;

		foreach (var pid in Provinces.Keys)
		{
			if (pid <= 1)
				continue;

			var province = Provinces[pid];
			if (province.populations.Count <= 0)
				continue;
			var ethName = province.populations[0].ethnicity; // Assuming this is the String name (e.g., "Igbo")

			// Default to black or transparent if ethnicity not found
			var displayColor = Colors.Gray;

			if (ethnicNameToColor.TryGetValue(ethName, out var ethnicColor))
				displayColor = ethnicColor.AsColor();

			// Update the lookup texture
			stateColorImage.SetPixel(pid, 0, displayColor);
			stateColorImage.SetPixel(pid, 1, displayColor);
		}

		stateColorTexture.Update(stateColorImage);
		GD.Print("MapManager: Ethnic View Updated.");
	}

	private Color _get_gdp_heatmap_color(int gdp, float maxGdp)
	{
		if (gdp <= 0)
			return new Color(0.1f, 0.1f, 0.1f);
		var intensity = Mathf.Clamp(Mathf.Sqrt(gdp) / Mathf.Sqrt(maxGdp), 0.0f, 1.0f);
		return _get_gdp_gradient().Sample(intensity);
	}

	private void _ShowGDPMap()
	{
		if (Provinces.Count == 0)
			return;

		var currentMaxGdp = 1.0f;
		foreach (var provinceVariant in Provinces.Values)
		{
			var province = provinceVariant;
			if (province.gdp > currentMaxGdp)
				currentMaxGdp = province.gdp;
		}

		foreach (var pid in Provinces.Keys)
		{
			if (pid <= 1)
				continue;

			var province = Provinces[pid];
			var color = _get_gdp_heatmap_color(province.gdp, currentMaxGdp);
			stateColorImage.SetPixel(pid, 0, color);
			stateColorImage.SetPixel(pid, 1, color);
		}

		stateColorTexture.Update(stateColorImage);
		GD.Print("MapManager: GDP View Updated. Max GDP: ", currentMaxGdp);
	}

	public Color GetCountryDisplayColor(string aCountryName)
	{
		var countries = CountryManager.Instance.countries;
		if (!countries.TryGetValue(aCountryName, out var countryValue) || countryValue.AsGodotObject() == null)
			return Colors.Gray;
		var countryData = countryValue.AsGodotObject();
		var owner = countryData.Get("owner").AsString();
		if (!countries.TryGetValue(owner, out var ownerValue) || ownerValue.AsGodotObject() == null)
			return countryData.Get("countryColor").AsColor();
		var ownerData = ownerValue.AsGodotObject();
		return (countryData.Get("countryColor").AsColor() + 3 * ownerData.Get("countryColor").AsColor()) * 0.25f;
	}

	public void ShowCountriesMap()
	{
		stateColorImage.SetPixel(0, 0, SEA_MAIN); // ID 0: Sea
		stateColorImage.SetPixel(1, 0, Colors.Black); // ID 1: Borders/Grid

		foreach (var pid in Provinces.Keys)
		{
			if (pid <= 1)
				continue;

			var province = Provinces[pid];
			stateColorImage.SetPixel(pid, 0, GetCountryDisplayColor(province.country));
			stateColorImage.SetPixel(pid, 1, GetCountryDisplayColor(province.GetFunctionalOwner()));
		}

		stateColorTexture.Update(stateColorImage);
	}

	private void _ShowResourcesMap()
	{
		stateColorImage.SetPixel(0, 0, SEA_MAIN); // ID 0: Sea
		stateColorImage.SetPixel(1, 0, Colors.Black); // ID 1: Borders/Grid

		foreach (var pid in Provinces.Keys)
		{
			if (pid <= 1)
				continue;

			var province = Provinces[pid];
			stateColorImage.SetPixel(pid, 0, GetCountryDisplayColor(province.country));
			stateColorImage.SetPixel(pid, 1, GetCountryDisplayColor(province.GetFunctionalOwner()));
		}

		stateColorTexture.Update(stateColorImage);
	}

	public void _ShowBiomesMap()
	{
		stateColorImage.SetPixel(0, 0, SEA_MAIN); // ID 0: Sea
		stateColorImage.SetPixel(1, 0, Colors.Black); // ID 1: Borders/Grid

		foreach (var pid in Provinces.Keys)
		{
			if (pid <= 1)
				continue;

			var province = Provinces[pid];
			var biomeColor = biomes.TryGetValue(province.biome, out var biomeValue)
				? biomeValue.color.ToColor()
				: Colors.AntiqueWhite;

			stateColorImage.SetPixel(pid, 0, biomeColor);
			stateColorImage.SetPixel(pid, 1, biomeColor);
		}

		stateColorTexture.Update(stateColorImage);
	}

	public void ProvinceUpdated()
	{
		if (IndustryBuilding() != 0) // GDScript: if GameState.industry_building
			ShowIndustryCountry(CountryData.NameOf(CountryManager.Instance.playerCountry));
	}

	public void ShowIndustryCountry(string countryName)
	{
		if (!countryToProvinces.ContainsKey(countryName))
		{
			GD.PushWarning("MapManager: Country " + countryName + " not found.");
			return;
		}

		var provincesNearSea = GetProvincesNearSea(countryName);

		foreach (var pidVariant in GetArray(countryToProvinces, countryName))
		{
			var pid = pidVariant;
			var province = Provinces[pid];
			var color = Colors.White; // Default color

			// TODO(sockmit2007): This whole section has to be reworked.
			if (province.city.Length > 0)
				color = Colors.Yellow;
			//elif province.factory == province.FACTORY_BUILT:
			//	color = Color.GREEN
			else if (BuildingManager.Instance.IsProvinceBuilding(pid))
				color = Colors.Orange; // Show progress
			//elif province.port == province.PORT_BUILT:
			//	color = Color.BLUE
			//elif province.port == province.PORT_BUILDING:
			//	color = Color.CYAN # Show progress
			else if (Array.IndexOf(provincesNearSea, pid) >= 0)
				color = Colors.LightSkyBlue;

			stateColorImage.SetPixel(pid, 0, color);
			stateColorImage.SetPixel(pid, 1, color);
		}

		stateColorTexture.Update(stateColorImage);
	}

	public void TransferOwnership(int pid, string newOwnerName)
	{
		var province = Provinces[pid];
		var oldOwnerName = province.country;
		var oldControllerName = province.GetFunctionalOwner();
		var countries = CountryManager.Instance.countries;

		if (BuildingManager.Instance.constructionQueue.TryGetValue(pid, out var project))
			project.AsGodotDictionary()["country"] = CountryManager.Instance.GetCountry(newOwnerName);

		if (Provinces.ContainsKey(pid))
		{
			province.country = newOwnerName;
			if (oldOwnerName == oldControllerName && oldOwnerName != "Sea")
			{
				countries.TryGetValue(oldControllerName, out var oldControllerValue);
				var oldCountry = oldControllerValue.AsGodotObject();
				if (oldCountry != null)
				{
					oldCountry.Set("totalPopulation", oldCountry.Get("totalPopulation").AsInt32() - province.GetPopulation());
					oldCountry.Set("factoriesAmount", oldCountry.Get("factoriesAmount").AsInt32() - province.GetFactories());
				}
			}
			if (province.city != "" && countryToCities.TryGetValue(oldControllerName, out var oldCities))
				oldCities.Remove(pid);
			if (province.city != "")
				GetOrAddArray(countryToCities, newOwnerName).Add(pid);
			GetOrAddArray(allowedPids, newOwnerName).Add(pid);
			var newCountry = CountryManager.Instance.GetCountry(newOwnerName);
			newCountry.Set("totalPopulation", newCountry.Get("totalPopulation").AsInt32() + province.GetPopulation());
			newCountry.Set("factoriesAmount", newCountry.Get("factoriesAmount").AsInt32() + province.GetFactories());
		}
		else
		{
			GD.PushError("MapManager: Attempted to transfer ownership of non-existent PID: ", pid);
			return;
		}

		if (countryToOccupiedProvinces.TryGetValue(province.occupier, out var occupiedList))
			occupiedList.Remove(pid);

		if (countryToOwnedProvinces.TryGetValue(oldOwnerName, out var ownedList))
			ownedList.Remove(pid);

		if (countryToRightfulProvinces.TryGetValue(oldOwnerName, out var rightfulList))
			rightfulList.Remove(pid);

		if (countryToProvinces.TryGetValue(oldControllerName, out var controllerList))
			controllerList.Remove(pid);

		if (!countryToProvinces.ContainsKey(newOwnerName))
			countryToProvinces[newOwnerName] = new List<int>();

		if (!countryToOwnedProvinces.ContainsKey(newOwnerName))
			countryToOwnedProvinces[newOwnerName] = new List<int>();

		if (!countryToProvinces[newOwnerName].Contains(pid))
			countryToProvinces[newOwnerName].Add(pid);

		if (!countryToOwnedProvinces[newOwnerName].Contains(pid))
			countryToOwnedProvinces[newOwnerName].Add(pid);

		province.occupier = "";

		UpdateLookup(
			pid,
			CountryColor(newOwnerName, Colors.Gray),
			CountryColor(newOwnerName, Colors.Gray));
		EmitSignal(SignalName.ProvinceOwnershipChanged, pid, oldOwnerName, newOwnerName);
	}

	public void OccupyProvince(int pid, string newOwnerName)
	{
		var province = Provinces[pid];
		var oldOwnerName = province.country;
		var oldControllerName = province.GetFunctionalOwner();
		var countries = CountryManager.Instance.countries;

		if (Provinces.ContainsKey(pid))
		{
			province.occupier = newOwnerName;
			if (province.city != "" && countryToCities.TryGetValue(oldControllerName, out var oldCities))
				oldCities.Remove(pid);
		}
		else
		{
			GD.PushError("MapManager: Attempted to transfer ownership of non-existent PID: ", pid);
			return;
		}

		if (countryToProvinces.TryGetValue(oldControllerName, out var controllerList))
			controllerList.Remove(pid);

		if (countryToOccupiedProvinces.TryGetValue(oldControllerName, out var occupiedOld))
			occupiedOld.Remove(pid);

		if (!countryToProvinces.ContainsKey(newOwnerName))
			countryToProvinces[newOwnerName] = new List<int>();

		if (!countryToProvinces[newOwnerName].Contains(pid))
			countryToProvinces[newOwnerName].Add(pid);

		if (province.city != "")
			GetOrAddArray(countryToCities, newOwnerName).Add(pid);

		if (province.occupier == oldOwnerName)
		{
			province.occupier = "";
			var newCountry = CountryManager.Instance.GetCountry(newOwnerName);
			newCountry.Set("totalPopulation", newCountry.Get("totalPopulation").AsInt32() + province.GetPopulation());
			newCountry.Set("factoriesAmount", newCountry.Get("factoriesAmount").AsInt32() + province.GetFactories());

			// NOTE(soi): preserved from original: erase(pid), not erase(old_owner_name)
		}
		else
		{
			if (oldControllerName == oldOwnerName && oldControllerName != "Sea")
			{
				var oldCountry = CountryManager.Instance.GetCountry(oldOwnerName);
				oldCountry.Set("totalPopulation", oldCountry.Get("totalPopulation").AsInt32() - province.GetPopulation());
				oldCountry.Set("factoriesAmount", oldCountry.Get("factoriesAmount").AsInt32() - province.GetFactories());
			}

			if (!countryToRightfulProvinces.ContainsKey(oldOwnerName))
				countryToRightfulProvinces[oldOwnerName] = new List<int>();

			if (!countryToRightfulProvinces[oldOwnerName].Contains(pid))
				countryToRightfulProvinces[oldOwnerName].Add(pid);

			if (!countryToOccupiedProvinces.ContainsKey(newOwnerName))
				countryToOccupiedProvinces[newOwnerName] = new List<int>();

			if (!countryToOccupiedProvinces[newOwnerName].Contains(pid))
				countryToOccupiedProvinces[newOwnerName].Add(pid);
		}

		UpdateLookup(pid,
			CountryColor(province.country, Colors.Gray),
			CountryColor(province.GetFunctionalOwner(), Colors.Gray));
		EmitSignal(SignalName.ProvinceOwnershipChanged, pid, oldControllerName, newOwnerName);
	}

	public void DeoccupyProvince(int pid)
	{
		var province = Provinces[pid];
		var oldController = province.GetFunctionalOwner();

		if (province.occupier != "")
		{
			if (province.city != "" && countryToCities.TryGetValue(province.occupier, out var occupierCities))
				occupierCities.Remove(pid);
			if (province.country != "Sea")
			{
				var country = CountryManager.Instance.GetCountry(province.country);
				country.Set("totalPopulation", country.Get("totalPopulation").AsInt32() + province.GetPopulation());
				country.Set("factoriesAmount", country.Get("factoriesAmount").AsInt32() + province.GetFactories());
				if (province.city != "")
					GetOrAddArray(countryToCities, province.country).Add(pid);
			}

			if (countryToProvinces.TryGetValue(province.occupier, out var occupierProvinces))
				occupierProvinces.Remove(pid);

			if (countryToOccupiedProvinces.TryGetValue(province.occupier, out var occupierOccupied))
				occupierOccupied.Remove(pid);

			// NOTE(soi): preserved from original: erase(pid), not erase(province.country)

			if (!countryToProvinces.ContainsKey(province.country))
				countryToProvinces[province.country] = new List<int>();

			if (!countryToProvinces[province.country].Contains(pid))
				countryToProvinces[province.country].Add(pid);
		}

		province.occupier = "";

		UpdateLookup(pid,
			CountryColor(province.country, Colors.Gray),
			CountryColor(province.GetFunctionalOwner(), Colors.Gray));
		EmitSignal(SignalName.ProvinceOwnershipChanged, pid, oldController, province.country);
	}

	public int[] GetProvincesNearSea(string countryName)
	{
		var provincesNearSea = new List<int>();

		foreach (var pidVariant in GetArray(countryToProvinces, countryName))
		{
			var pid = pidVariant;
			foreach (var neighborIdVariant in provinceGraph.GetPointConnections(pid))
			{
				var neighborId = (int)neighborIdVariant;
				Provinces.TryGetValue(neighborId, out var neighborProvince);

				if (neighborProvince != null && neighborProvince.type == 0) // Assuming 0 is SEA
				{
					provincesNearSea.Add(pid);
					break;
				}
			}
		}

		return provincesNearSea.ToArray();
	}

	public Godot.Collections.Array<Godot.Collections.Dictionary> GetAllReleasables(string myCountry)
	{
		var releasables = new Godot.Collections.Array<Godot.Collections.Dictionary>();

		// 1. Get a list of all province IDs I currently own
		var myProvinces = new List<int>();
		foreach (var objVariant in Provinces.Values)
		{
			var obj = objVariant;
			// Using 'country' as per your Province resource
			if (obj.country == myCountry)
				myProvinces.Add(obj.id);
		}

		// 2. Check every country in the registry
		foreach (var potentialCountry in globalClaimsRegistry.Keys)
		{
			if (potentialCountry == myCountry)
				continue;

			var requiredProvinces = globalClaimsRegistry[potentialCountry];

			var ownedProvinces = new List<int>();

			// 3. Verify I own every province they claim
			foreach (var pIdVariant in requiredProvinces)
			{
				var pId = pIdVariant;
				if (myProvinces.Contains(pId))
					ownedProvinces.Add(pId);
			}

			// no province and country exitsts -> don care
			// no province and country not exitsts -> don care
			// province and country exitsts -> return
			// province and country not exitsts -> release
			if (ownedProvinces.Count == 0)
				continue;

			releasables.Add(new Godot.Collections.Dictionary
			{
				{ "country", potentialCountry },
				{ "owned_provinces", ownedProvinces.ToArray() },
			});
		}

		return releasables;
	}

	public bool _country_exists_on_map(string cName)
	{
		foreach (var objVariant in Provinces.Values)
		{
			var obj = objVariant;
			if (obj.country == cName)
				return true;
		}
		return false;
	}

	public void ReleaseCountry(string aCountryName)
	{
		CountryManager.Instance.AddCountry(new Godot.Collections.Dictionary { { "name", aCountryName } });

		foreach (var objVariant in Provinces.Values)
		{
			var obj = objVariant;
			if (Array.IndexOf(obj.claims, aCountryName) >= 0)
			{
				foreach (var troopVariant in ProvinceTroops(obj.id))
				{
					if (troopVariant is TroopData data)
						TroopManager.Instance.RemoveTroop(data);
				}

				TransferOwnership(obj.id, aCountryName);
			}
		}

		CountryManager.Instance.CleanupEmptyCountries();
		ShowCountriesMap();
	}

	public void ReleasePuppet(string aPuppeter, string aPuppetee)
	{
		CountryManager.Instance.AddCountry(new Godot.Collections.Dictionary { { "name", aPuppetee } });

		foreach (var objVariant in Provinces.Values)
		{
			var obj = objVariant;
			if (obj.claims.Length == 1 && obj.claims[0] == aPuppetee)
				TransferOwnership(obj.id, aPuppetee);
		}

		var countries = CountryManager.Instance.countries;
		CountryManager.Instance.MakePuppet(
			countries.ContainsKey(aPuppeter) ? countries[aPuppeter].AsGodotObject() : null,
			countries.ContainsKey(aPuppetee) ? countries[aPuppetee].AsGodotObject() : null);
		CountryManager.Instance.CleanupEmptyCountries();
		ShowCountriesMap();
	}

	public GodotObject InstantiateCountryFromProvinces(Godot.Collections.Dictionary aCountryData, int[] aClaims)
	{
		var countryer = CountryManager.Instance.AddCountry(aCountryData);
		var name = aCountryData.ContainsKey("name") ? aCountryData["name"].AsString() : "";
		foreach (var pid in aClaims)
		{
			foreach (var troopVariant in ProvinceTroops(pid))
			{
				if (troopVariant is TroopData data)
					TroopManager.Instance.RemoveTroop(data);
			}

			TransferOwnership(pid, name);
		}

		CountryManager.Instance.CleanupEmptyCountries();
		ShowCountriesMap();
		return countryer;
	}

	public Godot.Collections.Array<Godot.Collections.Dictionary> GetAllCities()
	{
		var pids = new Godot.Collections.Array<Godot.Collections.Dictionary>();
		foreach (var objVariant in Provinces.Values)
		{
			var obj = objVariant;
			if (obj.city.Length > 0)
			{
				pids.Add(new Godot.Collections.Dictionary
				{
					{ "id", obj.id },
					{ "city", obj.city },
				});
			}
		}
		return pids;
	}

	public Godot.Collections.Array GetCitiesProvinceCountry(string countryName)
	{
		var provinces = new Godot.Collections.Array();
		foreach (var pidVariant in GetArray(countryToProvinces, countryName))
		{
			var pid = pidVariant;
			var province = Provinces[pid];
			if (province.GetFunctionalOwner() == countryName && province.city.Length > 0)
				provinces.Add(pid);
		}
		return provinces;
	}

	// Returns provinces that specifically border a certain enemy
	public int[] GetProvincesBorderingEnemies(string countryName, Godot.Collections.Array<string> enemies)
	{
		var specificBorders = new List<int>();

		var enemyDict = new Godot.Collections.Dictionary();
		foreach (var enemy in enemies)
			enemyDict[enemy] = true;

		foreach (var provIdVariant in GetArray(countryToProvinces, countryName))
		{
			var provId = provIdVariant;
			foreach (var neighborIdVariant in provinceGraph.GetPointConnections(provId))
			{
				var neighborId = (int)neighborIdVariant;
				var neighborProv = Provinces[neighborId];
				var provOwner = neighborProv.occupier != "" ? neighborProv.occupier : neighborProv.country;
				if (enemyDict.ContainsKey(provOwner))
				{
					specificBorders.Add(provId);
					break;
				}
			}
		}

		return specificBorders.ToArray();
	}

	public void AnnexCountry(string annexer, string annexee)
	{
		foreach (var troopVariant in TroopManager.Instance.GetTroopsForCountry(annexee))
		{
			TroopManager.Instance.RemoveTroop(troopVariant);
		}

		foreach (var pidVariant in GetArray(countryToOccupiedProvinces, annexee))
			DeoccupyProvince(pidVariant);

		var provincesToTransfer = GetArray(countryToOwnedProvinces, annexee);

		if (provincesToTransfer.Count == 0)
		{
			GD.Print("MapManager: No provinces found for ", annexee);
			return;
		}

		foreach (var pidVariant in provincesToTransfer.ToArray())
			TransferOwnership(pidVariant, annexer);

		GD.Print("ANNEXATION COMPLETE: ", annexer, " has taken all of ", annexee);
	}

	public void _BuildGlobalRegistry()
	{
		globalClaimsRegistry.Clear();
		foreach (var objVariant in Provinces.Values)
		{
			var obj = objVariant;
			foreach (var countryName in obj.claims)
			{
				if (!globalClaimsRegistry.TryGetValue(countryName, out var claims))
					globalClaimsRegistry[countryName] = claims = new List<int>();
				claims.Add(obj.id);
			}
		}
	}

	public void AllowPids(GodotObject accesser, GodotObject accessee)
	{
		var accesserName = CountryData.NameOf(accesser);
		var accesseeName = CountryData.NameOf(accessee);
		if (!allowedPids.ContainsKey(accesserName))
			allowedPids[accesserName] = new List<int>();
		foreach (var province in GetArray(countryToProvinces, accesseeName))
			allowedPids[accesserName].Add(province);
	}

	public void UnallowPids(GodotObject unaccesser, GodotObject unaccessee)
	{
		var unaccesserName = CountryData.NameOf(unaccesser);
		var unaccesseeName = CountryData.NameOf(unaccessee);
		foreach (var province in GetArray(countryToProvinces, unaccesseeName))
		{
			if (allowedPids.TryGetValue(unaccesserName, out var accessList))
				accessList.Remove(province);
		}
	}

	public void ChangeProvinceTypes(Godot.Collections.Array pids, int type, string countryName = "")
	{
		foreach (var pidVariant in pids)
		{
			var pid = pidVariant.AsInt32();
			var province = Provinces[pid];
			province.type = type;
			province.country = countryName;
			GD.Print(province.type);
			GD.Print(province.country);
		}
		var world = GameState.Instance.currentWorld;
		if (world != null)
		{
			_SetTypeMap(
				world.Get("mat").AsGodotObject() as Material,
				world.Get("typeImg").AsGodotObject() as Image);
		}
	}

	public void _SetTypeMap(Material mat, Image typeImg)
	{
		var uncertainPixels = new List<Vector2I>();

		// --- PASS 1: Direct Mapping ---
		for (var i = 0; i < MAP_HEIGHT * MAP_WIDTH; i++)
		{
			var x = i % MAP_WIDTH;
			var y = i / MAP_WIDTH;
			var pid = _get_pid_fast(x, y);
			Provinces.TryGetValue(pid, out var province);

			if (province != null)
				typeImg.SetPixel(x, y, new Color(province.type, province.type, province.type));
			else
				// It's a border (PID 1 or null). Mark as uncertain for now.
				uncertainPixels.Add(new Vector2I(x, y));
		}

		// --- PASS 2: Intelligent Flood-Check ---
		foreach (var pos in uncertainPixels)
		{
			var touchesLand = false;

			// Check 8-way neighbors (Radius 1 ONLY - very important)
			for (var dy = -1; dy <= 1; dy++)
			{
				for (var dx = -1; dx <= 1; dx++)
				{
					if (dx == 0 && dy == 0)
						continue;

					var nx = pos.X + dx;
					var ny = pos.Y + dy;

					if (nx >= 0 && nx < MAP_WIDTH && ny >= 0 && ny < MAP_HEIGHT)
					{
						var nid = _get_pid_fast(nx, ny);
						if (nid > 1)
						{
							Provinces.TryGetValue(nid, out var nProv);
							if (nProv != null && nProv.type != 0)
								touchesLand = true;
						}
					}
				}
			}

			if (touchesLand)
				typeImg.SetPixel(pos.X, pos.Y, new Color(1, 1, 1));
			else
				// If it only touches sea (or nothing), it's a Sea Grid/Open Water
				typeImg.SetPixel(pos.X, pos.Y, new Color(0, 0, 0));
		}
		((ShaderMaterial)mat).SetShaderParameter("type_map", ImageTexture.CreateFromImage(typeImg));
	}

	public enum MapView
	{
		COUNTRIES,
		POPULATION,
		INFRASTRUCTURE,
		GDP,
		ETHNICITY,
		FACTION,
		RESOURCES,
		BIOMES,
	}

	public MapView currentView = MapView.COUNTRIES;

	public void CycleMapMode(bool shift)
	{
		var size = Enum.GetValues(typeof(MapView)).Length;
		SetMapMode((MapView)(((int)currentView + (shift ? 1 : -1) + size) % size));
	}

	public void SetMapMode(MapView aMapView)
	{
		currentView = aMapView;
		EmitSignal(SignalName.MapViewChanged, (int)currentView);
		switch (aMapView)
		{
			case MapView.COUNTRIES:
				ShowCountriesMap();
				GD.Print("Map Mode: Countries");
				break;
			case MapView.POPULATION:
				_ShowPopulationMap();
				GD.Print("Map Mode: Population");
				break;
			case MapView.INFRASTRUCTURE:
				_ShowInfrastructureMap();
				GD.Print("Map Mode: Infrastructure");
				break;
			case MapView.GDP:
				_ShowGDPMap();
				GD.Print("Map Mode: GDP");
				break;
			case MapView.ETHNICITY:
				_ShowEthnicMap();
				GD.Print("Map Mode: Ethnicity");
				break;
			case MapView.FACTION:
				_ShowFactionMap();
				GD.Print("Map Mode: Factions");
				break;
			case MapView.RESOURCES:
				_ShowResourcesMap();
				GD.Print("Map Mode: Resources");
				break;
			case MapView.BIOMES:
				_ShowBiomesMap();
				GD.Print("Map Mode: Biomes");
				break;
		}
	}

	// --- Private helpers (not part of the GDScript surface) ---

	private static Color CountryColor(string countryName, Color fallback)
		=> CountryManager.Instance.GetCountryColor(countryName, fallback);

	private static int InteractionType()
		=> (int)GameState.Instance.interactionType;

	private static int IndustryBuilding()
		=> (int)GameState.Instance.industryBuilding;

	private static void SwitchTooltip(int mode)
	{
		var tooltip = GameState.Instance.tooltip;
		if (tooltip != null && GodotObject.IsInstanceValid(tooltip))
			tooltip.SwitchTooltip(mode);
	}

	private static readonly List<int> EmptyProvinceIds = new();

	private static List<int> GetArray(Dictionary<string, List<int>> dict, string key)
		=> dict.TryGetValue(key, out var list) ? list : EmptyProvinceIds;

	private static List<int> GetOrAddArray(Dictionary<string, List<int>> dict, string key)
	{
		if (!dict.TryGetValue(key, out var list))
			dict[key] = list = new List<int>();
		return list;
	}

	private static List<TroopData> ProvinceTroops(int pid)
	{
		var result = new List<TroopData>();
		if (TroopManager.Instance.troopsByProvince.TryGetValue(pid, out var troops))
			foreach (var troop in troops)
				result.Add(troop);
		return result;
	}
}

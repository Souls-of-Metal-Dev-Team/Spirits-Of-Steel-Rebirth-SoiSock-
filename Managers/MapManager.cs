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
	public static MapManager Instance => Autoload.Get<MapManager>("MapManager");

	public bool DEBUG_MODE = false;

	[Signal]
	public delegate void ProvinceHoveredEventHandler(int province_id, string country_name);

	[Signal]
	public delegate void CountryClickedEventHandler(string country_name);

	[Signal]
	public delegate void ProvinceOwnershipChangedEventHandler(int pid, string old_owner, string new_owner);

	// Emitted when a click couldn't be processed (so likely sea or border)
	[Signal]
	public delegate void CloseSidemenuEventHandler();

	// Emitted when the active map view changes (value is a MapView)
	[Signal]
	public delegate void MapViewChangedEventHandler(int view);

	// Emitted when a country is picked in SELECT_COUNTRY mode
	[Signal]
	public delegate void CountrySelectedEventHandler(string country_name);

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
	public Image id_map_image;
	public Image state_color_image;
	public ImageTexture state_color_texture;
	public int max_province_id = 0;

	public Godot.Collections.Dictionary ethnic_name_to_color = new();
	public Godot.Collections.Dictionary gdp_map = new();

	public Dictionary<string, List<int>> country_to_provinces = new();
	public Dictionary<string, List<int>> country_to_owned_provinces = new();
	public Dictionary<string, List<int>> country_to_occupied_provinces = new();
	public Dictionary<string, List<int>> country_to_rightful_provinces = new();
	public Dictionary<string, List<int>> country_to_cities = new();
	public Dictionary<int, Province> province_objects => _provinces;
	private readonly Dictionary<int, Province> _provinces = new();
	public Godot.Collections.Dictionary buildingTypesToCountryCount = new(); // Stores {BuildingID: {CountryName: nuumber}}

	public Godot.Collections.Dictionary biomes = new();
	public Godot.Collections.Dictionary resources = new();

	public int current_hovered_pid = -1;
	public int last_hovered_pid = -1;
	public Color original_color = Colors.Black;
	public Dictionary<int, Vector2> province_centers = new(); // Stores {ID: Vector2(x, y)}
	public Dictionary<int, int> unique_regions = new(); // NOTE(soi): eh
	public SoiStar province_graph = new SoiStar(); // NOTE(soi): ehh

	// This will look like: {"french_empire": [101, 102, 103], "canada": [1, 2, 5]}
	public Dictionary<string, List<int>> global_claims_registry = new();
	public float world_tension = 0.1f; // Global tension level (0.1 to 1.0)

	public Godot.Collections.Array<Godot.Collections.Dictionary> all_cities = new();

	public Godot.Collections.Dictionary<string, ImportantFigure> significantFigures = new();

	public Label gay = new Label();
	// soiladin time
	public Dictionary<string, List<int>> allowed_pids = new();

	public void IncreaseWorldTension(float a_amount)
	{
		world_tension = Mathf.Clamp(world_tension + a_amount, 0.1f, 1.0f);
	}

	public const string CACHE_FOLDER = "res://map_data/";

	// NOTE(soi): these should probably be in like world or smthng
	[Export] public Texture2D region_texture;
	[Export] public Texture2D culture_texture;
	[Export] public Texture2D population_texture;
	[Export] public Texture2D city_texture;
	[Export] public Texture2D gdp_texture;
	[Export] public Texture2D ethnicity_texture;
	[Export] public Texture2D claims_texture;

	[Export] public int MAP_WIDTH = 0;
	[Export] public int MAP_HEIGHT = 625;

	// GDScript cannot read C# static constants through an instance.
	public int map_width => MAP_WIDTH;
	public int map_height => MAP_HEIGHT;

	public Dictionary<string, Texture2D> icon_cache = new();

	public void LoadBiomes(Godot.Collections.Array a_biomeData)
	{
		biomes.Clear();
		foreach (var biomeVariant in a_biomeData)
		{
			var biome = biomeVariant.AsGodotDictionary();
			biomes[biome["name"].AsString()] = BiomeData.FromDict(biome);
		}
	}

	public void LoadResources(Godot.Collections.Array a_resourceData)
	{
		resources.Clear();
		foreach (var resourceVariant in a_resourceData)
		{
			var resource = resourceVariant.AsGodotDictionary();
			resources[resource["name"].AsString()] = ResourceData.FromDict(resource);
		}
	}

	// Helper to check both custom and default locations
	public string FindResourceResource(string sub_path)
	{
		var full_default = "res://assets/icons/Resources/" + sub_path;
		if (ResourceLoader.Exists(full_default))
			return full_default;
		return "";
	}

	public Texture2D GetResourceIcon(string a_resourceType)
	{
		var cache_key = a_resourceType;

		// If already cached -> return it
		if (icon_cache.TryGetValue(cache_key, out var cachedIcon))
			return cachedIcon;

		// Cache key needs to include ideology if provided
		if (!resources.ContainsKey(a_resourceType))
		{
			var tex = GD.Load<Texture2D>(FindResourceResource("Droplet.svg"));
			icon_cache[cache_key] = tex;
			return tex;
		}

		// 0. Check Redirects
		// 4. Fallback to old flat structure (just in case): {path}/country_flag.png
		var resource = resources[a_resourceType].AsGodotObject() as ResourceData;
		var path = FindResourceResource($"{resource?.icon}.svg");

		if (path == "")
			path = FindResourceResource("Droplet.svg");

		if (path != "")
		{
			var tex = GD.Load<Texture2D>(path);
			icon_cache[cache_key] = tex;
			return tex;
		}

		return null;
	}

	public void _ClearInternalData()
	{
		all_cities.Clear();
		unique_regions.Clear();
		_provinces.Clear();
		country_to_provinces.Clear();
		country_to_owned_provinces.Clear();
		country_to_occupied_provinces.Clear();
		allowed_pids.Clear();
		province_centers.Clear();
		// Clear graph and global registry
		global_claims_registry.Clear();
		// Re-init significant figures if needed, but for now just clear
		significantFigures.Clear();
		// Reset state variables
		max_province_id = 0;
		current_hovered_pid = -1;
		last_hovered_pid = -1;
		hoveredCountry = "Sea";
	}

	public void Initialize(
		Texture2D a_map,
		Godot.Collections.Dictionary a_provinceData,
		Godot.Collections.Array a_progress
	)
	{
		_ClearInternalData();

		MAP_WIDTH = a_map.GetWidth();
		MAP_HEIGHT = a_map.GetHeight();

		id_map_image = Image.CreateEmpty(MAP_WIDTH, MAP_HEIGHT, false, Image.Format.Rgb8);
		var next_id = 2;

		var mapImage = a_map.GetImage();
		if (mapImage.GetFormat() != Image.Format.Rgb8)
			mapImage.Convert(Image.Format.Rgb8);

		var cultureSprite = GetNodeOrNull<Sprite2D>("../Main/MapContainer/CultureSprite");
		if (cultureSprite != null)
			cultureSprite.Texture = a_map;

		province_graph.NeighborFilterEnabled = true;
		province_graph.Clear();

		var inc = 0.1f / (MAP_WIDTH * MAP_HEIGHT);

		// Flat per-province arrays, indexed by pid. Positions are interleaved: [x0, y0, x1, y1, ...].
		// pid 0 and 1 are unused placeholders, so real province IDs index directly.
		var first_pos = new List<int> { 0, 0, 0, 0 };
		var last_pos = new List<int> { 0, 0, 0, 0 };
		var pixel_count = new List<int> { 0, 0 };

		// RGB8 raw bytes: 3 per pixel, row-major. Index = packed RGB, same as old to_rgba32() >> 8.
		var mapData = mapImage.GetData();
		// pid of every pixel scanned so far, indexed by i. Border pixels stay 0.
		// Left / top / two-left / two-up neighbours have already been scanned when we read them.
		var pid_buf = new int[MAP_WIDTH * MAP_HEIGHT];

		for (var i = 0; i < MAP_WIDTH * MAP_HEIGHT; i++)
		{
			var x = i % MAP_WIDTH;
			var y = i / MAP_WIDTH;
			var byte_offset = i * 3;
			var index = (mapData[byte_offset] << 16) | (mapData[byte_offset + 1] << 8) | mapData[byte_offset + 2];

			// Check for Borders/Grid (ID 1)
			if (index == 0)
			{
				// NOTE: int 1 converts to Color.hex(1), so this pixel reads back as pid 0.
				id_map_image.SetPixel(x, y, new Color(1u));
				continue;
			}

			// If this is a new region (Unique Sea Zone or Land Province)
			if (unique_regions.TryAdd(index, next_id))
			{

				var province_dict = a_provinceData.ContainsKey(index.ToString())
					? a_provinceData[index.ToString()].AsGodotDictionary()
					: new Godot.Collections.Dictionary();
				var province = Province.FromDict(province_dict);
				province.id = next_id;

				_provinces[next_id] = province;

				// First pixel of a province in scan order. Last starts here and moves on.
				first_pos.Add(x);
				first_pos.Add(y);
				last_pos.Add(x);
				last_pos.Add(y);
				pixel_count.Add(0);

				next_id += 1;
			}

			// Write the unique ID to your id_map_image
			var pid = unique_regions[index];
			id_map_image.SetPixel(x, y, new Color((uint)((pid << 8) | 0x000000FF)));

			// NOTE(soi): its soiladin time 2
			if (!province_graph.HasPoint(pid))
				province_graph.AddPoint(pid, new Vector2(x, y));

			pid_buf[i] = pid;

			// Connect to the left / top pixel. If that pixel is a 1px border (0),
			// look one pixel further across it, like _scan_across_border did.
			if (x != 0)
			{
				var left_pid = pid_buf[i - 1];
				if (left_pid == 0 && x >= 2)
					left_pid = pid_buf[i - 2];
				if (left_pid > 1 && left_pid != pid && !province_graph.ArePointsConnected(pid, left_pid))
					province_graph.ConnectPoints(pid, left_pid);
			}

			if (y != 0)
			{
				var top_pid = pid_buf[i - MAP_WIDTH];
				if (top_pid == 0 && y >= 2)
					top_pid = pid_buf[i - 2 * MAP_WIDTH];
				if (top_pid > 1 && top_pid != pid && !province_graph.ArePointsConnected(pid, top_pid))
					province_graph.ConnectPoints(pid, top_pid);
			}

			var k = pid << 1;
			last_pos[k] = x;
			last_pos[k + 1] = y;
			pixel_count[pid] += 1;
		}

		a_progress[0] = a_progress[0].AsDouble() + inc * MAP_WIDTH * MAP_HEIGHT;
		max_province_id = next_id - 1;
		BuildLookupTexture();
		a_progress[0] = a_progress[0].AsDouble() + 0.015;

		_calculate_province_centroids(first_pos, last_pos, pixel_count);
		a_progress[0] = a_progress[0].AsDouble() + 0.01;

		// Pass 2: Load troops now that centroids (positions) are definitely known
		foreach (var I_index in a_provinceData.Keys)
		{
			var key = I_index.AsInt32();
			if (unique_regions.TryGetValue(key, out var regionPid))
			{
				var p_dict = a_provinceData[I_index].AsGodotDictionary();
				if (p_dict.ContainsKey("troops"))
					TroopManager.Instance.load_troops_for_province(regionPid, p_dict["troops"].AsGodotArray());
			}
		}

		a_progress[0] = a_progress[0].AsDouble() + 0.025;
		_build_country_to_provinces();
		a_progress[0] = a_progress[0].AsDouble() + 0.025;
		// _build_adjacency_list(a_progress)
		_BuildGlobalRegistry();
		a_progress[0] = a_progress[0].AsDouble() + 0.025;
	}

	public void LoadProvinces(
		CompressedTexture2D region_map,
		Godot.Collections.Dictionary a_provinceData,
		Godot.Collections.Array a_progress
	)
	{
		Initialize(region_map, a_provinceData, a_progress);
	}

	public Godot.Collections.Dictionary SaveProvinces()
	{
		var provinces = new Godot.Collections.Dictionary();
		foreach (var index in unique_regions.Keys)
		{
			var next_id = unique_regions[index];
			var province = _provinces[next_id];
			var troops = TroopManager.Instance.get_serialized_troops_for_province(next_id);
			provinces[index.ToString()] = province.ToDictWithTroops(troops);
		}
		return provinces;
	}

	public Godot.Collections.Array SaveResourcesData()
	{
		var returnResources = new Godot.Collections.Array();
		foreach (var resourceVariant in resources.Values)
		{
			var resource = (ResourceData)resourceVariant.AsGodotObject();
			returnResources.Add(resource.ToDict());
		}
		return returnResources;
	}

	public Godot.Collections.Array SaveBiomeData()
	{
		var returnBiomes = new Godot.Collections.Array();
		foreach (var biomeVariant in biomes.Values)
		{
			var biome = (BiomeData)biomeVariant.AsGodotObject();
			returnBiomes.Add(biome.ToDict());
		}
		return returnBiomes;
	}

	public Godot.Collections.Dictionary SaveBuildingData()
	{
		var returnBuildings = new Godot.Collections.Dictionary();
		var designs = EconomyManager.Instance.building_designs;
		foreach (var I_country in designs.Keys)
		{
			var country = I_country;
			var countryBuildings = new Godot.Collections.Array();
			foreach (var I_building in designs[I_country].Values)
			{
				var template = (BuildingTemplate)I_building.AsGodotObject();
				countryBuildings.Add(template.ToDict());
			}
			returnBuildings[country] = countryBuildings;
		}
		return returnBuildings;
	}

	// NOTE(soi): where was this even used for???
	public void draw_province_centroids(Image image, Color color = default)
	{
		if (color == default)
			color = new Color(0, 1, 0, 1);

		if (image == null)
		{
			GD.PushWarning("No Image provided for drawing centroids!");
			return;
		}

		foreach (var center in province_centers.Values)
		{
			var x = Mathf.RoundToInt(center.X);
			var y = Mathf.RoundToInt(center.Y);

			// stay inside bounds
			if (x >= 0 && x < image.GetWidth() && y >= 0 && y < image.GetHeight())
				image.SetPixel(x, y, color);
		}
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

		foreach (var pid in _provinces.Keys)
		{
			var province = _provinces[pid];
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

		country_to_provinces = result;
		allowed_pids = new Dictionary<string, List<int>>();
		foreach (var pair in result)
			allowed_pids[pair.Key] = new List<int>(pair.Value);
		country_to_owned_provinces = owned;
		country_to_occupied_provinces = occupied;
		country_to_rightful_provinces = rightful;
		country_to_cities = cities;
	}

	public void BuildLookupTexture()
	{
		state_color_image = Image.CreateEmpty(max_province_id + 1, 2, false, Image.Format.Rgba8);

		for (var pid = 0; pid <= max_province_id; pid++)
		{
			if (pid <= 1)
			{
				state_color_image.SetPixel(pid, 0, Colors.Black);
				state_color_image.SetPixel(pid, 1, Colors.Black);
				continue;
			}

			_provinces.TryGetValue(pid, out var province);

			if (province == null || province.type == 0) // 0 is province.SEA
			{
				state_color_image.SetPixel(pid, 0, Colors.Black);
				state_color_image.SetPixel(pid, 1, Colors.Black);
				continue;
			}

			state_color_image.SetPixel(pid, 0, CountryColor(province.country, Colors.Gray));
			state_color_image.SetPixel(pid, 1, CountryColor(province.GetFunctionalOwner(), Colors.Gray));
		}

		state_color_texture = ImageTexture.CreateFromImage(state_color_image);
	}

	private bool _is_sea(Color c)
	{
		return _dist_sq(c, SEA_RASTER) < 0.001f || _dist_sq(c, SEA_MAIN) < 0.001f;
	}

	private float _dist_sq(Color c1, Color c2)
	{
		return Mathf.Pow(c1.R - c2.R, 2) + Mathf.Pow(c1.G - c2.G, 2) + Mathf.Pow(c1.B - c2.B, 2);
	}

	public void UpdateProvinceColor(int pid, string country_name)
	{
		if (pid <= 1 || pid > max_province_id)
			return;

		var new_color = CountryColor(country_name, Colors.Gray);
		UpdateLookup(pid, new_color, new_color);

		if (pid == last_hovered_pid)
		{
			original_color = new_color;
			UpdateLookup(pid,
				new_color + new Color(0.15f, 0.15f, 0.15f, 0),
				new_color + new Color(0.15f, 0.15f, 0.15f, 0));
		}
	}

	public void SetProvinceColor(int pid, Color primaryColor)
	{
		if (pid <= 1 || pid > max_province_id)
			return;

		var province = _provinces[pid];
		UpdateLookup(pid, primaryColor, CountryColor(province.country, Colors.Gray));
	}

	public void SetProvinceColors(int a_pid, Color a_primaryColor, Color a_secondaryColor)
	{
		if (a_pid <= 1 || a_pid > max_province_id)
			return;

		UpdateLookup(a_pid, a_primaryColor, a_secondaryColor);
	}

	public void ResetProvinceColor(int pid)
	{
		if (pid <= 1 || pid > max_province_id)
			return;

		var province = _provinces[pid];
		UpdateLookup(pid,
			CountryColor(province.GetFunctionalOwner(), Colors.Gray),
			CountryColor(province.country, Colors.Gray));
	}

	public void SetCountryColor(string country_name, Color custom_color = default)
	{
		var new_color = custom_color;
		if (new_color == default)
			new_color = Colors.Transparent;
		if (new_color == Colors.Transparent)
			new_color = CountryColor(country_name, Colors.Gray);

		var provinces = GetArray(country_to_provinces, country_name);

		if (provinces.Count == 0)
		{
			GD.Print("Warning: No provinces found for country: ", country_name);
			return;
		}

		foreach (var pidVariant in provinces)
		{
			var pid = pidVariant;
			UpdateLookup(pid, new_color, new_color);

			if (pid == last_hovered_pid)
			{
				original_color = new_color;
				UpdateLookup(pid,
					new_color + new Color(0.15f, 0.15f, 0.15f, 0),
					new_color + new Color(0.15f, 0.15f, 0.15f, 0));
			}
		}
	}

	public int GetProvinceAtPos(Vector2 pos, Sprite2D map_sprite = null)
	{
		if (id_map_image == null)
			return 0;

		int x;
		int y;

		if (map_sprite != null)
		{
			var local = map_sprite.ToLocal(pos);
			var sprite_size = map_sprite.Texture.GetSize();

			// If sprite is centered, offset the local position to be top-left based
			if (map_sprite.Centered)
				local += sprite_size * 0.5f;

			// --- INFINITE SCROLL MATH ---
			x = (int)local.X % (int)sprite_size.X;
			if (x < 0)
				x += (int)sprite_size.X;
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

		return (int)(id_map_image.GetPixel(x, y).ToRgba32() >> 8);
	}

	private string _GetHoverViewer()
	{
		if (InteractionType() == 1) // GameState.InteractionType.SELECT_COUNTRY
			return "";
		return CountryData.NameOf(CountryManager.Instance.player_country);
	}

	public void UnhighlightCountry()
	{
		if (hoveredCountry == "Sea")
			return;

		var countryColor = GetCountryDisplayColor(hoveredCountry);
		foreach (var provinceVariant in GetArray(country_to_provinces, hoveredCountry))
		{
			var province = provinceVariant;
			UpdateLookup(
				province,
				GetCountryDisplayColor((_provinces[province]).country),
				countryColor);
		}
		foreach (var provinceVariant in GetArray(country_to_rightful_provinces, hoveredCountry))
		{
			var province = provinceVariant;
			UpdateLookup(
				province,
				countryColor,
				GetCountryDisplayColor((_provinces[province]).GetFunctionalOwner()));
		}
		hoveredCountry = "Sea";
	}

	public void HighlightCountry(int a_pid, Color a_highlightColor)
	{
		var newHoveredCountry = (_provinces[a_pid]).GetFunctionalOwner();

		foreach (var provinceVariant in GetArray(country_to_provinces, newHoveredCountry))
		{
			var province = provinceVariant;
			var provinceObject = _provinces[province];
			UpdateLookup(
				province,
				provinceObject.occupier == newHoveredCountry
					? GetCountryDisplayColor(provinceObject.country)
					: a_highlightColor,
				a_highlightColor);
		}
		foreach (var provinceVariant in GetArray(country_to_rightful_provinces, newHoveredCountry))
		{
			var province = provinceVariant;
			var provinceObject = _provinces[province];
			UpdateLookup(
				province,
				a_highlightColor,
				GetCountryDisplayColor(provinceObject.GetFunctionalOwner()));
		}
		hoveredCountry = newHoveredCountry;
	}

	public void HoverCountry(int a_pid, Color a_highlightColor)
	{
		// Still inside the same country, so the highlight is already correct.
		if (_provinces.TryGetValue(a_pid, out var hoveredProvince)
			&& hoveredProvince.GetFunctionalOwner() == hoveredCountry)
		{
			last_hovered_pid = a_pid;
			return;
		}

		ResetHover();

		_provinces.TryGetValue(a_pid, out var province);
		if (a_highlightColor == Colors.Transparent || province == null || province.country == "Sea")
		{
			Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
			EmitSignal(SignalName.ProvinceHovered, -1, "");
			return;
		}

		if (current_view != MapView.COUNTRIES)
			return;

		HighlightCountry(a_pid, a_highlightColor);
		last_hovered_pid = a_pid;
		Input.SetDefaultCursorShape(Input.CursorShape.PointingHand);
		EmitSignal(SignalName.ProvinceHovered, a_pid, _GetHoverViewer());
	}

	public void HoverProvince(int a_pid, Color a_highlightColor)
	{
		ResetHover();

		if (a_pid <= 1)
		{
			Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
			EmitSignal(SignalName.ProvinceHovered, -1, "");
			SwitchTooltip(-1);
			return;
		}

		switch (current_view)
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

		if (a_highlightColor != Colors.Transparent)
		{
			original_color = state_color_image.GetPixel(a_pid, 0);
			UpdateLookup(a_pid, a_highlightColor, a_highlightColor);
			last_hovered_pid = a_pid;
			Input.SetDefaultCursorShape(Input.CursorShape.PointingHand);
			EmitSignal(SignalName.ProvinceHovered, a_pid, _GetHoverViewer());
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
		else if (last_hovered_pid > 1)
			UpdateLookup(last_hovered_pid, original_color, original_color);
		last_hovered_pid = -1;
	}

	public Color GetContextualHighlight(int pid)
	{
		if (pid <= 1)
			return Colors.Transparent;

		if (InteractionType() == 1) // SELECT_COUNTRY
			return Colors.AntiqueWhite;

		var player_name = CountryData.NameOf(CountryManager.Instance.player_country);
		var province = _provinces[pid];
		var is_player_owned = province.GetFunctionalOwner() == player_name;

		if (!is_player_owned)
			return Colors.Transparent;

		if (IndustryBuilding() == 2) // PORT
		{
			var coastal_provinces = GetProvincesNearSea(player_name);
			var has_port = false;
			foreach (var building in province.buildings)
			{
				if (building.type == "Port")
				{
					has_port = true;
					break;
				}
			}
			if (Array.IndexOf(coastal_provinces, pid) >= 0 && has_port)
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
			return state_color_image.GetPixel(pid, 0).Lightened(0.2f).Blend(Colors.GreenYellow);

		return Colors.Transparent;
	}

	// Relation of a_country1 as seen from a_country2. Returns CountryRelation bitflags.
	public int GetRelativeCountryRelation(GodotObject a_country1, GodotObject a_country2)
	{
		if (a_country1.Get("country_name").AsString() == a_country2.Get("country_name").AsString())
			return (int)CountryRelation.PLAYER;
		if (a_country2.Get("puppets").AsGodotArray().Contains(a_country1.Get("country_name").AsString()))
			return (int)CountryRelation.PUPPET; // never neutral

		var relation = RELATION_NEUTRAL_BIT;
		if (FactionManager.Instance.in_faction(a_country2, a_country1))
			relation |= RELATION_ALLY_BIT;
		if (WarManager.Instance.is_at_war(a_country1, a_country2))
			relation |= RELATION_ENEMY_BIT;
		return relation;
	}

	public bool IsLandProvince(int pid)
	{
		var province = _provinces[pid];
		return pid > 1 && province.type != 0; // 0 is SEA
	}

	public void ClickSea()
	{
		if (IndustryBuilding() != 0) // DEFAULT
		{
			GameState.Instance.reset_industry_building();
			ShowCountriesMap();
		}
		else
		{
			EmitSignal(SignalName.CloseSidemenu);
		}
	}

	public void SelectCountryAt(int pid)
	{
		var province = _provinces[pid];
		EmitSignal(SignalName.CountrySelected, province.GetFunctionalOwner());
		ShowCountriesMap();
	}

	public void DeployAt(int pid)
	{
		var player_country_name = CountryData.NameOf(CountryManager.Instance.player_country);
		var province = _provinces[pid];
		var is_player_owned = province.GetFunctionalOwner() == player_country_name;
		_SetDeploymentPID(is_player_owned, pid, player_country_name);
	}

	public void BuildAt(int pid)
	{
		if (EconomyManager.Instance.is_province_building(pid))
			return;

		var province = _provinces[pid];
		var owner_name = province.GetFunctionalOwner();
		var countries = CountryManager.Instance.countries;
		var relation = GetRelativeCountryRelation(
			countries.TryGetValue(owner_name, out var ownerValue) ? ownerValue.AsGodotObject() : null,
			CountryManager.Instance.player_country);
		// Only player and puppets (no neutral bit) can be built in.
		var country_name = (relation & RELATION_NEUTRAL_BIT) == 0 ? owner_name : "";
		if (country_name != "" && _ProvinceBuildIndustry(pid, country_name, IndustryBuilding()))
		{
			_cleanup_interaction_state();
			ShowIndustryCountry(country_name);
			EmitSignal(SignalName.CountryClicked, country_name);
		}
		else
		{
			GD.Print("Action Failed: Cannot build in foreign territory.");
			GameState.Instance.reset_industry_building();
			ShowCountriesMap();
		}
	}

	// move this to CountryManager?
	public void _SetDeploymentPID(
		bool a_is_player_owned,
		int a_pid,
		string a_player_name
	)
	{
		var province = _provinces[a_pid];
		if (a_is_player_owned && province.city != "")
		{
			EmitSignal(SignalName.CountryClicked, a_player_name);
			CountryManager.Instance.player_country.Set("deploy_pid", a_pid);
			GameState.Instance.interactionType = 0; // DEFAULT
			_cleanup_interaction_state();
		}
	}

	public bool _ProvinceBuildIndustry(int pid, string a_countryName, int type)
	{
		var province = _provinces[pid];
		var country = CountryManager.Instance.GetCountry(a_countryName);

		// 1. Safety Check: Is there already something there or currently building?
		// Using your Enums: 0 = NO, 1 = BUILDING, 2 = BUILT
		switch (type)
		{
			case 1: // FACTORY
				if (province.buildings.Count >= 4)
					return false;
				EconomyManager.Instance.start_construction(pid, "Factory", 10, 150.0f, country);
				break;
			case 2: // PORT
				var all_ports = true;
				foreach (var building in province.buildings)
				{
					if (building.type != "Port")
					{
						all_ports = false;
						break;
					}
				}
				if (province.buildings.Count >= 4 && all_ports)
					return false;

				// 3. Sea check for Ports
				if (Array.IndexOf(GetProvincesNearSea(a_countryName), pid) >= 0)
					EconomyManager.Instance.start_construction(pid, "Port", 10, 150.0f, country);
				else
				{
					GD.Print("Action Failed: Port must be on a coast!");
					return false;
				}
				break;
			case 3: // INFRASTRUCTURE
				if (province.infrastructure >= province.maxInfrastructure)
					return false;
				EconomyManager.Instance.StartInfrastructureConstruction(pid, 10, 150.0f, country);
				break;
		}

		return true;
	}

	public void _cleanup_interaction_state()
	{
		Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
		if (last_hovered_pid > 1)
		{
			UpdateLookup(last_hovered_pid, original_color, original_color);
			last_hovered_pid = -1;
		}
	}

	// To probe around and still register a click if we hit province/country border
	public int GetProvinceWithRadius(Vector2 center, Sprite2D map_sprite, int radius)
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
			var pid = GetProvinceAtPos(center + off, map_sprite);
			if (pid > 1)
				return pid;
		}

		return -1;
	}

	public void UpdateLookup(int pid, Color colorMain, Color colorSecondary)
	{
		state_color_image.SetPixel(pid, 0, colorMain);
		state_color_image.SetPixel(pid, 1, colorSecondary);
		state_color_texture.Update(state_color_image);
	}

	// Center = midpoint of first and last pixel seen in scan order (top-left-most, bottom-right-most).
	private void _calculate_province_centroids(
		List<int> a_first_pos,
		List<int> a_last_pos,
		List<int> a_pixel_count
	)
	{
		for (var pid = 2; pid < a_pixel_count.Count; pid++)
		{
			if (a_pixel_count[pid] <= 0)
				continue;

			var k = pid * 2;
			var center = new Vector2(
				(a_first_pos[k] + a_last_pos[k]) * 0.5f,
				(a_first_pos[k + 1] + a_last_pos[k + 1]) * 0.5f);

			province_centers[pid] = center;
			// Graph point starts at the province's first scanned pixel. Move it to the center
			// so SoiStar path costs use real province distances.
			if (province_graph.HasPoint(pid))
				province_graph.SetPointPosition(pid, center);
			if (_provinces.TryGetValue(pid, out var province))
				province.center = center;
		}

		GD.Print($"MapManager: Centroids calculated for {province_centers.Count} provinces.");
	}

	private void _build_adjacency_list(Godot.Collections.Array a_progress)
	{
		province_graph.NeighborFilterEnabled = true;
		province_graph.Clear();

		var inc = 0.1f / (MAP_WIDTH * MAP_HEIGHT);

		for (var i = 0; i < MAP_WIDTH * MAP_HEIGHT; i++)
		{
			a_progress[0] = a_progress[0].AsDouble() + inc;
			var x = i % MAP_WIDTH;
			var y = i / MAP_WIDTH;
			var pid = _get_pid_fast(x, y);
			if (pid <= 1)
				continue;

			if (!province_graph.HasPoint(pid))
				province_graph.AddPoint(pid, new Vector2(x, y));

			// 4-directional neighbors
			foreach (var d in ALL_DIRECTIONS)
			{
				var nx = x + d.X;
				var ny = y + d.Y;
				if (nx < 0 || ny < 0 || nx >= MAP_WIDTH || ny >= MAP_HEIGHT)
					continue;

				var neighbor = _get_pid_fast(nx, ny);

				// Normal adjacency (Land-to-Land)
				if (neighbor > 0 && neighbor != pid)
				{
					province_graph.AddPoint(neighbor, new Vector2(nx, ny));
					province_graph.ConnectPoints(pid, neighbor);
					continue;
				}

				// Border pixel scan (ID=1)
				if (neighbor == 0)
				{
					var across = _scan_across_border(nx, ny, pid);
					if (across > 0 && across != pid)
					{
						province_graph.AddPoint(across, new Vector2(nx, ny));
						province_graph.ConnectPoints(pid, across);
					}
				}
			}
		}

		GD.Print("MapManager: Adjacency list built and synced to Province objects.");
	}

	private int _scan_across_border(int x, int y, int pid)
	{
		// Check right
		if (x + 1 < MAP_WIDTH)
		{
			var n = _get_pid_fast(x + 1, y);
			if (n > 0 && n != pid)
				return n;
		}

		// Check down
		if (y + 1 < MAP_HEIGHT)
		{
			var n = _get_pid_fast(x, y + 1);
			if (n > 0 && n != pid)
				return n;
		}

		return -1;
	}

	// Faster direct pid fetch
	public int _get_pid_fast(int x, int y)
	{
		return (int)(id_map_image.GetPixel(x, y).ToRgba32() >> 8);
	}

	// --- Pathfinding (delegated to SoiStar) ---//

	public Dictionary<Vector2I, long[]> path_cache = new();

	public long[] find_path(int start_pid, int end_pid, Godot.Collections.Array allowed_countries = null)
	{
		if (start_pid == end_pid)
			return new long[] { start_pid };

		allowed_countries ??= new Godot.Collections.Array();
		var use_cache = allowed_countries.Count == 0;
		var cache_key = new Vector2I(start_pid, end_pid);

		if (use_cache && path_cache.TryGetValue(cache_key, out var cached_path))
			return (long[])cached_path.Clone();

		// Set context for SoiStar filtering
		province_graph.context_allowed_countries.Clear();
		foreach (var country in allowed_countries)
			province_graph.context_allowed_countries.Add(country.AsString());
		var pathResult = province_graph.GetIdPath(start_pid, end_pid);
		province_graph.context_allowed_countries.Clear();

		var pathList = new List<long>();
		foreach (var id in pathResult)
			pathList.Add(id);
		var path = pathList.ToArray();

		if (use_cache && path.Length != 0)
			path_cache[cache_key] = path;

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

	private Color _get_heatmap_color(int pop, float max_pop)
	{
		if (pop <= 0)
			return new Color(0.1f, 0.1f, 0.15f, 1.0f);
		var intensity = Mathf.Clamp(pop / max_pop, 0.0f, 1.0f);
		return _get_population_gradient().Sample(intensity);
	}

	// soilad aneurysm time
	public void _ShowFactionMap()
	{
		var faction_color = Colors.Gray;
		if (_provinces.Count == 0)
			return;

		var current_max_gdp = 1.0f;
		foreach (var provinceVariant in _provinces.Values)
		{
			var province = provinceVariant;
			if (province.gdp > current_max_gdp)
				current_max_gdp = province.gdp;
		}

		var factions = FactionManager.Instance.factions;
		foreach (var pid in _provinces.Keys)
		{
			if (pid <= 1)
				continue;

			var province = _provinces[pid];
			var total = 0;
			faction_color = Colors.Gray;
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
						faction_color = faction.color;
						break;
					}
				}
			}
			state_color_image.SetPixel(pid, 0, faction_color / total);
			state_color_image.SetPixel(pid, 1, faction_color / total);
		}

		state_color_texture.Update(state_color_image);
		current_view = MapView.FACTION;
		EmitSignal(SignalName.MapViewChanged, (int)current_view);
		GD.Print("MapManager: Faction View Updated");
	}

	private void _ShowPopulationMap()
	{
		if (_provinces.Count == 0)
			return;
		var current_max_pop = 1.0f;
		foreach (var provinceVariant in _provinces.Values)
		{
			var province = provinceVariant;
			if (province.GetPopulation() > current_max_pop)
				current_max_pop = province.GetPopulation();
		}

		foreach (var pid in _provinces.Keys)
		{
			if (pid <= 1)
				continue;
			var province = _provinces[pid];
			var color = _get_heatmap_color(province.GetPopulation(), current_max_pop);
			state_color_image.SetPixel(pid, 0, color);
			state_color_image.SetPixel(pid, 1, color);
		}

		state_color_texture.Update(state_color_image);
		GD.Print("MapManager: Population View Updated. Max Pop found: ", current_max_pop);
	}

	private void _ShowInfrastructureMap()
	{
		if (_provinces.Count == 0)
			return;

		var player = CountryManager.Instance.player_country;
		var allowed = player == null ? new Godot.Collections.Array() : player.Call("get_all_allowed_countries").AsGodotArray();
		var player_name = CountryData.NameOf(player);
		var countries = CountryManager.Instance.countries;

		foreach (var pid in _provinces.Keys)
		{
			var province = _provinces[pid];
			if (pid <= 1 || province.country == "Sea")
				continue;
			if (InteractionType() != 1 && player_name != province.country && !allowed.Contains(province.country))
			{
				state_color_image.SetPixel(pid, 0, CountryColor(province.country, Colors.Black));
			}
			else
			{
				var infra = province.infrastructure;
				var country_color = countries.TryGetValue(province.country, out var colorValue)
					? colorValue.AsGodotObject().Get("country_color").AsColor()
					: Colors.White;
				if (infra == 0)
					state_color_image.SetPixel(pid, 0, country_color * 0.5f + Colors.White * 0.5f);
				else if (infra == province.maxInfrastructure)
					state_color_image.SetPixel(pid, 0, country_color * 0.5f + Colors.Blue * 0.5f);
				else
					state_color_image.SetPixel(pid, 0,
						country_color * 0.5f + Colors.Yellow * 0.5f * ((float)infra / province.maxInfrastructure));
			}
		}

		state_color_texture.Update(state_color_image);
		GD.Print("MapManager: Infrastructure View Updated.");
	}

	private void _ShowEthnicMap()
	{
		if (_provinces.Count == 0)
			return;

		foreach (var pid in _provinces.Keys)
		{
			if (pid <= 1)
				continue;

			var province = _provinces[pid];
			if (province.populations.Count <= 0)
				continue;
			var eth_name = province.populations[0].ethnicity; // Assuming this is the String name (e.g., "Igbo")

			// Default to black or transparent if ethnicity not found
			var display_color = Colors.Gray;

			if (ethnic_name_to_color.TryGetValue(eth_name, out var ethnicColor))
				display_color = ethnicColor.AsColor();

			// Update the lookup texture
			state_color_image.SetPixel(pid, 0, display_color);
			state_color_image.SetPixel(pid, 1, display_color);
		}

		state_color_texture.Update(state_color_image);
		GD.Print("MapManager: Ethnic View Updated.");
	}

	private Color _get_gdp_heatmap_color(int gdp, float max_gdp)
	{
		if (gdp <= 0)
			return new Color(0.1f, 0.1f, 0.1f);
		var intensity = Mathf.Clamp(Mathf.Sqrt(gdp) / Mathf.Sqrt(max_gdp), 0.0f, 1.0f);
		return _get_gdp_gradient().Sample(intensity);
	}

	private void _ShowGDPMap()
	{
		if (_provinces.Count == 0)
			return;

		var current_max_gdp = 1.0f;
		foreach (var provinceVariant in _provinces.Values)
		{
			var province = provinceVariant;
			if (province.gdp > current_max_gdp)
				current_max_gdp = province.gdp;
		}

		foreach (var pid in _provinces.Keys)
		{
			if (pid <= 1)
				continue;

			var province = _provinces[pid];
			var color = _get_gdp_heatmap_color(province.gdp, current_max_gdp);
			state_color_image.SetPixel(pid, 0, color);
			state_color_image.SetPixel(pid, 1, color);
		}

		state_color_texture.Update(state_color_image);
		GD.Print("MapManager: GDP View Updated. Max GDP: ", current_max_gdp);
	}

	public Color GetCountryDisplayColor(string a_countryName)
	{
		var countries = CountryManager.Instance.countries;
		if (!countries.TryGetValue(a_countryName, out var countryValue) || countryValue.AsGodotObject() == null)
			return Colors.Gray;
		var country_data = countryValue.AsGodotObject();
		var owner = country_data.Get("owner").AsString();
		if (!countries.TryGetValue(owner, out var ownerValue) || ownerValue.AsGodotObject() == null)
			return country_data.Get("country_color").AsColor();
		var owner_data = ownerValue.AsGodotObject();
		return (country_data.Get("country_color").AsColor() + 3 * owner_data.Get("country_color").AsColor()) * 0.25f;
	}

	public void ShowCountriesMap()
	{
		state_color_image.SetPixel(0, 0, SEA_MAIN); // ID 0: Sea
		state_color_image.SetPixel(1, 0, Colors.Black); // ID 1: Borders/Grid

		foreach (var pid in _provinces.Keys)
		{
			if (pid <= 1)
				continue;

			var province = _provinces[pid];
			state_color_image.SetPixel(pid, 0, GetCountryDisplayColor(province.country));
			state_color_image.SetPixel(pid, 1, GetCountryDisplayColor(province.GetFunctionalOwner()));
		}

		state_color_texture.Update(state_color_image);
	}

	private void _ShowResourcesMap()
	{
		state_color_image.SetPixel(0, 0, SEA_MAIN); // ID 0: Sea
		state_color_image.SetPixel(1, 0, Colors.Black); // ID 1: Borders/Grid

		foreach (var pid in _provinces.Keys)
		{
			if (pid <= 1)
				continue;

			var province = _provinces[pid];
			state_color_image.SetPixel(pid, 0, GetCountryDisplayColor(province.country));
			state_color_image.SetPixel(pid, 1, GetCountryDisplayColor(province.GetFunctionalOwner()));
		}

		state_color_texture.Update(state_color_image);
	}

	public void _ShowBiomesMap()
	{
		state_color_image.SetPixel(0, 0, SEA_MAIN); // ID 0: Sea
		state_color_image.SetPixel(1, 0, Colors.Black); // ID 1: Borders/Grid

		foreach (var pid in _provinces.Keys)
		{
			if (pid <= 1)
				continue;

			var province = _provinces[pid];
			var biomeColor = biomes.TryGetValue(province.biome, out var biomeValue)
				? ((BiomeData)biomeValue.AsGodotObject()).color
				: Colors.AntiqueWhite;

			state_color_image.SetPixel(pid, 0, biomeColor);
			state_color_image.SetPixel(pid, 1, biomeColor);
		}

		state_color_texture.Update(state_color_image);
	}

	public void ProvinceUpdated()
	{
		if (IndustryBuilding() != 0) // GDScript: if GameState.industry_building
			ShowIndustryCountry(CountryData.NameOf(CountryManager.Instance.player_country));
	}

	public void ShowIndustryCountry(string country_name)
	{
		if (!country_to_provinces.ContainsKey(country_name))
		{
			GD.PushWarning("MapManager: Country " + country_name + " not found.");
			return;
		}

		var provinces_near_sea = GetProvincesNearSea(country_name);

		foreach (var pidVariant in GetArray(country_to_provinces, country_name))
		{
			var pid = pidVariant;
			var province = _provinces[pid];
			var color = Colors.White; // Default color

			// TODO(sockmit2007): This whole section has to be reworked.
			if (province.city.Length > 0)
				color = Colors.Yellow;
			//elif province.factory == province.FACTORY_BUILT:
			//	color = Color.GREEN
			else if (EconomyManager.Instance.is_province_building(pid))
				color = Colors.Orange; // Show progress
			//elif province.port == province.PORT_BUILT:
			//	color = Color.BLUE
			//elif province.port == province.PORT_BUILDING:
			//	color = Color.CYAN # Show progress
			else if (Array.IndexOf(provinces_near_sea, pid) >= 0)
				color = Colors.LightSkyBlue;

			state_color_image.SetPixel(pid, 0, color);
			state_color_image.SetPixel(pid, 1, color);
		}

		state_color_texture.Update(state_color_image);
	}

	public void TransferOwnership(int pid, string new_owner_name)
	{
		var province = _provinces[pid];
		var old_owner_name = province.country;
		var oldControllerName = province.GetFunctionalOwner();
		var countries = CountryManager.Instance.countries;

		if (EconomyManager.Instance.construction_queue.TryGetValue(pid, out var project))
			project.AsGodotDictionary()["country"] = CountryManager.Instance.GetCountry(new_owner_name);

		if (_provinces.ContainsKey(pid))
		{
			province.country = new_owner_name;
			if (old_owner_name == oldControllerName && old_owner_name != "Sea")
			{
				countries.TryGetValue(oldControllerName, out var oldControllerValue);
				var oldCountry = oldControllerValue.AsGodotObject();
				if (oldCountry != null)
				{
					oldCountry.Set("total_population", oldCountry.Get("total_population").AsInt32() - province.GetPopulation());
					oldCountry.Set("factories_amount", oldCountry.Get("factories_amount").AsInt32() - province.GetFactories());
				}
			}
			if (province.city != "" && country_to_cities.TryGetValue(oldControllerName, out var oldCities))
				oldCities.Remove(pid);
			if (province.city != "")
				GetOrAddArray(country_to_cities, new_owner_name).Add(pid);
			GetOrAddArray(allowed_pids, new_owner_name).Add(pid);
			var newCountry = CountryManager.Instance.GetCountry(new_owner_name);
			newCountry.Set("total_population", newCountry.Get("total_population").AsInt32() + province.GetPopulation());
			newCountry.Set("factories_amount", newCountry.Get("factories_amount").AsInt32() + province.GetFactories());
		}
		else
		{
			GD.PushError("MapManager: Attempted to transfer ownership of non-existent PID: ", pid);
			return;
		}

		if (country_to_occupied_provinces.TryGetValue(province.occupier, out var occupiedList))
			occupiedList.Remove(pid);

		if (country_to_owned_provinces.TryGetValue(old_owner_name, out var ownedList))
			ownedList.Remove(pid);

		if (country_to_rightful_provinces.TryGetValue(old_owner_name, out var rightfulList))
			rightfulList.Remove(pid);

		if (country_to_provinces.TryGetValue(oldControllerName, out var controllerList))
			controllerList.Remove(pid);

		if (!country_to_provinces.ContainsKey(new_owner_name))
			country_to_provinces[new_owner_name] = new List<int>();

		if (!country_to_owned_provinces.ContainsKey(new_owner_name))
			country_to_owned_provinces[new_owner_name] = new List<int>();

		if (!country_to_provinces[new_owner_name].Contains(pid))
			country_to_provinces[new_owner_name].Add(pid);

		if (!country_to_owned_provinces[new_owner_name].Contains(pid))
			country_to_owned_provinces[new_owner_name].Add(pid);

		province.occupier = "";

		UpdateLookup(
			pid,
			CountryColor(new_owner_name, Colors.Gray),
			CountryColor(new_owner_name, Colors.Gray));
		EmitSignal(SignalName.ProvinceOwnershipChanged, pid, old_owner_name, new_owner_name);
	}

	public void OccupyProvince(int pid, string new_owner_name)
	{
		var province = _provinces[pid];
		var old_owner_name = province.country;
		var oldControllerName = province.GetFunctionalOwner();
		var countries = CountryManager.Instance.countries;

		if (_provinces.ContainsKey(pid))
		{
			province.occupier = new_owner_name;
			if (province.city != "" && country_to_cities.TryGetValue(oldControllerName, out var oldCities))
				oldCities.Remove(pid);
		}
		else
		{
			GD.PushError("MapManager: Attempted to transfer ownership of non-existent PID: ", pid);
			return;
		}

		if (country_to_provinces.TryGetValue(oldControllerName, out var controllerList))
			controllerList.Remove(pid);

		if (country_to_occupied_provinces.TryGetValue(oldControllerName, out var occupiedOld))
			occupiedOld.Remove(pid);

		if (!country_to_provinces.ContainsKey(new_owner_name))
			country_to_provinces[new_owner_name] = new List<int>();

		if (!country_to_provinces[new_owner_name].Contains(pid))
			country_to_provinces[new_owner_name].Add(pid);

		if (province.city != "")
			GetOrAddArray(country_to_cities, new_owner_name).Add(pid);

		if (province.occupier == old_owner_name)
		{
			province.occupier = "";
			var newCountry = CountryManager.Instance.GetCountry(new_owner_name);
			newCountry.Set("total_population", newCountry.Get("total_population").AsInt32() + province.GetPopulation());
			newCountry.Set("factories_amount", newCountry.Get("factories_amount").AsInt32() + province.GetFactories());

			// NOTE(soi): preserved from original: erase(pid), not erase(old_owner_name)
		}
		else
		{
			if (oldControllerName == old_owner_name && oldControllerName != "Sea")
			{
				var oldCountry = CountryManager.Instance.GetCountry(old_owner_name);
				oldCountry.Set("total_population", oldCountry.Get("total_population").AsInt32() - province.GetPopulation());
				oldCountry.Set("factories_amount", oldCountry.Get("factories_amount").AsInt32() - province.GetFactories());
			}

			if (!country_to_rightful_provinces.ContainsKey(old_owner_name))
				country_to_rightful_provinces[old_owner_name] = new List<int>();

			if (!country_to_rightful_provinces[old_owner_name].Contains(pid))
				country_to_rightful_provinces[old_owner_name].Add(pid);

			if (!country_to_occupied_provinces.ContainsKey(new_owner_name))
				country_to_occupied_provinces[new_owner_name] = new List<int>();

			if (!country_to_occupied_provinces[new_owner_name].Contains(pid))
				country_to_occupied_provinces[new_owner_name].Add(pid);
		}

		UpdateLookup(pid,
			CountryColor(province.country, Colors.Gray),
			CountryColor(province.GetFunctionalOwner(), Colors.Gray));
		EmitSignal(SignalName.ProvinceOwnershipChanged, pid, oldControllerName, new_owner_name);
	}

	public void DeoccupyProvince(int pid)
	{
		var province = _provinces[pid];
		var old_controller = province.GetFunctionalOwner();

		if (province.occupier != "")
		{
			if (province.city != "" && country_to_cities.TryGetValue(province.occupier, out var occupierCities))
				occupierCities.Remove(pid);
			if (province.country != "Sea")
			{
				var country = CountryManager.Instance.GetCountry(province.country);
				country.Set("total_population", country.Get("total_population").AsInt32() + province.GetPopulation());
				country.Set("factories_amount", country.Get("factories_amount").AsInt32() + province.GetFactories());
				if (province.city != "")
					GetOrAddArray(country_to_cities, province.country).Add(pid);
			}

			if (country_to_provinces.TryGetValue(province.occupier, out var occupierProvinces))
				occupierProvinces.Remove(pid);

			if (country_to_occupied_provinces.TryGetValue(province.occupier, out var occupierOccupied))
				occupierOccupied.Remove(pid);

			// NOTE(soi): preserved from original: erase(pid), not erase(province.country)

			if (!country_to_provinces.ContainsKey(province.country))
				country_to_provinces[province.country] = new List<int>();

			if (!country_to_provinces[province.country].Contains(pid))
				country_to_provinces[province.country].Add(pid);
		}

		province.occupier = "";

		UpdateLookup(pid,
			CountryColor(province.country, Colors.Gray),
			CountryColor(province.GetFunctionalOwner(), Colors.Gray));
		EmitSignal(SignalName.ProvinceOwnershipChanged, pid, old_controller, province.country);
	}

	private Vector3 _parse_color_string(string s)
	{
		var parts = s.Replace("(", "").Replace(")", "").Replace(" ", "").Split(',');
		return new Vector3(
			float.Parse(parts[0], CultureInfo.InvariantCulture),
			float.Parse(parts[1], CultureInfo.InvariantCulture),
			float.Parse(parts[2], CultureInfo.InvariantCulture));
	}

	// Helper to ensure we never return a single String when an Array is expected
	private Godot.Collections.Array _force_array(Variant data)
	{
		if (data.VariantType == Variant.Type.Array)
			return data.AsGodotArray();
		if (data.VariantType == Variant.Type.String)
			return new Godot.Collections.Array { data.AsString() }; // Wrap the single country in a list
		return new Godot.Collections.Array();
	}

	public int[] GetProvincesNearSea(string country_name)
	{
		var provinces_near_sea = new List<int>();

		foreach (var pidVariant in GetArray(country_to_provinces, country_name))
		{
			var pid = pidVariant;
			foreach (var neighborIdVariant in province_graph.GetPointConnections(pid))
			{
				var neighbor_id = (int)neighborIdVariant;
				_provinces.TryGetValue(neighbor_id, out var neighbor_province);

				if (neighbor_province != null && neighbor_province.type == 0) // Assuming 0 is SEA
				{
					provinces_near_sea.Add(pid);
					break;
				}
			}
		}

		return provinces_near_sea.ToArray();
	}

	// Returns an array of province IDs that are on the border of a different country
	public int[] GetBorderProvinces(string country_name)
	{
		var border_provinces = new List<int>();

		// Get all provinces owned by this country
		foreach (var provIdVariant in GetArray(country_to_provinces, country_name))
		{
			var prov_id = provIdVariant;
			_provinces.TryGetValue(prov_id, out var province_data);

			if (province_data == null)
				continue;

			// Province.cs currently has no `neighbors` field; fall back to graph connections.
			var neighborIds = new Godot.Collections.Array();
			foreach (var connection in province_graph.GetPointConnections(prov_id))
				neighborIds.Add(connection);

			// Check neighbors of this province
			foreach (var neighborIdVariant in neighborIds)
			{
				var neighbor_id = (int)neighborIdVariant;
				// If the neighbor is owned by someone else (and isn't sea/neutral)
				if (_provinces.TryGetValue(neighbor_id, out var neighborProvince)
					&& neighborProvince.GetFunctionalOwner() != country_name)
				{
					border_provinces.Add(prov_id);
					break; // Move to next province once we know this one is a border
				}
			}
		}

		return border_provinces.ToArray();
	}

	public Godot.Collections.Array<Godot.Collections.Dictionary> GetAllReleasables(string my_country)
	{
		var releasables = new Godot.Collections.Array<Godot.Collections.Dictionary>();

		// 1. Get a list of all province IDs I currently own
		var my_provinces = new List<int>();
		foreach (var objVariant in _provinces.Values)
		{
			var obj = objVariant;
			// Using 'country' as per your Province resource
			if (obj.country == my_country)
				my_provinces.Add(obj.id);
		}

		// 2. Check every country in the registry
		foreach (var potentialCountry in global_claims_registry.Keys)
		{
			var potential_country = potentialCountry;
			if (potential_country == my_country)
				continue;

			var required_provinces = global_claims_registry[potentialCountry];

			var owned_provinces = new List<int>();

			// 3. Verify I own every province they claim
			foreach (var pIdVariant in required_provinces)
			{
				var p_id = pIdVariant;
				if (my_provinces.Contains(p_id))
					owned_provinces.Add(p_id);
			}

			// no province and country exitsts -> don care
			// no province and country not exitsts -> don care
			// province and country exitsts -> return
			// province and country not exitsts -> release
			if (owned_provinces.Count == 0)
				continue;

			releasables.Add(new Godot.Collections.Dictionary
			{
				{ "country", potential_country },
				{ "owned_provinces", owned_provinces.ToArray() },
			});
		}

		return releasables;
	}

	public bool _country_exists_on_map(string c_name)
	{
		foreach (var objVariant in _provinces.Values)
		{
			var obj = objVariant;
			if (obj.country == c_name)
				return true;
		}
		return false;
	}

	public void ReleaseCountry(string a_countryName)
	{
		CountryManager.Instance.add_country(new Godot.Collections.Dictionary { { "name", a_countryName } });

		foreach (var objVariant in _provinces.Values)
		{
			var obj = objVariant;
			if (Array.IndexOf(obj.claims, a_countryName) >= 0)
			{
				foreach (var troopVariant in ProvinceTroops(obj.id))
				{
					var troop = troopVariant.AsGodotObject();
					if (GodotObject.IsInstanceValid(troop))
						TroopManager.Instance.RemoveTroop(troop as TroopData);
				}

				TransferOwnership(obj.id, a_countryName);
			}
		}

		CountryManager.Instance.cleanup_empty_countries();
		ShowCountriesMap();
	}

	public void ReleasePuppet(string a_puppeter, string a_puppetee)
	{
		CountryManager.Instance.add_country(new Godot.Collections.Dictionary { { "name", a_puppetee } });

		foreach (var objVariant in _provinces.Values)
		{
			var obj = objVariant;
			if (obj.claims.Length == 1 && obj.claims[0] == a_puppetee)
				TransferOwnership(obj.id, a_puppetee);
		}

		var countries = CountryManager.Instance.countries;
		CountryManager.Instance.make_puppet(
			countries.ContainsKey(a_puppeter) ? countries[a_puppeter].AsGodotObject() : null,
			countries.ContainsKey(a_puppetee) ? countries[a_puppetee].AsGodotObject() : null);
		CountryManager.Instance.cleanup_empty_countries();
		ShowCountriesMap();
	}

	public void InstantiateCountryFromClaims(Godot.Collections.Dictionary a_countryData)
	{
		CountryManager.Instance.add_country(a_countryData);
		var name = a_countryData.ContainsKey("name") ? a_countryData["name"].AsString() : "";

		foreach (var objVariant in _provinces.Values)
		{
			var obj = objVariant;
			if (Array.IndexOf(obj.claims, name) >= 0)
			{
				foreach (var troopVariant in ProvinceTroops(obj.id))
				{
					var troop = troopVariant.AsGodotObject();
					if (GodotObject.IsInstanceValid(troop))
						TroopManager.Instance.RemoveTroop(troop as TroopData);
				}

				TransferOwnership(obj.id, name);
			}
		}

		CountryManager.Instance.cleanup_empty_countries();
		ShowCountriesMap();
	}

	public GodotObject InstantiateCountryFromProvinces(Godot.Collections.Dictionary a_countryData, int[] a_claims)
	{
		var countryer = CountryManager.Instance.add_country(a_countryData);
		var name = a_countryData.ContainsKey("name") ? a_countryData["name"].AsString() : "";
		foreach (var pid in a_claims)
		{
			foreach (var troopVariant in ProvinceTroops(pid))
			{
				var troop = troopVariant.AsGodotObject();
				if (GodotObject.IsInstanceValid(troop))
					TroopManager.Instance.RemoveTroop(troop as TroopData);
			}

			TransferOwnership(pid, name);
		}

		CountryManager.Instance.cleanup_empty_countries();
		ShowCountriesMap();
		return countryer;
	}

	public Godot.Collections.Array<Godot.Collections.Dictionary> GetAllCities()
	{
		var pids = new Godot.Collections.Array<Godot.Collections.Dictionary>();
		foreach (var objVariant in _provinces.Values)
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

	public Godot.Collections.Array GetCitiesProvinceCountry(string country_name)
	{
		var provinces = new Godot.Collections.Array();
		foreach (var pidVariant in GetArray(country_to_provinces, country_name))
		{
			var pid = pidVariant;
			var province = _provinces[pid];
			if (province.GetFunctionalOwner() == country_name && province.city.Length > 0)
				provinces.Add(pid);
		}
		return provinces;
	}

	// Returns provinces that specifically border a certain enemy
	public int[] GetProvincesBorderingEnemies(string country_name, Godot.Collections.Array<string> enemies)
	{
		var specific_borders = new List<int>();

		var enemy_dict = new Godot.Collections.Dictionary();
		foreach (var enemy in enemies)
			enemy_dict[enemy] = true;

		foreach (var provIdVariant in GetArray(country_to_provinces, country_name))
		{
			var prov_id = provIdVariant;
			foreach (var neighborIdVariant in province_graph.GetPointConnections(prov_id))
			{
				var neighbor_id = (int)neighborIdVariant;
				var neighbor_prov = _provinces[neighbor_id];
				var prov_owner = neighbor_prov.occupier != "" ? neighbor_prov.occupier : neighbor_prov.country;
				if (enemy_dict.ContainsKey(prov_owner))
				{
					specific_borders.Add(prov_id);
					break;
				}
			}
		}

		return specific_borders.ToArray();
	}

	public void AnnexCountry(string annexer, string annexee)
	{
		//var playerobj = CountryManager.player_country
		foreach (var troopVariant in TroopManager.Instance.get_troops_for_country(annexee))
		{
			TroopManager.Instance.RemoveTroop(troopVariant.As<TroopData>());
		}

		foreach (var pidVariant in GetArray(country_to_occupied_provinces, annexee))
			DeoccupyProvince(pidVariant);

		var provinces_to_transfer = GetArray(country_to_owned_provinces, annexee);

		if (provinces_to_transfer.Count == 0)
		{
			GD.Print("MapManager: No provinces found for ", annexee);
			return;
		}

		foreach (var pidVariant in provinces_to_transfer.ToArray())
			TransferOwnership(pidVariant, annexer);

		//playerobj.reset_manpower()
		GD.Print("ANNEXATION COMPLETE: ", annexer, " has taken all of ", annexee);
	}

	public void _BuildGlobalRegistry()
	{
		global_claims_registry.Clear();
		foreach (var objVariant in _provinces.Values)
		{
			var obj = objVariant;
			foreach (var country_name in obj.claims)
			{
				if (!global_claims_registry.TryGetValue(country_name, out var claims))
					global_claims_registry[country_name] = claims = new List<int>();
				claims.Add(obj.id);
			}
		}
	}

	public void AllowPids(GodotObject accesser, GodotObject accessee)
	{
		var accesserName = CountryData.NameOf(accesser);
		var accesseeName = CountryData.NameOf(accessee);
		if (!allowed_pids.ContainsKey(accesserName))
			allowed_pids[accesserName] = new List<int>();
		foreach (var province in GetArray(country_to_provinces, accesseeName))
			allowed_pids[accesserName].Add(province);
	}

	public void UnallowPids(GodotObject unaccesser, GodotObject unaccessee)
	{
		var unaccesserName = CountryData.NameOf(unaccesser);
		var unaccesseeName = CountryData.NameOf(unaccessee);
		foreach (var province in GetArray(country_to_provinces, unaccesseeName))
		{
			if (allowed_pids.TryGetValue(unaccesserName, out var accessList))
				accessList.Remove(province);
		}
	}

	public void ChangeProvinceTypes(Godot.Collections.Array pids, int type, string country_name = "")
	{
		foreach (var pidVariant in pids)
		{
			var pid = pidVariant.AsInt32();
			var province = _provinces[pid];
			province.type = type;
			province.country = country_name;
			GD.Print(province.type);
			GD.Print(province.country);
		}
		var world = GameState.Instance.current_world;
		if (world != null)
		{
			_SetTypeMap(
				world.Get("mat").AsGodotObject() as Material,
				world.Get("type_img").AsGodotObject() as Image);
		}
	}

	public void _SetTypeMap(Material mat, Image type_img)
	{
		var uncertain_pixels = new List<Vector2I>();

		// --- PASS 1: Direct Mapping ---
		for (var i = 0; i < MAP_HEIGHT * MAP_WIDTH; i++)
		{
			var x = i % MAP_WIDTH;
			var y = i / MAP_WIDTH;
			var pid = _get_pid_fast(x, y);
			_provinces.TryGetValue(pid, out var province);

			if (province != null)
				type_img.SetPixel(x, y, new Color(province.type, province.type, province.type));
			else
				// It's a border (PID 1 or null). Mark as uncertain for now.
				uncertain_pixels.Add(new Vector2I(x, y));
		}

		// --- PASS 2: Intelligent Flood-Check ---
		foreach (var pos in uncertain_pixels)
		{
			var touches_land = false;
			//var touches_sea = false

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
							_provinces.TryGetValue(nid, out var n_prov);
							if (n_prov != null && n_prov.type != 0)
								touches_land = true;
							//else:
							//	touches_sea = true
						}
					}
				}
			}

			if (touches_land)
				type_img.SetPixel(pos.X, pos.Y, new Color(1, 1, 1));
			else
				// If it only touches sea (or nothing), it's a Sea Grid/Open Water
				type_img.SetPixel(pos.X, pos.Y, new Color(0, 0, 0));
		}
		((ShaderMaterial)mat).SetShaderParameter("type_map", ImageTexture.CreateFromImage(type_img));
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

	public MapView current_view = MapView.COUNTRIES;

	public void CycleMapMode(bool shift)
	{
		var size = Enum.GetValues(typeof(MapView)).Length;
		SetMapMode((MapView)(((int)current_view + (shift ? 1 : -1) + size) % size));
	}

	public void SetMapMode(MapView a_mapView)
	{
		current_view = a_mapView;
		EmitSignal(SignalName.MapViewChanged, (int)current_view);
		switch (a_mapView)
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

	private static Color CountryColor(string country_name, Color fallback)
		=> CountryManager.Instance.GetCountryColor(country_name, fallback);

	private static int InteractionType()
		=> (int)GameState.Instance.interactionType;

	private static int IndustryBuilding()
		=> (int)GameState.Instance.industry_building;

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

	private static Godot.Collections.Array ProvinceTroops(int pid)
	{
		var result = new Godot.Collections.Array();
		if (TroopManager.Instance.troops_by_province.TryGetValue(pid, out var troops))
			foreach (var troop in troops)
				result.Add(troop);
		return result;
	}
}

using System;
using Godot;
using Sosr;

namespace Sosr;

public partial class MusicManager : Node
{
	public static MusicManager Instance => Autoload.Get<MusicManager>("MusicManager");

	// GDScript cannot see C# nested enums. Keep the numeric values identical so
	// GDScript call sites (and the other C# managers) can pass plain ints.
	public enum SFX
	{
		TROOP_MOVE,
		TROOP_SELECTED,
		BATTLE_START,
		OPEN_MENU,
		DECLARE_WAR,
		HOVERED,
		CLOSE_MENU,
		GAME_OVER,
		POPUP,
		BUILD,
		CLAPPING,
	}

	public enum MUSIC { MAIN_THEME, BATTLE_THEME }

	// GDScript connects to `trackChanged`; the C# signal is `TrackChanged`.
	[Signal]
	public delegate void TrackChangedEventHandler(string trackName);

	public const string default_music_path = "res://assets/music/";
	public const string custom_music_path = "res://radios/";
	private static readonly bool USE_INTERACTIVE = false; // Fallback to manual shuffle if interactive fails

	public int current_track_type = -1;

	private AudioStream _interactive_stream = null;

	public AudioStreamPlayer music_player => GetNodeOrNull<AudioStreamPlayer>("/root/Main/Audio/Music");

	public Godot.Collections.Array<AudioStreamPlayer> sfx_players
	{
		get
		{
			var players = new Godot.Collections.Array<AudioStreamPlayer>();
			var sfx_node = GetNodeOrNull("/root/Main/Audio/SFX");
			if (sfx_node is AudioStreamPlayer single)
				players.Add(single);
			if (sfx_node != null)
			{
				foreach (var child in sfx_node.GetChildren())
					if (child is AudioStreamPlayer player)
						players.Add(player);
			}
			return players;
		}
	}

	public Godot.Collections.Dictionary sfx_map = new()
	{
		{ (int)SFX.TROOP_MOVE, GD.Load<AudioStream>("res://assets/snd/moveDivSound.mp3") },
		{ (int)SFX.TROOP_SELECTED, GD.Load<AudioStream>("res://assets/snd/selectDivSound.mp3") },
		{ (int)SFX.BATTLE_START, GD.Load<AudioStream>("res://assets/snd/startSound.mp3") },
		{ (int)SFX.OPEN_MENU, GD.Load<AudioStream>("res://assets/snd/openMenuSound.mp3") },
		{ (int)SFX.DECLARE_WAR, GD.Load<AudioStream>("res://assets/snd/declareWarSound.mp3") },
		{ (int)SFX.HOVERED, GD.Load<AudioStream>("res://assets/snd/hoveredSound.mp3") },
		{ (int)SFX.CLOSE_MENU, GD.Load<AudioStream>("res://assets/snd/closeMenuSound.mp3") },
		{ (int)SFX.GAME_OVER, GD.Load<AudioStream>("res://assets/snd/endGameSound.mp3") },
		{ (int)SFX.POPUP, GD.Load<AudioStream>("res://assets/snd/popupSound.mp3") },
		{ (int)SFX.BUILD, GD.Load<AudioStream>("res://assets/snd/buildSound.mp3") },
		{ (int)SFX.CLAPPING, GD.Load<AudioStream>("res://assets/snd/clappingSound.mp3") },
	};

	public Godot.Collections.Dictionary music_map = new()
	{
		{ (int)MUSIC.MAIN_THEME, new Godot.Collections.Dictionary() },
		{ (int)MUSIC.BATTLE_THEME, new Godot.Collections.Dictionary() },
	};

	public Godot.Collections.Array<string> radios = new() { "default" };

	public Godot.Collections.Dictionary music_volume_map = new()
	{
		{ (int)MUSIC.MAIN_THEME, 1.0f },
		{ (int)MUSIC.BATTLE_THEME, 1.0f },
	};

	public int last_track_type = (int)MUSIC.MAIN_THEME;
	public bool locking_custom_track = false;

	private static Godot.Collections.Dictionary radio_map(Godot.Collections.Dictionary map, int track)
		=> map.ContainsKey(track) ? map[track].AsGodotDictionary() : null;

	private static float setting_volume(string key)
	{
		var settings = SettingsManager.Instance?.settings;
		if (settings == null)
			return 1.0f;
		return settings.GetValue("Sound", key).AsSingle();
	}

	private static void AddSongs(Godot.Collections.Array<AudioStream> songs, Godot.Collections.Dictionary radioMap, Godot.Collections.Array<string> radioList)
	{
		foreach (var radio in radioList)
		{
			if (radioMap == null || !radioMap.ContainsKey(radio))
				continue;
			var group = radioMap[radio].AsGodotArray();
			foreach (var song in group)
			{
				var stream = song.AsGodotObject() as AudioStream;
				if (stream != null)
					songs.Add(stream);
			}
		}
	}

	public override void _Ready()
	{
		var default_dir = DirAccess.Open(default_music_path);
		if (default_dir != null)
		{
			foreach (var radio in default_dir.GetDirectories())
			{
				if (radio == "superevents")
					continue;
				foreach (var track in new[] { (int)MUSIC.MAIN_THEME, (int)MUSIC.BATTLE_THEME })
					((Godot.Collections.Dictionary)music_map[track])[radio] = new Godot.Collections.Array<AudioStream>();
				_load_music_folder(default_music_path, radio, (int)MUSIC.MAIN_THEME);
				_load_music_folder(default_music_path, radio, (int)MUSIC.BATTLE_THEME);
				if (!radios.Contains(radio))
					radios.Add(radio);
			}
		}

		var custom_dir = DirAccess.Open(custom_music_path);
		if (custom_dir != null)
		{
			foreach (var radio in custom_dir.GetDirectories())
			{
				if (radio == "superevents")
					continue;
				foreach (var track in new[] { (int)MUSIC.MAIN_THEME, (int)MUSIC.BATTLE_THEME })
					((Godot.Collections.Dictionary)music_map[track])[radio] = new Godot.Collections.Array<AudioStream>();
				_load_music_folder(custom_music_path, radio, (int)MUSIC.MAIN_THEME);
				_load_music_folder(custom_music_path, radio, (int)MUSIC.BATTLE_THEME);
				if (!radios.Contains(radio))
					radios.Add(radio);
			}
		}

		// warDeclared passes (attacker, defender); drop them before calling.
		var wars = WarManager.Instance;
		if (wars != null)
		{
			wars.WarDeclared += (_, _) =>
			{
				play_music((int)MUSIC.BATTLE_THEME);
				play_sfx((int)SFX.DECLARE_WAR);
			};
		}

		// Audio.gd calls setup_audio() when it is ready; the scene tree isn't ready here.
	}

	public void setup_audio()
	{
		var player = music_player;
		if (player == null)
		{
			GD.PushError("MusicManager: setup_audio called but music_player is still null!");
			return;
		}

		GD.Print("MusicManager: Setting up audio system...");
		var finishedCallback = Callable.From(_on_music_finished);
		if (!player.IsConnected(AudioStreamPlayer.SignalName.Finished, finishedCallback))
			player.Connect(AudioStreamPlayer.SignalName.Finished, finishedCallback);
		if (_interactive_stream == null)
		{
			_interactive_stream = player.Stream;
			GD.Print("MusicManager: Captured interactive stream: ", _interactive_stream);
		}
		update_interactive_playlists();
		if (current_track_type == -1)
			play_music((int)MUSIC.MAIN_THEME);
		else
			play_music(current_track_type);

		debug_audio_status();
	}

	public void debug_audio_status()
	{
		GD.Print("--- AUDIO DIAGNOSTIC ---");
		GD.Print("Master Bus Volume: ", AudioServer.GetBusVolumeDb(0), " Muted: ", AudioServer.IsBusMute(0));
		var music_bus_idx = AudioServer.GetBusIndex("Music");
		if (music_bus_idx != -1)
			GD.Print("Music Bus Volume: ", AudioServer.GetBusVolumeDb(music_bus_idx), " Muted: ", AudioServer.IsBusMute(music_bus_idx));
		else
			GD.Print("Music Bus NOT FOUND!");

		var m_player = music_player;
		if (m_player != null)
		{
			GD.Print("Music Player Node: ", m_player.GetPath());
			GD.Print("Music Player Stream: ", m_player.Stream);
			GD.Print("Music Player Playing: ", m_player.Playing);
			GD.Print("Music Player Volume DB: ", m_player.VolumeDb);
			GD.Print("Music Player Bus: ", m_player.Bus);
		}
		else
			GD.Print("Music Player Node NOT FOUND at path '/root/Main/Audio/Music'");
		GD.Print("-----------------------");
	}

	public void debug_play_direct()
	{
		GD.Print("MusicManager: Attempting direct playback test...");
		var m_player = music_player;
		if (m_player == null)
			return;
		var test_path = "res://assets/music/default/gameMusic/Miyasan_Miyasan.mp3";
		var stream = ResourceLoader.Load(test_path) as AudioStream;
		if (stream != null)
		{
			GD.Print("MusicManager: Loaded test stream: ", test_path);
			m_player.Stream = stream;
			m_player.VolumeDb = 0; // Full volume
			m_player.Play();
			GD.Print("MusicManager: Called play() on test stream.");
		}
		else
			GD.Print("MusicManager: FAILED to load test stream: ", test_path);
	}

	public void update_interactive_playlists()
	{
		var m_player = music_player;
		if (m_player == null)
			return;

		AudioStreamInteractive interactive = null;
		if (m_player.Stream is AudioStreamInteractive streamInteractive)
			interactive = streamInteractive;
		else if (_interactive_stream is AudioStreamInteractive savedInteractive)
			interactive = savedInteractive;

		if (interactive == null)
		{
			GD.Print("MusicManager: No AudioStreamInteractive found to update!");
			return;
		}

		GD.Print("MusicManager: Updating interactive playlists for radios: ", string.Join(", ", radios));
		for (var clip_idx = 0; clip_idx < interactive.ClipCount; clip_idx++)
		{
			var clip_name = interactive.GetClipName(clip_idx);
			var playlist = interactive.GetClipStream(clip_idx) as AudioStreamPlaylist;
			if (playlist == null)
				continue;

			var target_track = -1;
			if (clip_name.ToString() == "Main")
				target_track = (int)MUSIC.MAIN_THEME;
			else if (clip_name.ToString() == "War")
				target_track = (int)MUSIC.BATTLE_THEME;

			if (target_track != -1)
			{
				var songs = new Godot.Collections.Array<AudioStream>();
				AddSongs(songs, radio_map(music_map, target_track), radios);

				if (songs.Count > 0)
				{
					var new_playlist = new AudioStreamPlaylist
					{
						StreamCount = songs.Count,
						Loop = true,
						Shuffle = true,
					};
					for (var i = 0; i < songs.Count; i++)
						new_playlist.SetListStream(i, songs[i]);

					interactive.SetClipStream(clip_idx, new_playlist);
					GD.Print($"MusicManager: Assigned fresh playlist to '{clip_name}' with {songs.Count} songs.");
				}
				else
					GD.Print($"MusicManager: No songs found for radio group '{target_track}'");
			}
		}

		// Force refresh the playback by re-assigning the stream
		if (m_player.Stream == interactive || m_player.Stream == _interactive_stream)
		{
			m_player.Stream = null; // Temporarily clear to force rebuild
			m_player.Stream = interactive;
			if (current_track_type != -1)
				play_music(current_track_type);
			GD.Print("MusicManager: Forced stream refresh for AudioStreamInteractive.");
		}
	}

	public void _load_music_folder(string path, string radio, int track_enum)
	{
		path += radio;
		switch (track_enum)
		{
			case (int)MUSIC.MAIN_THEME:
				path += "/gameMusic";
				break;
			case (int)MUSIC.BATTLE_THEME:
				path += "/warMusic";
				break;
		}

		var dir = DirAccess.Open(path);
		if (dir == null)
			return;
		dir.ListDirBegin();
		var file_name = dir.GetNext();
		while (file_name != "")
		{
			if (!dir.CurrentIsDir() && !file_name.EndsWith(".import", StringComparison.Ordinal))
			{
				var stream = ResourceLoader.Load(path + "/" + file_name) as AudioStream;
				if (stream != null)
					((Godot.Collections.Dictionary)music_map[track_enum])[radio].AsGodotArray().Add(stream);
			}
			file_name = dir.GetNext();
		}
	}

	public void play_music(int track)
	{
		// If a custom track is playing and locked, ignore normal music requests
		if (locking_custom_track)
		{
			if (track == (int)MUSIC.MAIN_THEME || track == (int)MUSIC.BATTLE_THEME)
				last_track_type = track;
			return;
		}

		var radioMap = radio_map(music_map, track);
		if (radioMap == null || radioMap.Count == 0)
			return;

		var m_player = music_player;
		if (m_player == null)
			return;

		if (_interactive_stream == null && m_player.Stream is AudioStreamInteractive)
		{
			_interactive_stream = m_player.Stream;
			update_interactive_playlists();
		}

		// Store as last track if it's a valid standard track
		if (track == (int)MUSIC.MAIN_THEME || track == (int)MUSIC.BATTLE_THEME)
			last_track_type = track;

		if (current_track_type == track && m_player.Playing)
			return;

		current_track_type = track;

		if (USE_INTERACTIVE)
		{
			m_player.Play();
			GD.Print("MusicManager: Started m_player playback.");

			EmitSignal(SignalName.TrackChanged, m_player.Stream.ResourcePath.GetFile());

			var playback = m_player.GetStreamPlayback() as AudioStreamPlaybackInteractive;
			if (playback != null)
			{
				if (track == (int)MUSIC.MAIN_THEME)
					playback.SwitchToClipByName("Main");
				else if (track == (int)MUSIC.BATTLE_THEME)
					playback.SwitchToClipByName("War");
			}
		}
		else
		{
			// Manual Fallback Logic
			var songs = new Godot.Collections.Array<AudioStream>();
			AddSongs(songs, radioMap, radios);

			if (songs.Count > 0)
			{
				var song = songs[GD.RandRange(0, songs.Count - 1)];
				GD.Print("MusicManager: Manual Fallback - Playing song: ", song.ResourcePath.GetFile());
				EmitSignal(SignalName.TrackChanged, song.ResourcePath.GetFile());
				m_player.Stream = song;
				m_player.Play();
			}
			else
				GD.Print("MusicManager: Manual Fallback - No songs found!");
		}

		var vol = music_volume_map.ContainsKey(track) ? music_volume_map[track].AsSingle() : 1.0f;
		vol *= setting_volume("music_volume");
		m_player.VolumeDb = Mathf.LinearToDb(vol);
		GD.Print($"MusicManager: Playing track {track} at volume linear: {vol} (db: {m_player.VolumeDb})");
	}

	public void play_custom_file(string full_path)
	{
		if (!FileAccess.FileExists(full_path) && !ResourceLoader.Exists(full_path))
		{
			GD.PushWarning("MusicManager: Custom file not found: " + full_path);
			return;
		}

		// Stop standard shuffle
		current_track_type = -1;

		var stream = ResourceLoader.Load(full_path) as AudioStream;
		if (stream != null)
		{
			var m_player = music_player;
			if (m_player != null)
			{
				locking_custom_track = true;
				if (_interactive_stream == null)
					_interactive_stream = m_player.Stream;
				GD.Print($"MusicManager: Playing custom file: {full_path}. Locking interactive music.");
				m_player.Stream = stream;
				m_player.VolumeDb = Mathf.LinearToDb(1.0f); // Default volume for events
				m_player.Play();
			}
		}
	}

	public void resume_last_track()
	{
		locking_custom_track = false; // Ensure lock is released
		var m_player = music_player;
		if (_interactive_stream != null && m_player != null)
			m_player.Stream = _interactive_stream;
		if (last_track_type != -1)
			play_music(last_track_type);
		else
			play_music((int)MUSIC.MAIN_THEME);
	}

	public void skip_track()
	{
		if (locking_custom_track)
		{
			resume_last_track();
			return;
		}
		var m_player = music_player;
		if (m_player != null)
		{
			m_player.Stop();
			if (current_track_type != -1)
				play_music(current_track_type);
			else
				play_music((int)MUSIC.MAIN_THEME);
		}
	}

	public void _on_music_finished()
	{
		if (locking_custom_track)
		{
			resume_last_track();
			return;
		}

		if (!USE_INTERACTIVE)
		{
			// In manual mode, we need to pick the next song
			GD.Print("MusicManager: Manual Fallback - Song finished, picking next...");
			if (current_track_type != -1)
				play_music(current_track_type);
			else
				play_music((int)MUSIC.MAIN_THEME);
		}
		// In interactive mode, AudioStreamPlaylist handles it natively.
	}

	public void play_sfx(int sfx)
	{
		if (!sfx_map.ContainsKey(sfx))
			return;
		var players = sfx_players;
		if (players.Count == 0)
			return;
		AudioStreamPlayer player = null;
		foreach (var candidate in players)
		{
			if (!candidate.Playing)
			{
				player = candidate;
				break;
			}
		}
		if (player == null)
			player = players[0];
		player.Stream = sfx_map[sfx].AsGodotObject() as AudioStream;
		player.VolumeDb = Mathf.LinearToDb(1.0f * setting_volume("sfx_volume"));
		player.Play();
	}

	public void stop_all_sfx()
	{
		foreach (var p in sfx_players)
			p.Stop();
	}

	public void set_music_volume(float volume_linear)
	{
		var player = music_player;
		if (player != null)
		{
			var baseVolume = music_volume_map.ContainsKey(current_track_type)
				? music_volume_map[current_track_type].AsSingle()
				: 0.5f;
			player.VolumeDb = Mathf.LinearToDb(baseVolume * volume_linear);
		}
	}

	public void set_sfx_volume(float _volume_linear)
	{
		// No-op in the original implementation.
	}

	public void _toggle_pause()
	{
		var player = music_player;
		if (player != null)
			player.Playing = !player.Playing;
	}
}

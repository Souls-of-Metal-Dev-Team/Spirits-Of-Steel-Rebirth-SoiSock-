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

	public const string defaultMusicPath = "res://assets/music/";
	public const string customMusicPath = "res://radios/";
	private static readonly bool USE_INTERACTIVE = false; // Fallback to manual shuffle if interactive fails

	public int currentTrackType = -1;

	private AudioStream _interactive_stream = null;

	public AudioStreamPlayer musicPlayer => GetNodeOrNull<AudioStreamPlayer>("/root/Main/Audio/Music");

	public Godot.Collections.Array<AudioStreamPlayer> sfxPlayers
	{
		get
		{
			var players = new Godot.Collections.Array<AudioStreamPlayer>();
			var sfxNode = GetNodeOrNull("/root/Main/Audio/SFX");
			if (sfxNode is AudioStreamPlayer single)
				players.Add(single);
			if (sfxNode != null)
			{
				foreach (var child in sfxNode.GetChildren())
					if (child is AudioStreamPlayer player)
						players.Add(player);
			}
			return players;
		}
	}

	public Godot.Collections.Dictionary sfxMap = new()
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

	public Godot.Collections.Dictionary musicMap = new()
	{
		{ (int)MUSIC.MAIN_THEME, new Godot.Collections.Dictionary() },
		{ (int)MUSIC.BATTLE_THEME, new Godot.Collections.Dictionary() },
	};

	public Godot.Collections.Array<string> radios = new() { "default" };

	public Godot.Collections.Dictionary musicVolumeMap = new()
	{
		{ (int)MUSIC.MAIN_THEME, 1.0f },
		{ (int)MUSIC.BATTLE_THEME, 1.0f },
	};

	public int lastTrackType = (int)MUSIC.MAIN_THEME;
	public bool lockingCustomTrack = false;

	private static Godot.Collections.Dictionary RadioMap(Godot.Collections.Dictionary map, int track)
		=> map.ContainsKey(track) ? map[track].AsGodotDictionary() : null;

	private static float SettingVolume(string key)
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
		var defaultDir = DirAccess.Open(defaultMusicPath);
		if (defaultDir != null)
		{
			foreach (var radio in defaultDir.GetDirectories())
			{
				if (radio == "superevents")
					continue;
				foreach (var track in new[] { (int)MUSIC.MAIN_THEME, (int)MUSIC.BATTLE_THEME })
					((Godot.Collections.Dictionary)musicMap[track])[radio] = new Godot.Collections.Array<AudioStream>();
				_load_music_folder(defaultMusicPath, radio, (int)MUSIC.MAIN_THEME);
				_load_music_folder(defaultMusicPath, radio, (int)MUSIC.BATTLE_THEME);
				if (!radios.Contains(radio))
					radios.Add(radio);
			}
		}

		var customDir = DirAccess.Open(customMusicPath);
		if (customDir != null)
		{
			foreach (var radio in customDir.GetDirectories())
			{
				if (radio == "superevents")
					continue;
				foreach (var track in new[] { (int)MUSIC.MAIN_THEME, (int)MUSIC.BATTLE_THEME })
					((Godot.Collections.Dictionary)musicMap[track])[radio] = new Godot.Collections.Array<AudioStream>();
				_load_music_folder(customMusicPath, radio, (int)MUSIC.MAIN_THEME);
				_load_music_folder(customMusicPath, radio, (int)MUSIC.BATTLE_THEME);
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
				PlayMusic((int)MUSIC.BATTLE_THEME);
				PlaySfx((int)SFX.DECLARE_WAR);
			};
		}

		// Audio.gd calls setup_audio() when it is ready; the scene tree isn't ready here.
	}

	public void SetupAudio()
	{
		var player = musicPlayer;
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
		UpdateInteractivePlaylists();
		if (currentTrackType == -1)
			PlayMusic((int)MUSIC.MAIN_THEME);
		else
			PlayMusic(currentTrackType);

		DebugAudioStatus();
	}

	public void DebugAudioStatus()
	{
		GD.Print("--- AUDIO DIAGNOSTIC ---");
		GD.Print("Master Bus Volume: ", AudioServer.GetBusVolumeDb(0), " Muted: ", AudioServer.IsBusMute(0));
		var musicBusIdx = AudioServer.GetBusIndex("Music");
		if (musicBusIdx != -1)
			GD.Print("Music Bus Volume: ", AudioServer.GetBusVolumeDb(musicBusIdx), " Muted: ", AudioServer.IsBusMute(musicBusIdx));
		else
			GD.Print("Music Bus NOT FOUND!");

		var mPlayer = musicPlayer;
		if (mPlayer != null)
		{
			GD.Print("Music Player Node: ", mPlayer.GetPath());
			GD.Print("Music Player Stream: ", mPlayer.Stream);
			GD.Print("Music Player Playing: ", mPlayer.Playing);
			GD.Print("Music Player Volume DB: ", mPlayer.VolumeDb);
			GD.Print("Music Player Bus: ", mPlayer.Bus);
		}
		else
			GD.Print("Music Player Node NOT FOUND at path '/root/Main/Audio/Music'");
		GD.Print("-----------------------");
	}

	public void DebugPlayDirect()
	{
		GD.Print("MusicManager: Attempting direct playback test...");
		var mPlayer = musicPlayer;
		if (mPlayer == null)
			return;
		var testPath = "res://assets/music/default/gameMusic/Miyasan_Miyasan.mp3";
		var stream = ResourceLoader.Load(testPath) as AudioStream;
		if (stream != null)
		{
			GD.Print("MusicManager: Loaded test stream: ", testPath);
			mPlayer.Stream = stream;
			mPlayer.VolumeDb = 0; // Full volume
			mPlayer.Play();
			GD.Print("MusicManager: Called play() on test stream.");
		}
		else
			GD.Print("MusicManager: FAILED to load test stream: ", testPath);
	}

	public void UpdateInteractivePlaylists()
	{
		var mPlayer = musicPlayer;
		if (mPlayer == null)
			return;

		AudioStreamInteractive interactive = null;
		if (mPlayer.Stream is AudioStreamInteractive streamInteractive)
			interactive = streamInteractive;
		else if (_interactive_stream is AudioStreamInteractive savedInteractive)
			interactive = savedInteractive;

		if (interactive == null)
		{
			GD.Print("MusicManager: No AudioStreamInteractive found to update!");
			return;
		}

		GD.Print("MusicManager: Updating interactive playlists for radios: ", string.Join(", ", radios));
		for (var clipIdx = 0; clipIdx < interactive.ClipCount; clipIdx++)
		{
			var clipName = interactive.GetClipName(clipIdx);
			var playlist = interactive.GetClipStream(clipIdx) as AudioStreamPlaylist;
			if (playlist == null)
				continue;

			var targetTrack = -1;
			if (clipName.ToString() == "Main")
				targetTrack = (int)MUSIC.MAIN_THEME;
			else if (clipName.ToString() == "War")
				targetTrack = (int)MUSIC.BATTLE_THEME;

			if (targetTrack != -1)
			{
				var songs = new Godot.Collections.Array<AudioStream>();
				AddSongs(songs, RadioMap(musicMap, targetTrack), radios);

				if (songs.Count > 0)
				{
					var newPlaylist = new AudioStreamPlaylist
					{
						StreamCount = songs.Count,
						Loop = true,
						Shuffle = true,
					};
					for (var i = 0; i < songs.Count; i++)
						newPlaylist.SetListStream(i, songs[i]);

					interactive.SetClipStream(clipIdx, newPlaylist);
					GD.Print($"MusicManager: Assigned fresh playlist to '{clipName}' with {songs.Count} songs.");
				}
				else
					GD.Print($"MusicManager: No songs found for radio group '{targetTrack}'");
			}
		}

		// Force refresh the playback by re-assigning the stream
		if (mPlayer.Stream == interactive || mPlayer.Stream == _interactive_stream)
		{
			mPlayer.Stream = null; // Temporarily clear to force rebuild
			mPlayer.Stream = interactive;
			if (currentTrackType != -1)
				PlayMusic(currentTrackType);
			GD.Print("MusicManager: Forced stream refresh for AudioStreamInteractive.");
		}
	}

	public void _load_music_folder(string path, string radio, int trackEnum)
	{
		path += radio;
		switch (trackEnum)
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
		var fileName = dir.GetNext();
		while (fileName != "")
		{
			if (!dir.CurrentIsDir() && !fileName.EndsWith(".import", StringComparison.Ordinal))
			{
				var stream = ResourceLoader.Load(path + "/" + fileName) as AudioStream;
				if (stream != null)
					((Godot.Collections.Dictionary)musicMap[trackEnum])[radio].AsGodotArray().Add(stream);
			}
			fileName = dir.GetNext();
		}
	}

	public void PlayMusic(int track)
	{
		// If a custom track is playing and locked, ignore normal music requests
		if (lockingCustomTrack)
		{
			if (track == (int)MUSIC.MAIN_THEME || track == (int)MUSIC.BATTLE_THEME)
				lastTrackType = track;
			return;
		}

		var radioMap = RadioMap(musicMap, track);
		if (radioMap == null || radioMap.Count == 0)
			return;

		var mPlayer = musicPlayer;
		if (mPlayer == null)
			return;

		if (_interactive_stream == null && mPlayer.Stream is AudioStreamInteractive)
		{
			_interactive_stream = mPlayer.Stream;
			UpdateInteractivePlaylists();
		}

		// Store as last track if it's a valid standard track
		if (track == (int)MUSIC.MAIN_THEME || track == (int)MUSIC.BATTLE_THEME)
			lastTrackType = track;

		if (currentTrackType == track && mPlayer.Playing)
			return;

		currentTrackType = track;

		if (USE_INTERACTIVE)
		{
			mPlayer.Play();
			GD.Print("MusicManager: Started m_player playback.");

			EmitSignal(SignalName.TrackChanged, mPlayer.Stream.ResourcePath.GetFile());

			var playback = mPlayer.GetStreamPlayback() as AudioStreamPlaybackInteractive;
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
				mPlayer.Stream = song;
				mPlayer.Play();
			}
			else
				GD.Print("MusicManager: Manual Fallback - No songs found!");
		}

		var vol = musicVolumeMap.ContainsKey(track) ? musicVolumeMap[track].AsSingle() : 1.0f;
		vol *= SettingVolume("music_volume");
		mPlayer.VolumeDb = Mathf.LinearToDb(vol);
		GD.Print($"MusicManager: Playing track {track} at volume linear: {vol} (db: {mPlayer.VolumeDb})");
	}

	public void PlayCustomFile(string fullPath)
	{
		if (!FileAccess.FileExists(fullPath) && !ResourceLoader.Exists(fullPath))
		{
			GD.PushWarning("MusicManager: Custom file not found: " + fullPath);
			return;
		}

		// Stop standard shuffle
		currentTrackType = -1;

		var stream = ResourceLoader.Load(fullPath) as AudioStream;
		if (stream != null)
		{
			var mPlayer = musicPlayer;
			if (mPlayer != null)
			{
				lockingCustomTrack = true;
				if (_interactive_stream == null)
					_interactive_stream = mPlayer.Stream;
				GD.Print($"MusicManager: Playing custom file: {fullPath}. Locking interactive music.");
				mPlayer.Stream = stream;
				mPlayer.VolumeDb = Mathf.LinearToDb(1.0f); // Default volume for events
				mPlayer.Play();
			}
		}
	}

	public void ResumeLastTrack()
	{
		lockingCustomTrack = false; // Ensure lock is released
		var mPlayer = musicPlayer;
		if (_interactive_stream != null && mPlayer != null)
			mPlayer.Stream = _interactive_stream;
		if (lastTrackType != -1)
			PlayMusic(lastTrackType);
		else
			PlayMusic((int)MUSIC.MAIN_THEME);
	}

	public void SkipTrack()
	{
		if (lockingCustomTrack)
		{
			ResumeLastTrack();
			return;
		}
		var mPlayer = musicPlayer;
		if (mPlayer != null)
		{
			mPlayer.Stop();
			if (currentTrackType != -1)
				PlayMusic(currentTrackType);
			else
				PlayMusic((int)MUSIC.MAIN_THEME);
		}
	}

	public void _on_music_finished()
	{
		if (lockingCustomTrack)
		{
			ResumeLastTrack();
			return;
		}

		if (!USE_INTERACTIVE)
		{
			// In manual mode, we need to pick the next song
			GD.Print("MusicManager: Manual Fallback - Song finished, picking next...");
			if (currentTrackType != -1)
				PlayMusic(currentTrackType);
			else
				PlayMusic((int)MUSIC.MAIN_THEME);
		}
		// In interactive mode, AudioStreamPlaylist handles it natively.
	}

	public void PlaySfx(int sfx)
	{
		if (!sfxMap.ContainsKey(sfx))
			return;
		var players = sfxPlayers;
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
		player.Stream = sfxMap[sfx].AsGodotObject() as AudioStream;
		player.VolumeDb = Mathf.LinearToDb(1.0f * SettingVolume("sfx_volume"));
		player.Play();
	}

	public void StopAllSfx()
	{
		foreach (var p in sfxPlayers)
			p.Stop();
	}

	public void SetMusicVolume(float volumeLinear)
	{
		var player = musicPlayer;
		if (player != null)
		{
			var baseVolume = musicVolumeMap.ContainsKey(currentTrackType)
				? musicVolumeMap[currentTrackType].AsSingle()
				: 0.5f;
			player.VolumeDb = Mathf.LinearToDb(baseVolume * volumeLinear);
		}
	}

	public void SetSfxVolume(float _volume_linear)
	{
		// No-op in the original implementation.
	}

	public void _toggle_pause()
	{
		var player = musicPlayer;
		if (player != null)
			player.Playing = !player.Playing;
	}
}

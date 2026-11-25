using System.Globalization;
using Godot;

[GlobalClass]
public partial class GameClock : Node
{
	[Signal]
	public delegate void HourPassedEventHandler();

	[Signal]
	public delegate void DayPassedEventHandler();

	// NOTE: Godot derives C# signal names from the delegate name and snake_cases them,
	// so this is exposed to the engine as "clock_update" (GDScript call sites use "clockUpdate").
	[Signal]
	public delegate void ClockUpdateEventHandler();

	[Export] public int hours_per_tick = 1; // How many in-game hours pass per tick
	[Export] public float seconds_per_tick = 1.0f; // Real seconds per tick

	[Export] public int start_year = 2010;
	[Export] public int start_month = 1; // 1-12
	[Export] public int start_day = 1; // 1-31
	[Export] public int start_hour = 0;

	public float time_scale = MIN_SPEED;
	public const int MIN_SPEED = 8;
	public const float MAX_SPEED = 80.0f;

	// GDScript cannot read C# static constants through an instance.
	public float max_speed => MAX_SPEED;

	public int hour;
	public Godot.Collections.Dictionary date_dict;
	public float accumulated_time = 0.0f;

	public bool paused = true;

	public GameClock()
	{
		hour = start_hour;
		date_dict = new Godot.Collections.Dictionary
		{
			{ "year", start_year },
			{ "month", start_month },
			{ "day", start_day },
		};
	}

	public override void _Process(double delta)
	{
		if (paused)
			return;

		accumulated_time += (float)delta * time_scale;
		while (accumulated_time >= seconds_per_tick)
		{
			accumulated_time -= seconds_per_tick;
			hour += hours_per_tick;
			EmitSignal(SignalName.HourPassed);

			var dateDictAsUnixTime = Time.GetUnixTimeFromDatetimeDict(date_dict);
			while (hour >= 24)
			{
				hour -= 24;
				dateDictAsUnixTime += 86400;
				EmitSignal(SignalName.DayPassed);
			}
			date_dict = Time.GetDatetimeDictFromUnixTime(dateDictAsUnixTime);
		}
	}

	public float get_absolute_days()
	{
		var dateUnix = Time.GetUnixTimeFromDatetimeDict(date_dict);
		// date_dict usually has hour 0 if updated via Time.get_datetime_dict_from_unix_time(date_dict_as_unix_time)
		// where date_dict_as_unix_time was multiple of 86400.
		var tickProgress = accumulated_time / seconds_per_tick;
		var hoursToday = hour + (tickProgress * hours_per_tick);
		return ((float)dateUnix / 86400.0f) + (hoursToday / 24.0f);
	}

	public string get_time_string()
	{
		return hour.ToString("D2", CultureInfo.InvariantCulture) + ":00";
	}

	public string get_date_string()
	{
		return string.Format(CultureInfo.InvariantCulture, "{0:D4}-{1:D2}-{2:D2}",
			date_dict["year"].AsInt32(), date_dict["month"].AsInt32(), date_dict["day"].AsInt32());
	}

	public string get_datetime_string()
	{
		return string.Format(CultureInfo.InvariantCulture, "{0:D2}:00 {1:D4}-{2:D2}-{3:D2}",
			hour, date_dict["year"].AsInt32(), date_dict["month"].AsInt32(), date_dict["day"].AsInt32());
	}

	public Godot.Collections.Dictionary ToDict()
	{
		return new Godot.Collections.Dictionary
		{
			{ "hour", hour },
			{ "date", date_dict },
		};
	}

	public void FromDict(Godot.Collections.Dictionary data)
	{
		hour = data.ContainsKey("hour") ? data["hour"].AsInt32() : start_hour;
		date_dict = data.ContainsKey("date")
			? data["date"].AsGodotDictionary()
			: new Godot.Collections.Dictionary
			{
				{ "year", start_year },
				{ "month", start_month },
				{ "day", start_day },
			};
	}

	public void set_speed(float scale)
	{
		time_scale = Mathf.Clamp(scale, MIN_SPEED, MAX_SPEED);
		EmitSignal(SignalName.ClockUpdate);
	}

	public void decrease_speed()
	{
		set_speed(time_scale - 8);
	}

	public void increase_speed()
	{
		set_speed(time_scale + 8);
	}

	public void pause()
	{
		paused = true;
		EmitSignal(SignalName.ClockUpdate);
	}

	public void resume()
	{
		if (Sosr.GameState.Instance.in_peace_process)
			return;

		paused = false;
		EmitSignal(SignalName.ClockUpdate);
	}

	public void toggle_pause()
	{
		if (time_scale == 0)
			return;

		if (paused)
		{
			if (Sosr.GameState.Instance.in_peace_process)
				return;
			resume();
		}
		else
		{
			pause();
		}
	}
}

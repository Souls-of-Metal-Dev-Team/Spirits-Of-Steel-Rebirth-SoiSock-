using System.Globalization;
using Godot;

[GlobalClass]
public partial class GameClock : Node
{
	[Signal]
	public delegate void HourPassedEventHandler();

	[Signal]
	public delegate void DayPassedEventHandler();

	[Signal]
	public delegate void ClockUpdateEventHandler();

	[Export] public int hoursPerTick = 1; // How many in-game hours pass per tick
	[Export] public float secondsPerTick = 1.0f; // Real seconds per tick

	[Export] public int startYear = 2010;
	[Export] public int startMonth = 1; // 1-12
	[Export] public int startDay = 1; // 1-31
	[Export] public int startHour = 0;

	public float timeScale = MIN_SPEED;
	public const int MIN_SPEED = 8;
	public const float MAX_SPEED = 80.0f;

	// GDScript cannot read C# static constants through an instance.
	public float maxSpeed => MAX_SPEED;

	public int hour;
	public Godot.Collections.Dictionary dateDict;
	public float accumulatedTime = 0.0f;

	public bool paused = true;

	public GameClock()
	{
		hour = startHour;
		dateDict = new Godot.Collections.Dictionary
		{
			{ "year", startYear },
			{ "month", startMonth },
			{ "day", startDay },
		};
	}

	public override void _Process(double delta)
	{
		if (paused)
			return;

		accumulatedTime += (float)delta * timeScale;
		while (accumulatedTime >= secondsPerTick)
		{
			accumulatedTime -= secondsPerTick;
			hour += hoursPerTick;
			EmitSignal(SignalName.HourPassed);

			var dateDictAsUnixTime = Time.GetUnixTimeFromDatetimeDict(dateDict);
			while (hour >= 24)
			{
				hour -= 24;
				dateDictAsUnixTime += 86400;
				EmitSignal(SignalName.DayPassed);
			}
			dateDict = Time.GetDatetimeDictFromUnixTime(dateDictAsUnixTime);
		}
	}

	public float GetAbsoluteDays()
	{
		var dateUnix = Time.GetUnixTimeFromDatetimeDict(dateDict);
		// date_dict usually has hour 0 if updated via Time.get_datetime_dict_from_unix_time(date_dict_as_unix_time)
		// where date_dict_as_unix_time was multiple of 86400.
		var tickProgress = accumulatedTime / secondsPerTick;
		var hoursToday = hour + (tickProgress * hoursPerTick);
		return ((float)dateUnix / 86400.0f) + (hoursToday / 24.0f);
	}

	public string GetDateString()
	{
		return string.Format(CultureInfo.InvariantCulture, "{0:D4}-{1:D2}-{2:D2}",
			dateDict["year"].AsInt32(), dateDict["month"].AsInt32(), dateDict["day"].AsInt32());
	}

	public string GetDatetimeString()
	{
		return string.Format(CultureInfo.InvariantCulture, "{0:D2}:00 {1:D4}-{2:D2}-{3:D2}",
			hour, dateDict["year"].AsInt32(), dateDict["month"].AsInt32(), dateDict["day"].AsInt32());
	}

	public Godot.Collections.Dictionary ToDict()
	{
		return new Godot.Collections.Dictionary
		{
			{ "hour", hour },
			{ "date", dateDict },
		};
	}

	public void FromDict(Godot.Collections.Dictionary data)
	{
		hour = data.ContainsKey("hour") ? data["hour"].AsInt32() : startHour;
		dateDict = data.ContainsKey("date")
			? data["date"].AsGodotDictionary()
			: new Godot.Collections.Dictionary
			{
				{ "year", startYear },
				{ "month", startMonth },
				{ "day", startDay },
			};
	}

	public void SetSpeed(float scale)
	{
		timeScale = Mathf.Clamp(scale, MIN_SPEED, MAX_SPEED);
		EmitSignal(SignalName.ClockUpdate);
	}

	public void DecreaseSpeed()
	{
		SetSpeed(timeScale - 8);
	}

	public void IncreaseSpeed()
	{
		SetSpeed(timeScale + 8);
	}

	public void pause()
	{
		paused = true;
		EmitSignal(SignalName.ClockUpdate);
	}

	public void resume()
	{
		if (Sosr.GameState.Instance.inPeaceProcess)
			return;

		paused = false;
		EmitSignal(SignalName.ClockUpdate);
	}

	public void TogglePause()
	{
		if (timeScale == 0)
			return;

		if (paused)
		{
			if (Sosr.GameState.Instance.inPeaceProcess)
				return;
			resume();
		}
		else
		{
			pause();
		}
	}
}

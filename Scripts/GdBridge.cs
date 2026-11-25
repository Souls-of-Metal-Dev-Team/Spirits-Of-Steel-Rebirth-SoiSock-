using System;
using System.Globalization;
using Godot;

namespace Sosr;

// Access to GDScript autoloads and global classes from C# during the incremental port.
// Autoloads are ordinary nodes under /root, not Engine singletons.
internal static class GdBridge
{
    public static GodotObject Autoload(string name)
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        return tree.Root.GetNode(name);
    }

    public static Variant Call(string autoload, StringName method, params Variant[] args)
        => Autoload(autoload).Call(method, args);

    public static Variant Get(string autoload, StringName property)
        => Autoload(autoload).Get(property);

    public static void Set(string autoload, StringName property, Variant value)
        => Autoload(autoload).Set(property, value);

    public static string CurrentStart()
        => Get("GameState", "current_start").AsString();

    public static bool InPeaceProcess()
        => Get("GameState", "in_peace_process").AsBool();

    public static bool Instabuild()
        => Get("GameState", "instabuild").AsBool();

    public static bool IndustryBuilding()
        => Get("GameState", "industry_building").AsBool();

    public static GodotObject CurrentWorld()
        => Get("GameState", "current_world").AsGodotObject();

    public static GodotObject PlayerCountry()
        => Get("CountryManager", "player_country").AsGodotObject();

    public static Godot.Collections.Dictionary Countries()
        => Get("CountryManager", "countries").AsGodotDictionary();

    public static GodotObject Country(string name)
        => Countries()[name].AsGodotObject();

    public static Variant MapDataLoadJson(string path)
    {
        if (!FileAccess.FileExists(path))
            return new Variant();
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null)
            return new Variant();
        var text = file.GetAsText();
        if (string.IsNullOrEmpty(text))
            return new Variant();
        return Json.ParseString(text);
    }

    public static bool MapDataSaveJson(string path, Variant data)
    {
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        if (file == null)
            return false;
        file.StoreString(Json.Stringify(data, "\t"));
        return true;
    }

    // GDScript String.capitalize() behaviour for display strings.
    public static string Capitalize(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        var text = value.Replace('_', ' ');
        var chars = text.ToCharArray();
        var capitalizeNext = true;
        for (var i = 0; i < chars.Length; i++)
        {
            if (char.IsLetter(chars[i]))
            {
                chars[i] = capitalizeNext
                    ? char.ToUpper(chars[i], CultureInfo.InvariantCulture)
                    : char.ToLower(chars[i], CultureInfo.InvariantCulture);
                capitalizeNext = false;
            }
            else
            {
                capitalizeNext = true;
            }
        }
        return new string(chars);
    }

    // Mirrors GDScript step_decimals() closely enough for skill tooltips.
    public static int StepDecimals(double step)
    {
        if (step == 0)
            return 0;
        var text = Math.Abs(step).ToString("0.###############", CultureInfo.InvariantCulture);
        var dot = text.IndexOf('.');
        return dot < 0 ? 0 : text.Length - dot - 1;
    }
}

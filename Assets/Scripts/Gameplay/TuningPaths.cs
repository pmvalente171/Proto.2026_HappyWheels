namespace Gameplay
{
    /// <summary>Tab names for <see cref="GameArchitecture.Tuning.TunableAttribute"/>.</summary>
    public static class TuningTabs
    {
        public const string Bike = "Bike";
    }

    /// <summary>Folder paths inside the tabs for <see cref="GameArchitecture.Tuning.TunableAttribute"/>. Nest with '/'.</summary>
    public static class TuningPaths
    {
        public const string Steering = "Steering";

        public const string Drivetrain = "Pedalling";
        public const string DrivetrainGears = Drivetrain + "/Gears";
        public const string DrivetrainBoost = Drivetrain + "/Boost";

        public const string Stamina = "Stamina";

        public const string Braking = "Braking";
    }
}

using System;
using System.Collections.Generic;

namespace Funbit.Ets.Telemetry.Server.Data
{
    // Top-level REST v1 payload
    public class TelemetryV1
    {
        public int ServerVersion { get; set; } = 1;
        public GameV1 Game { get; set; }
        public TruckV1 Truck { get; set; }
        public List<TrailerV1> Trailers { get; set; }
        public JobV1 Job { get; set; }
        public NavigationV1 Navigation { get; set; }
        public GameplayV1 Gameplay { get; set; }
    }

    public class GameV1
    {
        public bool Connected { get; set; }
        public string GameName { get; set; }
        public DateTime Time { get; set; }
        public bool Paused { get; set; }
        public string Version { get; set; }
        public string TelemetryPluginVersion { get; set; }
        public float TimeScale { get; set; }
        public DateTime NextRestStopTime { get; set; }
    }

    public class TruckV1
    {
        public string Id { get; set; }
        public string Make { get; set; }
        public string Model { get; set; }
        public string LicensePlate { get; set; }
        public string LicensePlateCountryId { get; set; }
        public string LicensePlateCountry { get; set; }

        public float Speed { get; set; }            // km/h
        public bool CruiseControlOn { get; set; }
        public float CruiseControlSpeed { get; set; } // km/h
        public float Odometer { get; set; }         // km

        public int Gear { get; set; }
        public int DisplayedGear { get; set; }
        public int ForwardGears { get; set; }
        public int ReverseGears { get; set; }
        public float EngineRpm { get; set; }
        public float EngineRpmMax { get; set; }

        public float Fuel { get; set; }
        public float FuelCapacity { get; set; }
        public float FuelAverageConsumption { get; set; }
        public float FuelRange { get; set; }

        // Input values
        public float UserSteer { get; set; }
        public float UserThrottle { get; set; }
        public float UserBrake { get; set; }
        public float UserClutch { get; set; }
        public float GameSteer { get; set; }
        public float GameThrottle { get; set; }
        public float GameBrake { get; set; }
        public float GameClutch { get; set; }

        public int RetarderBrake { get; set; }
        public int RetarderStepCount { get; set; }
        public int ShifterSlot { get; set; }
        public string ShifterType { get; set; }

        public bool EngineOn { get; set; }
        public bool ElectricOn { get; set; }
        public bool WipersOn { get; set; }
        public bool ParkBrakeOn { get; set; }
        public bool MotorBrakeOn { get; set; }

        public float AirPressure { get; set; }
        public bool AirPressureWarningOn { get; set; }
        public float AirPressureWarningValue { get; set; }
        public bool AirPressureEmergencyOn { get; set; }
        public float AirPressureEmergencyValue { get; set; }
        public float BrakeTemperature { get; set; }
        public float Adblue { get; set; }
        public float AdblueCapacity { get; set; }
        public float OilTemperature { get; set; }
        public float OilPressure { get; set; }
        public bool OilPressureWarningOn { get; set; }
        public float OilPressureWarningValue { get; set; }
        public float WaterTemperature { get; set; }
        public bool WaterTemperatureWarningOn { get; set; }
        public float WaterTemperatureWarningValue { get; set; }
        public float BatteryVoltage { get; set; }
        public bool BatteryVoltageWarningOn { get; set; }
        public float BatteryVoltageWarningValue { get; set; }

        public float LightsDashboardValue { get; set; }
        public bool LightsDashboardOn { get; set; }
        public bool BlinkerLeftActive { get; set; }
        public bool BlinkerRightActive { get; set; }
        public bool BlinkerLeftOn { get; set; }
        public bool BlinkerRightOn { get; set; }
        public bool LightsParkingOn { get; set; }
        public bool LightsBeamLowOn { get; set; }
        public bool LightsBeamHighOn { get; set; }
        public bool LightsAuxFrontOn { get; set; }
        public bool LightsAuxRoofOn { get; set; }
        public bool LightsBeaconOn { get; set; }
        public bool LightsBrakeOn { get; set; }
        public bool LightsReverseOn { get; set; }

        public PlacementV1 Placement { get; set; }
        public Vector3V1 Acceleration { get; set; }
        public Vector3V1 Head { get; set; }
        public Vector3V1 Cabin { get; set; }
        public Vector3V1 Hook { get; set; }
    }

    public class TrailerV1
    {
        public bool Attached { get; set; }
        public string Id { get; set; }
        public string Name { get; set; }
        public string BrandId { get; set; }
        public string Brand { get; set; }
        public string LicensePlate { get; set; }
        public string LicensePlateCountryId { get; set; }
        public string LicensePlateCountry { get; set; }
        public float WearChassis { get; set; }
        public float WearWheels { get; set; }
        public float WearBody { get; set; }
        public float CargoDamage { get; set; }
        public PlacementV1 Placement { get; set; }
    }

    public class JobV1
    {
        public int Income { get; set; }
        public DateTime DeadlineTime { get; set; }
        public DateTime RemainingTime { get; set; }
        public int PlannedDistanceKm { get; set; }
        public string SourceCityId { get; set; }
        public string SourceCity { get; set; }
        public string SourceCompanyId { get; set; }
        public string SourceCompany { get; set; }
        public string DestinationCityId { get; set; }
        public string DestinationCity { get; set; }
        public string DestinationCompanyId { get; set; }
        public string DestinationCompany { get; set; }
        public string CargoId { get; set; }
        public string Cargo { get; set; }
        public float CargoMass { get; set; }
        public int UnitCount { get; set; }
        public string JobMarket { get; set; }
    }

    public class NavigationV1
    {
        public DateTime EstimatedTime { get; set; }
        public int EstimatedDistance { get; set; } // meters
        public int SpeedLimit { get; set; } // km/h
    }

    public class GameplayV1
    {
        public bool JobFinished { get; set; }
        public bool JobCancelled { get; set; }
        public bool JobDelivered { get; set; }
        public bool Fined { get; set; }
        public bool Tollgate { get; set; }
        public bool Ferry { get; set; }
        public bool Train { get; set; }
        public bool Refuel { get; set; }
        public bool RefuelPayed { get; set; }
    }

    public class PlacementV1
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float Heading { get; set; }
        public float Pitch { get; set; }
        public float Roll { get; set; }
    }

    public class Vector3V1
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
    }
}

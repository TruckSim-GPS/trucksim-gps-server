using System;
using System.Collections.Generic;
using Funbit.Ets.Telemetry.Server.Helpers;
using SCSSdkClient;
using SCSSdkClient.Object;

namespace Funbit.Ets.Telemetry.Server.Data
{
    public class ScsTelemetryDataReader : IDisposable
    {
        const string ScsTelemetryMapName = "Local\\SCSTelemetry";

        readonly SharedMemory _sharedMemory = new SharedMemory();
        readonly object _lock = new object();

        static readonly Lazy<ScsTelemetryDataReader> _instance = new Lazy<ScsTelemetryDataReader>(() => new ScsTelemetryDataReader());
        public static ScsTelemetryDataReader Instance => _instance.Value;

        ScsTelemetryDataReader()
        {
            // Create or open the shared memory mapping
            _sharedMemory.Connect(ScsTelemetryMapName);
        }

        public bool IsConnected => _sharedMemory.Hooked;

        public TelemetryV1 Read()
        {
            lock (_lock)
            {
                var scs = _sharedMemory.Update<SCSTelemetry>();

                var game = new GameV1
                {
                    Connected = scs?.SdkActive == true,
                    GameName = scs?.Game == SCSGame.Ets2 ? "ETS2" : scs?.Game == SCSGame.Ats ? "ATS" : null,
                    Time = scs?.CommonValues?.GameTime?.Date ?? DateTime.MinValue,
                    Paused = scs?.Paused ?? false,
                    Version = scs != null ? $"{scs.GameVersion.Major}.{scs.GameVersion.Minor}" : null,
                    TelemetryPluginVersion = scs?.DllVersion.ToString(),
                    TimeScale = scs?.CommonValues?.Scale ?? 0f,
                    NextRestStopTime = scs?.CommonValues?.NextRestStopTime?.Date ?? DateTime.MinValue
                };

                var truck = MapTruck(scs);
                var trailers = MapTrailers(scs);
                var job = MapJob(scs);
                var nav = MapNavigation(scs);

                return new TelemetryV1
                {
                    ServerVersion = 1,
                    Game = game,
                    Truck = truck,
                    Trailers = trailers,
                    Job = job,
                    Navigation = nav,
                    Gameplay = new GameplayV1() // optional, can be extended later
                };
            }
        }

        static TruckV1 MapTruck(SCSTelemetry scs)
        {
            var t = scs?.TruckValues;
            var curr = t?.CurrentValues;
            var dash = curr?.DashboardValues;
            var motor = curr?.MotorValues;
            var lights = curr?.LightsValues;
            var caps = t?.ConstantsValues?.CapacityValues;
            var warn = t?.ConstantsValues?.WarningFactorValues;
            var ctrl = scs?.ControlValues;

            return new TruckV1
            {
                Id = t?.ConstantsValues?.BrandId,
                Make = t?.ConstantsValues?.Brand,
                Model = t?.ConstantsValues?.Name ?? t?.ConstantsValues?.Id,
                LicensePlate = t?.ConstantsValues?.LicensePlate,
                LicensePlateCountry = t?.ConstantsValues?.LicensePlateCountry,
                LicensePlateCountryId = t?.ConstantsValues?.LicensePlateCountryId,

                Speed = dash?.Speed?.Kph ?? 0f,
                CruiseControlOn = dash?.CruiseControl ?? false,
                CruiseControlSpeed = dash?.CruiseControlSpeed?.Kph ?? 0f,
                Odometer = dash?.Odometer ?? 0f,

                Gear = motor?.GearValues?.Selected ?? 0,
                DisplayedGear = dash?.GearDashboards ?? 0,
                ForwardGears = (int)(t?.ConstantsValues?.MotorValues?.ForwardGearCount ?? 0u),
                ReverseGears = (int)(t?.ConstantsValues?.MotorValues?.ReverseGearCount ?? 0u),
                EngineRpm = dash?.RPM ?? 0f,
                EngineRpmMax = t?.ConstantsValues?.MotorValues?.EngineRpmMax ?? 0f,

                Fuel = dash?.FuelValue?.Amount ?? 0f,
                FuelCapacity = caps?.Fuel ?? 0f,
                FuelAverageConsumption = dash?.FuelValue?.AverageConsumption ?? 0f,
                FuelRange = dash?.FuelValue?.Range ?? 0f,

                // input values
                UserSteer = ctrl?.InputValues?.Steering ?? 0f,
                UserThrottle = ctrl?.InputValues?.Throttle ?? 0f,
                UserBrake = ctrl?.InputValues?.Brake ?? 0f,
                UserClutch = ctrl?.InputValues?.Clutch ?? 0f,
                GameSteer = ctrl?.GameValues?.Steering ?? 0f,
                GameThrottle = ctrl?.GameValues?.Throttle ?? 0f,
                GameBrake = ctrl?.GameValues?.Brake ?? 0f,
                GameClutch = ctrl?.GameValues?.Clutch ?? 0f,

                RetarderBrake = (int)(motor?.BrakeValues?.RetarderLevel ?? 0u),
                RetarderStepCount = (int)(t?.ConstantsValues?.MotorValues?.RetarderStepCount ?? 0u),
                ShifterSlot = (int)(motor?.GearValues?.HShifterSlot ?? 0u),
                ShifterType = t?.ConstantsValues?.MotorValues?.ShifterTypeValue.ToString().ToLowerInvariant(),

                EngineOn = curr?.EngineEnabled ?? false,
                ElectricOn = curr?.ElectricEnabled ?? false,
                WipersOn = dash?.Wipers ?? false,
                ParkBrakeOn = motor?.BrakeValues?.ParkingBrake ?? false,
                MotorBrakeOn = motor?.BrakeValues?.MotorBrake ?? false,

                AirPressure = motor?.BrakeValues?.AirPressure ?? 0f,
                AirPressureWarningOn = dash?.WarningValues?.AirPressure ?? false,
                AirPressureWarningValue = warn?.AirPressure ?? 0f,
                AirPressureEmergencyOn = dash?.WarningValues?.AirPressureEmergency ?? false,
                AirPressureEmergencyValue = warn?.AirPressureEmergency ?? 0f,
                BrakeTemperature = motor?.BrakeValues?.Temperature ?? 0f,
                Adblue = dash?.AdBlue ?? 0f,
                AdblueCapacity = caps?.AdBlue ?? 0f,
                OilTemperature = dash?.OilTemperature ?? 0f,
                OilPressure = dash?.OilPressure ?? 0f,
                OilPressureWarningOn = dash?.WarningValues?.OilPressure ?? false,
                OilPressureWarningValue = warn?.OilPressure ?? 0f,
                WaterTemperature = dash?.WaterTemperature ?? 0f,
                WaterTemperatureWarningOn = dash?.WarningValues?.WaterTemperature ?? false,
                WaterTemperatureWarningValue = warn?.WaterTemperature ?? 0f,
                BatteryVoltage = dash?.BatteryVoltage ?? 0f,
                BatteryVoltageWarningOn = dash?.WarningValues?.BatteryVoltage ?? false,
                BatteryVoltageWarningValue = warn?.BatteryVoltage ?? 0f,

                LightsDashboardValue = lights?.DashboardBacklight ?? 0f,
                LightsDashboardOn = (lights?.DashboardBacklight ?? 0f) > 0f,
                BlinkerLeftActive = lights?.BlinkerLeftActive ?? false,
                BlinkerRightActive = lights?.BlinkerRightActive ?? false,
                BlinkerLeftOn = lights?.BlinkerLeftOn ?? false,
                BlinkerRightOn = lights?.BlinkerRightOn ?? false,
                LightsParkingOn = lights?.Parking ?? false,
                LightsBeamLowOn = lights?.BeamLow ?? false,
                LightsBeamHighOn = lights?.BeamHigh ?? false,
                LightsAuxFrontOn = (lights?.AuxFront ?? AuxLevel.Off) != AuxLevel.Off,
                LightsAuxRoofOn = (lights?.AuxRoof ?? AuxLevel.Off) != AuxLevel.Off,
                LightsBeaconOn = lights?.Beacon ?? false,
                LightsBrakeOn = lights?.Brake ?? false,
                LightsReverseOn = lights?.Reverse ?? false,

                Placement = new PlacementV1
                {
                    X = (float)(curr?.PositionValue?.Position?.X ?? 0),
                    Y = (float)(curr?.PositionValue?.Position?.Y ?? 0),
                    Z = (float)(curr?.PositionValue?.Position?.Z ?? 0),
                    Heading = curr?.PositionValue?.Orientation?.Heading ?? 0f,
                    Pitch = curr?.PositionValue?.Orientation?.Pitch ?? 0f,
                    Roll = curr?.PositionValue?.Orientation?.Roll ?? 0f
                },
                Acceleration = new Vector3V1
                {
                    X = curr?.AccelerationValues?.LinearVelocity?.X ?? 0f,
                    Y = curr?.AccelerationValues?.LinearVelocity?.Y ?? 0f,
                    Z = curr?.AccelerationValues?.LinearVelocity?.Z ?? 0f
                },
                Head = new Vector3V1
                {
                    X = t?.Positioning?.Head?.X ?? 0f,
                    Y = t?.Positioning?.Head?.Y ?? 0f,
                    Z = t?.Positioning?.Head?.Z ?? 0f
                },
                Cabin = new Vector3V1
                {
                    X = t?.Positioning?.Cabin?.X ?? 0f,
                    Y = t?.Positioning?.Cabin?.Y ?? 0f,
                    Z = t?.Positioning?.Cabin?.Z ?? 0f
                },
                Hook = new Vector3V1
                {
                    X = t?.Positioning?.Hook?.X ?? 0f,
                    Y = t?.Positioning?.Hook?.Y ?? 0f,
                    Z = t?.Positioning?.Hook?.Z ?? 0f
                }
            };
        }

        static List<TrailerV1> MapTrailers(SCSTelemetry scs)
        {
            var list = new List<TrailerV1>();
            var arr = scs?.TrailerValues;
            if (arr == null) return list;

            foreach (var tr in arr)
            {
                if (tr == null) continue;
                list.Add(new TrailerV1
                {
                    Attached = tr.Attached,
                    Id = tr.Id,
                    Name = tr.Name,
                    BrandId = tr.BrandId,
                    Brand = tr.Brand,
                    LicensePlate = tr.LicensePlate,
                    LicensePlateCountryId = tr.LicensePlateCountryId,
                    LicensePlateCountry = tr.LicensePlateCountry,
                    WearChassis = tr.DamageValues?.Chassis ?? 0f,
                    WearWheels = tr.DamageValues?.Wheels ?? 0f,
                    WearBody = tr.DamageValues?.Body ?? 0f,
                    CargoDamage = tr.DamageValues?.Cargo ?? 0f,
                    Placement = new PlacementV1
                    {
                        X = (float)(tr.Position?.Position?.X ?? 0),
                        Y = (float)(tr.Position?.Position?.Y ?? 0),
                        Z = (float)(tr.Position?.Position?.Z ?? 0),
                        Heading = tr.Position?.Orientation?.Heading ?? 0f,
                        Pitch = tr.Position?.Orientation?.Pitch ?? 0f,
                        Roll = tr.Position?.Orientation?.Roll ?? 0f
                    }
                });
            }

            return list;
        }

        static JobV1 MapJob(SCSTelemetry scs)
        {
            var j = scs?.JobValues;
            if (j == null) return new JobV1();
            return new JobV1
            {
                Income = (int)j.Income,
                DeadlineTime = j.DeliveryTime?.Date ?? DateTime.MinValue,
                RemainingTime = j.RemainingDeliveryTime?.Date ?? DateTime.MinValue,
                PlannedDistanceKm = (int)j.PlannedDistanceKm,
                SourceCityId = j.CitySourceId,
                SourceCity = j.CitySource,
                SourceCompanyId = j.CompanySourceId,
                SourceCompany = j.CompanySource,
                DestinationCityId = j.CityDestinationId,
                DestinationCity = j.CityDestination,
                DestinationCompanyId = j.CompanyDestinationId,
                DestinationCompany = j.CompanyDestination,
                CargoId = j.CargoValues?.Id,
                Cargo = j.CargoValues?.Name,
                CargoMass = j.CargoValues?.Mass ?? 0f,
                UnitCount = (int)(j.CargoValues?.UnitCount ?? 0u),
                JobMarket = j.Market.ToString()
            };
        }

        static NavigationV1 MapNavigation(SCSTelemetry scs)
        {
            var n = scs?.NavigationValues;
            if (n == null) return new NavigationV1();
            // EstimatedTime: represent relative seconds as ISO-like baseline from DateTime.MinValue
            var eta = DateTime.MinValue.AddSeconds(n.NavigationTime);
            return new NavigationV1
            {
                EstimatedTime = eta,
                EstimatedDistance = (int)n.NavigationDistance,
                SpeedLimit = (int)(n.SpeedLimit?.Kph ?? 0f)
            };
        }

        public void Dispose()
        {
            _sharedMemory?.Disconnect();
        }
    }
}

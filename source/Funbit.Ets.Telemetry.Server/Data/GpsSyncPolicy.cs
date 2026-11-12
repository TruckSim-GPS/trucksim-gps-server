using System;

namespace Funbit.Ets.Telemetry.Server.Data
{
    public class GpsSyncSample
    {
        public DateTime Now { get; set; }
        public bool Live { get; set; }
        public string ProfileKey { get; set; }
        public bool Paused { get; set; }
        public float RouteDistance { get; set; }
        public bool OnJob { get; set; }
        public bool AppConnected { get; set; }
        public bool AppWantsGameRoute { get; set; }
        public float TruckX { get; set; }
        public float TruckZ { get; set; }
        public GpsWaypointV1 NextWaypoint { get; set; }
    }

    public class GpsSyncPolicy
    {
        public static readonly TimeSpan RouteGoneAfter = TimeSpan.FromSeconds(3);
        public static readonly TimeSpan ResumeSettle = TimeSpan.FromMilliseconds(500);
        public static readonly TimeSpan PressDelay = TimeSpan.FromSeconds(1);
        public static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(15);
        public static readonly TimeSpan SoftHold = TimeSpan.FromSeconds(10);
        public static readonly TimeSpan PressWindow = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan SwallowedPressWindow = TimeSpan.FromSeconds(5);
        public const int MaxPresses = 3;
        public const float JumpMeters = 1000f;
        public const float JumpRatio = 0.1f;
        public const float PassNearMeters = 100f;
        public const float PassLeftMeters = 150f;
        public const float ArriveMeters = 40f;

        bool _live;
        string _profile;
        bool _paused;
        DateTime? _unpausedSince;
        DateTime? _zeroSince;
        bool _resumeEpoch;
        float _lastDistance;
        bool _pending;
        int _presses;
        DateTime _firstPress;
        DateTime _lastPress;
        GpsWaypointV1 _passWaypoint;
        bool _nearWaypoint;
        bool _arrivedAtWaypoint;
        bool _gameRouteAsked;
        bool _onJob;
        bool _pressSaveOwed;

        public bool RouteUp { get; private set; }
        public int Epoch { get; private set; }
        public string EpochReason { get; private set; }
        public DateTime EpochStart { get; private set; } = DateTime.MinValue;
        public DateTime ValidAfter { get; private set; } = DateTime.MaxValue;
        public bool Pending => _pending;

        public bool Update(GpsSyncSample s)
        {
            if (!s.Live)
            {
                _live = false;
                ForgetRoute();
                ValidAfter = DateTime.MaxValue;
                return false;
            }
            if (!_live || s.ProfileKey != _profile)
            {
                _live = true;
                _profile = s.ProfileKey;
                _paused = s.Paused;
                _unpausedSince = s.Paused ? (DateTime?)null : s.Now;
                _onJob = s.OnJob;
                ForgetRoute();
                ValidAfter = DateTime.MaxValue;
            }
            if (s.OnJob != _onJob)
            {
                _onJob = s.OnJob;
                if (ValidAfter < s.Now)
                    ValidAfter = s.Now;
            }

            if (s.Paused)
            {
                if (!_paused)
                {
                    _paused = true;
                    _unpausedSince = null;
                    _resumeEpoch = RouteUp;
                }
                return false;
            }
            if (_paused)
            {
                _paused = false;
                _unpausedSince = s.Now;
                _zeroSince = null;
            }
            bool settled = s.Now - _unpausedSince.Value >= ResumeSettle;

            bool gameRouteAsked = s.OnJob && s.AppWantsGameRoute;
            if (gameRouteAsked && !_gameRouteAsked && RouteUp)
                BeginEpoch(s, "app asked for the game route", true);
            _gameRouteAsked = gameRouteAsked;

            float distance = s.RouteDistance;
            if (distance > 0f)
            {
                _zeroSince = null;
                if (!RouteUp)
                {
                    BeginEpoch(s, "route appeared", true);
                }
                else if (_resumeEpoch)
                {
                    if (settled)
                    {
                        _resumeEpoch = false;
                        BeginEpoch(s, "resumed after a pause", false);
                    }
                }
                else if (_lastDistance > 0f && Math.Abs(distance - _lastDistance) > Math.Max(JumpMeters, JumpRatio * _lastDistance))
                {
                    BeginEpoch(s, "route jumped while driving", false);
                }
                _lastDistance = distance;
            }
            else
            {
                if (_zeroSince == null)
                    _zeroSince = s.Now;
                if (RouteUp && s.Now - _zeroSince.Value >= RouteGoneAfter)
                    ForgetRoute();
            }

            var reached = RouteUp && s.NextWaypoint != null ? WaypointReached(s) : null;
            if (reached != null)
                BeginEpoch(s, reached, reached == "waypoint passed");

            if (!_pending)
                return false;
            if (_presses > 0 && s.Now - _firstPress >= SoftHold && ValidAfter < EpochStart)
                ValidAfter = EpochStart;
            if (_presses >= MaxPresses)
            {
                if (s.Now - _lastPress >= GiveUpAfter)
                    _pending = false;
                return false;
            }
            bool canPress = s.AppConnected && (!s.OnJob || s.AppWantsGameRoute) && RouteUp && distance > 0f && settled && s.Now - EpochStart >= PressDelay
                            && (s.Now - _lastPress >= PressWindow || _pressSaveOwed && EpochStart > _lastPress && s.Now - _lastPress >= SwallowedPressWindow);
            if (!canPress)
                return false;
            if (_presses == 0)
                _firstPress = s.Now;
            _presses++;
            _lastPress = s.Now;
            _pressSaveOwed = true;
            return true;
        }

        // The game moves a waypoint behind about 50 m before it (measured in ATS 1.61); passing it
        // farther away is caught on leaving.
        string WaypointReached(GpsSyncSample s)
        {
            var waypoint = s.NextWaypoint;
            if (_passWaypoint == null || _passWaypoint.X != waypoint.X || _passWaypoint.Z != waypoint.Z)
            {
                _passWaypoint = waypoint;
                _nearWaypoint = false;
                _arrivedAtWaypoint = false;
            }
            float dx = s.TruckX - waypoint.X, dz = s.TruckZ - waypoint.Z;
            float meters = (float)Math.Sqrt(dx * dx + dz * dz);
            if (meters <= PassNearMeters)
                _nearWaypoint = true;
            else if (_nearWaypoint && meters > PassLeftMeters)
            {
                _nearWaypoint = false;
                return "waypoint passed";
            }
            if (!_arrivedAtWaypoint && meters <= ArriveMeters)
            {
                _arrivedAtWaypoint = true;
                return "waypoint reached";
            }
            return null;
        }

        public bool Shows(SaveGpsState save, DateTime writtenAt, bool onJob, bool appWantsGameRoute)
        {
            return RouteUp && save.Waypoints.Count > 0 && (onJob ? appWantsGameRoute : !save.JobActive) && writtenAt > ValidAfter;
        }

        public void SaveWritten(DateTime writtenAt, bool hasTarget)
        {
            bool earlierPress = _pressSaveOwed && EpochStart > _lastPress;
            if (writtenAt >= _lastPress)
                _pressSaveOwed = false;
            if (hasTarget && writtenAt > EpochStart && !earlierPress)
                _pending = false;
        }

        public void PressLost()
        {
            _pressSaveOwed = false;
            if (_pending && _presses > 0 && ValidAfter < EpochStart)
                ValidAfter = EpochStart;
        }

        void BeginEpoch(GpsSyncSample s, string reason, bool hard)
        {
            RouteUp = true;
            Epoch++;
            EpochReason = reason;
            EpochStart = s.Now;
            _pending = true;
            _presses = 0;
            _resumeEpoch = false;
            bool hide = !s.AppConnected || (hard && s.Now - _lastPress >= PressWindow);
            if (hide || ValidAfter > s.Now)
                ValidAfter = s.Now;
        }

        void ForgetRoute()
        {
            RouteUp = false;
            _pending = false;
            _zeroSince = null;
            _lastDistance = 0f;
            _resumeEpoch = false;
            _passWaypoint = null;
            _nearWaypoint = false;
            _arrivedAtWaypoint = false;
        }
    }
}

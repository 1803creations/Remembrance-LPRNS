using Rage;
using Rage.Attributes;
using Rage.Native;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using PersistentWorld.Database;
using LSPD_First_Response.Mod.API;

[assembly: Plugin("Directional ALPR", Author = "YourName", Description = "Front/Rear ALPR System with Persistent World Database")]

namespace DirectionalALPR
{
    public enum ALPRStatus
    {
        Clean,
        NoRegistration,
        ExpiredRegistration,
        NoInsurance,
        ExpiredInsurance,
        Stolen,
        Wanted,
        Incarcerated,
        SuspendedLicense,
        RevokedLicense,
        MultipleHits
    }

    public class ALPRResult
    {
        public string Plate { get; set; }
        public float Distance { get; set; }
        public ALPRStatus Status { get; set; }
        public string Alert { get; set; }
        public int HitCount { get; set; }
    }

    public class Main : Plugin
    {
        private DatabaseManager _database;
        private bool _alprEnabled = true;
        private DateTime _lastToggle = DateTime.Now;
        private DateTime _lastScan = DateTime.MinValue;
        private const int SCAN_INTERVAL = 250;
        private const int NOTIFICATION_COOLDOWN_SECONDS = 5;
        private DateTime _lastNotification = DateTime.MinValue;

        // For native audio
        private bool _useNativeSounds = true;

        private ALPRResult _frontResult;
        private ALPRResult _rearResult;
        private List<ScannedVehicle> _scannedVehicles = new List<ScannedVehicle>();
        private List<ActiveBlip> _activeBlips = new List<ActiveBlip>();

        private float _displayWidth = 0.25f;
        private float _displayHeight = 0.20f;
        private float _displayX = 0.87f;
        private float _displayY = 0.88f;

        private class ScannedVehicle
        {
            public Vehicle Vehicle { get; set; }
            public string Plate { get; set; }
            public DateTime ScanTime { get; set; }
            public ALPRResult Result { get; set; }
            public bool Notified { get; set; }
        }

        private class ActiveBlip
        {
            public Blip Blip { get; set; }
            public Vehicle Vehicle { get; set; }
            public DateTime CreatedTime { get; set; }
            public ALPRStatus Status { get; set; }
        }

        public override void Initialize()
        {
            Game.LogTrivial("[Directional ALPR] Initializing...");

            try
            {
                // Connect to Persistent World database
                string gtaPath = AppDomain.CurrentDomain.BaseDirectory;
                string databasePath = Path.Combine(gtaPath, "Plugins", "LSPDFR", "PersistentWorld", "PersistentWorld.db");
                _database = new DatabaseManager(databasePath);
                _database.InitializeDatabase();

                Game.LogTrivial("[Directional ALPR] Database connected successfully");
                Game.DisplayNotification("~b~Directional ALPR~s~ loaded. Press ~y~F7~s~ to toggle");

                GameFiber.StartNew(MainLoop);
            }
            catch (Exception ex)
            {
                Game.LogTrivial($"[Directional ALPR] Failed to connect to database: {ex.Message}");
                Game.DisplayNotification("~r~Directional ALPR~s~ failed to load - Database not found");
            }
        }

        public override void Finally()
        {
            ClearAllBlips();
            _database?.Dispose();
            Game.LogTrivial("[Directional ALPR] Unloaded.");
        }

        private void ClearAllBlips()
        {
            foreach (var blip in _activeBlips)
            {
                if (blip.Blip.Exists())
                    blip.Blip.Delete();
            }
            _activeBlips.Clear();
        }

        private bool IsInEmergencyVehicle()
        {
            Ped player = Game.LocalPlayer.Character;
            if (!player.Exists() || !player.IsInAnyVehicle(false))
                return false;

            Vehicle currentVehicle = player.CurrentVehicle;

            // Check if it's an emergency vehicle by class
            if (currentVehicle.Class == VehicleClass.Emergency)
                return true;

            // Also check by model name for any additional emergency vehicles
            string modelName = currentVehicle.Model.Name?.ToLower() ?? "";
            if (modelName.Contains("police") ||
                modelName.Contains("sheriff") ||
                modelName.Contains("fbi") ||
                modelName.Contains("fire") ||
                modelName.Contains("ambulance") ||
                modelName.Contains("pranger") ||
                modelName.Contains("riot"))
            {
                return true;
            }

            return false;
        }

        private void MainLoop()
        {
            while (true)
            {
                GameFiber.Yield();

                // Toggle with F7
                if (Game.IsKeyDown(System.Windows.Forms.Keys.F7) && (DateTime.Now - _lastToggle).TotalMilliseconds > 500)
                {
                    _alprEnabled = !_alprEnabled;
                    if (!_alprEnabled) ClearAllBlips();
                    Game.DisplayNotification($"ALPR ~b~{(_alprEnabled ? "ENABLED" : "DISABLED")}");
                    _lastToggle = DateTime.Now;
                }

                // Only run ALPR if enabled AND in an emergency vehicle
                bool inEmergencyVeh = IsInEmergencyVehicle();

                if (_alprEnabled && inEmergencyVeh)
                {
                    // Run cleanup more frequently to catch deleted vehicles
                    CleanupOldData();

                    if ((DateTime.Now - _lastScan).TotalMilliseconds >= SCAN_INTERVAL)
                    {
                        ProcessALPR();
                        _lastScan = DateTime.Now;
                    }

                    DrawUI(); // Draw UI only when in emergency vehicle
                }
                else if (!inEmergencyVeh)
                {
                    // Clear results when not in emergency vehicle
                    _frontResult = null;
                    _rearResult = null;
                    _scannedVehicles.Clear();
                    ClearAllBlips();
                }
            }
        }

        private void CleanupOldData()
        {
            Ped player = Game.LocalPlayer.Character;
            if (!player.Exists()) return;

            Vector3 playerPos = player.Position;

            // Remove scanned vehicles that no longer exist or are out of range
            _scannedVehicles.RemoveAll(v =>
                !v.Vehicle.Exists() ||
                !v.Vehicle || // Check if vehicle is invalid
                Vector3.Distance(playerPos, v.Vehicle.Position) > 60f);

            // CRITICAL FIX: Check each blip's vehicle and delete if vehicle is gone
            List<ActiveBlip> blipsToRemove = new List<ActiveBlip>();

            foreach (var blip in _activeBlips)
            {
                bool shouldRemove = false;

                // Check if vehicle no longer exists
                if (!blip.Vehicle.Exists() || !blip.Vehicle)
                {
                    shouldRemove = true;
                }
                // Check if vehicle is out of range (beyond cleanup distance)
                else if (Vector3.Distance(playerPos, blip.Vehicle.Position) > 100f)
                {
                    shouldRemove = true;
                }
                // Check if blip is too old
                else if ((DateTime.Now - blip.CreatedTime).TotalSeconds > 25) // Slightly over 20 seconds
                {
                    shouldRemove = true;
                }

                if (shouldRemove)
                {
                    if (blip.Blip.Exists())
                        blip.Blip.Delete();
                    blipsToRemove.Add(blip);
                }
            }

            // Remove all marked blips
            foreach (var blip in blipsToRemove)
            {
                _activeBlips.Remove(blip);
            }
        }

        private void ProcessALPR()
        {
            Ped player = Game.LocalPlayer.Character;
            if (!player.Exists() || !player.IsInAnyVehicle(false))
            {
                _frontResult = null;
                _rearResult = null;
                return;
            }

            Vehicle patrolCar = player.CurrentVehicle;
            Vector3 playerPos = patrolCar.Position;
            Vector3 forward = patrolCar.ForwardVector;
            Vector3 backward = -forward;

            float bestFrontDot = 0.7f;
            float bestRearDot = 0.7f;

            Vehicle bestFront = null;
            Vehicle bestRear = null;

            foreach (Vehicle v in World.GetAllVehicles())
            {
                if (!v.Exists() || !v || v == patrolCar) continue;

                Vector3 toVehicle = v.Position - playerPos;
                float distance = toVehicle.Length();

                if (distance > 40f) continue;

                Vector3 dir = toVehicle;
                dir.Normalize();

                float dotFront = Vector3.Dot(forward, dir);
                float dotRear = Vector3.Dot(backward, dir);

                if (dotFront > bestFrontDot)
                {
                    bestFrontDot = dotFront;
                    bestFront = v;
                }

                if (dotRear > bestRearDot)
                {
                    bestRearDot = dotRear;
                    bestRear = v;
                }
            }

            _frontResult = bestFront != null ? GetVehicleResult(bestFront) : null;
            _rearResult = bestRear != null ? GetVehicleResult(bestRear) : null;
        }

        private ALPRResult GetVehicleResult(Vehicle veh)
        {
            string plate = CleanPlate(veh.LicensePlate);
            float distance = veh.DistanceTo(Game.LocalPlayer.Character);

            // Check if we've already scanned this vehicle recently
            var existing = _scannedVehicles.FirstOrDefault(v => v.Vehicle == veh);
            if (existing != null && (DateTime.Now - existing.ScanTime).TotalSeconds < 30)
            {
                return existing.Result;
            }

            // New scan - look up in database
            var result = ScanVehicle(veh, plate, distance);

            // Store in cache
            _scannedVehicles.Add(new ScannedVehicle
            {
                Vehicle = veh,
                Plate = plate,
                ScanTime = DateTime.Now,
                Result = result,
                Notified = false
            });

            // Handle notifications and blips for hits
            if (result.Status != ALPRStatus.Clean && result.HitCount > 0)
            {
                CreateBlipForVehicle(veh, result.Status);
                SendNotification(veh, result);
            }

            return result;
        }

        private ALPRResult ScanVehicle(Vehicle veh, string plate, float distance)
        {
            var result = new ALPRResult
            {
                Plate = plate,
                Distance = distance,
                Status = ALPRStatus.Clean,
                Alert = "",
                HitCount = 0
            };

            try
            {
                var vehicleData = _database.LookupByPlate(plate);

                if (vehicleData == null || vehicleData.Count == 0)
                {
                    return result;
                }

                List<string> alerts = new List<string>();

                // Registration check
                if (vehicleData.ContainsKey("no_registration") && Convert.ToInt32(vehicleData["no_registration"]) == 1)
                {
                    alerts.Add("NO REG");
                    result.HitCount++;
                }
                else if (vehicleData.ContainsKey("registration_expiry") && vehicleData["registration_expiry"] != null)
                {
                    if (DateTime.TryParse(vehicleData["registration_expiry"].ToString(), out DateTime regExpiry))
                    {
                        if (regExpiry < DateTime.Now)
                        {
                            alerts.Add("REG EXP");
                            result.HitCount++;
                        }
                    }
                }

                // Insurance check
                if (vehicleData.ContainsKey("no_insurance") && Convert.ToInt32(vehicleData["no_insurance"]) == 1)
                {
                    alerts.Add("NO INS");
                    result.HitCount++;
                }
                else if (vehicleData.ContainsKey("insurance_expiry") && vehicleData["insurance_expiry"] != null)
                {
                    if (DateTime.TryParse(vehicleData["insurance_expiry"].ToString(), out DateTime insExpiry))
                    {
                        if (insExpiry < DateTime.Now)
                        {
                            alerts.Add("INS EXP");
                            result.HitCount++;
                        }
                    }
                }

                // Stolen check
                if (vehicleData.ContainsKey("is_stolen") && Convert.ToInt32(vehicleData["is_stolen"]) == 1)
                {
                    alerts.Add("STOLEN");
                    result.HitCount++;
                }

                // Get owner info if available
                if (vehicleData.ContainsKey("ped_id") && vehicleData["ped_id"] != null)
                {
                    int pedId = Convert.ToInt32(vehicleData["ped_id"]);
                    string firstName = vehicleData.ContainsKey("first_name") ? vehicleData["first_name"].ToString() : "";
                    string lastName = vehicleData.ContainsKey("last_name") ? vehicleData["last_name"].ToString() : "";

                    if (!string.IsNullOrEmpty(firstName) && !string.IsNullOrEmpty(lastName))
                    {
                        var persons = _database.LookupByName(firstName, lastName);
                        if (persons != null && persons.Count > 0)
                        {
                            var owner = persons[0];

                            // Check owner wanted status
                            if (owner.ContainsKey("is_wanted") && Convert.ToBoolean(owner["is_wanted"]))
                            {
                                alerts.Add("WANTED");
                                result.HitCount++;
                            }

                            // Check owner license status
                            if (owner.ContainsKey("license_status"))
                            {
                                string licenseStatus = owner["license_status"].ToString().ToUpper();
                                if (licenseStatus == "SUSPENDED")
                                {
                                    alerts.Add("LIC SUSP");
                                    result.HitCount++;
                                }
                                else if (licenseStatus == "REVOKED")
                                {
                                    alerts.Add("LIC REV");
                                    result.HitCount++;
                                }
                            }

                            // Check owner incarcerated
                            if (owner.ContainsKey("is_incarcerated") && Convert.ToBoolean(owner["is_incarcerated"]))
                            {
                                alerts.Add("JAILED");
                                result.HitCount++;
                            }
                        }
                    }
                }

                // Set final status based on priority
                if (result.HitCount > 0)
                {
                    // Priority order: Wanted/Stolen highest, then License issues, then Reg/Insurance
                    if (alerts.Any(a => a == "WANTED" || a == "STOLEN"))
                        result.Status = ALPRStatus.Stolen;
                    else if (alerts.Any(a => a == "LIC SUSP" || a == "LIC REV"))
                        result.Status = ALPRStatus.SuspendedLicense;
                    else if (alerts.Any(a => a == "REG EXP" || a == "NO REG"))
                        result.Status = ALPRStatus.ExpiredRegistration;
                    else if (alerts.Any(a => a == "INS EXP" || a == "NO INS"))
                        result.Status = ALPRStatus.ExpiredInsurance;
                    else if (alerts.Any(a => a == "JAILED"))
                        result.Status = ALPRStatus.Incarcerated;

                    if (result.HitCount > 1)
                        result.Status = ALPRStatus.MultipleHits;

                    // Create alert string (max 2 alerts)
                    result.Alert = string.Join(" ", alerts.Take(2));
                    if (alerts.Count > 2)
                        result.Alert += $" +{alerts.Count - 2}";
                }
            }
            catch (Exception ex)
            {
                Game.LogTrivial($"[ALPR] Scan error for {plate}: {ex.Message}");
            }

            return result;
        }

        private void CreateBlipForVehicle(Vehicle veh, ALPRStatus status)
        {
            // Don't create duplicate blips
            if (_activeBlips.Any(b => b.Vehicle == veh))
                return;

            Blip blip = veh.AttachBlip();

            // Set color based on severity
            switch (status)
            {
                case ALPRStatus.Stolen:
                case ALPRStatus.Wanted:
                case ALPRStatus.SuspendedLicense:
                case ALPRStatus.RevokedLicense:
                    blip.Color = Color.Red;
                    blip.IsFriendly = false;
                    break;
                case ALPRStatus.ExpiredRegistration:
                case ALPRStatus.NoRegistration:
                case ALPRStatus.ExpiredInsurance:
                case ALPRStatus.NoInsurance:
                    blip.Color = Color.Yellow;
                    break;
                case ALPRStatus.MultipleHits:
                    blip.Color = Color.Orange;
                    break;
                default:
                    blip.Color = Color.White;
                    break;
            }

            blip.IsRouteEnabled = false;

            _activeBlips.Add(new ActiveBlip
            {
                Blip = blip,
                Vehicle = veh,
                CreatedTime = DateTime.Now,
                Status = status
            });
        }

        // Modified SendNotification method to include sound
        private void SendNotification(Vehicle veh, ALPRResult result)
        {
            // Rate limit notifications
            if ((DateTime.Now - _lastNotification).TotalSeconds < NOTIFICATION_COOLDOWN_SECONDS)
                return;

            var existing = _scannedVehicles.FirstOrDefault(v => v.Vehicle == veh);
            if (existing != null && existing.Notified)
                return;

            // Play notification sound using native audio
            PlayNotificationSound();

            string color = GetNotificationColor(result.Status);

            Game.DisplayNotification(
                $"~b~ALPR HIT~s~\n" +
                $"Vehicle: ~y~{result.Plate}~s~\n" +
                $"Alert: {color}{result.Alert}~s~\n" +
                $"Total hits: ~r~{result.HitCount}");

            _lastNotification = DateTime.Now;
            if (existing != null)
                existing.Notified = true;
        }

        // Add this new method to play sound using native GTA V audio
        private void PlayNotificationSound()
        {
            try
            {
                // Method 1: Use built-in Rage function (if available in your version)
                if (_useNativeSounds)
                {
                    // Play a simple beep sound
                    NativeFunction.Natives.PLAY_SOUND_FRONTEND(-1, "Beep_Red", "DLC_HEIST_HACKING_SNAKE_SOUNDS", false);

                    // Alternative police-related sounds you can try:
                    // NativeFunction.Natives.PLAY_SOUND_FRONTEND(-1, "Fingerprint", "HUD_FRONTEND_DEFAULT_SOUNDSET", false);
                    // NativeFunction.Natives.PLAY_SOUND_FRONTEND(-1, "5_Second_Timer", "DLC_HEIST_HACKING_SNAKE_SOUNDS", false);
                    // NativeFunction.Natives.PLAY_SOUND_FRONTEND(-1, "TIMER_STOP", "HUD_MINI_GAME_SOUNDSET", false);
                    // NativeFunction.Natives.PLAY_SOUND_FRONTEND(-1, "Event_Start_Text", "GTAO_FM_Events_Soundset", false);
                    // NativeFunction.Natives.PLAY_SOUND_FRONTEND(-1, "ATM_WINDOW", "HUD_FRONTEND_DEFAULT_SOUNDSET", false);
                }
            }
            catch (Exception ex)
            {
                Game.LogTrivial($"[Directional ALPR] Failed to play native sound: {ex.Message}");
            }
        }

        // Optional: Add a method to test different sounds
        private void TestSound(string soundName, string soundSet)
        {
            try
            {
                NativeFunction.Natives.PLAY_SOUND_FRONTEND(-1, soundName, soundSet, false);
                Game.DisplayNotification($"Playing sound: {soundName} from {soundSet}");
            }
            catch (Exception ex)
            {
                Game.LogTrivial($"[Directional ALPR] Failed to play test sound: {ex.Message}");
            }
        }

        private string GetNotificationColor(ALPRStatus status)
        {
            switch (status)
            {
                case ALPRStatus.Stolen:
                case ALPRStatus.Wanted:
                case ALPRStatus.SuspendedLicense:
                case ALPRStatus.RevokedLicense:
                    return "~r~";
                case ALPRStatus.ExpiredRegistration:
                case ALPRStatus.NoRegistration:
                case ALPRStatus.ExpiredInsurance:
                case ALPRStatus.NoInsurance:
                    return "~y~";
                case ALPRStatus.MultipleHits:
                    return "~o~";
                default:
                    return "~w~";
            }
        }

        private string CleanPlate(string plate)
        {
            return plate?.Replace(" ", "").ToUpper().Trim() ?? "NO PLATE";
        }

        private void DrawUI()
        {
            // Background
            DrawRect(_displayX, _displayY, _displayWidth, _displayHeight, 15, 15, 15, 220);

            // Header
            DrawText("ALPR",
                _displayX - 0.1f,
                _displayY - 0.075f,
                0.42f,
                Color.Cyan,
                false);

            float lineY = _displayY - 0.04f;

            // Front vehicle
            DrawPlateLine("FRONT", _frontResult, lineY);

            // Rear vehicle
            DrawPlateLine("REAR", _rearResult, lineY + 0.045f);

            // Status footer
            string statusText = _alprEnabled ? "ACTIVE" : "OFF";
            Color statusColor = _alprEnabled ? Color.LightGreen : Color.Red;

            DrawText($"F7: {statusText}",
                _displayX - 0.1f,
                _displayY + 0.065f,
                0.30f,
                statusColor,
                false);
        }

        private void DrawPlateLine(string label, ALPRResult result, float y)
        {
            if (result == null)
            {
                DrawText($"{label}: ---",
                    _displayX - 0.1f,
                    y,
                    0.35f,
                    Color.White,
                    false);
                return;
            }

            Color color = GetDisplayColor(result.Status);
            string alertText = string.IsNullOrEmpty(result.Alert) ? "" : $" {result.Alert}";

            DrawText($"{label}: {result.Plate} [{result.Distance:F0}m]{alertText}",
                _displayX - 0.1f,
                y,
                0.35f,
                color,
                false);
        }

        private Color GetDisplayColor(ALPRStatus status)
        {
            switch (status)
            {
                case ALPRStatus.Clean:
                    return Color.LightGreen;
                case ALPRStatus.ExpiredRegistration:
                case ALPRStatus.NoRegistration:
                case ALPRStatus.ExpiredInsurance:
                case ALPRStatus.NoInsurance:
                    return Color.Yellow;
                case ALPRStatus.Stolen:
                case ALPRStatus.Wanted:
                case ALPRStatus.SuspendedLicense:
                case ALPRStatus.RevokedLicense:
                    return Color.Red;
                case ALPRStatus.MultipleHits:
                    return Color.Orange;
                case ALPRStatus.Incarcerated:
                    return Color.Purple;
                default:
                    return Color.White;
            }
        }

        private void DrawText(string text, float x, float y, float scale, Color color, bool centered)
        {
            NativeFunction.Natives.SET_TEXT_FONT(0);
            NativeFunction.Natives.SET_TEXT_SCALE(scale, scale);
            NativeFunction.Natives.SET_TEXT_COLOUR(color.R, color.G, color.B, color.A);
            NativeFunction.Natives.SET_TEXT_CENTRE(centered);
            NativeFunction.Natives.SET_TEXT_OUTLINE();
            NativeFunction.Natives.BEGIN_TEXT_COMMAND_DISPLAY_TEXT("STRING");
            NativeFunction.Natives.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME(text);
            NativeFunction.Natives.END_TEXT_COMMAND_DISPLAY_TEXT(x, y);
        }

        private void DrawRect(float x, float y, float width, float height, int r, int g, int b, int a)
        {
            NativeFunction.Natives.DRAW_RECT(x, y, width, height, r, g, b, a);
        }
    }
}
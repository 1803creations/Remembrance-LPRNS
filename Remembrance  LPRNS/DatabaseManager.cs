using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text;
using Rage;

namespace PersistentWorld.Database
{
    public class VehicleRecord
    {
        public string LicensePlate { get; set; }
        public string VehicleModel { get; set; }
        public string ColorPrimary { get; set; }
        public string ColorSecondary { get; set; }
        public string RegisteredState { get; set; } = "San Andreas";
        public string OwnerType { get; set; } // 'person' or 'company'
        public int OwnerId { get; set; }
        public string Notes { get; set; }
    }

    public class DatabaseManager : IDisposable
    {
        private string _connectionString;
        private SQLiteConnection _connection;
        private Random _random = new Random();

        public DatabaseManager(string databasePath)
        {
            _connectionString = $"Data Source={databasePath};Version=3;";
            _connection = new SQLiteConnection(_connectionString);
        }

        public void InitializeDatabase()
        {
            _connection.Open();

            // Create tables with updated schema (peds table now has NO vehicle columns)
            string[] createTableQueries = {
                @"
                CREATE TABLE IF NOT EXISTS companies (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL UNIQUE,
                    industry TEXT,
                    headquarters_address TEXT,
                    phone_number TEXT
                )",
                
                // PEDS TABLE - NO VEHICLE COLUMNS
                @"
                CREATE TABLE IF NOT EXISTS peds (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    first_name TEXT NOT NULL,
                    last_name TEXT NOT NULL,
                    model_name TEXT NOT NULL,
                    gender TEXT,
                    home_address TEXT,
                    
                    -- HOME LOCATION FIELDS
                    has_home INTEGER DEFAULT 0,
                    home_type TEXT DEFAULT 'None',
                    home_coord_x REAL,
                    home_coord_y REAL,
                    home_coord_z REAL,
                    
                    -- SPAWN PERCENTAGES
                    is_home_percent INTEGER DEFAULT 30,
                    is_driving_percent INTEGER DEFAULT 30,
                    in_world_percent INTEGER DEFAULT 40,
                    is_carrying_gun_percent INTEGER DEFAULT 10,
                    
                    license_number TEXT UNIQUE,
                    license_status TEXT DEFAULT 'Valid',
                    license_reason TEXT,
                    license_expiry TEXT,
                    license_class TEXT DEFAULT 'Class C',
                    date_of_birth TEXT,
                    is_wanted INTEGER DEFAULT 0,
                    wanted_reason TEXT,
                    wanted_last_seen DATETIME,
                    is_incarcerated INTEGER DEFAULT 0,
                    incarcerated_reason TEXT,
                    incarcerated_date DATETIME,
                    incarcerated_days INTEGER DEFAULT 0,
                    release_date DATETIME,
                    is_active INTEGER DEFAULT 1
                )",
                
                // VEHICLES TABLE
                @"
                CREATE TABLE IF NOT EXISTS vehicles (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    license_plate TEXT UNIQUE NOT NULL,
                    vehicle_model TEXT NOT NULL,
                    color_primary TEXT,
                    color_secondary TEXT,
                    registered_state TEXT DEFAULT 'San Andreas',
                    owner_type TEXT NOT NULL,
                    owner_id INTEGER NOT NULL,
                    
                    -- VEHICLE STATUS FIELDS
                    registration_expiry TEXT DEFAULT '2026-12-01',
                    insurance_expiry TEXT DEFAULT '2026-12-01',
                    is_impounded INTEGER DEFAULT 0,
                    impounded_reason TEXT,
                    impounded_date DATETIME,
                    impounded_location TEXT,
                    is_stolen INTEGER DEFAULT 0,
                    stolen_reason TEXT,
                    stolen_date DATETIME,
                    stolen_recovered_date DATETIME,
                    no_registration INTEGER DEFAULT 0,
                    no_insurance INTEGER DEFAULT 0,
                    
                    is_active INTEGER DEFAULT 1,
                    notes TEXT,
                    created_date DATETIME DEFAULT CURRENT_TIMESTAMP,
                    last_modified DATETIME DEFAULT CURRENT_TIMESTAMP
                )",
                
                // TICKETS TABLE
                @"
                CREATE TABLE IF NOT EXISTS tickets (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ped_id INTEGER,
                    vehicle_id INTEGER,
                    offense TEXT NOT NULL,
                    fine_amount INTEGER,
                    date_issued DATETIME DEFAULT CURRENT_TIMESTAMP,
                    issuing_officer TEXT,
                    location TEXT,
                    notes TEXT,
                    FOREIGN KEY (ped_id) REFERENCES peds(id),
                    FOREIGN KEY (vehicle_id) REFERENCES vehicles(id)
                )",
                
                // INCARCERATION HISTORY TABLE
                @"
                CREATE TABLE IF NOT EXISTS incarceration_history (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ped_id INTEGER NOT NULL,
                    reason TEXT NOT NULL,
                    days_sentenced INTEGER NOT NULL,
                    date_incarcerated DATETIME DEFAULT CURRENT_TIMESTAMP,
                    date_released DATETIME,
                    released_by TEXT,
                    notes TEXT,
                    FOREIGN KEY (ped_id) REFERENCES peds(id)
                )",

                @"
                CREATE TABLE IF NOT EXISTS employment (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ped_id INTEGER NOT NULL,
                    company_id INTEGER NOT NULL,
                    job_title TEXT,
                    start_date TEXT,
                    end_date TEXT,
                    is_current INTEGER DEFAULT 1,
                    FOREIGN KEY (ped_id) REFERENCES peds(id),
                    FOREIGN KEY (company_id) REFERENCES companies(id)
                )"
            };

            foreach (string query in createTableQueries)
            {
                using (var command = new SQLiteCommand(query, _connection))
                {
                    command.ExecuteNonQuery();
                }
            }

            // Add columns if they don't exist (for backward compatibility)
            try
            {
                string[] alterQueries = {
                    "ALTER TABLE peds ADD COLUMN has_home INTEGER DEFAULT 0",
                    "ALTER TABLE peds ADD COLUMN home_type TEXT DEFAULT 'None'",
                    "ALTER TABLE peds ADD COLUMN home_coord_x REAL",
                    "ALTER TABLE peds ADD COLUMN home_coord_y REAL",
                    "ALTER TABLE peds ADD COLUMN home_coord_z REAL",
                    "ALTER TABLE peds ADD COLUMN is_home_percent INTEGER DEFAULT 30",
                    "ALTER TABLE peds ADD COLUMN is_driving_percent INTEGER DEFAULT 30",
                    "ALTER TABLE peds ADD COLUMN in_world_percent INTEGER DEFAULT 40",
                    "ALTER TABLE peds ADD COLUMN is_carrying_gun_percent INTEGER DEFAULT 10",
                    "ALTER TABLE peds ADD COLUMN license_status TEXT DEFAULT 'Valid'",
                    "ALTER TABLE peds ADD COLUMN license_reason TEXT",
                    "ALTER TABLE peds ADD COLUMN license_expiry TEXT",
                    "ALTER TABLE peds ADD COLUMN license_class TEXT DEFAULT 'Class C'",
                    "ALTER TABLE peds ADD COLUMN is_wanted INTEGER DEFAULT 0",
                    "ALTER TABLE peds ADD COLUMN wanted_reason TEXT",
                    "ALTER TABLE peds ADD COLUMN wanted_last_seen DATETIME",
                    "ALTER TABLE peds ADD COLUMN is_incarcerated INTEGER DEFAULT 0",
                    "ALTER TABLE peds ADD COLUMN incarcerated_reason TEXT",
                    "ALTER TABLE peds ADD COLUMN incarcerated_date DATETIME",
                    "ALTER TABLE peds ADD COLUMN incarcerated_days INTEGER DEFAULT 0",
                    "ALTER TABLE peds ADD COLUMN release_date DATETIME",

                    // Add new vehicle columns if they don't exist
                    "ALTER TABLE vehicles ADD COLUMN registration_expiry TEXT DEFAULT '2026-12-01'",
                    "ALTER TABLE vehicles ADD COLUMN insurance_expiry TEXT DEFAULT '2026-12-01'",
                    "ALTER TABLE vehicles ADD COLUMN is_impounded INTEGER DEFAULT 0",
                    "ALTER TABLE vehicles ADD COLUMN impounded_reason TEXT",
                    "ALTER TABLE vehicles ADD COLUMN impounded_date DATETIME",
                    "ALTER TABLE vehicles ADD COLUMN impounded_location TEXT",
                    "ALTER TABLE vehicles ADD COLUMN is_stolen INTEGER DEFAULT 0",
                    "ALTER TABLE vehicles ADD COLUMN stolen_reason TEXT",
                    "ALTER TABLE vehicles ADD COLUMN stolen_date DATETIME",
                    "ALTER TABLE vehicles ADD COLUMN stolen_recovered_date DATETIME",
                    "ALTER TABLE vehicles ADD COLUMN no_registration INTEGER DEFAULT 0",
                    "ALTER TABLE vehicles ADD COLUMN no_insurance INTEGER DEFAULT 0"
                };

                foreach (string query in alterQueries)
                {
                    try
                    {
                        using (var command = new SQLiteCommand(query, _connection))
                        {
                            command.ExecuteNonQuery();
                        }
                    }
                    catch (Exception ex)
                    {
                        Game.LogTrivial($"[Database] Note: {ex.Message} (this is normal if column already exists)");
                    }
                }
            }
            catch (Exception ex)
            {
                Game.LogTrivial($"[Database] Error in alter queries: {ex.Message}");
            }

            // Migrate old data if needed
            MigrateToUnifiedVehicles();

            Game.LogTrivial("Database tables created/verified");
        }

        private void MigrateToUnifiedVehicles()
        {
            try
            {
                Game.LogTrivial("[Database] Checking for old vehicle tables to migrate...");

                var tables = GetTableList();

                if ((tables.Contains("personal_vehicles") || tables.Contains("fleet_vehicles")) &&
                    IsTableEmpty("vehicles"))
                {
                    Game.LogTrivial("[Database] Starting migration to unified vehicles table...");

                    using (var transaction = _connection.BeginTransaction())
                    {
                        int migratedCount = 0;

                        if (tables.Contains("personal_vehicles"))
                        {
                            string migratePersonal = @"
                                INSERT INTO vehicles (
                                    license_plate, vehicle_model, color_primary, color_secondary, 
                                    registered_state, owner_type, owner_id, notes,
                                    registration_expiry, insurance_expiry, is_stolen, no_registration, no_insurance
                                )
                                SELECT 
                                    license_plate, 
                                    vehicle_model, 
                                    color_primary, 
                                    color_secondary, 
                                    registered_state,
                                    'person',
                                    owner_id,
                                    'Migrated from personal_vehicles',
                                    '2026-12-01',
                                    '2026-12-01',
                                    0,
                                    0,
                                    0
                                FROM personal_vehicles
                                WHERE license_plate IS NOT NULL AND license_plate != ''";

                            using (var cmd = new SQLiteCommand(migratePersonal, _connection))
                            {
                                migratedCount += cmd.ExecuteNonQuery();
                            }
                        }

                        if (tables.Contains("fleet_vehicles"))
                        {
                            string migrateFleet = @"
                                INSERT INTO vehicles (
                                    license_plate, vehicle_model, color_primary, color_secondary, 
                                    registered_state, owner_type, owner_id, notes,
                                    registration_expiry, insurance_expiry, is_stolen, no_registration, no_insurance
                                )
                                SELECT 
                                    license_plate, 
                                    vehicle_model, 
                                    color_primary, 
                                    color_secondary,
                                    'Fleet',
                                    'company',
                                    company_id,
                                    notes,
                                    '2026-12-01',
                                    '2026-12-01',
                                    0,
                                    0,
                                    0
                                FROM fleet_vehicles
                                WHERE license_plate IS NOT NULL AND license_plate != ''";

                            using (var cmd = new SQLiteCommand(migrateFleet, _connection))
                            {
                                migratedCount += cmd.ExecuteNonQuery();
                            }
                        }

                        transaction.Commit();
                        Game.LogTrivial($"[Database] Migration completed: {migratedCount} vehicles migrated");
                    }
                }
                else
                {
                    Game.LogTrivial("[Database] No migration needed");
                }
            }
            catch (Exception ex)
            {
                Game.LogTrivial($"[Database] Migration error: {ex.Message}");
            }
        }

        private List<string> GetTableList()
        {
            var tables = new List<string>();
            string query = "SELECT name FROM sqlite_master WHERE type='table'";

            using (var cmd = new SQLiteCommand(query, _connection))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    tables.Add(reader["name"].ToString());
                }
            }

            return tables;
        }

        private bool IsTableEmpty(string tableName)
        {
            string query = $"SELECT COUNT(*) FROM {tableName}";
            using (var cmd = new SQLiteCommand(query, _connection))
            {
                long count = (long)cmd.ExecuteScalar();
                return count == 0;
            }
        }

        public SQLiteConnection GetConnection()
        {
            return _connection;
        }

        //=============================================================================
        // LOOKUP METHODS - UPDATED TO USE JOIN FOR VEHICLES
        //=============================================================================
        public List<Dictionary<string, object>> LookupByName(string firstName, string lastName)
        {
            var results = new List<Dictionary<string, object>>();

            string query = @"
                SELECT 
                    p.id,
                    p.first_name,
                    p.last_name,
                    p.home_address,
                    p.license_number,
                    p.license_status,
                    p.license_reason,
                    p.license_expiry,
                    p.license_class,
                    p.model_name,
                    p.gender,
                    p.date_of_birth,
                    p.has_home,
                    p.home_type,
                    p.home_coord_x,
                    p.home_coord_y,
                    p.home_coord_z,
                    p.is_home_percent,
                    p.is_driving_percent,
                    p.in_world_percent,
                    p.is_carrying_gun_percent,
                    p.is_wanted,
                    p.wanted_reason,
                    p.wanted_last_seen,
                    p.is_incarcerated,
                    p.incarcerated_reason,
                    p.incarcerated_days,
                    p.release_date,
                    p.is_active,
                    e.company_id,
                    c.name as employer_name,
                    e.job_title,
                    -- Get ALL vehicles for this person in a single query
                    GROUP_CONCAT(v.id || '|' || v.license_plate || '|' || v.vehicle_model || '|' || 
                                v.color_primary || '|' || v.color_secondary || '|' || 
                                v.registration_expiry || '|' || v.insurance_expiry || '|' ||
                                v.is_impounded || '|' || v.is_stolen, ';') as vehicles_data
                FROM peds p
                LEFT JOIN employment e ON p.id = e.ped_id AND e.is_current = 1
                LEFT JOIN companies c ON e.company_id = c.id
                LEFT JOIN vehicles v ON v.owner_type = 'person' AND v.owner_id = p.id AND v.is_active = 1
                WHERE (LENGTH(@firstName) = 0 OR p.first_name = @firstName)
                AND (LENGTH(@lastName) = 0 OR p.last_name = @lastName)
                GROUP BY p.id
                ORDER BY p.last_name, p.first_name";

            using (var command = new SQLiteCommand(query, _connection))
            {
                command.Parameters.AddWithValue("@firstName", firstName ?? "");
                command.Parameters.AddWithValue("@lastName", lastName ?? "");

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var person = new Dictionary<string, object>();

                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            string columnName = reader.GetName(i);
                            if (!reader.IsDBNull(i))
                            {
                                person[columnName] = reader.GetValue(i);
                            }
                            else
                            {
                                person[columnName] = null;
                            }
                        }

                        // Parse vehicles_data into owned_vehicles list
                        if (person.ContainsKey("vehicles_data") && person["vehicles_data"] != null)
                        {
                            string vehiclesData = person["vehicles_data"].ToString();
                            person["owned_vehicles"] = ParseVehiclesData(vehiclesData);
                        }
                        else
                        {
                            person["owned_vehicles"] = new List<Dictionary<string, object>>();
                        }

                        // Get ticket history (keep as separate query)
                        if (person.ContainsKey("id") && person["id"] != null)
                        {
                            person["ticket_history"] = GetPersonTickets(Convert.ToInt32(person["id"]));
                            person["incarceration_history"] = GetIncarcerationHistory(Convert.ToInt32(person["id"]));
                        }

                        results.Add(person);
                    }
                }
            }

            return results;
        }

        private List<Dictionary<string, object>> ParseVehiclesData(string vehiclesData)
        {
            var vehicles = new List<Dictionary<string, object>>();

            if (string.IsNullOrEmpty(vehiclesData))
                return vehicles;

            string[] vehicleStrings = vehiclesData.Split(';');

            foreach (string vehicleString in vehicleStrings)
            {
                if (string.IsNullOrEmpty(vehicleString)) continue;

                string[] parts = vehicleString.Split('|');
                if (parts.Length >= 5)
                {
                    var vehicle = new Dictionary<string, object>();

                    // Parse ID
                    if (int.TryParse(parts[0], out int id))
                        vehicle["id"] = id;

                    vehicle["license_plate"] = parts[1];
                    vehicle["vehicle_model"] = parts[2];
                    vehicle["color_primary"] = parts[3];
                    vehicle["color_secondary"] = parts[4];

                    if (parts.Length > 5) vehicle["registration_expiry"] = parts[5];
                    if (parts.Length > 6) vehicle["insurance_expiry"] = parts[6];

                    if (parts.Length > 7 && int.TryParse(parts[7], out int impounded))
                        vehicle["is_impounded"] = impounded;

                    if (parts.Length > 8 && int.TryParse(parts[8], out int stolen))
                        vehicle["is_stolen"] = stolen;

                    vehicles.Add(vehicle);
                }
            }

            return vehicles;
        }

        public Dictionary<string, object> LookupByPlate(string plate)
        {
            var result = new Dictionary<string, object>();

            string query = @"
                SELECT 
                    v.id,
                    v.vehicle_model,
                    v.license_plate,
                    v.color_primary,
                    v.color_secondary,
                    v.registered_state,
                    v.owner_type,
                    v.owner_id,
                    v.notes,
                    v.registration_expiry,
                    v.insurance_expiry,
                    v.is_impounded,
                    v.impounded_reason,
                    v.impounded_date,
                    v.impounded_location,
                    v.is_stolen,
                    v.stolen_reason,
                    v.stolen_date,
                    v.stolen_recovered_date,
                    v.no_registration,
                    v.no_insurance,
                    CASE 
                        WHEN v.owner_type = 'person' THEN p.first_name || ' ' || p.last_name
                        WHEN v.owner_type = 'company' THEN c.name
                        ELSE 'Unknown'
                    END as owner_name,
                    p.id as ped_id,
                    p.first_name,
                    p.last_name,
                    p.home_address,
                    p.license_status,
                    p.license_number,
                    p.is_wanted,
                    p.wanted_reason,
                    p.is_incarcerated,
                    p.has_home,
                    p.home_type,
                    p.home_coord_x,
                    p.home_coord_y,
                    p.home_coord_z,
                    p.is_home_percent,
                    p.is_driving_percent,
                    p.in_world_percent,
                    p.is_carrying_gun_percent,
                    c.name as company_name,
                    c.industry
                FROM vehicles v
                LEFT JOIN peds p ON v.owner_type = 'person' AND v.owner_id = p.id
                LEFT JOIN companies c ON v.owner_type = 'company' AND v.owner_id = c.id
                WHERE v.license_plate = @plate AND v.is_active = 1";

            using (var command = new SQLiteCommand(query, _connection))
            {
                command.Parameters.AddWithValue("@plate", plate);

                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            string columnName = reader.GetName(i);
                            if (!reader.IsDBNull(i))
                            {
                                result[columnName] = reader.GetValue(i);
                            }
                            else
                            {
                                result[columnName] = null;
                            }
                        }

                        if (result.ContainsKey("id") && result["id"] != null)
                        {
                            result["ticket_history"] = GetVehicleTicketHistory(Convert.ToInt32(result["id"]));
                        }
                    }
                }
            }

            return result;
        }

        // NEW METHOD: Get vehicle by plate (simple version)
        public Dictionary<string, object> GetVehicleByPlate(string plate)
        {
            string query = "SELECT * FROM vehicles WHERE license_plate = @plate AND is_active = 1";

            using (var cmd = new SQLiteCommand(query, _connection))
            {
                cmd.Parameters.AddWithValue("@plate", plate);

                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        var vehicle = new Dictionary<string, object>();
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            string columnName = reader.GetName(i);
                            if (!reader.IsDBNull(i))
                            {
                                vehicle[columnName] = reader.GetValue(i);
                            }
                            else
                            {
                                vehicle[columnName] = null;
                            }
                        }
                        return vehicle;
                    }
                }
            }

            return null;
        }

        //=============================================================================
        // VEHICLE METHODS
        //=============================================================================
        public void AddVehicle(VehicleRecord vehicle)
        {
            string query = @"
                INSERT INTO vehicles (
                    license_plate, vehicle_model, color_primary, color_secondary, 
                    registered_state, owner_type, owner_id, notes,
                    registration_expiry, insurance_expiry, is_stolen, no_registration, no_insurance
                )
                VALUES (
                    @plate, @model, @color1, @color2, @state, @ownerType, @ownerId, @notes,
                    '2026-12-01', '2026-12-01', 0, 0, 0
                )";

            using (var cmd = new SQLiteCommand(query, _connection))
            {
                cmd.Parameters.AddWithValue("@plate", vehicle.LicensePlate);
                cmd.Parameters.AddWithValue("@model", vehicle.VehicleModel);
                cmd.Parameters.AddWithValue("@color1", vehicle.ColorPrimary ?? "");
                cmd.Parameters.AddWithValue("@color2", vehicle.ColorSecondary ?? "");
                cmd.Parameters.AddWithValue("@state", vehicle.RegisteredState);
                cmd.Parameters.AddWithValue("@ownerType", vehicle.OwnerType);
                cmd.Parameters.AddWithValue("@ownerId", vehicle.OwnerId);
                cmd.Parameters.AddWithValue("@notes", vehicle.Notes ?? "");

                cmd.ExecuteNonQuery();
            }

            Game.LogTrivial($"Added vehicle: {vehicle.LicensePlate} ({vehicle.VehicleModel})");
        }

        //=============================================================================
        // VEHICLE MODEL UPDATE METHODS - UPDATED (NO MORE PEDS UPDATE)
        //=============================================================================

        /// <summary>
        /// Updates vehicle model in the database (vehicles table only)
        /// </summary>
        public void UpdateVehicleModel(string licensePlate, string newModel)
        {
            try
            {
                if (string.IsNullOrEmpty(licensePlate) || string.IsNullOrEmpty(newModel))
                    return;

                Game.LogTrivial($"[Database] Attempting to update vehicle model for plate {licensePlate} to {newModel}");

                string query = @"
                    UPDATE vehicles 
                    SET vehicle_model = @model,
                        last_modified = CURRENT_TIMESTAMP
                    WHERE license_plate = @plate AND is_active = 1";

                using (var cmd = new SQLiteCommand(query, _connection))
                {
                    cmd.Parameters.AddWithValue("@plate", licensePlate);
                    cmd.Parameters.AddWithValue("@model", newModel);

                    int rowsAffected = cmd.ExecuteNonQuery();

                    if (rowsAffected > 0)
                    {
                        Game.LogTrivial($"[Database] Updated vehicle model for {licensePlate} to {newModel}");
                    }
                    else
                    {
                        Game.LogTrivial($"[Database] No vehicle found with plate {licensePlate} to update");
                    }
                }
            }
            catch (Exception ex)
            {
                Game.LogTrivial($"[Database] Error updating vehicle model: {ex.Message}");
            }
        }

        /// <summary>
        /// Batch updates multiple vehicle models at once
        /// </summary>
        public void BatchUpdateVehicleModels(Dictionary<string, string> plateToModelMap)
        {
            try
            {
                if (plateToModelMap == null || plateToModelMap.Count == 0)
                    return;

                Game.LogTrivial($"[Database] Batch updating {plateToModelMap.Count} vehicle models");

                using (var transaction = _connection.BeginTransaction())
                {
                    string query = @"
                        UPDATE vehicles 
                        SET vehicle_model = @model,
                            last_modified = CURRENT_TIMESTAMP
                        WHERE license_plate = @plate AND is_active = 1";

                    int updatedCount = 0;

                    foreach (var kvp in plateToModelMap)
                    {
                        if (string.IsNullOrEmpty(kvp.Key) || string.IsNullOrEmpty(kvp.Value))
                            continue;

                        using (var cmd = new SQLiteCommand(query, _connection))
                        {
                            cmd.Parameters.AddWithValue("@plate", kvp.Key);
                            cmd.Parameters.AddWithValue("@model", kvp.Value);

                            int rowsAffected = cmd.ExecuteNonQuery();
                            if (rowsAffected > 0)
                            {
                                updatedCount++;
                            }
                        }
                    }

                    transaction.Commit();
                    Game.LogTrivial($"[Database] Batch update completed: {updatedCount} vehicles updated");
                }
            }
            catch (Exception ex)
            {
                Game.LogTrivial($"[Database] Error in batch update: {ex.Message}");
            }
        }

        //=============================================================================
        // VEHICLE STATUS METHODS
        //=============================================================================

        // Set vehicle as impounded
        public void SetVehicleImpounded(string licensePlate, string reason, string location)
        {
            try
            {
                string query = @"
                    UPDATE vehicles 
                    SET is_impounded = 1,
                        impounded_reason = @reason,
                        impounded_date = @date,
                        impounded_location = @location,
                        last_modified = CURRENT_TIMESTAMP
                    WHERE license_plate = @plate AND is_active = 1";

                using (var cmd = new SQLiteCommand(query, _connection))
                {
                    cmd.Parameters.AddWithValue("@plate", licensePlate);
                    cmd.Parameters.AddWithValue("@reason", reason ?? "");
                    cmd.Parameters.AddWithValue("@date", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    cmd.Parameters.AddWithValue("@location", location ?? "");

                    cmd.ExecuteNonQuery();
                    Game.LogTrivial($"[Database] Vehicle {licensePlate} marked as impounded");
                }
            }
            catch (Exception ex)
            {
                Game.LogTrivial($"[Database] Error setting vehicle impounded: {ex.Message}");
            }
        }

        // Release vehicle from impound
        public void ReleaseImpoundedVehicle(string licensePlate)
        {
            try
            {
                string query = @"
                    UPDATE vehicles 
                    SET is_impounded = 0,
                        impounded_reason = NULL,
                        impounded_date = NULL,
                        impounded_location = NULL,
                        last_modified = CURRENT_TIMESTAMP
                    WHERE license_plate = @plate AND is_active = 1";

                using (var cmd = new SQLiteCommand(query, _connection))
                {
                    cmd.Parameters.AddWithValue("@plate", licensePlate);
                    cmd.ExecuteNonQuery();
                    Game.LogTrivial($"[Database] Vehicle {licensePlate} released from impound");
                }
            }
            catch (Exception ex)
            {
                Game.LogTrivial($"[Database] Error releasing vehicle: {ex.Message}");
            }
        }

        // Set vehicle as stolen
        public void SetVehicleStolen(string licensePlate, string reason)
        {
            try
            {
                string query = @"
                    UPDATE vehicles 
                    SET is_stolen = 1,
                        stolen_reason = @reason,
                        stolen_date = @date,
                        stolen_recovered_date = NULL,
                        last_modified = CURRENT_TIMESTAMP
                    WHERE license_plate = @plate AND is_active = 1";

                using (var cmd = new SQLiteCommand(query, _connection))
                {
                    cmd.Parameters.AddWithValue("@plate", licensePlate);
                    cmd.Parameters.AddWithValue("@reason", reason ?? "");
                    cmd.Parameters.AddWithValue("@date", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                    cmd.ExecuteNonQuery();
                    Game.LogTrivial($"[Database] Vehicle {licensePlate} marked as stolen");
                }
            }
            catch (Exception ex)
            {
                Game.LogTrivial($"[Database] Error setting vehicle stolen: {ex.Message}");
            }
        }

        // Recover stolen vehicle
        public void RecoverStolenVehicle(string licensePlate)
        {
            try
            {
                string query = @"
                    UPDATE vehicles 
                    SET is_stolen = 0,
                        stolen_reason = NULL,
                        stolen_recovered_date = @date,
                        last_modified = CURRENT_TIMESTAMP
                    WHERE license_plate = @plate AND is_active = 1";

                using (var cmd = new SQLiteCommand(query, _connection))
                {
                    cmd.Parameters.AddWithValue("@plate", licensePlate);
                    cmd.Parameters.AddWithValue("@date", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                    cmd.ExecuteNonQuery();
                    Game.LogTrivial($"[Database] Vehicle {licensePlate} marked as recovered");
                }
            }
            catch (Exception ex)
            {
                Game.LogTrivial($"[Database] Error recovering vehicle: {ex.Message}");
            }
        }

        // Set no registration flag
        public void SetNoRegistration(string licensePlate, bool noRegistration)
        {
            try
            {
                string query = @"
                    UPDATE vehicles 
                    SET no_registration = @value,
                        last_modified = CURRENT_TIMESTAMP
                    WHERE license_plate = @plate AND is_active = 1";

                using (var cmd = new SQLiteCommand(query, _connection))
                {
                    cmd.Parameters.AddWithValue("@plate", licensePlate);
                    cmd.Parameters.AddWithValue("@value", noRegistration ? 1 : 0);

                    cmd.ExecuteNonQuery();
                    Game.LogTrivial($"[Database] Vehicle {licensePlate} no_registration set to {noRegistration}");
                }
            }
            catch (Exception ex)
            {
                Game.LogTrivial($"[Database] Error setting no_registration: {ex.Message}");
            }
        }

        // Set no insurance flag
        public void SetNoInsurance(string licensePlate, bool noInsurance)
        {
            try
            {
                string query = @"
                    UPDATE vehicles 
                    SET no_insurance = @value,
                        last_modified = CURRENT_TIMESTAMP
                    WHERE license_plate = @plate AND is_active = 1";

                using (var cmd = new SQLiteCommand(query, _connection))
                {
                    cmd.Parameters.AddWithValue("@plate", licensePlate);
                    cmd.Parameters.AddWithValue("@value", noInsurance ? 1 : 0);

                    cmd.ExecuteNonQuery();
                    Game.LogTrivial($"[Database] Vehicle {licensePlate} no_insurance set to {noInsurance}");
                }
            }
            catch (Exception ex)
            {
                Game.LogTrivial($"[Database] Error setting no_insurance: {ex.Message}");
            }
        }

        // Update registration expiry
        public void UpdateRegistrationExpiry(string licensePlate, string expiryDate)
        {
            try
            {
                string query = @"
                    UPDATE vehicles 
                    SET registration_expiry = @expiry,
                        last_modified = CURRENT_TIMESTAMP
                    WHERE license_plate = @plate AND is_active = 1";

                using (var cmd = new SQLiteCommand(query, _connection))
                {
                    cmd.Parameters.AddWithValue("@plate", licensePlate);
                    cmd.Parameters.AddWithValue("@expiry", expiryDate ?? "");

                    cmd.ExecuteNonQuery();
                    Game.LogTrivial($"[Database] Vehicle {licensePlate} registration expiry set to {expiryDate}");
                }
            }
            catch (Exception ex)
            {
                Game.LogTrivial($"[Database] Error updating registration expiry: {ex.Message}");
            }
        }

        // Update insurance expiry
        public void UpdateInsuranceExpiry(string licensePlate, string expiryDate)
        {
            try
            {
                string query = @"
                    UPDATE vehicles 
                    SET insurance_expiry = @expiry,
                        last_modified = CURRENT_TIMESTAMP
                    WHERE license_plate = @plate AND is_active = 1";

                using (var cmd = new SQLiteCommand(query, _connection))
                {
                    cmd.Parameters.AddWithValue("@plate", licensePlate);
                    cmd.Parameters.AddWithValue("@expiry", expiryDate ?? "");

                    cmd.ExecuteNonQuery();
                    Game.LogTrivial($"[Database] Vehicle {licensePlate} insurance expiry set to {expiryDate}");
                }
            }
            catch (Exception ex)
            {
                Game.LogTrivial($"[Database] Error updating insurance expiry: {ex.Message}");
            }
        }

        //=============================================================================
        // TICKET METHODS
        //=============================================================================
        public void AddTicket(int pedId, int vehicleId, string offense, int fine, string location)
        {
            try
            {
                Game.LogTrivial($"[TICKET DEBUG] ===== ADDING TICKET =====");
                Game.LogTrivial($"[TICKET DEBUG] pedId: {pedId}");
                Game.LogTrivial($"[TICKET DEBUG] vehicleId: {vehicleId}");
                Game.LogTrivial($"[TICKET DEBUG] offense: '{offense}'");
                Game.LogTrivial($"[TICKET DEBUG] fine: {fine}");
                Game.LogTrivial($"[TICKET DEBUG] location: '{location}'");

                // Validate required fields
                if (pedId <= 0)
                {
                    Game.LogTrivial($"[TICKET ERROR] Invalid ped ID: {pedId}");
                    Game.DisplayNotification("~r~Error: Invalid person ID");
                    return;
                }

                if (string.IsNullOrEmpty(offense))
                {
                    Game.LogTrivial($"[TICKET ERROR] Offense is empty");
                    Game.DisplayNotification("~r~Error: No offense selected");
                    return;
                }

                // Check if ped exists
                string checkPedQuery = "SELECT COUNT(*) FROM peds WHERE id = @pedId";
                using (var checkPedCmd = new SQLiteCommand(checkPedQuery, _connection))
                {
                    checkPedCmd.Parameters.AddWithValue("@pedId", pedId);
                    long pedExists = (long)checkPedCmd.ExecuteScalar();
                    if (pedExists == 0)
                    {
                        Game.LogTrivial($"[TICKET ERROR] Ped ID {pedId} does not exist in database");
                        Game.DisplayNotification("~r~Error: Person not found in database");
                        return;
                    }
                }

                string query = @"
                    INSERT INTO tickets (
                        ped_id, 
                        vehicle_id, 
                        offense, 
                        fine_amount, 
                        issuing_officer, 
                        location, 
                        date_issued,
                        notes
                    ) VALUES (
                        @pedId, 
                        @vehicleId, 
                        @offense, 
                        @fine, 
                        @officer, 
                        @location, 
                        @date,
                        @notes
                    )";

                using (var command = new SQLiteCommand(query, _connection))
                {
                    command.Parameters.AddWithValue("@pedId", pedId);

                    // Handle vehicleId - if 0 or negative, use 1 as placeholder
                    if (vehicleId > 0)
                    {
                        // Check if vehicle exists
                        string checkVehQuery = "SELECT COUNT(*) FROM vehicles WHERE id = @vehId";
                        using (var checkVehCmd = new SQLiteCommand(checkVehQuery, _connection))
                        {
                            checkVehCmd.Parameters.AddWithValue("@vehId", vehicleId);
                            long vehExists = (long)checkVehCmd.ExecuteScalar();
                            if (vehExists == 0)
                            {
                                Game.LogTrivial($"[TICKET DEBUG] Vehicle ID {vehicleId} not found, using placeholder 1");
                                command.Parameters.AddWithValue("@vehicleId", 1);
                            }
                            else
                            {
                                command.Parameters.AddWithValue("@vehicleId", vehicleId);
                            }
                        }
                    }
                    else
                    {
                        // Use vehicle ID 1 as placeholder for citations without a vehicle
                        command.Parameters.AddWithValue("@vehicleId", 1);
                    }

                    command.Parameters.AddWithValue("@offense", offense);
                    command.Parameters.AddWithValue("@fine", fine);
                    command.Parameters.AddWithValue("@officer", "Officer");
                    command.Parameters.AddWithValue("@location", string.IsNullOrEmpty(location) ? "Los Santos" : location);
                    command.Parameters.AddWithValue("@date", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    command.Parameters.AddWithValue("@notes", "");

                    int rowsAffected = command.ExecuteNonQuery();

                    if (rowsAffected > 0)
                    {
                        Game.LogTrivial($"[TICKET] Added ticket for ped {pedId}: {offense}, fine ${fine}");
                    }
                    else
                    {
                        Game.LogTrivial($"[TICKET ERROR] No rows affected when adding ticket for ped {pedId}");
                        Game.DisplayNotification("~r~Error: Could not save ticket");
                    }
                }
            }
            catch (SQLiteException ex)
            {
                Game.LogTrivial($"[TICKET SQLITE ERROR] ===== SQLITE ERROR =====");
                Game.LogTrivial($"[TICKET SQLITE ERROR] Message: {ex.Message}");
                Game.LogTrivial($"[TICKET SQLITE ERROR] Error Code: {ex.ResultCode}");
                Game.LogTrivial($"[TICKET SQLITE ERROR] Stack Trace: {ex.StackTrace}");

                if (ex.Message.Contains("FOREIGN KEY"))
                {
                    Game.DisplayNotification("~r~Error: Invalid person or vehicle reference");
                }
                else if (ex.Message.Contains("NOT NULL"))
                {
                    Game.DisplayNotification("~r~Error: Missing required field");
                }
                else
                {
                    Game.DisplayNotification($"~r~Database error: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Game.LogTrivial($"[TICKET ERROR] {ex.Message}");
                Game.LogTrivial($"[TICKET ERROR] Stack Trace: {ex.StackTrace}");
                Game.DisplayNotification($"~r~Error issuing ticket: {ex.Message}");
            }
        }

        private List<Dictionary<string, object>> GetVehicleTicketHistory(int vehicleId)
        {
            var tickets = new List<Dictionary<string, object>>();

            string query = @"
                SELECT * FROM tickets 
                WHERE vehicle_id = @vehicleId 
                ORDER BY date_issued DESC";

            using (var command = new SQLiteCommand(query, _connection))
            {
                command.Parameters.AddWithValue("@vehicleId", vehicleId);

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var ticket = new Dictionary<string, object>();
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            string columnName = reader.GetName(i);
                            if (!reader.IsDBNull(i))
                            {
                                ticket[columnName] = reader.GetValue(i);
                            }
                            else
                            {
                                ticket[columnName] = null;
                            }
                        }
                        tickets.Add(ticket);
                    }
                }
            }

            return tickets;
        }

        private List<Dictionary<string, object>> GetPersonTickets(int pedId)
        {
            var tickets = new List<Dictionary<string, object>>();

            string query = @"
                SELECT t.*, v.license_plate, v.vehicle_model
                FROM tickets t
                LEFT JOIN vehicles v ON t.vehicle_id = v.id
                WHERE t.ped_id = @pedId
                ORDER BY t.date_issued DESC";

            using (var command = new SQLiteCommand(query, _connection))
            {
                command.Parameters.AddWithValue("@pedId", pedId);

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var ticket = new Dictionary<string, object>();
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            string columnName = reader.GetName(i);
                            if (!reader.IsDBNull(i))
                            {
                                ticket[columnName] = reader.GetValue(i);
                            }
                            else
                            {
                                ticket[columnName] = null;
                            }
                        }
                        tickets.Add(ticket);
                    }
                }
            }

            return tickets;
        }

        //=============================================================================
        // INCARCERATION METHODS
        //=============================================================================
        private List<Dictionary<string, object>> GetIncarcerationHistory(int pedId)
        {
            var history = new List<Dictionary<string, object>>();

            string query = @"
                SELECT * FROM incarceration_history 
                WHERE ped_id = @pedId 
                ORDER BY date_incarcerated DESC";

            using (var command = new SQLiteCommand(query, _connection))
            {
                command.Parameters.AddWithValue("@pedId", pedId);

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var record = new Dictionary<string, object>();
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            string columnName = reader.GetName(i);
                            if (!reader.IsDBNull(i))
                            {
                                record[columnName] = reader.GetValue(i);
                            }
                            else
                            {
                                record[columnName] = null;
                            }
                        }
                        history.Add(record);
                    }
                }
            }

            return history;
        }

        public void IncarceratePed(int pedId, string reason, int days, string notes = "")
        {
            try
            {
                DateTime now = DateTime.Now;
                DateTime releaseDate = now.AddDays(days);

                string updateQuery = @"
                    UPDATE peds SET 
                        is_incarcerated = 1,
                        incarcerated_reason = @reason,
                        incarcerated_date = @date,
                        incarcerated_days = @days,
                        release_date = @releaseDate
                    WHERE id = @pedId";

                using (var cmd = new SQLiteCommand(updateQuery, _connection))
                {
                    cmd.Parameters.AddWithValue("@pedId", pedId);
                    cmd.Parameters.AddWithValue("@reason", reason);
                    cmd.Parameters.AddWithValue("@date", now.ToString("yyyy-MM-dd HH:mm:ss"));
                    cmd.Parameters.AddWithValue("@days", days);
                    cmd.Parameters.AddWithValue("@releaseDate", releaseDate.ToString("yyyy-MM-dd HH:mm:ss"));

                    cmd.ExecuteNonQuery();
                }

                string historyQuery = @"
                    INSERT INTO incarceration_history (ped_id, reason, days_sentenced, date_incarcerated, notes)
                    VALUES (@pedId, @reason, @days, @date, @notes)";

                using (var cmd = new SQLiteCommand(historyQuery, _connection))
                {
                    cmd.Parameters.AddWithValue("@pedId", pedId);
                    cmd.Parameters.AddWithValue("@reason", reason);
                    cmd.Parameters.AddWithValue("@days", days);
                    cmd.Parameters.AddWithValue("@date", now.ToString("yyyy-MM-dd HH:mm:ss"));
                    cmd.Parameters.AddWithValue("@notes", notes ?? "");

                    cmd.ExecuteNonQuery();
                }

                Game.LogTrivial($"[INCARCERATE] Ped {pedId} incarcerated for {days} days: {reason}");
            }
            catch (Exception ex)
            {
                Game.LogTrivial($"[INCARCERATE] Error: {ex.Message}");
            }
        }

        public void ReleasePed(int pedId, string releasedBy = "")
        {
            try
            {
                DateTime now = DateTime.Now;

                string updateQuery = @"
                    UPDATE peds SET 
                        is_incarcerated = 0,
                        release_date = @releaseDate
                    WHERE id = @pedId";

                using (var cmd = new SQLiteCommand(updateQuery, _connection))
                {
                    cmd.Parameters.AddWithValue("@pedId", pedId);
                    cmd.Parameters.AddWithValue("@releaseDate", now.ToString("yyyy-MM-dd HH:mm:ss"));

                    cmd.ExecuteNonQuery();
                }

                string historyQuery = @"
                    UPDATE incarceration_history 
                    SET date_released = @releaseDate, released_by = @releasedBy
                    WHERE ped_id = @pedId AND date_released IS NULL";

                using (var cmd = new SQLiteCommand(historyQuery, _connection))
                {
                    cmd.Parameters.AddWithValue("@pedId", pedId);
                    cmd.Parameters.AddWithValue("@releaseDate", now.ToString("yyyy-MM-dd HH:mm:ss"));
                    cmd.Parameters.AddWithValue("@releasedBy", releasedBy ?? "System");

                    cmd.ExecuteNonQuery();
                }

                Game.LogTrivial($"[RELEASE] Ped {pedId} released");
            }
            catch (Exception ex)
            {
                Game.LogTrivial($"[RELEASE] Error: {ex.Message}");
            }
        }

        //=============================================================================
        // WANTED METHODS
        //=============================================================================
        public void SetWanted(int pedId, bool wanted, string reason = "")
        {
            try
            {
                string query = @"
                    UPDATE peds SET 
                        is_wanted = @wanted,
                        wanted_reason = @reason,
                        wanted_last_seen = @lastSeen
                    WHERE id = @pedId";

                using (var cmd = new SQLiteCommand(query, _connection))
                {
                    cmd.Parameters.AddWithValue("@pedId", pedId);
                    cmd.Parameters.AddWithValue("@wanted", wanted ? 1 : 0);
                    cmd.Parameters.AddWithValue("@reason", reason ?? "");
                    cmd.Parameters.AddWithValue("@lastSeen", wanted ? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") : "");

                    cmd.ExecuteNonQuery();
                }

                Game.LogTrivial($"Ped {pedId} wanted set to {wanted}");
            }
            catch (Exception ex)
            {
                Game.LogTrivial($"[WANTED] Error: {ex.Message}");
            }
        }

        //=============================================================================
        // UTILITY METHODS
        //=============================================================================
        public int GetPedCount()
        {
            try
            {
                using (var cmd = new SQLiteCommand("SELECT COUNT(*) FROM peds", _connection))
                {
                    return Convert.ToInt32(cmd.ExecuteScalar());
                }
            }
            catch (Exception ex)
            {
                Game.LogTrivial($"[Persistent World] Error counting peds: {ex.Message}");
                return 0;
            }
        }

        //=============================================================================
        // STOP THE PED IMPORT - UPDATED (no vehicle fields in peds table)
        //=============================================================================
        public void ImportStopThePedPeds()
        {
            try
            {
                Game.LogTrivial("[Persistent World] ===== STARTING STOP THE PED PEDESTRIAN IMPORT =====");

                string[] possiblePaths = {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins", "LSPDFR", "StopThePed", "StopThePed.db"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins", "LSPDFR", "StopThePed.db"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "StopThePed", "StopThePed.db"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "StopThePed.db")
                };

                string stpPath = null;
                foreach (string path in possiblePaths)
                {
                    if (File.Exists(path))
                    {
                        stpPath = path;
                        break;
                    }
                }

                if (stpPath == null)
                {
                    Game.LogTrivial("[Persistent World] Stop The Ped database not found.");
                    return;
                }

                Game.LogTrivial($"[Persistent World] Found Stop The Ped database at: {stpPath}");

                string[] commonModels = {
                    "a_m_y_skater_01", "a_m_y_skater_02", "a_m_y_stlat_01", "a_m_y_stwhi_01",
                    "a_m_y_sunbathe_01", "a_m_y_surfer_01", "a_m_y_vindouche_01", "a_m_y_vinewood_01",
                    "a_m_y_vinewood_02", "a_f_y_skater_01", "a_f_y_vinewood_01", "a_f_y_vinewood_02",
                    "g_m_y_famdnf_01", "g_m_y_famfor_01", "g_m_y_famca_01", "g_m_y_lost_01",
                    "g_m_y_mexgang_01", "g_m_y_mexgoon_01", "g_m_y_pologoon_01", "g_m_y_salvagoon_01"
                };

                using (var stpConn = new SQLiteConnection($"Data Source={stpPath};Version=3;"))
                {
                    stpConn.Open();

                    List<string> tables = new List<string>();
                    string tableQuery = "SELECT name FROM sqlite_master WHERE type='table'";
                    using (var tableCmd = new SQLiteCommand(tableQuery, stpConn))
                    using (var tableReader = tableCmd.ExecuteReader())
                    {
                        while (tableReader.Read())
                        {
                            tables.Add(tableReader["name"].ToString());
                        }
                    }

                    string[] possibleTables = { "Pedestrians", "Characters", "People", "Peds", "Persons" };
                    string dataTable = null;

                    foreach (var table in possibleTables)
                    {
                        if (tables.Contains(table, StringComparer.OrdinalIgnoreCase))
                        {
                            dataTable = table;
                            break;
                        }
                    }

                    if (dataTable == null)
                    {
                        Game.LogTrivial("[Persistent World] Could not find pedestrian data table.");
                        return;
                    }

                    List<string> columns = new List<string>();
                    string columnQuery = $"PRAGMA table_info({dataTable})";
                    using (var columnCmd = new SQLiteCommand(columnQuery, stpConn))
                    using (var columnReader = columnCmd.ExecuteReader())
                    {
                        while (columnReader.Read())
                        {
                            columns.Add(columnReader["name"].ToString().ToLower());
                        }
                    }

                    string query = $"SELECT * FROM {dataTable}";
                    int imported = 0;
                    int skipped = 0;
                    int totalProcessed = 0;

                    using (var cmd = new SQLiteCommand(query, stpConn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            totalProcessed++;

                            try
                            {
                                string firstName = "";
                                string lastName = "";
                                string fullName = "";

                                if (columns.Contains("firstname") || columns.Contains("first_name"))
                                {
                                    string col = columns.Contains("firstname") ? "firstname" : "first_name";
                                    firstName = reader[col]?.ToString()?.Trim() ?? "";
                                }

                                if (columns.Contains("lastname") || columns.Contains("last_name"))
                                {
                                    string col = columns.Contains("lastname") ? "lastname" : "last_name";
                                    lastName = reader[col]?.ToString()?.Trim() ?? "";
                                }

                                if (columns.Contains("fullname") || columns.Contains("full_name") || columns.Contains("name"))
                                {
                                    string col = columns.Contains("fullname") ? "fullname" : (columns.Contains("full_name") ? "full_name" : "name");
                                    fullName = reader[col]?.ToString()?.Trim() ?? "";
                                }

                                if (string.IsNullOrEmpty(firstName) && string.IsNullOrEmpty(lastName) && !string.IsNullOrEmpty(fullName))
                                {
                                    string[] nameParts = fullName.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                                    if (nameParts.Length >= 2)
                                    {
                                        firstName = nameParts[0];
                                        lastName = string.Join(" ", nameParts, 1, nameParts.Length - 1);
                                    }
                                    else if (nameParts.Length == 1)
                                    {
                                        firstName = nameParts[0];
                                        lastName = "";
                                    }
                                }

                                if (string.IsNullOrEmpty(firstName) && string.IsNullOrEmpty(lastName))
                                {
                                    skipped++;
                                    continue;
                                }

                                if (string.IsNullOrEmpty(lastName)) lastName = "";

                                var existing = LookupByName(firstName, lastName);
                                if (existing != null && existing.Count > 0)
                                {
                                    skipped++;
                                    continue;
                                }

                                string gender = "Unknown";
                                if (columns.Contains("gender"))
                                {
                                    gender = reader["gender"]?.ToString() ?? "Unknown";
                                }
                                else if (firstName.ToLower().EndsWith("a") || firstName.ToLower().EndsWith("ia") || firstName.ToLower().EndsWith("e"))
                                {
                                    gender = "Female";
                                }
                                else
                                {
                                    gender = "Male";
                                }

                                string dob = "";
                                if (columns.Contains("dob") || columns.Contains("dateofbirth") || columns.Contains("date_of_birth"))
                                {
                                    string dobCol = columns.Contains("dob") ? "dob" : (columns.Contains("dateofbirth") ? "dateofbirth" : "date_of_birth");
                                    var dobValue = reader[dobCol];
                                    if (dobValue != DBNull.Value)
                                    {
                                        try
                                        {
                                            DateTime dobDate = Convert.ToDateTime(dobValue);
                                            dob = dobDate.ToString("yyyy-MM-dd");
                                        }
                                        catch
                                        {
                                            dob = dobValue.ToString() ?? "";
                                        }
                                    }
                                }

                                string address = "";
                                if (columns.Contains("address") || columns.Contains("homeaddress") || columns.Contains("home_address"))
                                {
                                    string addrCol = columns.Contains("address") ? "address" : (columns.Contains("homeaddress") ? "homeaddress" : "home_address");
                                    address = reader[addrCol]?.ToString() ?? "";
                                }

                                string licenseNumber = "";
                                if (columns.Contains("licensenumber") || columns.Contains("license_number"))
                                {
                                    string licenseCol = columns.Contains("licensenumber") ? "licensenumber" : "license_number";
                                    licenseNumber = reader[licenseCol]?.ToString() ?? "";
                                }

                                if (string.IsNullOrEmpty(licenseNumber))
                                {
                                    licenseNumber = "STP" + _random.Next(100000, 999999).ToString();
                                }

                                string licenseStatus = "Valid";
                                if (columns.Contains("licensestatus") || columns.Contains("license_status"))
                                {
                                    string statusCol = columns.Contains("licensestatus") ? "licensestatus" : "license_status";
                                    licenseStatus = reader[statusCol]?.ToString() ?? "Valid";
                                }

                                string modelName = "";
                                if (columns.Contains("model") || columns.Contains("modelname") || columns.Contains("model_name"))
                                {
                                    string modelCol = columns.Contains("model") ? "model" : (columns.Contains("modelname") ? "modelname" : "model_name");
                                    modelName = reader[modelCol]?.ToString() ?? "";
                                }

                                if (string.IsNullOrEmpty(modelName))
                                {
                                    modelName = commonModels[_random.Next(commonModels.Length)];
                                }

                                // UPDATED: No vehicle fields in INSERT
                                string insertQuery = @"
                                    INSERT INTO peds (
                                        first_name, last_name, model_name, gender, home_address,
                                        has_home, home_type,
                                        is_home_percent, is_driving_percent, in_world_percent, is_carrying_gun_percent,
                                        license_number, license_status, license_reason, license_expiry, license_class,
                                        date_of_birth,
                                        is_wanted, wanted_reason,
                                        is_incarcerated,
                                        is_active
                                    ) VALUES (
                                        @firstName, @lastName, @modelName, @gender, @address,
                                        0, 'None',
                                        30, 30, 40, 10,
                                        @licenseNumber, @licenseStatus, '', '2026-12-31', 'Class C',
                                        @dob,
                                        0, '',
                                        0,
                                        1
                                    )";

                                using (var insertCmd = new SQLiteCommand(insertQuery, _connection))
                                {
                                    insertCmd.Parameters.AddWithValue("@firstName", firstName);
                                    insertCmd.Parameters.AddWithValue("@lastName", lastName);
                                    insertCmd.Parameters.AddWithValue("@modelName", modelName);
                                    insertCmd.Parameters.AddWithValue("@gender", gender);
                                    insertCmd.Parameters.AddWithValue("@address", address);
                                    insertCmd.Parameters.AddWithValue("@licenseNumber", licenseNumber);
                                    insertCmd.Parameters.AddWithValue("@licenseStatus", licenseStatus);
                                    insertCmd.Parameters.AddWithValue("@dob", dob);

                                    insertCmd.ExecuteNonQuery();
                                    imported++;
                                }

                                if (imported % 100 == 0)
                                {
                                    Game.LogTrivial($"[Persistent World] Imported {imported} peds so far...");
                                }
                            }
                            catch (Exception ex)
                            {
                                Game.LogTrivial($"[Persistent World] Error importing ped: {ex.Message}");
                                skipped++;
                            }
                        }
                    }

                    Game.LogTrivial($"[Persistent World] ===== IMPORT COMPLETE =====");
                    Game.LogTrivial($"[Persistent World] Processed: {totalProcessed} records");
                    Game.LogTrivial($"[Persistent World] Imported: {imported} new peds");
                    Game.LogTrivial($"[Persistent World] Skipped: {skipped} (duplicates or missing data)");
                }
            }
            catch (Exception ex)
            {
                Game.LogTrivial($"[Persistent World] Error in Stop The Ped import: {ex.Message}");
            }
        }

        //=============================================================================
        // SEED DATA - UPDATED (no vehicle fields in peds table)
        //=============================================================================
        public void SeedInitialData()
        {
            using (var command = new SQLiteCommand("SELECT COUNT(*) FROM peds", _connection))
            {
                long count = (long)command.ExecuteScalar();
                if (count > 0)
                {
                    Game.LogTrivial("Database already has data, skipping seed");
                    return;
                }
            }

            string[] companyQueries = {
                "INSERT INTO companies (name, industry, headquarters_address) VALUES ('Coors Brewing', 'Beverage Manufacturing', '123 Industrial Way, Paleto Bay')",
                "INSERT INTO companies (name, industry, headquarters_address) VALUES ('First Energy', 'Utilities', '456 Power Grid Ave, Davis')",
                "INSERT INTO companies (name, industry, headquarters_address) VALUES ('Amazon INC', 'Retail/Logistics', '789 Warehouse District, La Puerta')"
            };

            foreach (string query in companyQueries)
            {
                using (var command = new SQLiteCommand(query, _connection))
                {
                    command.ExecuteNonQuery();
                }
            }

            // UPDATED: No vehicle fields in peds INSERT
            string[] pedQueries = {
                @"INSERT INTO peds (
                    first_name, last_name, model_name, gender, home_address,
                    has_home, home_type,
                    is_home_percent, is_driving_percent, in_world_percent, is_carrying_gun_percent,
                    license_number, license_status, license_reason, license_expiry, license_class,
                    date_of_birth, is_wanted, wanted_reason, is_incarcerated
                ) VALUES (
                    'Ned', 'Stark', 'player_zero', 'Male', '839 Gibraltar Ave, Vinewood Hills',
                    1, 'Exterior',
                    80, 10, 10, 0,
                    'S1234567', 'Valid', '', '2026-12-31', 'Class C',
                    '1960-01-15', 0, '', 0
                )",
                @"INSERT INTO peds (
                    first_name, last_name, model_name, gender, home_address,
                    has_home, home_type,
                    is_home_percent, is_driving_percent, in_world_percent, is_carrying_gun_percent,
                    license_number, license_status, license_reason, license_expiry, license_class,
                    date_of_birth, is_wanted, wanted_reason, is_incarcerated
                ) VALUES (
                    'Tony', 'Soprano', 's_m_y_construct_01', 'Male', '742 Evergreen Terrace, Paleto Bay',
                    1, 'Exterior',
                    20, 60, 20, 30,
                    'S2345678', 'Suspended', 'Failure to appear', '2024-06-15', 'Class C',
                    '1965-08-22', 1, 'Grand theft auto, Assault', 0
                )",
                @"INSERT INTO peds (
                    first_name, last_name, model_name, gender, home_address,
                    has_home, home_type,
                    is_home_percent, is_driving_percent, in_world_percent, is_carrying_gun_percent,
                    license_number, license_status, license_reason, license_expiry, license_class,
                    date_of_birth, is_wanted, wanted_reason, is_incarcerated
                ) VALUES (
                    'Carmela', 'Soprano', 's_f_y_shop_01', 'Female', '742 Evergreen Terrace, Paleto Bay',
                    1, 'Exterior',
                    40, 30, 30, 0,
                    'S3456789', 'Valid', '', '2025-03-20', 'Class C',
                    '1967-11-03', 0, '', 0
                )"
            };

            foreach (string query in pedQueries)
            {
                using (var command = new SQLiteCommand(query, _connection))
                {
                    command.ExecuteNonQuery();
                }
            }

            // Get the IDs of the inserted peds
            int nedId = 1, tonyId = 2, carmelaId = 3;

            // Get actual IDs if possible
            using (var cmd = new SQLiteCommand("SELECT id FROM peds WHERE first_name = 'Ned' AND last_name = 'Stark'", _connection))
            {
                var result = cmd.ExecuteScalar();
                if (result != null) nedId = Convert.ToInt32(result);
            }

            using (var cmd = new SQLiteCommand("SELECT id FROM peds WHERE first_name = 'Tony' AND last_name = 'Soprano'", _connection))
            {
                var result = cmd.ExecuteScalar();
                if (result != null) tonyId = Convert.ToInt32(result);
            }

            using (var cmd = new SQLiteCommand("SELECT id FROM peds WHERE first_name = 'Carmela' AND last_name = 'Soprano'", _connection))
            {
                var result = cmd.ExecuteScalar();
                if (result != null) carmelaId = Convert.ToInt32(result);
            }

            string[] vehicleQueries = {
                $@"INSERT INTO vehicles (
                    license_plate, vehicle_model, color_primary, color_secondary, 
                    registered_state, owner_type, owner_id, notes,
                    registration_expiry, insurance_expiry, is_stolen, no_registration, no_insurance
                ) VALUES (
                    'STARK01', 'emperor', 'Black', 'Silver', 'San Andreas', 'person', {nedId}, 'Ned\'s personal car',
                    '2026-12-01', '2026-12-01', 0, 0, 0
                )",
                $@"INSERT INTO vehicles (
                    license_plate, vehicle_model, color_primary, color_secondary, 
                    registered_state, owner_type, owner_id, notes,
                    registration_expiry, insurance_expiry, is_stolen, no_registration, no_insurance
                ) VALUES (
                    'TONY01', 'bobcatxl', 'White', 'Tan', 'San Andreas', 'person', {tonyId}, 'Tony\'s personal truck',
                    '2026-12-01', '2026-12-01', 0, 0, 0
                )",
                $@"INSERT INTO vehicles (
                    license_plate, vehicle_model, color_primary, color_secondary, 
                    registered_state, owner_type, owner_id, notes,
                    registration_expiry, insurance_expiry, is_stolen, no_registration, no_insurance
                ) VALUES (
                    'CARM01', 'ingot', 'Gold', 'Brown', 'San Andreas', 'person', {carmelaId}, 'Carmela\'s personal car',
                    '2026-12-01', '2026-12-01', 0, 0, 0
                )",
                @"INSERT INTO vehicles (
                    license_plate, vehicle_model, color_primary, color_secondary, 
                    registered_state, owner_type, owner_id, notes,
                    registration_expiry, insurance_expiry, is_stolen, no_registration, no_insurance
                ) VALUES (
                    'COORS01', 'muleadd3', 'Silver', 'Gold', 'Fleet', 'company', 1, 'Coors Brewing delivery truck',
                    '2026-12-01', '2026-12-01', 0, 0, 0
                )",
                @"INSERT INTO vehicles (
                    license_plate, vehicle_model, color_primary, color_secondary, 
                    registered_state, owner_type, owner_id, notes,
                    registration_expiry, insurance_expiry, is_stolen, no_registration, no_insurance
                ) VALUES (
                    'ENERGY1', 'utilitytruck', 'Yellow', 'Black', 'Fleet', 'company', 2, 'First Energy service vehicle',
                    '2026-12-01', '2026-12-01', 0, 0, 0
                )",
                @"INSERT INTO vehicles (
                    license_plate, vehicle_model, color_primary, color_secondary, 
                    registered_state, owner_type, owner_id, notes,
                    registration_expiry, insurance_expiry, is_stolen, no_registration, no_insurance
                ) VALUES (
                    'AMZN01', 'boxville4', 'Blue', 'White', 'Fleet', 'company', 3, 'Amazon delivery van',
                    '2026-12-01', '2026-12-01', 0, 0, 0
                )"
            };

            foreach (string query in vehicleQueries)
            {
                using (var command = new SQLiteCommand(query, _connection))
                {
                    command.ExecuteNonQuery();
                }
            }

            string[] ticketQueries = {
                $"INSERT INTO tickets (ped_id, vehicle_id, offense, fine_amount, issuing_officer, location, date_issued) VALUES ({tonyId}, 2, 'Speeding', 150, 'Officer Jenkins', 'Highway 1', '2024-01-15')",
                $"INSERT INTO tickets (ped_id, vehicle_id, offense, fine_amount, issuing_officer, location, date_issued) VALUES ({tonyId}, 4, 'Expired Registration', 75, 'Officer Martinez', 'Downtown', '2024-02-03')"
            };

            foreach (string query in ticketQueries)
            {
                using (var command = new SQLiteCommand(query, _connection))
                {
                    command.ExecuteNonQuery();
                }
            }

            Game.LogTrivial("Seed data inserted successfully");
        }

        public void Dispose()
        {
            if (_connection != null && _connection.State == System.Data.ConnectionState.Open)
            {
                _connection.Close();
                _connection.Dispose();
            }
        }
    }
}
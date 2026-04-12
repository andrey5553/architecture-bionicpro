-- 1. Создание таблицы сырой телеметрии
CREATE TABLE IF NOT EXISTS raw_telemetry (
    event_time DateTime,
    serial_number String,
    battery_level UInt8,
    muscle_voltage Float32,
    steps_count UInt32,
    error_code UInt8
) ENGINE = MergeTree()
ORDER BY event_time;

-- 2. Генерация данных

-- Данные для user1 (SN-USER-01): Активный пользователь, без ошибок
INSERT INTO raw_telemetry (event_time, serial_number, battery_level, muscle_voltage, steps_count, error_code)
SELECT
    now() - INTERVAL number HOUR,
    'SN-USER-01',
    100 - (number % 50),      -- Разряд батареи
    10 + (rand() % 100) / 10, -- Напряжение от 10 до 20
    rand() % 1000,            -- Шаги
    0                         -- Нет ошибок
FROM numbers(48);             -- Данные за 48 часов

-- Данные для user2 (SN-USER-02): Мало ходит, иногда возникают ошибки
INSERT INTO raw_telemetry (event_time, serial_number, battery_level, muscle_voltage, steps_count, error_code)
SELECT
    now() - INTERVAL number HOUR,
    'SN-USER-02',
    90 - (number % 30),       -- Медленнее разряжается
    5 + (rand() % 50) / 10,   -- Слабое напряжение
    rand() % 200,             -- Мало шагов
    if(rand() % 10 == 0, 1, 0) -- Иногда ошибка (код 1)
FROM numbers(48);

-- Данные для prothetic1 (SN-PRO-01): Очень активный (тестировщик)
INSERT INTO raw_telemetry (event_time, serial_number, battery_level, muscle_voltage, steps_count, error_code)
SELECT
    now() - INTERVAL number HOUR,
    'SN-PRO-01',
    100 - (number % 80),      -- Быстро разряжается
    20 + (rand() % 150) / 10, -- Высокое напряжение
    1000 + (rand() % 2000),   -- Много шагов
    0
FROM numbers(48);

-- Данные для prothetic2 (SN-PRO-02): Нестабильная работа, критические ошибки
INSERT INTO raw_telemetry (event_time, serial_number, battery_level, muscle_voltage, steps_count, error_code)
SELECT
    now() - INTERVAL number HOUR,
    'SN-PRO-02',
    85 - (number % 85),
    rand() % 10,
    rand() % 500,
    if(rand() % 5 == 0, 99, 0) -- Частые критические ошибки (код 99)
FROM numbers(48);


-- 3. Создание Staging таблицы (структура)
CREATE TABLE IF NOT EXISTS stg_crm_users (
    keycloak_username String,
    full_name String,
    prosthesis_serial_number String
) ENGINE = MergeTree()
ORDER BY keycloak_username;

-- 4. Создание Витрины (структура)
CREATE TABLE IF NOT EXISTS report_user_daily_mart (
    report_date Date,
    keycloak_username String,
    client_name String,
    avg_battery_level Float32,
    total_steps UInt32,
    max_muscle_voltage Float32,
    errors_count UInt32
) ENGINE = SummingMergeTree()
ORDER BY (keycloak_username, report_date);

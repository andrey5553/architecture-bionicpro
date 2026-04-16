-- =====================================================
-- 1. CDC TABLES (из PostgreSQL CRM через Kafka)
-- =====================================================

-- 1.1 Raw Kafka таблицы для CDC данных из CRM
CREATE TABLE IF NOT EXISTS crm_orders_raw
(
    `after` String,
    `before` String,
    `op` String,
    `ts_ms` DateTime64(3)
) ENGINE = Kafka
SETTINGS kafka_broker_list = 'kafka:9092',
         kafka_topic_list = 'crm.public.orders',
         kafka_group_name = 'clickhouse_orders_group',
         kafka_format = 'JSONEachRow';

CREATE TABLE IF NOT EXISTS crm_customers_raw
(
    `after` String,
    `before` String,
    `op` String,
    `ts_ms` DateTime64(3)
) ENGINE = Kafka
SETTINGS kafka_broker_list = 'kafka:9092',
         kafka_topic_list = 'crm.public.customers',
         kafka_group_name = 'clickhouse_customers_group',
         kafka_format = 'JSONEachRow';

CREATE TABLE IF NOT EXISTS crm_products_raw
(
    `after` String,
    `before` String,
    `op` String,
    `ts_ms` DateTime64(3)
) ENGINE = Kafka
SETTINGS kafka_broker_list = 'kafka:9092',
         kafka_topic_list = 'crm.public.products',
         kafka_group_name = 'clickhouse_products_group',
         kafka_format = 'JSONEachRow';

-- 1.2 Таблицы для хранения конечных данных CRM
CREATE TABLE IF NOT EXISTS crm_orders (
    id Int64,
    customer_id Int64,
    order_date DateTime,
    status String,
    total_amount Decimal(10,2),
    updated_at DateTime DEFAULT now()
) ENGINE = ReplacingMergeTree(updated_at)
ORDER BY id;

CREATE TABLE IF NOT EXISTS crm_customers (
    id Int64,
    name String,
    email String,
    phone String,
    created_at DateTime,
    updated_at DateTime DEFAULT now()
) ENGINE = ReplacingMergeTree(updated_at)
ORDER BY id;

CREATE TABLE IF NOT EXISTS crm_products (
    id Int64,
    name String,
    price Decimal(10,2),
    stock Int64,
    updated_at DateTime DEFAULT now()
) ENGINE = ReplacingMergeTree(updated_at)
ORDER BY id;

-- 1.3 Materialized Views для парсинга JSON из Kafka
CREATE MATERIALIZED VIEW IF NOT EXISTS mv_orders_consumer TO crm_orders AS
SELECT
    JSONExtractInt64(after, 'id') AS id,
    JSONExtractInt64(after, 'customer_id') AS customer_id,
    JSONExtractDateTime(after, 'order_date') AS order_date,
    JSONExtractString(after, 'status') AS status,
    JSONExtractDecimal(after, 'total_amount', 10, 2) AS total_amount,
    now() AS updated_at
FROM crm_orders_raw
WHERE op IN ('c', 'u', 'r');

CREATE MATERIALIZED VIEW IF NOT EXISTS mv_customers_consumer TO crm_customers AS
SELECT
    JSONExtractInt64(after, 'id') AS id,
    JSONExtractString(after, 'name') AS name,
    JSONExtractString(after, 'email') AS email,
    JSONExtractString(after, 'phone') AS phone,
    JSONExtractDateTime(after, 'created_at') AS created_at,
    now() AS updated_at
FROM crm_customers_raw
WHERE op IN ('c', 'u', 'r');

CREATE MATERIALIZED VIEW IF NOT EXISTS mv_products_consumer TO crm_products AS
SELECT
    JSONExtractInt64(after, 'id') AS id,
    JSONExtractString(after, 'name') AS name,
    JSONExtractDecimal(after, 'price', 10, 2) AS price,
    JSONExtractInt64(after, 'stock') AS stock,
    now() AS updated_at
FROM crm_products_raw
WHERE op IN ('c', 'u', 'r');

-- =====================================================
-- 2. EXISTING TELEMETRY TABLES (расширенные)
-- =====================================================

-- 2.1 Создание таблицы сырой телеметрии (с дополнительными полями)
CREATE TABLE IF NOT EXISTS raw_telemetry (
    event_time DateTime,
    serial_number String,
    battery_level UInt8,
    muscle_voltage Float32,
    steps_count UInt32,
    error_code UInt8,
    device_temperature Float32,      -- новое поле
    firmware_version String,          -- новое поле
    signal_quality UInt8,             -- новое поле (0-100)
    ingestion_time DateTime DEFAULT now()
) ENGINE = MergeTree()
PARTITION BY toYYYYMM(event_time)
ORDER BY (serial_number, event_time)
TTL event_time + INTERVAL 90 DAY;    -- автоматическая очистка старых данных

-- 2.2 Генерация расширенных тестовых данных

-- Данные для user1 (SN-USER-01): Активный пользователь, без ошибок
INSERT INTO raw_telemetry (event_time, serial_number, battery_level, muscle_voltage, steps_count, error_code, device_temperature, firmware_version, signal_quality)
SELECT
    now() - INTERVAL number HOUR,
    'SN-USER-01',
    100 - (number % 50),
    10 + (rand() % 100) / 10,
    rand() % 1000,
    0,
    35 + (rand() % 5),               -- температура 35-40°C
    'v2.1.0',
    90 + (rand() % 10)               -- качество сигнала 90-100%
FROM numbers(168);  -- данные за 7 дней

-- Данные для user2 (SN-USER-02): Мало ходит, иногда возникают ошибки
INSERT INTO raw_telemetry (event_time, serial_number, battery_level, muscle_voltage, steps_count, error_code, device_temperature, firmware_version, signal_quality)
SELECT
    now() - INTERVAL number HOUR,
    'SN-USER-02',
    90 - (number % 30),
    5 + (rand() % 50) / 10,
    rand() % 200,
    if(rand() % 10 == 0, 1, 0),
    36 + (rand() % 8),
    'v2.0.5',
    70 + (rand() % 25)
FROM numbers(168);

-- Данные для prothetic1 (SN-PRO-01): Очень активный (тестировщик)
INSERT INTO raw_telemetry (event_time, serial_number, battery_level, muscle_voltage, steps_count, error_code, device_temperature, firmware_version, signal_quality)
SELECT
    now() - INTERVAL number HOUR,
    'SN-PRO-01',
    100 - (number % 80),
    20 + (rand() % 150) / 10,
    1000 + (rand() % 2000),
    0,
    34 + (rand() % 4),
    'v2.1.2',
    95 + (rand() % 5)
FROM numbers(168);

-- Данные для prothetic2 (SN-PRO-02): Нестабильная работа, критические ошибки
INSERT INTO raw_telemetry (event_time, serial_number, battery_level, muscle_voltage, steps_count, error_code, device_temperature, firmware_version, signal_quality)
SELECT
    now() - INTERVAL number HOUR,
    'SN-PRO-02',
    85 - (number % 85),
    rand() % 10,
    rand() % 500,
    if(rand() % 5 == 0, 99, 0),
    38 + (rand() % 12),
    'v1.9.8',
    40 + (rand() % 50)
FROM numbers(168);

-- 2.3 Создание агрегированных таблиц для телеметрии
CREATE TABLE IF NOT EXISTS telemetry_hourly_aggregates (
    hour DateTime,
    serial_number String,
    avg_battery_level Float32,
    avg_muscle_voltage Float32,
    total_steps UInt64,
    errors_count UInt32,
    avg_temperature Float32,
    avg_signal_quality Float32
) ENGINE = SummingMergeTree()
ORDER BY (serial_number, hour);

-- Materialized View для часовой агрегации
CREATE MATERIALIZED VIEW IF NOT EXISTS mv_telemetry_hourly_aggregates TO telemetry_hourly_aggregates AS
SELECT
    toStartOfHour(event_time) AS hour,
    serial_number,
    avg(battery_level) AS avg_battery_level,
    avg(muscle_voltage) AS avg_muscle_voltage,
    sum(steps_count) AS total_steps,
    countIf(error_code > 0) AS errors_count,
    avg(device_temperature) AS avg_temperature,
    avg(signal_quality) AS avg_signal_quality
FROM raw_telemetry
GROUP BY hour, serial_number;

-- =====================================================
-- 3. STAGING TABLES (маппинг пользователей Keycloak)
-- =====================================================

-- 3.1 Staging таблица для маппинга пользователей из Keycloak
CREATE TABLE IF NOT EXISTS stg_keycloak_users (
    keycloak_username String,
    email String,
    full_name String,
    prosthesis_serial_number String,
    crm_customer_id Int64,           -- связь с CRM
    created_at DateTime DEFAULT now(),
    updated_at DateTime DEFAULT now()
) ENGINE = MergeTree()
ORDER BY keycloak_username;

-- 3.2 Вставка тестовых данных для маппинга
INSERT INTO stg_keycloak_users (keycloak_username, email, full_name, prosthesis_serial_number, crm_customer_id) VALUES
    ('ivan.petrov', 'ivan@example.com', 'Иван Петров', 'SN-USER-01', 1),
    ('maria.sidorova', 'maria@example.com', 'Мария Сидорова', 'SN-USER-02', 2),
    ('alexey.ivanov', 'alex@example.com', 'Алексей Иванов', 'SN-PRO-01', 3),
    ('test.engineer', 'test@bionicpro.com', 'Тестовый Инженер', 'SN-PRO-02', NULL);

-- =====================================================
-- 4. MAIN MART (обновленная витрина с интеграцией CRM и телеметрии)
-- =====================================================

-- 4.1 Основная витрина для отчетности (объединяет телеметрию, пользователей и CRM)
CREATE TABLE IF NOT EXISTS report_user_daily_mart (
    report_date Date,
    keycloak_username String,
    client_name String,
    prosthesis_serial_number String,
    
    -- Телеметрия
    avg_battery_level Float32,
    total_steps UInt64,
    max_muscle_voltage Float32,
    errors_count UInt32,
    avg_temperature Float32,
    avg_signal_quality Float32,
    
    -- CRM данные (заказы)
    total_orders UInt32,
    total_spent Decimal(10,2),
    avg_order_value Decimal(10,2),
    last_order_date DateTime,
    
    -- Метрики активности
    activity_score Float32,          -- комбинированный score активности
    device_health_score Float32,     -- score здоровья устройства
    
    updated_at DateTime DEFAULT now()
) ENGINE = SummingMergeTree()
ORDER BY (keycloak_username, report_date);

-- 4.2 Materialized View для заполнения витрины
CREATE MATERIALIZED VIEW IF NOT EXISTS mv_report_user_daily_mart TO report_user_daily_mart AS
SELECT
    toDate(telemetry.event_time) AS report_date,
    ku.keycloak_username,
    ku.full_name AS client_name,
    telemetry.serial_number AS prosthesis_serial_number,
    
    -- Телеметрия
    avg(telemetry.battery_level) AS avg_battery_level,
    sum(telemetry.steps_count) AS total_steps,
    max(telemetry.muscle_voltage) AS max_muscle_voltage,
    countIf(telemetry.error_code > 0) AS errors_count,
    avg(telemetry.device_temperature) AS avg_temperature,
    avg(telemetry.signal_quality) AS avg_signal_quality,
    
    -- CRM данные (через LEFT JOIN чтобы не терять пользователей без заказов)
    countDistinct(orders.id) AS total_orders,
    sum(orders.total_amount) AS total_spent,
    avg(orders.total_amount) AS avg_order_value,
    max(orders.order_date) AS last_order_date,
    
    -- Комбинированные метрики
    (avg(telemetry.battery_level) / 100 * 0.3 + 
     (sum(telemetry.steps_count) / 10000) * 0.4 + 
     (100 - avg(telemetry.error_code)) / 100 * 0.3) * 100 AS activity_score,
    
    (avg(telemetry.battery_level) / 100 * 0.4 + 
     avg(telemetry.signal_quality) / 100 * 0.4 + 
     (100 - avg(telemetry.error_code)) / 100 * 0.2) * 100 AS device_health_score,
    
    now() AS updated_at
    
FROM raw_telemetry telemetry
LEFT JOIN stg_keycloak_users ku ON telemetry.serial_number = ku.prosthesis_serial_number
LEFT JOIN crm_orders orders ON ku.crm_customer_id = orders.customer_id AND toDate(orders.order_date) = toDate(telemetry.event_time)
GROUP BY report_date, ku.keycloak_username, ku.full_name, telemetry.serial_number;

-- =====================================================
-- 5. ADDITIONAL VIEWS FOR API
-- =====================================================

-- 5.1 View для получения текущего статуса всех устройств
CREATE VIEW IF NOT EXISTS device_current_status AS
SELECT
    serial_number,
    argMax(battery_level, event_time) AS current_battery,
    argMax(muscle_voltage, event_time) AS current_muscle_voltage,
    argMax(error_code, event_time) AS last_error_code,
    argMax(device_temperature, event_time) AS current_temperature,
    argMax(signal_quality, event_time) AS current_signal_quality,
    max(event_time) AS last_telemetry_time,
    now() - max(event_time) AS seconds_since_last_update
FROM raw_telemetry
GROUP BY serial_number;

-- 5.2 View для аналитики по ошибкам
CREATE VIEW IF NOT EXISTS error_analytics AS
SELECT
    serial_number,
    error_code,
    toDate(event_time) AS error_date,
    count() AS error_count,
    avg(battery_level) AS avg_battery_at_error,
    avg(muscle_voltage) AS avg_muscle_voltage_at_error
FROM raw_telemetry
WHERE error_code > 0
GROUP BY serial_number, error_code, error_date;

-- 5.3 View для сравнения производительности устройств
CREATE VIEW IF NOT EXISTS device_performance_comparison AS
SELECT
    serial_number,
    avg(battery_level) AS avg_battery,
    avg(muscle_voltage) AS avg_muscle_voltage,
    avg(steps_count) AS avg_daily_steps,
    avg(device_temperature) AS avg_temperature,
    avg(signal_quality) AS avg_signal_quality,
    countIf(error_code > 0) / count() * 100 AS error_rate_percentage
FROM raw_telemetry
WHERE event_time >= now() - INTERVAL 30 DAY
GROUP BY serial_number;

-- 5.4 View для дашборда клиента (объединяет все данные по пользователю)
CREATE VIEW IF NOT EXISTS customer_dashboard AS
SELECT
    ku.keycloak_username,
    ku.full_name,
    ku.email,
    ku.prosthesis_serial_number,
    
    -- Текущий статус устройства
    dcs.current_battery,
    dcs.current_muscle_voltage,
    dcs.last_error_code,
    dcs.last_telemetry_time,
    
    -- Агрегированные метрики за последние 7 дней
    rdm7.avg_battery_level AS avg_battery_7d,
    rdm7.total_steps AS total_steps_7d,
    rdm7.errors_count AS errors_7d,
    rdm7.activity_score AS activity_score_7d,
    
    -- CRM метрики (все время)
    rdm.total_orders AS lifetime_orders,
    rdm.total_spent AS lifetime_spent,
    
    -- Оценка здоровья устройства
    rdm.device_health_score
    
FROM stg_keycloak_users ku
LEFT JOIN device_current_status dcs ON ku.prosthesis_serial_number = dcs.serial_number
LEFT JOIN (
    SELECT keycloak_username, 
           avg_battery_level, total_steps, errors_count, activity_score, device_health_score
    FROM report_user_daily_mart 
    WHERE report_date >= today() - 7
) rdm7 ON ku.keycloak_username = rdm7.keycloak_username
LEFT JOIN (
    SELECT keycloak_username, 
           sum(total_orders) AS total_orders, 
           sum(total_spent) AS total_spent
    FROM report_user_daily_mart 
    GROUP BY keycloak_username
) rdm ON ku.keycloak_username = rdm.keycloak_username;

-- =====================================================
-- 6. PERFORMANCE OPTIMIZATIONS
-- =====================================================

-- 6.1 Создание дополнительных индексов (через ORDER BY)
-- ORDER BY уже задан в таблицах, этого достаточно для ClickHouse

-- 6.2 Настройка TTL для автоматической очистки старых данных
ALTER TABLE raw_telemetry MODIFY TTL event_time + INTERVAL 90 DAY;

-- 6.3 Создание Dictionary для маппинга (in-memory cache)
CREATE DICTIONARY IF NOT EXISTS dict_serial_to_user
(
    serial_number String,
    keycloak_username String,
    full_name String
)
PRIMARY KEY serial_number
SOURCE(CLICKHOUSE(
    HOST 'localhost'
    PORT 9000
    USER 'default'
    PASSWORD 'clickhouse_password'
    DB 'default'
    TABLE 'stg_keycloak_users'
))
LIFETIME(MIN 300 MAX 360)
LAYOUT(HASHED());

-- =====================================================
-- 7. VERIFICATION QUERIES
-- =====================================================

-- Проверка наличия данных
SELECT '=== Telemetry Data ===' AS info;
SELECT count() FROM raw_telemetry;

SELECT '=== User Mapping ===' AS info;
SELECT * FROM stg_keycloark_users;

SELECT '=== Daily Mart Sample ===' AS info;
SELECT * FROM report_user_daily_mart 
WHERE report_date >= today() - 3 
LIMIT 10;

SELECT '=== Customer Dashboard ===' AS info;
SELECT * FROM customer_dashboard;

SELECT '=== Device Performance ===' AS info;
SELECT * FROM device_performance_comparison;

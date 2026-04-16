#!/bin/bash

echo "Refreshing materialized views..."

# Пересоздание материализованных представлений
docker exec clickhouse clickhouse-client --password clickhouse_password --query "
DROP TABLE IF EXISTS mv_report_user_daily_mart;
DROP TABLE IF EXISTS report_user_daily_mart;

CREATE TABLE report_user_daily_mart (
    report_date Date,
    keycloak_username String,
    client_name String,
    prosthesis_serial_number String,
    avg_battery_level Float32,
    total_steps UInt64,
    max_muscle_voltage Float32,
    errors_count UInt32,
    avg_temperature Float32,
    avg_signal_quality Float32,
    total_orders UInt32,
    total_spent Decimal(10,2),
    avg_order_value Decimal(10,2),
    last_order_date DateTime,
    activity_score Float32,
    device_health_score Float32,
    updated_at DateTime DEFAULT now()
) ENGINE = SummingMergeTree()
ORDER BY (keycloak_username, report_date);

CREATE MATERIALIZED VIEW mv_report_user_daily_mart TO report_user_daily_mart AS
SELECT
    toDate(telemetry.event_time) AS report_date,
    ku.keycloak_username,
    ku.full_name AS client_name,
    telemetry.serial_number AS prosthesis_serial_number,
    avg(telemetry.battery_level) AS avg_battery_level,
    sum(telemetry.steps_count) AS total_steps,
    max(telemetry.muscle_voltage) AS max_muscle_voltage,
    countIf(telemetry.error_code > 0) AS errors_count,
    avg(telemetry.device_temperature) AS avg_temperature,
    avg(telemetry.signal_quality) AS avg_signal_quality,
    countDistinct(orders.id) AS total_orders,
    sum(orders.total_amount) AS total_spent,
    avg(orders.total_amount) AS avg_order_value,
    max(orders.order_date) AS last_order_date,
    (avg(telemetry.battery_level) / 100 * 0.3 + 
     (sum(telemetry.steps_count) / 10000) * 0.4 + 
     (100 - avg(telemetry.error_code)) / 100 * 0.3) * 100 AS activity_score,
    (avg(telemetry.battery_level) / 100 * 0.4 + 
     avg(telemetry.signal_quality) / 100 * 0.4 + 
     (100 - avg(telemetry.error_code)) / 100 * 0.2) * 100 AS device_health_score,
    now() AS updated_at
FROM raw_telemetry telemetry
LEFT JOIN stg_keycloak_users ku ON telemetry.serial_number = ku.prosthesis_serial_number
LEFT JOIN crm_orders orders ON ku.crm_customer_id = orders.customer_id 
    AND toDate(orders.order_date) = toDate(telemetry.event_time)
GROUP BY report_date, ku.keycloak_username, ku.full_name, telemetry.serial_number;
"

echo "Mart refresh completed!"
CREATE DATABASE IF NOT EXISTS crm_analytics;

-- Kafka очередь (используем правильное имя топика)
CREATE TABLE IF NOT EXISTS crm_analytics.kafka_customers_queue (
    id UInt32,
    name String,
    age UInt8,
    gender String,
    email String,
    country String,
    __deleted String
) ENGINE = Kafka
SETTINGS
    kafka_broker_list = 'kafka:9092',
    kafka_topic_list = 'crm-postgres.public.customers-cdc',
    kafka_group_name = 'clickhouse_customers_group_new',
    kafka_format = 'JSONEachRow',
    kafka_skip_broken_messages = 1000;

-- Целевая таблица
CREATE TABLE IF NOT EXISTS crm_analytics.dim_customers (
    id UInt32,
    name String,
    age UInt8,
    gender String,
    email String,
    country String,
    _sign Int8 DEFAULT 1,
    _version UInt64 DEFAULT 1,
    updated_at DateTime DEFAULT now()
) ENGINE = ReplacingMergeTree(_version)
ORDER BY id;

-- Materialized View для автоматической загрузки
CREATE MATERIALIZED VIEW IF NOT EXISTS crm_analytics.mv_kafka_to_dim_customers
TO crm_analytics.dim_customers
AS SELECT
    id,
    name,
    age,
    gender,
    email,
    country,
    if(__deleted = 'true', -1, 1) AS _sign,
    now() AS _version,
    now() AS updated_at
FROM crm_analytics.kafka_customers_queue
WHERE id IS NOT NULL;

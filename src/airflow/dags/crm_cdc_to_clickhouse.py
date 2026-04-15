from datetime import datetime, timedelta
from airflow import DAG
from airflow.operators.python_operator import PythonOperator
from airflow.providers.http.operators.http import SimpleHttpOperator
from airflow.utils.task_group import TaskGroup
import json
import requests

default_args = {
    "owner": "data_engineer",
    "depends_on_past": False,
    "start_date": datetime(2025, 4, 5),
    "retries": 1,
    "retry_delay": timedelta(minutes=5),
}

dag = DAG(
    "crm_cdc_to_clickhouse",
    default_args=default_args,
    description="CDC: PostgreSQL → Kafka → ClickHouse (dim_customers)",
    schedule_interval="@once",  # или "@daily", но @once — для инициализации
    start_date=datetime(2025, 4, 5),
    catchup=False,
    tags=["etl", "cdc", "clickhouse"],
    is_paused_upon_creation=False,  # Важно: чтобы не был paused после загрузки
)


# --- TASK: Удалить коннектор, если существует ---
def delete_connector_if_exists(**kwargs):
    import requests

    url = "http://debezium:8083/connectors/crm-connector"
    try:
        response = requests.delete(url)
        if response.status_code == 404:
            print("✅ Коннектор не найден — ничего удалять не нужно.")
        elif response.status_code in {200, 204}:
            print("✅ Коннектор успешно удалён.")
        else:
            print(f"❌ Ошибка при удалении: {response.status_code} {response.text}")
            response.raise_for_status()
    except requests.exceptions.RequestException as e:
        print(f"❌ Сетевая ошибка: {e}")
        raise


delete_connector = PythonOperator(
    task_id="delete_existing_connector",
    python_callable=delete_connector_if_exists,
    dag=dag,
)


# --- TASK: Регистрация Debezium-коннектора ---
def register_debezium_connector_callable(**kwargs):
    url = "http://debezium:8083/connectors"
    payload = {
        "name": "crm-connector",
        "config": {
            "connector.class": "io.debezium.connector.postgresql.PostgresConnector",
            "database.hostname": "crm_db",
            "database.port": "5432",
            "database.user": "crm_user",
            "database.password": "crm_password",
            "database.dbname": "crm_db",
            "database.server.name": "crm-postgres",
            "table.include.list": "public.customers",
            "plugin.name": "pgoutput",
            "slot.name": "debezium_slot",
            "publication.name": "debezium_publication",
            "database.history.kafka.bootstrap.servers": "kafka:9092",
            "database.history.kafka.topic": "schema-changes.crm",
            "key.converter": "org.apache.kafka.connect.json.JsonConverter",
            "value.converter": "org.apache.kafka.connect.json.JsonConverter",
            "key.converter.schemas.enable": "false",
            "value.converter.schemas.enable": "false",
            "topic.prefix": "crm-postgres",
            "snapshot.mode": "always",
            "transforms": "unwrap,add_topic_suffix",
            "transforms.unwrap.type": "io.debezium.transforms.ExtractNewRecordState",
            "transforms.unwrap.delete.handling.mode": "rewrite",
            "transforms.add_topic_suffix.type": "org.apache.kafka.connect.transforms.RegexRouter",
            "transforms.add_topic_suffix.regex": "(.*)",
            "transforms.add_topic_suffix.replacement": "$1-cdc",
        },
    }
    response = requests.post(url, json=payload)
    if response.status_code not in {201, 409}:
        print(f"❌ Ошибка при создании коннектора: {response.status_code}")
        print(f"❌ Текст ошибки: {response.text}")
        response.raise_for_status()
    else:
        print(f"✅ Успешно: {response.status_code}")


register_connector = PythonOperator(
    task_id="register_debezium_connector",
    python_callable=register_debezium_connector_callable,
    dag=dag,
)


# --- TASK: Убедиться, что ClickHouse таблица существует ---
def create_dim_customers(**kwargs):
    from airflow_clickhouse_plugin.hooks.clickhouse import ClickHouseHook

    hook = ClickHouseHook(clickhouse_conn_id="clickhouse_conn")
    sql = """
    CREATE TABLE IF NOT EXISTS dim_customers (
        id UInt32,
        name String,
        age UInt8,
        gender String,
        email String,
        country String,
        _sign Int8 DEFAULT 1,
        _version UInt64 DEFAULT 1
    ) ENGINE = ReplacingMergeTree(_version)
    ORDER BY id
    """
    hook.execute(sql)


create_table = PythonOperator(
    task_id="create_dim_customers_table",
    python_callable=create_dim_customers,
    dag=dag,
)


# --- TASK: Создать материализованное представление из Kafka ---
def create_kafka_materialized_view(**kwargs):
    from airflow_clickhouse_plugin.hooks.clickhouse import ClickHouseHook

    hook = ClickHouseHook(clickhouse_conn_id="clickhouse_conn")

    hook.execute(
        """
        CREATE MATERIALIZED VIEW IF NOT EXISTS mv_kafka_to_dim_customers
        TO dim_customers
        AS SELECT
            id,
            name,
            age,
            gender,
            email,
            country,
            -- Если __deleted = "true" → _sign = -1, иначе +1
            if(__deleted = 'true', -1, 1) AS _sign,
            now() AS _version
        FROM kafka_customers_queue
        WHERE id IS NOT NULL;
        """
    )
    print("✅ Материализованное представление успешно создано.")


create_mv = PythonOperator(
    task_id="create_kafka_materialized_view",
    python_callable=create_kafka_materialized_view,
    dag=dag,
)

delete_connector >> register_connector >> create_table >> create_mv

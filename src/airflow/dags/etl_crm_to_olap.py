from airflow import DAG
from airflow.providers.postgres.hooks.postgres import PostgresHook
from clickhouse_driver import Client
from airflow.operators.python import PythonOperator
from airflow.utils.dates import days_ago
import logging

default_args = {
    'owner': 'bionic_pro',
    'start_date': days_ago(1),
    'retries': 1,
}

def extract_crm_load_stage():
    # 1. Чтение из Postgres
    pg_hook = PostgresHook(postgres_conn_id='crm_postgres')
    conn = pg_hook.get_conn()
    cursor = conn.cursor()
    cursor.execute("SELECT keycloak_username, full_name, prosthesis_serial_number FROM clients")
    crm_data = cursor.fetchall()
    
    logging.info(f"Extracted {len(crm_data)} users from CRM")

    # 2. Запись в ClickHouse
    # Важно: используем пользователя и пароль, заданные в docker-compose
    client = Client(host='clickhouse', user='default', password='clickhouse_password')
    
    client.execute("TRUNCATE TABLE stg_crm_users")
    
    if crm_data:
        client.execute(
            'INSERT INTO stg_crm_users (keycloak_username, full_name, prosthesis_serial_number) VALUES',
            crm_data
        )
        logging.info("Loaded users into Staging")

def calculate_mart():
    client = Client(host='clickhouse', user='default', password='clickhouse_password')
    
    # Исправленный SQL: группировка по u.full_name
    sql = """
        INSERT INTO report_user_daily_mart
        SELECT
            toDate(t.event_time) as report_date,
            u.keycloak_username,
            u.full_name as client_name,
            avg(t.battery_level) as avg_battery_level,
            sum(t.steps_count) as total_steps,
            max(t.muscle_voltage) as max_muscle_voltage,
            sum(if(t.error_code > 0, 1, 0)) as errors_count
        FROM raw_telemetry t
        INNER JOIN stg_crm_users u ON t.serial_number = u.prosthesis_serial_number
        GROUP BY report_date, u.keycloak_username, u.full_name
    """
    client.execute(sql)
    logging.info("Data Mart recalculated successfully")

with DAG(
    'bionic_etl_daily',
    default_args=default_args,
    schedule_interval='10 9 * * *',
    catchup=False
) as dag:

    t1 = PythonOperator(
        task_id='sync_crm_users',
        python_callable=extract_crm_load_stage
    )

    t2 = PythonOperator(
        task_id='calculate_report_mart',
        python_callable=calculate_mart
    )

    t1 >> t2

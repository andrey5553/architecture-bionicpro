-- Тестовые запросы для проверки интеграции

-- 1. Проверка потока данных из CRM
SELECT '=== Orders from CRM ===';
SELECT id, customer_id, order_date, status, total_amount 
FROM crm_orders 
ORDER BY order_date DESC 
LIMIT 10;

-- 2. Проверка объединения телеметрии с CRM данными
SELECT 
    t.serial_number,
    t.event_time,
    t.steps_count,
    ku.keycloak_username,
    ku.full_name,
    o.total_amount AS order_amount
FROM raw_telemetry t
LEFT JOIN stg_keycloak_users ku ON t.serial_number = ku.prosthesis_serial_number
LEFT JOIN crm_orders o ON ku.crm_customer_id = o.customer_id 
    AND toDate(o.order_date) = toDate(t.event_time)
WHERE t.event_time >= today() - 1
LIMIT 20;

-- 3. Анализ пользователей с высокой активностью но без заказов
SELECT 
    ku.keycloak_username,
    ku.full_name,
    avg(rdm.total_steps) AS avg_daily_steps,
    sum(rdm.total_spent) AS total_spent
FROM stg_keycloak_users ku
LEFT JOIN report_user_daily_mart rdm ON ku.keycloak_username = rdm.keycloak_username
WHERE rdm.report_date >= today() - 30
GROUP BY ku.keycloak_username, ku.full_name
HAVING avg_daily_steps > 5000 AND total_spent = 0;

-- 4. Мониторинг здоровья устройств
SELECT 
    serial_number,
    current_battery,
    current_signal_quality,
    last_error_code,
    seconds_since_last_update,
    CASE 
        WHEN current_battery < 20 THEN 'CRITICAL'
        WHEN current_battery < 50 THEN 'WARNING'
        ELSE 'OK'
    END AS battery_status,
    CASE 
        WHEN seconds_since_last_update > 3600 THEN 'OFFLINE'
        WHEN seconds_since_last_update > 300 THEN 'WARNING'
        ELSE 'ONLINE'
    END AS device_status
FROM device_current_status;

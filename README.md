# Спринт 9

## Задание 1. Повышение безопасности системы

  ### Задача 1. Предложите архитектурное решение и доработайте диаграмму C4 для управления учётными данными пользователя. 
    Решение должно учитывать и обеспечивать следующие аспекты:
    - Унификацию доступа в системе BionicPRO. Это будет осуществляться через запрос данных учётных записей из внешнего источника, который расположен в стране представительства   компании. Принципы локального хранения персональной и медицинской информации не должны быть нарушены.
    - Безопасную схему работы с access- и refresh-токенами, которая исключает передачу фронтенду токенов, которые были получены от IdP.
    - Возможность поддержки аутентификации пользователей через различные внешние удостоверяющие службы, действующие в разных странах.

    ["Доработанная диаграмма C4 drawio"](/Task1/диаграмма%20контейнеров%20C4.drawio)
    ["Доработанная диаграмма C4 png"](/Task1/диаграмма%20контейнеров%20C4.png)

    На диаграмму добавлены:

    - Auth Service (BFF) - шлюз авторизации (keycloak, кеширование)
    - ETL Process - перенос данных из источников
    - ClickHouse - аналитическое хранилище для телеметрии
    - Report Service - сервис генерации отчетов

    ### Задача 2. Улучшите безопасность существующего приложения, заменив Code Grant на PKCE. 

  ### Задача 2. Улучшите безопасность существующего приложения, заменив Code Grant на PKCE. 
    Его нужно добавить к уже существующим приложениям — фронтенду и Keycloak. 
    Для перевода приложения PKCE (Proof Key for Code Exchange) необходимо внести изменения в 
    конфигурацию Keycloak (серверная часть) и в инициализацию клиента Keycloak в React-приложении.

    Изменения вносятся в файлы:
      1. Keycloak (realm-export.json):
        * Для клиента reports-frontend необходимо отключить implicitFlowEnabled (устаревший и небезопасный для SPA).
        * Необходимо отключить directAccessGrantsEnabled (вход по логину/паролю без браузерного редиректа), так как это увеличивает риск фишинга.
        * Необходимо включить standardFlowEnabled.
        * Необходимо добавить атрибут pkce.code.challenge.method: S256, чтобы сервер Keycloak требовал наличие PKCE при обмене кодами.

      2. Frontend (src/App.tsx):
        * Необходимо изменить параметры инициализации провайдера ReactKeycloakProvider.
        * Добавить pkceMethod: 'S256', чтобы библиотека keycloak-js автоматически генерировала code_verifier и  code_challenge перед отправкой пользователя на страницу входа.

  #### Проверим работоспособность и безопасность приложения.
  1. Запустим Docker Compose 
    `docker-compose up --build`
  [Скриншот с результатом успешного выполнения.](/Task1/part2/images/1%20развертывание%20сервисов%20(docker).png)

  2. Откроем веб-интерфейс по адресу `http://localhost:3000`
  [Скриншот с результатом успешного выполнения.](/Task1/part2/images/2%20spa%20приложение%20исходно.png)

  3. Нажмем кнопку логина, в результате чего произойдет переадресация на форму ввода имени пользователя и пароля.
  [Скриншот с результатом успешного выполнения.](/Task1/part2/images/3%20переадресация%20в%20keycloak.png)
  
  4. Зайдем под пользователем `prothetic1/prothetic123`
  На скриншоте видны заголовки POST-запроса, характерные для PKCE, в частности code_verifier.
  [Скриншот с результатом успешного выполнения.](/Task1/part2/images/4%20pkce%20авторизация%20пользователя.png)
  [Еще один скриншот с refresh_token.](/Task1/part2/images/5%20ответ%20со%20стороны%20сервера%20kecloak.png)

  5. Откроем административную панель Keycloak по адресу `http://localhost:8080`.
  Убедимся, что Keycloak требует PKCE для reports-frontend.
  [Скриншот 1 - вкладка Settings.](/Task1/part2/images/6%20настройка%20pkce%20для%20reports-frontend.png)
  - Client authentication: выключена
  - Standard flow: включена
  - Implicit flow: выключена
  - Direct access grants: выключена, пароли не передаются напрямую

    #### Таким образом, проведена замена Code Grant на PKCE.

  ### Задача 3. Обеспечьте безопасное получение и хранение access-и refresh-токенов.
  1. Написал сервис bionicpro-auth выполняющий роль backend-а для фронты, который реализует механизм запроса access- и refresh-токенов, а такжеинтеграцию с Keycloak и работу с сессиями.
  4. Настроил Keycloak на работу с refresh_token. Установите время работы access_token — не более 2 минут. ["Настройки keycloak по обновлению access token"](/Task1//part3//настройка%20keycloak%20на%20работу%20с%20обновлением%20access%20tokens.png)
  5. Когда пользователь успешно авторизуется на бэкенде, то сохраняю refresh_token.
  6. access_token сохранен.
  7. Обеспечена привязка access_token и refresh_token к сессии.
  8. В ответе фронтенду вместо токенов отдаю сессионную cookie c HTTP-only и Secure-флагами.
  9. access_token обновляется через refresh_token. 
  10. Обновлен код фронтенд-приложения, сделан обязательным прокидывание на бэкенд сессионной cookie. 
  11. После того как access_token устареет, то сервис сам сходит за новым в keycloak, используя refresh token. 
  12. Реализована ротация сессии в рамках действующего access_token для предотвращения session fixation attack.

  Создал сервис [bionicpro-auth](/src/backend/bionicpro-auth/) на C# .net10, овтечающий требованиям.
  Построил полноценную систему аутентификации и авторизации с:
  * Keycloak как Identity Provider
  * ASP.NET Core бэкенд с сессиями
  * React фронтенд
  * Docker Compose для оркестрации

  Результаты запуска сервисов прикладываю в виде картинок:
  * [Проверка, что все контейнеры отвечают](/Task1/part3/1%20проверка%20запуска%20docker-compose%20(проверяем%20что%20все%20контейнеры%20отвечают).png)
  * [Запуск фронты](/Task1/part3/2%20запуск%20фронты.png)
  * [Вход пользователя](/Task1/part3/4%20вход%20пользователя%20(добавка).png)
  * [Пользователи в keycloak](/Task1/part3/5%20keycloak%20пользователи.png)
  * [Отчет с авторизацией пользователя без refresh token](/Task1/part3/6%20получение%20отчета%20о%20пользователе%20с%20использование%20авторизации.png)
  * [Отчет с авторизацией пользователя с указанием времени жизни access token](/Task1/part3/7%20время%20жизни%20токена%20в%20отчете.png)
  * [Отчет с авторизацией пользователя после окончания таймаута access token (refresh token в действии)](/Task1/part3/8%20время%20жизни%20токена%20в%20отчете%20(после%20таймаута).png)


  ### Задача 4. Добавьте LDAP для возможности получения данных о пользователях представительства BionicPRO в другой стране.

    1. Очищаем список контейнеров в docker
    2. Загружаем заново все контейнеры  docker-compose up -d
    3. Проверяем логи импорта  docker-compose logs -f keycloak-import
    [Лог keycloak-import](/Task1/part4/images/логи%20keycloak-import%20об%20успешном%20создании%20ldap%20конфигурации%20в%20keycloak%20realm-export.png)
  
    # Проверка что пользователи из LDAP загрузились
    docker exec -it keycloak /opt/keycloak/bin/kcadm.sh config credentials --server http://localhost:8080 --realm master --user admin --password admin
    docker exec -it keycloak /opt/keycloak/bin/kcadm.sh get users -r reports-realm

    Keycloak Admin: http://localhost:8080 (admin/admin)
    PHP LDAP Admin: http://localhost:8090 (логин: cn=admin,dc=example,dc=com, пароль: admin)
    Frontend: http://localhost:3000
  
    Тестовые пользователи из LDAP:
    john.doe / password (имеет роль prothetic_user)
    jane.smith / password (имеет роль user)
    alex.johnson / password (имеет роль prothetic_user)

    Локальные пользователи Keycloak:
    user1 / password123 (роль user)
    user2 / password123 (роль user)
    admin1 / admin123 (роль administrator)
  
    Особенности настройки маппинга ролей:
    В конфигурации LDAP маппера настроено следующее:
      * Роли из LDAP (cn=user, cn=prothetic_user) маппятся на роли в Keycloak realm
      * Пользователь jane.smith получает роль user
      * Пользователи john.doe и alex.johnson получают роль prothetic_user
    Маппинг работает в режиме READ_ONLY - изменения ролей из Keycloak не будут писаться обратно в LDAP

  Теперь система поддерживает аутентификацию через LDAP для пользователей из другого представительства, с правильным маппингом ролей!

  ### Задача 5. Настройка MFA.

    Настройка OTP только для user1, остальные пользователи будут входить без OTP.
    1. Убираем глобальную OTP политику и оставляем OTP только для user1 это проделываем в [Файл](/src/keycloak/realm-clean.json)
    2. Создаем keycloak/setup-otp-user1.sh (только для user1)
    3. Обновляем import.sh
    4. Обновляем docker-compose.yml для keycloak-import (прокидываем скрипт - ./keycloak/setup-otp-user1.sh:/tmp/setup-otp-user1.sh)
    5. перезапускаем контейнеры
      docker-compose down -v
      docker-compose up -d --build
      Также лучше после перезапустить keycloak-import, так как keycloak стартует долго и есть шансы получить ошибку на этапе импорта
      делаем вот так:
      ``` docker-compose stop keycloak-import ```
      ``` docker-compose rm -f keycloak-import ```
      ``` docker-compose up -d keycloak-import ```
      ``` docker-compose logs -f keycloak-import ``` (проверяем логи)
      [Вот что будет при успешном запуске в логах](/Task1/part5/images/1%20лог%20keycloak-import.png)
      ```
    6. Проверяем, что все получилось, открываем http://localhost:8080/realms/reports-realm/account
    [Страница входа keyclock](/Task1/part5/images/2%20keycloak%20вход%20user1.png)
    [Аутентификатор](/Task1/part5/images/3%20keycloak%20установка%20аутентификатора.png) 
    [Вход после отправки кода через аутентификатор](/Task1/part5/images/4%20вход%20после%20прохождения%20аутентификации.png)

    Результат:
    user1 при первом входе обязан настроить OTP. После настройки OTP, вход без кода из приложения невозможен admin1 и другие пользователи входят как обычно LDAP пользователи также не требуют OTP (если не настроить отдельно).

  ### Задача 6. Добавление OAuth 2.0 от Яндекс ID.

    1. Используя механизм Identity Brokering, реализуйте аутентификацию пользователей через внешний Identity Prodider Яндекс ID. Обратите внимание, что сервис протезов получает данные профиля пользователя из Яндекса.
    2. После аутентификации сервис должен спрашивать пользователя о разрешении использовать данные.
    3. Сервис должен запрашивать у Яндекса данные профиля и сохранять их в БД.

    -----
    Успешно произвёл эти манипуляции в ручном режиме, но не стал добавлять это в сервис, т.к. это не может работать во время ревью: для работы нужен "ClientID" и "Client secret", которые могли бы скомпроментировать мой аккаунт. Выкладывание секрета в публичный репозиторий нежелательно, потому что им может воспользоваться злоумышленник, а без секрета конфигурация не заработает.

    Но концепция ясна: настраиваем Keycloak на работу с Яндексом, и разрешаем конфликты scope (openid, email, profile -> login:info login:email). Либо через прокси-приложение, которое бы переправляло запросы с Keycloak в Яндекс и обратно, либо через mapper этих самых scope'ов в конфигурации Keycloak. Первое проще, второе оптимальнее.

    Если коротко, надо создать файл настройки Яндекс ID провайдера
    ```json
    {
    "alias": "yandex",
    "displayName": "Yandex ID",
    "providerId": "oidc",
    "enabled": true,
    "storeToken": true,
    "addReadTokenRoleOnCreate": true,
    "trustEmail": true,
    "linkOnly": false,
    "config": {
        "clientId": "ВАШ_CLIENT_ID",
        "clientSecret": "ВАШ_CLIENT_SECRET",
        "authorizationUrl": "https://oauth.yandex.ru/authorize",
        "tokenUrl": "https://oauth.yandex.ru/token",
        "userInfoUrl": "https://login.yandex.ru/info",
        "logoutUrl": "https://oauth.yandex.ru/logout",
        "defaultScope": "login:info login:email",
        "clientAuthMethod": "client_secret_post",
        "userInfoMappingJson": "{\"login\":\"username\", \"email\":\"email\", \"first_name\":\"firstName\", \"last_name\":\"lastName\"}",
        "syncMode": "FORCE",
        "guiOrder": "1",
        "validateSignature": "false",
        "useJwksUrl": "false"
        }
    }
    ```
    Ну и далее импортировать настройки в keycloak
    Настроить бек и фронт для работы

## Задание 2. Разработка сервиса отчётов
  
  ### Задача 1. Создать архитектуру решения для подготовки и получения отчётов
  Продублировал схему диаграммы контейнеров, т к на ней уже было отражен процесс импорта данных в сервис отчетов
  [Диаграмма контейнеров C4 drawio](/Task2/part1/диаграмма%20контейнеров%20C4.drawio)
  [Диаграмма контейнеров C4 png](/Task2/part1/диаграмма%20контейнеров%20C4.png)

  ### Задача 2. Разработать Airflow DAG и настроить его на запуск по расписанию
  Шаг 1. Создание SQL-файлов для CRM и OLAP. 
  Каталог `sql`, в котором создадим файлы:
    - `init_crm.sql` для инициализации CRM 
    - `init_clickhouse.sql` для инициализации OLAP

  Файл `init_crm.sql` содержит пользователей, которые точно совпадают с конфигурацией Keycloak

  Шаг 2. Создание Airflow DAG. 
  Разместим решение в каталоге проекта `airflow`.
  Создадим `Dockerfile` для развертывания Airflow.
  Создадим подкаталог `dags` и в нем файл DAG `etl_crm_to_olap.py`.

  При запуске DAG в параметре schedule_interval задается [CRON-выражение.](https://yandex.cloud/ru/docs/serverless-integrations/concepts/cron) таким образом выполняется требование запуска по расписанию.

  Шаг 3. Внесение изменений в docker-compose.yaml для запуска Airflow, CRM, OLAP.
  В проекте представлен измененный файл `docker-compose.yaml`.

  Шаг 4. Проверка работы Airflow DAG. 
  1. Запустим Docker Compose 
  ```docker-compose down -v```
  ```docker-compose up -d --build```
  
  2. DAG будет выполнен. 
  [Как достучаться в докере до clickhouse](/Task2/part2/1%20докер%20идем%20в%20clickhouse.png)
  ```docker ps```
  ```docker exec -it src-clickhouse-1 clickhouse-client```
  ```select * from report_user_daily_mart;```

  3. Проверить логи ClickHouse
    ```docker-compose logs clickhouse``` 
    [Логи контейнера с clickhouse](/Task2/part2/4%20логи%20запуска%20контейнера%20с%20clickhouse.png)
  4. Открыть Airflow UI: http://localhost:8081 (Логин: admin, пароль: admin) [Скриншот](/Task2/part2/3%20UI%20aitflow.png)
  5. Запустить DAG вручную (принудительно из веб интерфейса)

  
  Успешный запуск DAG и результат
  [Скриншот с результатом успешного запроса в OLAP по результатам выполнения DAG.](/Task2/part2/2%20витрина%20с%20результатами%20в%20clickhouse.png)

### Задача 3. Создайте бэкенд-часть приложения для API.
  Сервис по работе с отчетностью у меня вложен как отдельный контроллер внутри сервис ```bionicpro-auth``` (ReportsController)
  не стал его выносить в отдельный сервис, учитывая, что это демо версия и нет цели настроить сервис для развертывания в проде,
  а так бы занялся его рефакторингом и пересмотром.
  Итак, сервис находится тут [Бекенд сервис](/src/backend/bionicpro-auth/)

  Также внутри ```docker-compose.yaml``` ныне представлено развертывание и агрегирование данных в OLAP среду (airflow)
  А именно следующие шаги:
  * crm_db 
  * airflow_db
  * airflow-init
  * airflow

  Описание работы с OLAP средой (Airflow). Основные компоненты OLAP-обработки:
  Источники данных:
  * CRM PostgreSQL (порт 5434) - оперативные данные
  * ClickHouse (порт 8123/9000) - аналитическое хранилище
  
  Airflow задачи:
  * Извлечение данных из CRM PostgreSQL
  * Трансформация и агрегация данных
  * Загрузка в ClickHouse (ETL/ELT процессы)
  * Планирование и мониторинг аналитических пайплайнов

  Хранилище метаданных:
  * PostgreSQL (порт 5435) для хранения DAG-файлов, истории запусков, логов

  Запуск:
  1. ```docker-compose down -v ``` - удаляем все контейнеры
  2. ```docker-compose up -d build```  - запуск контейнеров с билдом проектов
  3. ```docker-compose logs -f keycloak-import ``` - стоит проверить логи импорта realm-а в keycloak, бывает, что на момент импорта keycloak еще не запустиля
  (тогда потребуется удаление контейнера с keycloak-import и повторный его прогон, только для keycloak-import!)
  4. ```docker-compose logs airflow ``` - проверяем логи развертывания airflow
    тест подключения ```docker exec -it $(docker ps -q -f name=airflow) airflow db check ```
  5. желательно глянуть логи clickhouse 
  6. идем в UI шедулятора запуска сбора данных (DAGs) http://localhost:8081 , в списке DAG будет ```bionic_etl_daily```
  7. запускаем данную задачу в ручную, даже не ожидать запуска по расписанию [DAGs пример запуска в ручную](/Task2/part3/4%20airflow%20UI%20запуск%20задачи.png)
  8. проверяем, что данные успешно импортированы на витрину данных OLAP, вот как это можно запросить через докер [Витрина данных](/Task2/part3/1%20проверка%20наличия%20данных%20на%20витрине.png)
  9. теперь можно зайти пользователем на наш UI фронт приложения http://localhost:3000/ (войти под пользователями: "user1" или "prothetic1" или "prothetic2")
  10. запросить формирование отчета (кнопка ```Download Report```). Доступ к отчёту по пользователю предоставляется только в отношении активного (текущего) пользователя. 
  11. Результат выкачивания отчета для пользователя ```user1``` [Скриншот](/Task2/part3/2%20отчет%20по%20пользователю%20user1.png)
  12. Результат выкачивания отчета для пользователя ```prothetic1``` [Скриншот](/Task2/part3/2%20отчет%20по%20пользователю%20prothethic1.png)

  Решение включает в себя ETL-процесс, который объединяет данные с датчиков и данные из CRM, используя Apache Airflow, 
  и формирует готовую витрину отчётности в OLAP БД. 
  Итоговый отчёт по пользователю доступен через бэкенд-сервис API, который обозначен на исходной архитектуре.   

### Задача 4. Реализуйте ограничение доступа к эндпоинту отчётности.
### Задача 5. Добавьте в UI кнопку получения отчёта и вызова эндпоинта его генерации.
  Данные пункты решения описаны в решении на задачу 3.


## Задание 3. Снижение нагрузки на базу данных

  ### Задача 1-2-3. Добавьте в API-сервис реализацию записи сформированных отчётов в объектное хранилище, поддерживающее S3 API (Ceph, Minio). Конфигурация развёртывания Minio находится в репозитории спринта. При запросе отчёта сервис сначала проверяет наличие отчёта в S3. Если он там есть, то отдаёт ссылку на CDN. Если же отчёт не обнаружен, сервис должен его сгенерировать, положить в S3 и отдать ссылку на CDN в ответе. Для эмуляции CDN необходимо поднять и настроить Nginx как reverse proxy c включённым кешированием статических файлов. Продумать механизм обновления кеша в CDN и структуру хранения отчётов в S3 для быстрого доступа.

  Архитектура решения:
  Client → API Backend → Minio (S3) → Nginx (CDN/cache)
                         ↓
                   ClickHouse (данные)

  1. Сервис backend-а расширен для работы с minio и возможностью кешировать отчеты в хранилище, если отчета в нем нет, то обратиться к clickhouse                  
    Реализовано также на c# [Директория решения](/src/backend/bionicpro-auth/)
  2. frontend - изменений не потребовал
  3. nginx настроен в docker-compose, конфиг в директории ./nginx/ "cdn.conf"
  4. minio настроен в docker-compose, имя бакета reports 
  5. см ```docker-compose.yaml ```

  # Запуск
  1. ```docker-compose down -v ```
  2. ```docker-compose up -d ```
  3. Смотрим логи ```docker-compose logs -f backend ``` , ```docker-compose logs -f keycloak-import ```, ```docker-compose logs -f minio ```
  4. Если все хорошо, то давайте проверим UI
  5. minio http://localhost:9001/ (проверяем наличие бакета reports) [UI Minio](/Task3/images/9%20minio%20UI.png)
  6. keycloak http://localhost:8080/ (проверяем наличие reports-realm и пользователей) [UI Keycloak](/Task3/images/10%20keycloak%20UI.png)
  7. airflow http://localhost:8081/home (проверяем наличие DAG 'bionic_etl_daily') и запускаем в ручную! [UI airflow](/Task3/images/11%20airflow%20UI.png)
  8. frontend http://localhost:3000/ (заходим под prothetic1 или prothetic3 или user1) и формируем отчет, несколько раз!!!
  9. смотрим логи backend сервиса (в них должны быть записи о инвалидации кеша и так далее ) ```docker-compose logs -f backend ```
    (Отчет успешно сохранен в S3:
    Report saved to S3: reports/prothetic1/2026-04-14.json, ETag: "2021c6586ae417ee8a361593ed3e57ed"
    При повторном запросе отчет найден в S3:
    Report found in S3, last modified: 04/14/2026 14:22:29
    )
  10. Выкладываю отчет сохраненный в minio [Отчет в бакете reports MINIO](/Task3/images/2026-04-15.json)
  
  Скриншоты по результатам работы к данной задаче:
  * ["1 проверка работоспособности minio (и наличие bucket)"](/Task3/images/1%20проверка%20работоспособности%20minio%20(и%20наличие%20bucket).png)
  * ["2 проверка работоспособности остальных сервисов "](/Task3/images/2%20проверка%20работоспособности%20остальных%20сервисов%20.png)
  * ["3 keycloak-import перезапуск сервиса (reports-realm)"](/Task3/images/3%20keycloak-import%20перезапуск%20сервиса%20(reports-realm).png)
  * ["4 minio сохранение данных отчета в хранилие по пользователю  prothetic1"](/Task3/images/4%20minio%20сохранение%20данных%20отчета%20в%20хранилие%20по%20пользователю%20%20prothetic1.png)
  * ["5 отчет по пользователю prothetic на фронте"](/Task3/images/5%20отчет%20по%20пользователю%20prothetic%20на%20фронте.png)
  * ["6 minio сохранение данных отчета в хранилие по пользователю  prothetic1"](/Task3/images/6%20minio%20сохранение%20данных%20отчета%20в%20хранилие%20по%20пользователю%20%20prothetic1.png)
  * ["7 minio UI видно что отчеты по пользователю сохраняются"](/Task3/images/7%20minio%20UI%20видно%20что%20отчеты%20по%20пользователю%20сохраняются.png)
  * ["8 nginx (CDN у нас) видно что отчеты по пользователю сохраняются"](/Task3/images/8%20nginx%20(CDN%20у%20нас)%20видно%20что%20отчеты%20по%20пользователю%20сохраняются.png)

  Дополнительно можно проверить следующее, но не обязательно:
    # 1. Проверить создание bucket
    ```docker exec -it minio ls -la /data/ ```
    # 2. Проверить Nginx
    ```curl http://localhost:8082/health ```

    # 3. Проверить Minio API
    ```curl http://localhost:9002/reports/ ```
    # 4. Проверить содержимое
    ```docker exec -it minio cat /data/reports/prothetic1/2026-04-15.json ```
    # 5. Проверьте CDN доступ:
    # Через Nginx (CDN)
    ```curl http://localhost:8082/reports/prothetic1/2026-04-15.json ```

## Задание 4. Повышение оперативности и стабильности работы CRM
  
  ### Реализуйте механизм Change Data Capture (CDC) для отслеживания изменений в таблицах БД CRM. В качестве инструмента CDC используйте Debezium. С теорией по Debezium вы можете ознакомиться в спринте 8, теме 2, уроке 4. С документацией Postgres Debezium Connector вы можете ознакомиться в статье на официальном сайте. Настройте Debezium на отправку данных в топик Kafka. Настройте приём данных из топика Kafka в OLAP БД Clickhouse с помощью механизма KafkaEngine. Подготовьте витрину для отчётности, объединив данные при помощи MaterializedView в Clickhouse. Переведите сервис API на новую витрину.

  1. Реализован механизм Change Data Capture (CDC) [Debezium] (debezium/register-crm-connector.json).

  2. Настроен Debezium на отправку данных в топик Kafka. Сделано в [init-crm-connect.sql](olap-db/init-crm-connect.sql).

  3. Настроен приём данных из топика Kafka в OLAP БД Clickhouse с помощью механизма KafkaEngine.
    Сделано в [init-crm-connect.sql](olap-db/init-crm-connect.sql).

  4. Витрина для отчётности, объединение данных при помощи MaterializedView в Clickhouse.
    Сделано в [crm_cdc_to_clickhouse.py](airflow/dags/crm_cdc_to_clickhouse.py).

  5. Сервис через minio, так что витрина данных уже хранится отдельно.

  [Данные из PostgreSQL успешно попали в ClickHouse через Debezium + Kafka](/Task4/images/5%20тестовые%20данные%20в%20crm%20и%20далее%20едут%20в%20clickhouse.png)
  [Статус коннектора Debezium](/Task4/images/1%20статус%20коннектора%20Debezium.png)
  [Список торпиков kafka](/Task4/images/2%20список%20всех%20топиков%20в%20kafka.png)
  [Таблицы и данные crm + kafka](/Task4/images/3%20таблицы%20и%20данные%20в%20crm%20и%20clickhouse.png)
  [Данные в kafka](/Task4/images/4%20данные%20дошли%20до%20kafka.png)



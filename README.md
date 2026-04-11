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
      ```
      docker-compose stop keycloak-import
      docker-compose rm -f keycloak-import
      docker-compose up -d keycloak-import
      docker-compose logs -f keycloak-import (проверяем логи)
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

    


  


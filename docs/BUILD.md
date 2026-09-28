# Автоматическая сборка игры

Готовую Windows-версию собирает GitHub Actions — ни вам, ни друзьям не нужно открывать Unity.
После каждого обновления кода появляется новая сборка на странице
**[Releases](https://github.com/german122lol44466-max/YourFinalOrder/releases/latest)**:
архив `YourLastOrder-Windows.zip`. Друзья скачивают его, распаковывают и запускают `YourLastOrder.exe`.

## Разовая настройка (только у владельца репозитория, ~5 минут)

Unity разрешает собирать игры только с лицензией, поэтому сборщику нужна ваша **бесплатная Personal-лицензия**.

1. **Получите файл лицензии.**
   - Установите [Unity Hub](https://unity.com/download) и войдите в аккаунт Unity (можно создать бесплатно).
   - В Unity Hub: ⚙ **Settings → Licenses → Add → Get a free personal license**.
   - Откройте в Блокноте файл `C:\ProgramData\Unity\Unity_lic.ulf` (на macOS:
     `/Library/Application Support/Unity/Unity_lic.ulf`) и скопируйте **всё** содержимое.

2. **Добавьте секреты в репозиторий** на GitHub:
   **Settings → Secrets and variables → Actions → New repository secret**:

   | Имя | Значение |
   |---|---|
   | `UNITY_LICENSE` | всё содержимое `Unity_lic.ulf` |
   | `UNITY_EMAIL` | почта аккаунта Unity |
   | `UNITY_PASSWORD` | пароль аккаунта Unity |

   Секреты зашифрованы: их не видно ни в коде, ни в логах.

3. **Запустите сборку**: вкладка **Actions → «Сборка игры» → Run workflow**.
   Первая сборка идёт 30–50 минут (скачивается Unity и импортируется проект), следующие — быстрее.
   Потом сборка будет запускаться сама после каждого обновления кода.

> Если у аккаунта Unity включена двухфакторная аутентификация через приложение, вход сборщика
> может не пройти. В этом случае используйте для сборки отдельный аккаунт Unity без 2FA.

## Сборка вручную (если есть Unity)
В меню Unity: **Your Last Order → Собрать игру для Windows**. Готовая игра появится в `Build/Windows/`.

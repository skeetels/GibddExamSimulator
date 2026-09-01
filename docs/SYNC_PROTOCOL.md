# Протокол синхронизации Windows, Android и PWA

## Идентификаторы и формат

Каждая установка один раз создаёт случайный deviceId. Он не является hardware fingerprint. Каждая завершённая попытка получает UUID sessionId и сериализуется как StudySessionEnvelope schemaVersion 1 с deviceKind, временными метками UTC, bankVersion, bankSha256, rulesProfile, orderedQuestionIds, answer events и summary.

Канонический payload сортирует ответы по sequenceNumber и legacy aggregates по questionId, нормализует UTC и SHA-256 банка, после чего вычисляет PayloadSha256. Изменение deviceKind также меняет hash.

## Локальная транзакция

Сначала клиент атомарно:

1. вставляет неизменяемую завершённую сессию;
2. добавляет ссылку на неё в outbox;
3. удаляет draft только после успешного сохранения результата.

Windows и Android используют DesktopStudyStore/SQLite. PWA выполняет эквивалентную транзакцию IndexedDB. Активный draft сохраняется после старта, подтверждения ответа, перехода тренировки и запуска дополнительного блока.

## Push

SyncCoordinator читает due-записи outbox по порядку. SupabaseStudySessionRemote выполняет INSERT в study_sessions с пользовательским JWT. При конфликте session_id серверная копия читается: одинаковый payload_sha256 означает идемпотентный retry, отличающийся — конфликт данных. Успешная запись удаляется из outbox. Временная ошибка увеличивает attempt_count и назначает следующий retry.

Экзамен после upload автоматически вызывает telegram-report. HTTP 202/5xx считается незавершённой доставкой, поэтому outbox остаётся. Тренировки Telegram не вызывают.

## Pull

Клиент запрашивает строки текущего user_id с server_seq больше локального курсора. Каждая сессия валидируется и проверяется по hash. Вся страница вставляется вместе с новым курсором в одной транзакции. Если применение не завершилось, старый курсор сохраняется, и страница безопасно повторится.

## Объединённый профиль

LearningProfileBuilder дедуплицирует sessionId и не зависит от порядка строк. Ошибка Android поэтому участвует в рейтинге вопросов/билетов/блоков Windows и PWA; обратный сценарий идентичен. Селектор следующего экзамена и режимы Умные 10, Работа над ошибками и Слабые темы строятся после bounded pre-session sync.

## Безопасность

RLS разрешает authenticated-пользователю SELECT и INSERT только собственных study_sessions. UPDATE/DELETE клиенту не выдаются. Service role используется только внутри Edge Functions. Пароль не сохраняется: Windows защищает refresh token DPAPI, Android — SecureStorage/Android Keystore, PWA — локальным auth store с ограничениями браузера.

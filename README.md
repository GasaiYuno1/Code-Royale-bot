# code-royale-bot

Бот для CodinGame — Code Royale (C#, .NET 8). Контекст проекта и правила по коду рефери: `CLAUDE.md`.

- `src/RoyaleBot/` — бот (`Game/` модель ввода, `Strategy/` стратегии, `Bot.cs` цикл ходов).
- `tests/RoyaleBot.Tests/` — тесты без NuGet (мини-раннер `MiniTest.cs`).
- `tools/bundle.py` — склейка в один файл `dist/codingame.cs` для вставки на CodinGame; `tools/paste_page.py` — страница «скопировать код» для телефона.
- `tools/referee/` — сборка официального рефери (Kotlin) и матчи бота против боссов лиг.

Рефери: https://github.com/csj/code-royale
